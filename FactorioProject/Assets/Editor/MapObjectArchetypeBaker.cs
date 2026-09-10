using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ProjectF.MapObjects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectF.Editor.MapObjects
{
    internal static class MapObjectArchetypeBaker
    {
        private const string ItemDefinitionRoot = "Assets/Data/Items";
        private const string OutputRoot = "Assets/Data/MapObjectArchetypes";
        private const string ArchetypePropertyName = "mapObjectArchetype";

        [MenuItem("Tools/ProjectF/Map Objects/Bake All Archetypes")]
        private static void BakeAllArchetypes()
        {
            List<ItemDefinition> definitions = FindAllMapObjectDefinitions();
            int created = 0;
            int updated = 0;
            int unchanged = 0;
            int failed = 0;

            try
            {
                EnsureFolder(OutputRoot);
                for (int i = 0; i < definitions.Count; i++)
                {
                    ItemDefinition definition = definitions[i];
                    EditorUtility.DisplayProgressBar(
                        "Map Object Archetype Bake",
                        definition != null ? definition.name : "Missing definition",
                        definitions.Count > 0 ? (float)i / definitions.Count : 1f);

                    try
                    {
                        BakeResult result = Bake(definition);
                        CountResult(result, ref created, ref updated, ref unchanged);
                    }
                    catch (Exception exception)
                    {
                        failed++;
                        Debug.LogError($"MapObject Archetype bake failed for '{definition?.name}': {exception}", definition);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"MapObject Archetype bake complete. Created={created}, Updated={updated}, "
                + $"Unchanged={unchanged}, Failed={failed}, Total={definitions.Count}.");
        }

        [MenuItem("Assets/ProjectF/Bake Selected Map Object Archetypes")]
        private static void BakeSelectedArchetypes()
        {
            List<ItemDefinition> definitions = CollectSelectedDefinitions();
            if (definitions.Count <= 0)
            {
                Debug.LogWarning("Select one or more ItemDefinition assets with a MapObject prefab.");
                return;
            }

            EnsureFolder(OutputRoot);
            int created = 0;
            int updated = 0;
            int unchanged = 0;
            for (int i = 0; i < definitions.Count; i++)
            {
                BakeResult result = Bake(definitions[i]);
                CountResult(result, ref created, ref updated, ref unchanged);
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Selected MapObject Archetype bake complete. Created={created}, "
                + $"Updated={updated}, Unchanged={unchanged}.");
        }

        [MenuItem("Assets/ProjectF/Bake Selected Map Object Archetypes", true)]
        private static bool CanBakeSelectedArchetypes()
        {
            UnityEngine.Object[] selection = Selection.objects;
            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] is ItemDefinition definition && definition.mapObject != null)
                {
                    return true;
                }
            }

            return false;
        }

        [MenuItem("Tools/ProjectF/Map Objects/Validate Archetypes")]
        private static void ValidateArchetypes()
        {
            List<ItemDefinition> definitions = FindAllMapObjectDefinitions();
            int missing = 0;
            int stale = 0;
            int valid = 0;
            int staticReady = 0;
            var itemIds = new Dictionary<int, ItemDefinition>();

            for (int i = 0; i < definitions.Count; i++)
            {
                ItemDefinition definition = definitions[i];
                if (itemIds.TryGetValue(definition.id, out ItemDefinition duplicate))
                {
                    Debug.LogError(
                        $"Duplicate ItemDefinition id {definition.id}: '{duplicate.name}' and '{definition.name}'.",
                        definition);
                }
                else
                {
                    itemIds.Add(definition.id, definition);
                }

                MapObjectArchetype archetype = definition.MapObjectArchetype;
                if (archetype == null)
                {
                    missing++;
                    Debug.LogWarning($"'{definition.name}' has no baked MapObject Archetype.", definition);
                    continue;
                }

                BakeData bakeData;
                try
                {
                    bakeData = BuildBakeData(definition);
                }
                catch (Exception exception)
                {
                    stale++;
                    Debug.LogError(
                        $"Could not validate '{definition.name}' MapObject Archetype: {exception}",
                        definition);
                    continue;
                }

                if (!archetype.MatchesSource(
                        definition,
                        definition.mapObject,
                        bakeData.PrefabGuid,
                        bakeData.ContentHash))
                {
                    stale++;
                    Debug.LogWarning($"'{definition.name}' has a stale MapObject Archetype.", archetype);
                    continue;
                }

                valid++;
                if (archetype.SupportsStaticVirtualRendering)
                {
                    staticReady++;
                }
            }

            Debug.Log(
                $"MapObject Archetype validation complete. Valid={valid}, StaticRenderReady={staticReady}, "
                + $"Missing={missing}, Stale={stale}, Total={definitions.Count}.");
        }

        private static BakeResult Bake(ItemDefinition definition)
        {
            if (definition == null || definition.mapObject == null)
            {
                throw new ArgumentException("A MapObject ItemDefinition is required.", nameof(definition));
            }

            BakeData bakeData = BuildBakeData(definition);
            MapObjectArchetype archetype = ResolveOrCreateArchetype(definition, out bool created);
            if (!created && archetype.MatchesSource(
                    definition,
                    definition.mapObject,
                    bakeData.PrefabGuid,
                    bakeData.ContentHash))
            {
                AssignArchetype(definition, archetype);
                return BakeResult.Unchanged;
            }

            archetype.ReplaceBakedData(
                definition,
                definition.mapObject,
                bakeData.PrefabGuid,
                bakeData.ContentHash,
                bakeData.SourceRuntimeType,
                definition.mapObject.Status,
                definition.mapObject.FocusMode,
                bakeData.RootScale,
                bakeData.RootRotation,
                bakeData.RenderBounds,
                bakeData.HasRenderBounds,
                bakeData.Features,
                bakeData.Nodes,
                bakeData.RenderParts,
                bakeData.AnimationClips);

            EditorUtility.SetDirty(archetype);
            AssignArchetype(definition, archetype);
            return created ? BakeResult.Created : BakeResult.Updated;
        }

        private static BakeData BuildBakeData(ItemDefinition definition)
        {
            MapObject source = definition.mapObject;
            Transform root = source.transform;
            string prefabPath = AssetDatabase.GetAssetPath(source.gameObject);
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                throw new InvalidOperationException(
                    $"'{definition.name}' MapObject is not stored in a project asset.");
            }

            var transforms = new List<Transform>(32);
            var parentIndices = new List<int>(32);
            CollectOwnedTransforms(root, source, -1, transforms, parentIndices);

            HashSet<string> animatedTransformPaths = CollectAnimatedTransformPaths(
                transforms,
                root,
                out AnimationClip[] clips);
            var nodes = new MapObjectVisualNodeDefinition[transforms.Count];
            Matrix4x4 rootInverse = root.worldToLocalMatrix;
            for (int i = 0; i < transforms.Count; i++)
            {
                Transform node = transforms[i];
                string animationPath = AnimationUtility.CalculateTransformPath(node, root);
                bool isRoot = node == root;
                nodes[i] = new MapObjectVisualNodeDefinition(
                    BuildStableHierarchyPath(node, root),
                    animationPath,
                    parentIndices[i],
                    isRoot ? Vector3.zero : node.localPosition,
                    isRoot ? Quaternion.identity : node.localRotation,
                    isRoot ? Vector3.one : node.localScale,
                    rootInverse * node.localToWorldMatrix,
                    node.gameObject.activeSelf,
                    animatedTransformPaths.Contains(animationPath));
            }

            MapObjectArchetypeFeatureFlags features = DetectFeatures(root, source);
            var renderParts = new List<MapObjectRenderPartDefinition>(32);
            bool hasBounds = false;
            Bounds aggregateBounds = default;
            CollectRenderParts(
                transforms,
                rootInverse,
                renderParts,
                ref aggregateBounds,
                ref hasBounds,
                ref features);

            MapObjectAnimationClipDefinition[] clipDefinitions = BuildClipDefinitions(clips);
            MapObjectRenderPartDefinition[] renderPartArray = renderParts.ToArray();
            return new BakeData
            {
                PrefabGuid = AssetDatabase.AssetPathToGUID(prefabPath),
                SourceRuntimeType = source.GetType().AssemblyQualifiedName,
                RootScale = root.localScale,
                RootRotation = root.localRotation,
                RenderBounds = aggregateBounds,
                HasRenderBounds = hasBounds,
                Features = features,
                Nodes = nodes,
                RenderParts = renderPartArray,
                AnimationClips = clipDefinitions,
                ContentHash = CalculateContentHash(
                    source,
                    root.localScale,
                    root.localRotation,
                    aggregateBounds,
                    hasBounds,
                    features,
                    nodes,
                    renderPartArray,
                    clipDefinitions)
            };
        }

        private static MapObjectArchetype ResolveOrCreateArchetype(
            ItemDefinition definition,
            out bool created)
        {
            MapObjectArchetype assigned = definition.MapObjectArchetype;
            if (assigned != null && assigned.ItemDefinition == definition)
            {
                created = false;
                return assigned;
            }

            string path = BuildArchetypePath(definition);
            MapObjectArchetype archetype = AssetDatabase.LoadAssetAtPath<MapObjectArchetype>(path);
            if (archetype != null && archetype.ItemDefinition != null && archetype.ItemDefinition != definition)
            {
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                archetype = null;
            }

            if (archetype != null)
            {
                created = false;
                return archetype;
            }

            archetype = ScriptableObject.CreateInstance<MapObjectArchetype>();
            AssetDatabase.CreateAsset(archetype, path);
            created = true;
            return archetype;
        }

        private static void AssignArchetype(ItemDefinition definition, MapObjectArchetype archetype)
        {
            if (definition.MapObjectArchetype == archetype)
            {
                return;
            }

            SerializedObject serializedDefinition = new SerializedObject(definition);
            SerializedProperty property = serializedDefinition.FindProperty(ArchetypePropertyName);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"ItemDefinition.{ArchetypePropertyName} serialized property was not found.");
            }

            property.objectReferenceValue = archetype;
            serializedDefinition.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
        }

        private static void CollectOwnedTransforms(
            Transform current,
            MapObject owner,
            int parentIndex,
            List<Transform> transforms,
            List<int> parentIndices)
        {
            if (current == null)
            {
                return;
            }

            if (current != owner.transform)
            {
                MapObject nestedMapObject = current.GetComponent<MapObject>();
                if (nestedMapObject != null && nestedMapObject != owner)
                {
                    return;
                }
            }

            int currentIndex = transforms.Count;
            transforms.Add(current);
            parentIndices.Add(parentIndex);
            for (int i = 0; i < current.childCount; i++)
            {
                CollectOwnedTransforms(current.GetChild(i), owner, currentIndex, transforms, parentIndices);
            }
        }

        private static HashSet<string> CollectAnimatedTransformPaths(
            List<Transform> transforms,
            Transform root,
            out AnimationClip[] clips)
        {
            var animatedPaths = new HashSet<string>(StringComparer.Ordinal);
            var uniqueClips = new HashSet<AnimationClip>();
            for (int nodeIndex = 0; nodeIndex < transforms.Count; nodeIndex++)
            {
                Transform animationRoot = transforms[nodeIndex];
                AnimationClip[] nodeClips = AnimationUtility.GetAnimationClips(animationRoot.gameObject);
                string animationRootPath = AnimationUtility.CalculateTransformPath(animationRoot, root);
                for (int clipIndex = 0; clipIndex < nodeClips.Length; clipIndex++)
                {
                    AnimationClip clip = nodeClips[clipIndex];
                    if (clip == null)
                    {
                        continue;
                    }

                    uniqueClips.Add(clip);
                    EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                    for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
                    {
                        EditorCurveBinding binding = bindings[bindingIndex];
                        if (binding.type == typeof(Transform))
                        {
                            animatedPaths.Add(CombineAnimationPath(animationRootPath, binding.path));
                        }
                    }
                }
            }

            clips = new AnimationClip[uniqueClips.Count];
            uniqueClips.CopyTo(clips);
            Array.Sort(clips, CompareAnimationClips);
            return animatedPaths;
        }

        private static string CombineAnimationPath(string rootPath, string bindingPath)
        {
            if (string.IsNullOrEmpty(rootPath))
            {
                return bindingPath ?? string.Empty;
            }

            return string.IsNullOrEmpty(bindingPath)
                ? rootPath
                : rootPath + "/" + bindingPath;
        }

        private static int CompareAnimationClips(AnimationClip left, AnimationClip right)
        {
            string leftPath = left != null ? AssetDatabase.GetAssetPath(left) : string.Empty;
            string rightPath = right != null ? AssetDatabase.GetAssetPath(right) : string.Empty;
            int pathComparison = string.CompareOrdinal(leftPath, rightPath);
            return pathComparison != 0
                ? pathComparison
                : string.CompareOrdinal(left != null ? left.name : string.Empty, right != null ? right.name : string.Empty);
        }

        private static MapObjectAnimationClipDefinition[] BuildClipDefinitions(AnimationClip[] clips)
        {
            var result = new MapObjectAnimationClipDefinition[clips.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                int transformCurveCount = 0;
                EditorCurveBinding[] curveBindings = AnimationUtility.GetCurveBindings(clip);
                for (int bindingIndex = 0; bindingIndex < curveBindings.Length; bindingIndex++)
                {
                    if (curveBindings[bindingIndex].type == typeof(Transform))
                    {
                        transformCurveCount++;
                    }
                }

                int objectReferenceCurveCount = AnimationUtility.GetObjectReferenceCurveBindings(clip).Length;
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                result[i] = new MapObjectAnimationClipDefinition(
                    clip,
                    clip.length,
                    clip.frameRate,
                    settings.loopTime,
                    transformCurveCount,
                    objectReferenceCurveCount);
            }

            return result;
        }

        private static void CollectRenderParts(
            List<Transform> transforms,
            Matrix4x4 rootInverse,
            List<MapObjectRenderPartDefinition> destination,
            ref Bounds aggregateBounds,
            ref bool hasBounds,
            ref MapObjectArchetypeFeatureFlags features)
        {
            for (int nodeIndex = 0; nodeIndex < transforms.Count; nodeIndex++)
            {
                Transform node = transforms[nodeIndex];
                Renderer[] renderers = node.GetComponents<Renderer>();
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    Renderer renderer = renderers[rendererIndex];
                    if (renderer is MeshRenderer meshRenderer)
                    {
                        MeshFilter meshFilter = node.GetComponent<MeshFilter>();
                        Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                        if (mesh == null)
                        {
                            features |= MapObjectArchetypeFeatureFlags.UnsupportedRenderer;
                            continue;
                        }

                        AppendRendererParts(
                            MapObjectRenderPartKind.Mesh,
                            nodeIndex,
                            mesh,
                            meshRenderer,
                            mesh.bounds,
                            rootInverse * node.localToWorldMatrix,
                            destination,
                            ref aggregateBounds,
                            ref hasBounds);
                    }
                    else if (renderer is SkinnedMeshRenderer skinnedRenderer)
                    {
                        features |= MapObjectArchetypeFeatureFlags.SkinnedMesh;
                        if (skinnedRenderer.sharedMesh == null)
                        {
                            continue;
                        }

                        AppendRendererParts(
                            MapObjectRenderPartKind.SkinnedMesh,
                            nodeIndex,
                            skinnedRenderer.sharedMesh,
                            skinnedRenderer,
                            skinnedRenderer.localBounds,
                            rootInverse * node.localToWorldMatrix,
                            destination,
                            ref aggregateBounds,
                            ref hasBounds);
                    }
                    else if (!(renderer is ParticleSystemRenderer)
                             && !(renderer is LineRenderer)
                             && !(renderer is TrailRenderer))
                    {
                        features |= MapObjectArchetypeFeatureFlags.UnsupportedRenderer;
                    }
                }
            }
        }

        private static void AppendRendererParts(
            MapObjectRenderPartKind kind,
            int nodeIndex,
            Mesh mesh,
            Renderer renderer,
            Bounds localBounds,
            Matrix4x4 localToRoot,
            List<MapObjectRenderPartDefinition> destination,
            ref Bounds aggregateBounds,
            ref bool hasBounds)
        {
            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length <= 0 || mesh.subMeshCount <= 0)
            {
                return;
            }

            int renderPassCount = Mathf.Max(mesh.subMeshCount, materials.Length);
            for (int passIndex = 0; passIndex < renderPassCount; passIndex++)
            {
                int subMeshIndex = Mathf.Min(passIndex, mesh.subMeshCount - 1);
                Material material = materials[Mathf.Min(passIndex, materials.Length - 1)];
                if (material == null)
                {
                    continue;
                }

                destination.Add(new MapObjectRenderPartDefinition(
                    kind,
                    nodeIndex,
                    mesh,
                    material,
                    subMeshIndex,
                    localBounds,
                    renderer.gameObject.layer,
                    renderer.renderingLayerMask,
                    renderer.shadowCastingMode,
                    renderer.receiveShadows,
                    renderer.enabled && !renderer.forceRenderingOff));
            }

            Bounds rootBounds = TransformBounds(localBounds, localToRoot);
            if (!hasBounds)
            {
                aggregateBounds = rootBounds;
                hasBounds = true;
            }
            else
            {
                aggregateBounds.Encapsulate(rootBounds);
            }
        }

        private static MapObjectArchetypeFeatureFlags DetectFeatures(Transform root, MapObject owner)
        {
            MapObjectArchetypeFeatureFlags result = MapObjectArchetypeFeatureFlags.None;
            if (HasOwnedComponent<Animator>(root, owner)) result |= MapObjectArchetypeFeatureFlags.Animator;
            if (HasOwnedComponent<Animation>(root, owner)) result |= MapObjectArchetypeFeatureFlags.LegacyAnimation;
            if (HasOwnedComponent<SkinnedMeshRenderer>(root, owner)) result |= MapObjectArchetypeFeatureFlags.SkinnedMesh;
            if (HasOwnedComponent<ParticleSystem>(root, owner)) result |= MapObjectArchetypeFeatureFlags.ParticleSystem;
            if (HasOwnedComponent<Light>(root, owner)) result |= MapObjectArchetypeFeatureFlags.Light;
            if (HasOwnedComponent<AudioSource>(root, owner)) result |= MapObjectArchetypeFeatureFlags.AudioSource;
            if (HasOwnedComponent<Collider>(root, owner)) result |= MapObjectArchetypeFeatureFlags.Collider;
            if (HasOwnedComponent<Rigidbody>(root, owner)) result |= MapObjectArchetypeFeatureFlags.Rigidbody;
            if (HasOwnedComponent<LineRenderer>(root, owner)) result |= MapObjectArchetypeFeatureFlags.LineRenderer;
            if (HasOwnedComponent<TrailRenderer>(root, owner)) result |= MapObjectArchetypeFeatureFlags.TrailRenderer;
            if (HasOwnedComponent<LODGroup>(root, owner)) result |= MapObjectArchetypeFeatureFlags.LodGroup;
            return result;
        }

        private static bool HasOwnedComponent<T>(Transform root, MapObject owner) where T : Component
        {
            T[] components = root.GetComponentsInChildren<T>(true);
            for (int i = 0; i < components.Length; i++)
            {
                T component = components[i];
                MapObject nearestMapObject = component.GetComponentInParent<MapObject>(true);
                if (nearestMapObject == owner)
                {
                    return true;
                }
            }

            return false;
        }

        private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
        {
            Vector3 center = matrix.MultiplyPoint3x4(bounds.center);
            Vector3 extents = bounds.extents;
            Vector3 axisX = matrix.MultiplyVector(new Vector3(extents.x, 0f, 0f));
            Vector3 axisY = matrix.MultiplyVector(new Vector3(0f, extents.y, 0f));
            Vector3 axisZ = matrix.MultiplyVector(new Vector3(0f, 0f, extents.z));
            Vector3 worldExtents = new Vector3(
                Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
                Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
                Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z));
            return new Bounds(center, worldExtents * 2f);
        }

        private static Hash128 CalculateContentHash(
            MapObject source,
            Vector3 rootScale,
            Quaternion rootRotation,
            Bounds renderBounds,
            bool hasRenderBounds,
            MapObjectArchetypeFeatureFlags features,
            MapObjectVisualNodeDefinition[] nodes,
            MapObjectRenderPartDefinition[] renderParts,
            MapObjectAnimationClipDefinition[] animationClips)
        {
            var builder = new StringBuilder(4096);
            Append(builder, MapObjectArchetype.CurrentSchemaVersion);
            Append(builder, source.GetType().AssemblyQualifiedName);
            Append(builder, source.Status.mapSizeX);
            Append(builder, source.Status.mapSizeY);
            Append(builder, source.Status.centerCellX);
            Append(builder, source.Status.centerCellY);
            Append(builder, (int)source.FocusMode);
            Append(builder, rootScale);
            Append(builder, rootRotation);
            Append(builder, hasRenderBounds);
            Append(builder, renderBounds);
            Append(builder, (int)features);

            Append(builder, nodes.Length);
            for (int i = 0; i < nodes.Length; i++)
            {
                MapObjectVisualNodeDefinition node = nodes[i];
                Append(builder, node.HierarchyPath);
                Append(builder, node.AnimationPath);
                Append(builder, node.ParentIndex);
                Append(builder, node.LocalPosition);
                Append(builder, node.LocalRotation);
                Append(builder, node.LocalScale);
                Append(builder, node.DefaultLocalToRoot);
                Append(builder, node.ActiveByDefault);
                Append(builder, node.HasAnimatedTransform);
            }

            Append(builder, renderParts.Length);
            for (int i = 0; i < renderParts.Length; i++)
            {
                MapObjectRenderPartDefinition part = renderParts[i];
                Append(builder, (int)part.Kind);
                Append(builder, part.NodeIndex);
                AppendAssetIdentity(builder, part.Mesh);
                AppendAssetIdentity(builder, part.Material);
                Append(builder, part.SubMeshIndex);
                Append(builder, part.LocalBounds);
                Append(builder, part.Layer);
                Append(builder, part.RenderingLayerMask);
                Append(builder, (int)part.ShadowCastingMode);
                Append(builder, part.ReceiveShadows);
                Append(builder, part.EnabledByDefault);
            }

            Append(builder, animationClips.Length);
            for (int i = 0; i < animationClips.Length; i++)
            {
                MapObjectAnimationClipDefinition clipDefinition = animationClips[i];
                AnimationClip clip = clipDefinition.Clip;
                AppendAssetIdentity(builder, clip);
                Append(builder, clipDefinition.LengthSeconds);
                Append(builder, clipDefinition.FrameRate);
                Append(builder, clipDefinition.Looping);
                Append(builder, clipDefinition.TransformCurveCount);
                Append(builder, clipDefinition.ObjectReferenceCurveCount);

                string clipPath = clip != null ? AssetDatabase.GetAssetPath(clip) : string.Empty;
                Append(builder, string.IsNullOrWhiteSpace(clipPath)
                    ? default
                    : AssetDatabase.GetAssetDependencyHash(clipPath));
            }

            return Hash128.Compute(builder.ToString());
        }

        private static void AppendAssetIdentity(StringBuilder builder, UnityEngine.Object asset)
        {
            if (asset != null
                && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId))
            {
                Append(builder, guid);
                Append(builder, localId);
                return;
            }

            Append(builder, string.Empty);
            Append(builder, 0L);
        }

        private static void Append(StringBuilder builder, string value)
        {
            builder.Append(value ?? string.Empty).Append('|');
        }

        private static void Append(StringBuilder builder, bool value)
        {
            builder.Append(value ? '1' : '0').Append('|');
        }

        private static void Append(StringBuilder builder, int value)
        {
            builder.Append(value.ToString(CultureInfo.InvariantCulture)).Append('|');
        }

        private static void Append(StringBuilder builder, uint value)
        {
            builder.Append(value.ToString(CultureInfo.InvariantCulture)).Append('|');
        }

        private static void Append(StringBuilder builder, long value)
        {
            builder.Append(value.ToString(CultureInfo.InvariantCulture)).Append('|');
        }

        private static void Append(StringBuilder builder, float value)
        {
            builder.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
        }

        private static void Append(StringBuilder builder, Hash128 value)
        {
            Append(builder, value.ToString());
        }

        private static void Append(StringBuilder builder, Vector3 value)
        {
            Append(builder, value.x);
            Append(builder, value.y);
            Append(builder, value.z);
        }

        private static void Append(StringBuilder builder, Quaternion value)
        {
            Append(builder, value.x);
            Append(builder, value.y);
            Append(builder, value.z);
            Append(builder, value.w);
        }

        private static void Append(StringBuilder builder, Bounds value)
        {
            Append(builder, value.center);
            Append(builder, value.size);
        }

        private static void Append(StringBuilder builder, Matrix4x4 value)
        {
            for (int i = 0; i < 16; i++)
            {
                Append(builder, value[i]);
            }
        }

        private static string BuildStableHierarchyPath(Transform node, Transform root)
        {
            if (node == root)
            {
                return "0:" + root.name;
            }

            var segments = new List<string>(8);
            Transform current = node;
            while (current != null && current != root)
            {
                segments.Add(current.GetSiblingIndex() + ":" + current.name);
                current = current.parent;
            }

            segments.Add("0:" + root.name);
            segments.Reverse();
            return string.Join("/", segments);
        }

        private static List<ItemDefinition> FindAllMapObjectDefinitions()
        {
            string[] guids = AssetDatabase.FindAssets("t:ItemDefinition", new[] { ItemDefinitionRoot });
            var result = new List<ItemDefinition>(guids.Length);
            for (int i = 0; i < guids.Length; i++)
            {
                ItemDefinition definition = AssetDatabase.LoadAssetAtPath<ItemDefinition>(
                    AssetDatabase.GUIDToAssetPath(guids[i]));
                if (definition != null && definition.mapObject != null)
                {
                    result.Add(definition);
                }
            }

            result.Sort(CompareDefinitions);
            return result;
        }

        private static List<ItemDefinition> CollectSelectedDefinitions()
        {
            UnityEngine.Object[] selection = Selection.objects;
            var result = new List<ItemDefinition>(selection.Length);
            var seen = new HashSet<ItemDefinition>();
            for (int i = 0; i < selection.Length; i++)
            {
                if (selection[i] is ItemDefinition definition
                    && definition.mapObject != null
                    && seen.Add(definition))
                {
                    result.Add(definition);
                }
            }

            result.Sort(CompareDefinitions);
            return result;
        }

        private static int CompareDefinitions(ItemDefinition left, ItemDefinition right)
        {
            int idComparison = left.id.CompareTo(right.id);
            return idComparison != 0
                ? idComparison
                : string.CompareOrdinal(AssetDatabase.GetAssetPath(left), AssetDatabase.GetAssetPath(right));
        }

        private static string BuildArchetypePath(ItemDefinition definition)
        {
            string safeName = SanitizeFileName(
                !string.IsNullOrWhiteSpace(definition.itemName) ? definition.itemName : definition.name);
            return $"{OutputRoot}/MapObjectArchetype_{definition.id}_{safeName}.asset";
        }

        private static string SanitizeFileName(string value)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            var invalid = new HashSet<char>(invalidCharacters);
            char[] characters = value.ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                if (invalid.Contains(characters[i]) || characters[i] == '/')
                {
                    characters[i] = '_';
                }
            }

            return new string(characters).Trim();
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string[] segments = folderPath.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segments[i]);
                }

                current = next;
            }
        }

        private static void CountResult(
            BakeResult result,
            ref int created,
            ref int updated,
            ref int unchanged)
        {
            switch (result)
            {
                case BakeResult.Created:
                    created++;
                    break;
                case BakeResult.Updated:
                    updated++;
                    break;
                default:
                    unchanged++;
                    break;
            }
        }

        private enum BakeResult
        {
            Unchanged,
            Created,
            Updated
        }

        private sealed class BakeData
        {
            internal string PrefabGuid;
            internal Hash128 ContentHash;
            internal string SourceRuntimeType;
            internal Vector3 RootScale;
            internal Quaternion RootRotation;
            internal Bounds RenderBounds;
            internal bool HasRenderBounds;
            internal MapObjectArchetypeFeatureFlags Features;
            internal MapObjectVisualNodeDefinition[] Nodes;
            internal MapObjectRenderPartDefinition[] RenderParts;
            internal MapObjectAnimationClipDefinition[] AnimationClips;
        }
    }
}
