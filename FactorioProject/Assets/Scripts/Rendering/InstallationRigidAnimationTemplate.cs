using System;
using System.Collections.Generic;
using ProjectF.MapObjects;
using UnityEngine;

namespace ProjectF.Rendering
{
    // One curve set and rest pose per prefab. Only visible working machines
    // evaluate matrix poses; native Animator/Transform hierarchies stay idle.
    internal sealed class InstallationRigidAnimationTemplate
    {
        private readonly MapObjectArchetype archetype;
        private readonly MapObjectAnimationClipDefinition clip;
        private readonly Dictionary<string, int> byPath = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Track[] tracks;
        private readonly Vector3[] positions, scales, eulers;
        private readonly Quaternion[] rotations;
        private readonly bool[] eulerAnimated;
        private readonly Matrix4x4[] matrices, restMatrices;
        private bool workingPose;
        private readonly struct Track
        {
            internal readonly int Node, Channel;
            internal readonly AnimationCurve Curve;
            internal Track(int node, int channel, AnimationCurve curve) { Node = node; Channel = channel; Curve = curve; }
        }
        private InstallationRigidAnimationTemplate(MapObjectArchetype source)
        {
            archetype = source; clip = source.AnimationClips[0];
            int count = source.Nodes.Count;
            positions = new Vector3[count]; scales = new Vector3[count]; eulers = new Vector3[count];
            rotations = new Quaternion[count]; eulerAnimated = new bool[count]; matrices = new Matrix4x4[count];
            restMatrices = new Matrix4x4[count];
            for (int i = 0; i < count; i++)
            {
                var node = source.Nodes[i]; byPath.Add(node.AnimationPath, i);
                restMatrices[i] = (node.ParentIndex < 0 ? Matrix4x4.identity : restMatrices[node.ParentIndex])
                    * Matrix4x4.TRS(node.LocalPosition, node.LocalRotation.normalized, node.LocalScale);
            }
            var resolvedTracks = new List<Track>(clip.TransformCurves.Count);
            for (int i = 0; i < clip.TransformCurves.Count; i++)
            {
                var curve = clip.TransformCurves[i];
                // Native Animator also ignores bindings whose target was removed from a variant.
                if (!byPath.TryGetValue(curve.Path, out int node)) continue;
                var track = new Track(node, Channel(curve.Property), curve.Curve);
                resolvedTracks.Add(track);
                if (track.Channel >= 10) eulerAnimated[track.Node] = true;
            }
            tracks = resolvedTracks.ToArray();
        }
        internal static InstallationRigidAnimationTemplate Create(MapObjectArchetype source, bool ignoreMissingNodes = false)
        {
            if (source == null || source.Nodes.Count == 0 || source.AnimationClips.Count != 1) return null;
            var clip = source.AnimationClips[0];
            if (clip.ObjectReferenceCurveCount != 0 || clip.TransformCurves.Count == 0
                || clip.TransformCurves.Count != clip.TransformCurveCount) return null;
            var paths = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Nodes.Count; i++)
                if (!paths.Add(source.Nodes[i].AnimationPath) || source.Nodes[i].ParentIndex >= i) return null;
            for (int i = 0; i < clip.TransformCurves.Count; i++)
            {
                var curve = clip.TransformCurves[i];
                if (curve.Curve == null || (!ignoreMissingNodes && !paths.Contains(curve.Path)) || Channel(curve.Property) < 0) return null;
            }
            return new InstallationRigidAnimationTemplate(source);
        }
        private static int Channel(string property)
        {
            switch (property)
            {
                case "m_LocalPosition.x": return 0; case "m_LocalPosition.y": return 1; case "m_LocalPosition.z": return 2;
                case "m_LocalScale.x": return 3; case "m_LocalScale.y": return 4; case "m_LocalScale.z": return 5;
                case "m_LocalRotation.x": return 6; case "m_LocalRotation.y": return 7;
                case "m_LocalRotation.z": return 8; case "m_LocalRotation.w": return 9;
                case "localEulerAnglesRaw.x": case "localEulerAnglesBaked.x": return 10;
                case "localEulerAnglesRaw.y": case "localEulerAnglesBaked.y": return 11;
                case "localEulerAnglesRaw.z": case "localEulerAnglesBaked.z": return 12;
                default: return -1;
            }
        }
        internal int ResolveNode(Transform node, Transform root)
        {
            string path = node == root ? string.Empty : node.name;
            for (Transform parent = node.parent; parent != null && parent != root; parent = parent.parent)
                path = parent.name + "/" + path;
            return byPath.TryGetValue(path, out int index) ? index : -1;
        }
        internal void Evaluate(double phase, bool working)
        {
            workingPose = working;
            if (!working) return;
            for (int i = 0; i < matrices.Length; i++)
            {
                var node = archetype.Nodes[i]; positions[i] = node.LocalPosition; scales[i] = node.LocalScale;
                rotations[i] = node.LocalRotation; eulers[i] = node.LocalRotation.eulerAngles;
            }
            float time = clip.LengthSeconds > 0f ? (float)(phase % clip.LengthSeconds) : 0f;
            if (!clip.Looping) time = Mathf.Min((float)phase, clip.LengthSeconds);
            for (int i = 0; i < tracks.Length; i++)
            {
                Track track = tracks[i]; float value = track.Curve.Evaluate(time);
                if (track.Channel < 3) positions[track.Node][track.Channel] = value;
                else if (track.Channel < 6) scales[track.Node][track.Channel - 3] = value;
                else if (track.Channel < 10) rotations[track.Node][track.Channel - 6] = value;
                else eulers[track.Node][track.Channel - 10] = value;
            }
            for (int i = 0; i < matrices.Length; i++)
            {
                int parent = archetype.Nodes[i].ParentIndex;
                Quaternion rotation = eulerAnimated[i] ? Quaternion.Euler(eulers[i]) : rotations[i].normalized;
                matrices[i] = (parent < 0 ? Matrix4x4.identity : matrices[parent]) * Matrix4x4.TRS(positions[i], rotation, scales[i]);
            }
        }
        internal Matrix4x4 GetMatrix(int node) => workingPose ? matrices[node] : restMatrices[node];
    }
}
