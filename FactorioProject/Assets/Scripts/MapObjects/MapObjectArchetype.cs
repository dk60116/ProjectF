using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectF.MapObjects
{
    [Flags]
    public enum MapObjectArchetypeFeatureFlags
    {
        None = 0,
        Animator = 1 << 0,
        LegacyAnimation = 1 << 1,
        SkinnedMesh = 1 << 2,
        ParticleSystem = 1 << 3,
        Light = 1 << 4,
        AudioSource = 1 << 5,
        Collider = 1 << 6,
        Rigidbody = 1 << 7,
        LineRenderer = 1 << 8,
        TrailRenderer = 1 << 9,
        LodGroup = 1 << 10,
        UnsupportedRenderer = 1 << 11
    }

    public enum MapObjectRenderPartKind : byte
    {
        Mesh = 0,
        SkinnedMesh = 1
    }

    [Serializable]
    public struct MapObjectVisualNodeDefinition
    {
        [SerializeField] private string hierarchyPath;
        [SerializeField] private string animationPath;
        [SerializeField] private int parentIndex;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Quaternion localRotation;
        [SerializeField] private Vector3 localScale;
        [SerializeField] private Matrix4x4 defaultLocalToRoot;
        [SerializeField] private bool activeByDefault;
        [SerializeField] private bool hasAnimatedTransform;

        public string HierarchyPath => hierarchyPath;
        public string AnimationPath => animationPath;
        public int ParentIndex => parentIndex;
        public Vector3 LocalPosition => localPosition;
        public Quaternion LocalRotation => localRotation;
        public Vector3 LocalScale => localScale;
        public Matrix4x4 DefaultLocalToRoot => defaultLocalToRoot;
        public bool ActiveByDefault => activeByDefault;
        public bool HasAnimatedTransform => hasAnimatedTransform;

        public MapObjectVisualNodeDefinition(
            string hierarchyPath,
            string animationPath,
            int parentIndex,
            Vector3 localPosition,
            Quaternion localRotation,
            Vector3 localScale,
            Matrix4x4 defaultLocalToRoot,
            bool activeByDefault,
            bool hasAnimatedTransform)
        {
            this.hierarchyPath = hierarchyPath;
            this.animationPath = animationPath;
            this.parentIndex = parentIndex;
            this.localPosition = localPosition;
            this.localRotation = localRotation;
            this.localScale = localScale;
            this.defaultLocalToRoot = defaultLocalToRoot;
            this.activeByDefault = activeByDefault;
            this.hasAnimatedTransform = hasAnimatedTransform;
        }
    }

    [Serializable]
    public struct MapObjectRenderPartDefinition
    {
        [SerializeField] private MapObjectRenderPartKind kind;
        [SerializeField] private int nodeIndex;
        [SerializeField] private Mesh mesh;
        [SerializeField] private Material material;
        [SerializeField] private int subMeshIndex;
        [SerializeField] private Bounds localBounds;
        [SerializeField] private int layer;
        [SerializeField] private uint renderingLayerMask;
        [SerializeField] private ShadowCastingMode shadowCastingMode;
        [SerializeField] private bool receiveShadows;
        [SerializeField] private bool enabledByDefault;

        public MapObjectRenderPartKind Kind => kind;
        public int NodeIndex => nodeIndex;
        public Mesh Mesh => mesh;
        public Material Material => material;
        public int SubMeshIndex => subMeshIndex;
        public Bounds LocalBounds => localBounds;
        public int Layer => layer;
        public uint RenderingLayerMask => renderingLayerMask;
        public ShadowCastingMode ShadowCastingMode => shadowCastingMode;
        public bool ReceiveShadows => receiveShadows;
        public bool EnabledByDefault => enabledByDefault;

        public MapObjectRenderPartDefinition(
            MapObjectRenderPartKind kind,
            int nodeIndex,
            Mesh mesh,
            Material material,
            int subMeshIndex,
            Bounds localBounds,
            int layer,
            uint renderingLayerMask,
            ShadowCastingMode shadowCastingMode,
            bool receiveShadows,
            bool enabledByDefault)
        {
            this.kind = kind;
            this.nodeIndex = nodeIndex;
            this.mesh = mesh;
            this.material = material;
            this.subMeshIndex = subMeshIndex;
            this.localBounds = localBounds;
            this.layer = layer;
            this.renderingLayerMask = renderingLayerMask;
            this.shadowCastingMode = shadowCastingMode;
            this.receiveShadows = receiveShadows;
            this.enabledByDefault = enabledByDefault;
        }
    }

    [Serializable]
    public struct MapObjectAnimationClipDefinition
    {
        [SerializeField] private AnimationClip clip;
        [SerializeField] private float lengthSeconds;
        [SerializeField] private float frameRate;
        [SerializeField] private bool looping;
        [SerializeField] private int transformCurveCount;
        [SerializeField] private int objectReferenceCurveCount;

        public AnimationClip Clip => clip;
        public float LengthSeconds => lengthSeconds;
        public float FrameRate => frameRate;
        public bool Looping => looping;
        public int TransformCurveCount => transformCurveCount;
        public int ObjectReferenceCurveCount => objectReferenceCurveCount;

        public MapObjectAnimationClipDefinition(
            AnimationClip clip,
            float lengthSeconds,
            float frameRate,
            bool looping,
            int transformCurveCount,
            int objectReferenceCurveCount)
        {
            this.clip = clip;
            this.lengthSeconds = lengthSeconds;
            this.frameRate = frameRate;
            this.looping = looping;
            this.transformCurveCount = transformCurveCount;
            this.objectReferenceCurveCount = objectReferenceCurveCount;
        }
    }

    public sealed class MapObjectArchetype : ScriptableObject
    {
        public const int CurrentSchemaVersion = 1;

        [SerializeField] private int schemaVersion;
        [SerializeField] private ItemDefinition itemDefinition;
        [SerializeField] private MapObject sourcePrefab;
        [SerializeField] private int itemId = -1;
        [SerializeField] private string itemName;
        [SerializeField] private string sourcePrefabGuid;
        [SerializeField] private Hash128 sourceContentHash;
        [SerializeField] private string sourceRuntimeType;
        [SerializeField] private MapObject.MapObjectStatus mapStatus;
        [SerializeField] private MapObject.MultiFocusMode focusMode;
        [SerializeField] private Vector3 defaultRootScale = Vector3.one;
        [SerializeField] private Quaternion defaultRootRotation = Quaternion.identity;
        [SerializeField] private Bounds localRenderBounds;
        [SerializeField] private bool hasRenderBounds;
        [SerializeField] private MapObjectArchetypeFeatureFlags featureFlags;
        [SerializeField] private MapObjectVisualNodeDefinition[] nodes = Array.Empty<MapObjectVisualNodeDefinition>();
        [SerializeField] private MapObjectRenderPartDefinition[] renderParts = Array.Empty<MapObjectRenderPartDefinition>();
        [SerializeField] private MapObjectAnimationClipDefinition[] animationClips = Array.Empty<MapObjectAnimationClipDefinition>();

        public int SchemaVersion => schemaVersion;
        public ItemDefinition ItemDefinition => itemDefinition;
        public MapObject SourcePrefab => sourcePrefab;
        public int ItemId => itemId;
        public string ItemName => itemName;
        public string SourcePrefabGuid => sourcePrefabGuid;
        public Hash128 SourceContentHash => sourceContentHash;
        public string SourceRuntimeType => sourceRuntimeType;
        public MapObject.MapObjectStatus MapStatus => mapStatus;
        public MapObject.MultiFocusMode FocusMode => focusMode;
        public Vector3 DefaultRootScale => defaultRootScale;
        public Quaternion DefaultRootRotation => defaultRootRotation;
        public Bounds LocalRenderBounds => localRenderBounds;
        public bool HasRenderBounds => hasRenderBounds;
        public MapObjectArchetypeFeatureFlags FeatureFlags => featureFlags;
        public IReadOnlyList<MapObjectVisualNodeDefinition> Nodes => nodes;
        public IReadOnlyList<MapObjectRenderPartDefinition> RenderParts => renderParts;
        public IReadOnlyList<MapObjectAnimationClipDefinition> AnimationClips => animationClips;

        public bool SupportsStaticVirtualRendering =>
            renderParts != null
            && renderParts.Length > 0
            && (featureFlags & (MapObjectArchetypeFeatureFlags.Animator
                                | MapObjectArchetypeFeatureFlags.LegacyAnimation
                                | MapObjectArchetypeFeatureFlags.SkinnedMesh
                                | MapObjectArchetypeFeatureFlags.ParticleSystem
                                | MapObjectArchetypeFeatureFlags.Light
                                | MapObjectArchetypeFeatureFlags.AudioSource
                                | MapObjectArchetypeFeatureFlags.LineRenderer
                                | MapObjectArchetypeFeatureFlags.TrailRenderer
                                | MapObjectArchetypeFeatureFlags.LodGroup
                                | MapObjectArchetypeFeatureFlags.UnsupportedRenderer)) == 0;

        public bool MatchesSource(
            ItemDefinition definition,
            MapObject prefab,
            string prefabGuid,
            Hash128 contentHash)
        {
            return schemaVersion == CurrentSchemaVersion
                   && itemDefinition == definition
                   && sourcePrefab == prefab
                   && itemId == (definition != null ? definition.id : -1)
                   && itemName == (definition != null ? definition.itemName : string.Empty)
                   && sourcePrefabGuid == prefabGuid
                   && sourceContentHash.Equals(contentHash);
        }

        public void ReplaceBakedData(
            ItemDefinition definition,
            MapObject prefab,
            string prefabGuid,
            Hash128 contentHash,
            string runtimeType,
            MapObject.MapObjectStatus footprint,
            MapObject.MultiFocusMode objectFocusMode,
            Vector3 rootScale,
            Quaternion rootRotation,
            Bounds renderBounds,
            bool containsRenderBounds,
            MapObjectArchetypeFeatureFlags features,
            MapObjectVisualNodeDefinition[] visualNodes,
            MapObjectRenderPartDefinition[] visualRenderParts,
            MapObjectAnimationClipDefinition[] clips)
        {
            schemaVersion = CurrentSchemaVersion;
            itemDefinition = definition;
            sourcePrefab = prefab;
            itemId = definition != null ? definition.id : -1;
            itemName = definition != null ? definition.itemName : string.Empty;
            sourcePrefabGuid = prefabGuid ?? string.Empty;
            sourceContentHash = contentHash;
            sourceRuntimeType = runtimeType ?? string.Empty;
            mapStatus = footprint;
            focusMode = objectFocusMode;
            defaultRootScale = rootScale;
            defaultRootRotation = rootRotation;
            localRenderBounds = renderBounds;
            hasRenderBounds = containsRenderBounds;
            featureFlags = features;
            nodes = visualNodes ?? Array.Empty<MapObjectVisualNodeDefinition>();
            renderParts = visualRenderParts ?? Array.Empty<MapObjectRenderPartDefinition>();
            animationClips = clips ?? Array.Empty<MapObjectAnimationClipDefinition>();
        }
    }
}
