using System.Collections.Generic;

// UI and component lookup boundaries. Capability checks and toggle routing use actual sources.
public static partial class MapObjectTargetExtensions
{
    public static T GetComponent<T>(this IMapObjectTarget target) where T : class =>
        (target as FilterSceneTarget)?.ProductionComponent as T;
    public static T GetComponentInChildren<T>(this IMapObjectTarget target, bool includeInactive) where T : class => target.GetComponent<T>();
}
public partial class ItemDefinition { public bool itemFilter; }
public class RobotArmInstance : IMapObjectTarget { }
public class Spliterbelt : IMapObjectTarget { }
public class FilterSceneTarget : IMapObjectTarget
{
    public MapObject SceneObject { get; } = new();
    public ProductionMachine ProductionComponent;
    public int ItemId;
    public int ResolveItemId() => ItemId;
}
public partial class ProductionMachine : IMapObjectTarget, IProductionTargetSelection
{
    private int selectedItem = -1;
    public bool IsProductionTargetSelected(int item) => selectedItem == item;
    public void SetExclusiveProductionTarget(int item) => selectedItem = item;
    public void ClearProductionTargetSelection() => selectedItem = -1;
}
public partial class PlayerFilterProbe
{
    public static bool CanShowButton(IMapObjectTarget target, List<ItemDefinition> definitions) => SupportsItemFilter(target, definitions);
}
public partial class FilterSelectUI
{
    public bool Toggle(IMapObjectTarget target, int item, bool enabled) => TryApplyProductionTargetSelection(target, item, enabled);
}
