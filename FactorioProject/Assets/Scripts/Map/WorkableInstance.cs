using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.MapObjects
{
public sealed class WorkableInstance : IWorkableTarget
{
    internal readonly WorkableWorld World;
    internal readonly int Index;
    internal readonly uint Generation;
    internal readonly WorkableRenderTemplate Template;
    internal int OrderIndex;
    private readonly Bounds rangeBounds;
    public MapObjectHandle Handle { get; }
    public WorkableObject Prototype { get; }
    public BlockStateStore.InstallationSaveState Placement { get; }
    public Vector2Int StorageKey => BlockStateStore.GetInstallationStorageKey(Placement);
    public Vector2Int AnchorCoordinate => Placement.anchorCoordinate;
    public long WorkablePlacementSequence => Placement.placementSequence;
    public Vector3 WorldPosition => Placement.worldPosition;
    public Quaternion WorldRotation => Placement.worldRotation;
    public bool IsRuntimeActive => World.Contains(Index, Generation) && VirtualObjectWorld.Current != null && VirtualObjectWorld.Current.IsHandleAlive(Handle);
    public bool IsTargetActive => IsRuntimeActive;
    public MapObject SceneObject => null;
    public string ObjectName => Prototype.ObjectName;
    public bool AllowsFocus => Prototype.AllowsFocus;
    public bool AllowsAnimalTraversal => Prototype.AllowsAnimalTraversal;
    public MapObject.MultiFocusMode FocusMode => Prototype.FocusMode;
    public MapObject.MapObjectStatus Status => Prototype.Status;
    public ItemDefinition BoundItemDefinition => Template.Definition;
    public int ResolveItemId() => Placement.itemId;
    public int ResolvedItemId => Placement.itemId;
    public int ID => Placement.itemId;
    public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => Placement.occupiedCoordinates;
    public Matrix4x4 RootMatrix { get; }
    public Bounds CullBounds { get; }
    public bool ShowWorkableRange => Template.ShowRange;
    public bool GlobalRangeVisualSuppressed => false;
    public float RangeVisualYOffset => Template.RangeYOffset;
    internal bool PlacementPresentationSuppressed { get; set; }
    internal float PlacementPresentationScale { get; set; } = 1f;
    internal WorkableInstance(WorkableWorld world, int index, uint generation, MapObjectHandle handle,
        WorkableObject prototype, BlockStateStore.InstallationSaveState placement, WorkableRenderTemplate template)
    {
        World = world; Index = index; Generation = generation; Handle = handle; Prototype = prototype; Placement = placement; Template = template;
        RootMatrix = Matrix4x4.TRS(WorldPosition, WorldRotation, template.Scale);
        CullBounds = VirtualRenderBatchCollection.CalculateWorldBounds(template.LocalBounds, RootMatrix);
        Vector3 center = WorldPosition;
        var occupied = placement.occupiedCoordinates;
        if (occupied.Count > 0)
        {
            double x = 0, z = 0;
            for (int i = 0; i < occupied.Count; i++) { x += occupied[i].x; z += occupied[i].y; }
            center = new Vector3((float)(x / occupied.Count), WorldPosition.y, (float)(z / occupied.Count));
        }
        rangeBounds = new Bounds(center, new Vector3(template.RangeCells, 0.01f, template.RangeCells));
    }
    public bool TryGetWorkableRangeBounds(out Bounds bounds) { bounds = rangeBounds; return Template.RangeCells > 0; }
    public void SetSelectedRangeVisualRequested(bool requested) => WorkableObject.SetTargetSelected(this, requested);
    public bool IsItemFilterEnabled(int itemId, int count) => MapObject.IsItemAllowedByFilterMask(itemId, Placement.itemFilterMaskInitialized, Placement.itemFilterMaskWords);
    public void SetItemFilterEnabled(int itemId, int count, bool enabled)
    {
        if (itemId < 0) return;
        var words = Placement.itemFilterMaskWords;
        int required = (Math.Max(count, itemId + 1) + 63) >> 6;
        while (words.Count < required) words.Add(ulong.MaxValue);
        Placement.itemFilterMaskInitialized = true;
        if (enabled) words[itemId >> 6] |= 1UL << (itemId & 63); else words[itemId >> 6] &= ~(1UL << (itemId & 63));
    }
}
}
