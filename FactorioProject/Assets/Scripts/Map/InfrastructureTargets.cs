using UnityEngine;

public sealed partial class ConveyorRuntimeRecord
{
    public bool IsTargetActive => ConveyorWorld.Current != null
        && ConveyorWorld.Current.TryGetByStorageKey(StorageKey, out var current) && ReferenceEquals(current, this);
    public MapObject SceneObject => null;
    public string ObjectName => Prototype.ObjectName;
    public bool AllowsFocus => Prototype.AllowsFocus;
    public bool AllowsAnimalTraversal => Prototype.AllowsAnimalTraversal;
    public MapObject.MultiFocusMode FocusMode => Prototype.FocusMode;
    public MapObject.MapObjectStatus Status => Prototype.Status;
    public ItemDefinition BoundItemDefinition => InputOutputModule.ResolveItemDefinition(ItemId);
    public int ResolveItemId() => ItemId;
    public int ResolvedItemId => ItemId;
    public int ID => ItemId;
    public long SimulationId => PlacementSequence;
}

public sealed partial class PipeRuntimeRecord
{
    public bool IsTargetActive => PipeWorld.Current != null
        && PipeWorld.Current.TryGetByStorageKey(StorageKey, out var current) && ReferenceEquals(current, this);
    public MapObject SceneObject => null;
    public string ObjectName => Prototype.ObjectName;
    public bool AllowsFocus => Prototype.AllowsFocus;
    public bool AllowsAnimalTraversal => Prototype.AllowsAnimalTraversal;
    public MapObject.MultiFocusMode FocusMode => Prototype.FocusMode;
    public MapObject.MapObjectStatus Status => Prototype.Status;
    public ItemDefinition BoundItemDefinition => InputOutputModule.ResolveItemDefinition(ItemId);
    public int ResolveItemId() => ItemId;
    public int ResolvedItemId => ItemId;
    public int ID => ItemId;
    public long SimulationId => PlacementSequence;
}
