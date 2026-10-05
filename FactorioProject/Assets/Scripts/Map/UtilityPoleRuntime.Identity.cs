using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;

namespace ProjectF.Power
{
    // A prefab component is used only for authoring/temporary placement previews.
    // Installed poles retain numerical terminals and a shared immutable render template.
    public sealed partial class UtilityPoleRuntime
    {
        private readonly global::UtilityPole presentation;
        public global::UtilityPole Prototype { get; }
        public BlockStateStore.InstallationSaveState Placement { get; }
        public MapObjectHandle Handle { get; }
        internal UtilityPoleWorld World { get; }
        internal UtilityPoleRenderTemplate Template { get; }
        internal bool Registered;
        internal int OrderIndex;
        private float presentationScale = 1f;
        private bool presentationSuppressed;
        internal float PlacementPresentationScale
        {
            get => presentationScale;
            set { if (presentationScale == value) return; presentationScale = value; RefreshUtilityPoleWires(); }
        }
        internal bool PlacementPresentationSuppressed
        {
            get => presentationSuppressed;
            set
            {
                if (presentationSuppressed == value) return;
                presentationSuppressed = value; RefreshUtilityPoleWires();
                connectionLineVisualsDirty = true; RequestDeferredConnectionLineVisualRefresh();
            }
        }
        public Vector2Int StorageKey => BlockStateStore.GetInstallationStorageKey(Placement);
        public Vector2Int AnchorCoordinate => Placement != null ? Placement.anchorCoordinate : presentation.RuntimeAnchorCoordinate;
        public Vector2Int RuntimeAnchorCoordinate => AnchorCoordinate;
        public int RuntimeQuarterTurns => Placement != null ? Placement.quarterTurns : presentation.RuntimeQuarterTurns;
        public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => Placement != null ? Placement.occupiedCoordinates : presentation.RuntimeOccupiedCoordinates;
        public long SimulationId => Placement != null ? Placement.placementSequence : presentation.SimulationId;
        public Vector3 WorldPosition => Placement != null ? Placement.worldPosition : presentation != null ? presentation.transform.position : Vector3.zero;
        public Quaternion WorldRotation => Placement != null ? Placement.worldRotation : presentation != null ? presentation.transform.rotation : Quaternion.identity;
        public Matrix4x4 RootMatrix { get; }
        public Bounds CullBounds { get; }
        public bool IsRuntimeActive => Placement != null ? Registered && VirtualObjectWorld.Current != null && VirtualObjectWorld.Current.IsHandleAlive(Handle)
            : presentation != null && presentation.isActiveAndEnabled && presentation.gameObject.activeInHierarchy;
        // Placement previews disable their components but retain an active presentation object.
        internal bool IsPreviewPresentationActive => presentation != null && presentation.gameObject.activeInHierarchy;
        public bool IsTargetActive => IsRuntimeActive;
        public MapObject SceneObject => presentation;
        public string ObjectName => Prototype.ObjectName;
        public bool AllowsFocus => Prototype.AllowsFocus;
        public bool AllowsAnimalTraversal => Prototype.AllowsAnimalTraversal;
        public MapObject.MultiFocusMode FocusMode => Prototype.FocusMode;
        public MapObject.MapObjectStatus Status => Prototype.Status;
        public ItemDefinition BoundItemDefinition => Template != null ? Template.Definition : Prototype.BoundItemDefinition;
        public int ResolveItemId() => Placement != null ? Placement.itemId : Prototype.ResolveItemId();
        public int ResolvedItemId => ResolveItemId();
        public int ID => ResolveItemId();
        public float FocusActivationRadius => Prototype.FocusActivationRadius;
        internal UtilityPoleRuntime(global::UtilityPole source) { presentation = Prototype = source; RefreshAuthoring(); }
        internal UtilityPoleRuntime(UtilityPoleWorld world, global::UtilityPole prototype, BlockStateStore.InstallationSaveState placement,
            MapObjectHandle handle, UtilityPoleRenderTemplate template)
        {
            World = world; Prototype = prototype; Placement = placement; Handle = handle; Template = template;
            RootMatrix = Matrix4x4.TRS(WorldPosition, WorldRotation, template.Scale);
            CullBounds = VirtualRenderBatchCollection.CalculateWorldBounds(template.LocalBounds, RootMatrix);
            // Capture once per template. No Transform hierarchy is retained by an installed pole.
            ConfigurePoint(0, template.HasCenter, template.Center);
            ConfigurePoint(1, template.HasA, template.A); ConfigurePoint(2, template.HasB, template.B);
            ConfigureStyle(template.RangeOffset, template.Width, template.Sag, template.ConnectionSag, template.Segments, template.Color);
        }
        internal void RefreshAuthoring() { if (presentation != null) { presentation.Configure(this); EnsureUtilityPoleWires(); } }
        public bool TryGetPlacementRuntime(out Vector2Int anchor, out int turns)
        {
            if (Placement != null) { anchor = Placement.anchorCoordinate; turns = Placement.quarterTurns; return Registered; }
            if (presentation != null) return presentation.TryGetPlacementRuntime(out anchor, out turns);
            anchor = default; turns = 0; return false;
        }
        internal void ConfigurePoint(int index, bool present, Vector3 local)
        {
            UtilityPoleLinePoint point = index == 0 ? linePointCenter : index == 1 ? linePointA : linePointB;
            if (present) { if (point == null) point = new UtilityPoleLinePoint(this); point.Local = local; } else point = null;
            if (index == 0) linePointCenter = point; else if (index == 1) linePointA = point; else linePointB = point;
        }
        internal void ConfigureStyle(float offset, float width, float sag, float connectionSag, int segments, Color color)
        { supplyRangeVisualYOffset = offset; lineWidth = width; lineSagDepth = sag; connectionLineSagDepth = connectionSag; lineCurveSegments = segments; lineColor = color; EnsureUtilityPoleWires(); }
        internal void Persist(bool topologyChecked = false)
        {
            if (!IsRuntimeActive || Placement == null) return;
            // Match component save capture: a partial loaded graph must not overwrite
            // the saved connections of poles that have not been restored yet.
            if (!topologyChecked && World?.Store != null && !World.Store.CanCaptureUtilityPoleTopology) return;
            Placement.utilityPoleConnectedAnchors ??= new List<Vector2Int>(4);
            if (CaptureConnectedPoleAnchorCoordinates(Placement.utilityPoleConnectedAnchors)) Placement.utilityPoleConnectionsInitialized = true;
        }
    }
    internal sealed class UtilityPoleLinePoint
    {
        private readonly UtilityPoleRuntime owner;
        internal Vector3 Local;
        internal UtilityPoleLinePoint(UtilityPoleRuntime pole) { owner = pole; }
        // Matches the original wire endpoint calculation: placement bounce scales the body, not the wires.
        internal Vector3 Position => owner.WorldPosition + owner.WorldRotation * Local;
    }
}
