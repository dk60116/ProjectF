using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Immutable prefab hierarchy plus reusable matrix scratch, shared by all entities of this type.
internal sealed class RobotArmRenderTemplate
{
    private sealed class Node
    {
        internal int Parent;
        internal string Path;
        internal Vector3 Position, Scale;
        internal Quaternion Rotation;
        internal Mesh Mesh;
        internal Material[] Materials;
        internal int Layer;
        internal ShadowCastingMode Shadows;
        internal bool ReceiveShadows;
        internal RobotArmAnimationCurves.Track? Pick, Drop;
    }
    private readonly Node[] nodes;
    private readonly Matrix4x4[] matrices;
    private readonly int bodyIndex, handIndex;
    private readonly Vector3 rootScale;
    internal float CullDiameter { get; private set; } = 3f;
    internal int LayerMask { get; private set; }
    internal Quaternion BodyRotation => bodyIndex >= 0 ? nodes[bodyIndex].Rotation : Quaternion.identity;
    internal RobotArmRenderTemplate(RobotArm source)
    {
        rootScale = source.transform.localScale;
        var transforms = source.GetComponentsInChildren<Transform>(true);
        var indices = new Dictionary<Transform, int>();
        var list = new List<Node>();
        bool isLong = source.UseLongArmAnimation;
        var pick = isLong ? RobotArmAnimationCurves.LongPick : RobotArmAnimationCurves.ShortPick;
        var drop = isLong ? RobotArmAnimationCurves.LongDrop : RobotArmAnimationCurves.ShortDrop;
        bodyIndex = handIndex = -1;
        foreach (var t in transforms)
        {
            int parent = t == source.transform ? -1 : indices[t.parent];
            var n = new Node { Parent = parent, Path = parent < 0 ? "" : list[parent].Path.Length == 0 ? t.name : list[parent].Path + "/" + t.name,
                Position = parent < 0 ? Vector3.zero : t.localPosition, Rotation = parent < 0 ? Quaternion.identity : t.localRotation,
                Scale = parent < 0 ? Vector3.one : t.localScale, Layer = t.gameObject.layer };
            foreach (var track in pick) if (track.Path == n.Path) n.Pick = track;
            foreach (var track in drop) if (track.Path == n.Path) n.Drop = track;
            if (t == source.RuntimeBodyTemplate) bodyIndex = list.Count;
            if (t == source.RuntimeHandTemplate) handIndex = list.Count;
            var r = t.GetComponent<MeshRenderer>();
            var filter = t.GetComponent<MeshFilter>();
            bool active = true;
            for (var a = t; a != null && a != source.transform; a = a.parent) active &= a.gameObject.activeSelf;
            if (r != null && filter != null && r.enabled && active && t.GetComponentInParent<PortableObject>() == null)
            {
                n.Mesh = filter.sharedMesh; n.Materials = r.sharedMaterials; n.Shadows = r.shadowCastingMode; n.ReceiveShadows = r.receiveShadows;
                LayerMask |= 1 << n.Layer;
                foreach (var mat in n.Materials) if (mat != null) mat.enableInstancing = true;
            }
            indices.Add(t, list.Count);
            list.Add(n);
        }
        nodes = list.ToArray();
        matrices = new Matrix4x4[nodes.Length];
        foreach (var t in transforms)
        {
            float radius = source.transform.InverseTransformPoint(t.position).magnitude * Mathf.Max(rootScale.x, rootScale.y, rootScale.z);
            CullDiameter = Mathf.Max(CullDiameter, 2f * radius + 2f);
        }
    }
    private void Evaluate(RobotArmInstance arm)
    {
        Matrix4x4 root = Matrix4x4.TRS(arm.WorldPosition, arm.WorldRotation, rootScale);
        for (int i = 0; i < nodes.Length; i++)
        {
            Node n = nodes[i];
            matrices[i] = (n.Parent < 0 ? root : matrices[n.Parent]) *
                Matrix4x4.TRS(n.Position, EvaluateRotation(arm, n, i), n.Scale);
        }
    }
    private Quaternion EvaluateRotation(RobotArmInstance arm, Node node, int index)
    {
        if (index == bodyIndex) return arm.BodyRotation;
        var track = arm.AnimationKind == 1 ? node.Pick : arm.AnimationKind == 2 ? node.Drop : null;
        return track.HasValue && arm.AnimationTime < 1f ? track.Value.Evaluate(arm.AnimationTime) : node.Rotation;
    }
    private Matrix4x4 EvaluateChain(RobotArmInstance arm, int index)
    {
        if (index < 0) return Matrix4x4.TRS(arm.WorldPosition, arm.WorldRotation, rootScale);
        Node n = nodes[index];
        return EvaluateChain(arm, n.Parent) * Matrix4x4.TRS(n.Position, EvaluateRotation(arm, n, index), n.Scale);
    }
    internal Vector3 BodyWorld(RobotArmInstance arm) => EvaluateChain(arm, bodyIndex).MultiplyPoint3x4(Vector3.zero);
    internal Vector3 HandWorld(RobotArmInstance arm) => EvaluateChain(arm, handIndex >= 0 ? handIndex : 0).MultiplyPoint3x4(Vector3.zero);
    internal int Append(RobotArmInstance arm, VirtualRenderBatchCollection batches)
    {
        Evaluate(arm);
        int count = 0;
        int x = Mathf.FloorToInt(arm.WorldPosition.x / 8f), z = Mathf.FloorToInt(arm.WorldPosition.z / 8f);
        bool tint = GameManager.Instance != null && GameManager.Instance.ShowSleepAwake && arm.IsRuntimeSleeping;
        for (int i = 0; i < nodes.Length; i++)
        {
            Node n = nodes[i];
            if (n.Mesh == null || n.Materials == null) continue;
            for (int sub = 0; sub < Mathf.Min(n.Mesh.subMeshCount, n.Materials.Length); sub++)
            {
                if (n.Materials[sub] == null) continue;
                batches.AddMatrix(new VirtualRenderBatchKey(n.Mesh, n.Materials[sub], n.Layer, sub,
                    n.Shadows, n.ReceiveShadows, false, tint, false, default, 0, x, z), matrices[i]);
                count++;
            }
        }
        if (arm.HasHeldItem && GameManager.Instance != null && GameManager.Instance.ItemManger != null &&
            GameManager.Instance.ItemManger.TryGetItemSetById(arm.HeldItemId, out var item) && item.portableMesh != null && item.portableMat != null)
        {
            item.portableMat.enableInstancing = true;
            var matrix = matrices[handIndex >= 0 ? handIndex : 0];
            Vector3 position = arm.ItemPresentationPosition(matrix.MultiplyPoint3x4(Vector3.zero));
            matrix.SetColumn(3, new Vector4(position.x, position.y, position.z, 1f));
            batches.AddMatrix(new VirtualRenderBatchKey(item.portableMesh, item.portableMat, nodes[0].Layer, 0,
                ShadowCastingMode.On, true, false, tint, false, default, 0, x, z), matrix);
            count++;
        }
        return count;
    }
}
