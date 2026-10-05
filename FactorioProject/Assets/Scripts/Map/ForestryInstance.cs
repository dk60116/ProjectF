using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Simulation;

namespace ProjectF.MapObjects
{
    public abstract class ForestryInstance : IMapObjectTarget, IDataElectricConsumer,
        IMapObjectUpdateTick, IMapObjectUpdateTickInterval, IMapObjectSimulationIdentity, IFacilityRuntimeWakeTarget,
        ProjectF.Benchmark.IBenchmarkWorkProgressTarget
    {
        internal readonly ForestryWorld World;
        internal readonly int Index;
        internal readonly uint Generation;
        internal readonly ForestryRenderTemplate Template;
        internal int OrderIndex;
        internal ref ForestryWorld.State Data => ref World.GetState(Index, Generation);
        public MapObjectHandle Handle { get; }
        public InstallationObject Prototype { get; }
        public BlockStateStore.InstallationSaveState Placement { get; }
        public Vector2Int StorageKey => BlockStateStore.GetInstallationStorageKey(Placement);
        public Vector2Int AnchorCoordinate => Placement.anchorCoordinate;
        public Vector3 WorldPosition => Placement.worldPosition;
        public Quaternion WorldRotation => Placement.worldRotation;
        public long SimulationId => Placement.placementSequence;
        public bool IsRuntimeActive => World.Contains(Index, Generation) && VirtualObjectWorld.Current != null
            && VirtualObjectWorld.Current.IsHandleAlive(Handle);
        public bool IsTargetActive => IsRuntimeActive;
        public MapObject SceneObject => null;
        public string ObjectName => Prototype.ObjectName;
        public bool AllowsFocus => Prototype.AllowsFocus;
        public bool AllowsAnimalTraversal => Prototype.AllowsAnimalTraversal;
        public MapObject.MultiFocusMode FocusMode => Prototype.FocusMode;
        public MapObject.MapObjectStatus Status => Prototype.Status;
        public float FocusActivationRadius => Prototype.FocusActivationRadius;
        public ItemDefinition BoundItemDefinition => Template.Definition;
        public int ResolveItemId() => Placement.itemId;
        public int ResolvedItemId => Placement.itemId;
        public int ID => Placement.itemId;
        public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => Placement.occupiedCoordinates;
        public Matrix4x4 RootMatrix { get; }
        public Bounds CullBounds { get; }
        public Vector3 PowerLineWorldPosition => RootMatrix.MultiplyPoint3x4(Template.PowerLinePoint);
        public float ManagedUpdateTickIntervalSeconds => 0.1f;
        internal bool PlacementPresentationSuppressed { get; set; }
        internal float PlacementPresentationScale { get; set; } = 1f;
        internal abstract bool KeepScheduled { get; }
        internal abstract bool HasPowerDemand { get; }
        private bool publishedPowerDemand;
        protected void PublishPowerDemand()
        {
            if (publishedPowerDemand == HasPowerDemand) return;
            publishedPowerDemand = HasPowerDemand; UtilityPole.InvalidateDataConsumerDemand(this);
        }
        internal abstract bool IsWorking { get; }
        bool IFacilityRuntimeWakeTarget.IsFacilityRuntimeWakeTargetActive => IsRuntimeActive;
        void IFacilityRuntimeWakeTarget.WakeFacilityRuntimeTick() => Wake();
        internal ForestryInstance(ForestryWorld world, int index, uint generation, MapObjectHandle handle,
            InstallationObject prototype, BlockStateStore.InstallationSaveState placement, ForestryRenderTemplate template)
        {
            World = world; Index = index; Generation = generation; Handle = handle;
            Prototype = prototype; Placement = placement; Template = template;
            RootMatrix = Matrix4x4.TRS(WorldPosition, WorldRotation, template.Scale);
            CullBounds = VirtualRenderBatchCollection.CalculateWorldBounds(template.LocalBounds, RootMatrix);
        }
        public bool TryGetElectricPowerRequirement(out float watts)
        { watts = Template.Watts; return IsRuntimeActive && watts > 0; }
        public bool TryGetElectricPowerDemand(out float watts)
        { watts = Template.Watts; return IsRuntimeActive && HasPowerDemand && watts > 0; }
        public void WakeForElectricPowerChange() => Wake();
        public void Wake()
        {
            if (!IsRuntimeActive || Data.Applying) return;
            FacilitySimulationWorld.SetScheduled(this, true);
        }
        public void ManagedUpdateTick(float deltaTime)
        {
            if (!IsRuntimeActive) { FacilitySimulationWorld.Unregister(this); return; }
            Data.Applying = true;
            try
            {
                World.ProcessedUpdates++;
                ApplyTick(deltaTime);
                if (IsWorking) Data.AnimationPhase += deltaTime * Data.SupplyRatio;
                Persist();
            }
            finally
            {
                Data.Applying = false;
                PublishPowerDemand();
                FacilitySimulationWorld.SetScheduled(this, KeepScheduled);
            }
        }
        protected abstract void ApplyTick(float deltaTime);
        public abstract void Persist();
        public virtual bool TryRandomizeWorkProgress(System.Random random) => false;
        internal virtual void ClearItems() { }
        public bool IsItemFilterEnabled(int itemId, int count) => MapObject.IsItemAllowedByFilterMask(itemId,
            Placement.itemFilterMaskInitialized, Placement.itemFilterMaskWords);
        public void SetItemFilterEnabled(int itemId, int count, bool enabled)
        {
            if (itemId < 0) return;
            var words = Placement.itemFilterMaskWords;
            int required = (Math.Max(count, itemId + 1) + 63) >> 6;
            while (words.Count < required) words.Add(ulong.MaxValue);
            Placement.itemFilterMaskInitialized = true;
            if (enabled) words[itemId >> 6] |= 1UL << (itemId & 63); else words[itemId >> 6] &= ~(1UL << (itemId & 63));
            Wake();
        }
    }
}
