using UnityEngine;

/// <summary>Individual selection identity for both scene components and data-only resources.</summary>
public interface IMapObjectTarget
{
    MapObject SceneObject { get; }
    bool IsTargetActive { get; }
    Vector3 WorldPosition { get; }
    string ObjectName { get; }
    bool AllowsFocus { get; }
    bool AllowsAnimalTraversal { get; }
    MapObject.MultiFocusMode FocusMode { get; }
    MapObject.MapObjectStatus Status { get; }
    ItemDefinition BoundItemDefinition { get; }
    int ResolveItemId();
    int ResolvedItemId { get; }
    int ID { get; }
}

public static class MapObjectTargetExtensions
{
    public static bool IsItemFilterEnabled(this IMapObjectTarget target, int itemId, int count) =>
        target is RobotArmInstance arm ? arm.IsItemFilterEnabled(itemId, count) :
        target?.SceneObject != null && target.SceneObject.IsItemFilterEnabled(itemId, count);
    public static void SetItemFilterEnabled(this IMapObjectTarget target, int itemId, int count, bool enabled)
    {
        if (target is RobotArmInstance arm) arm.SetItemFilterEnabled(itemId, count, enabled);
        else target?.SceneObject?.SetItemFilterEnabled(itemId, count, enabled);
    }
    public static T GetComponent<T>(this IMapObjectTarget target) where T : Component => target?.SceneObject != null ? target.SceneObject.GetComponent<T>() : null;
    public static T GetComponentInParent<T>(this IMapObjectTarget target) where T : Component => target?.SceneObject != null ? target.SceneObject.GetComponentInParent<T>() : null;
    public static T GetComponentInChildren<T>(this IMapObjectTarget target, bool includeInactive = false) where T : Component => target?.SceneObject != null ? target.SceneObject.GetComponentInChildren<T>(includeInactive) : null;
    public static bool TryGetComponent<T>(this IMapObjectTarget target, out T component) where T : Component
    { component = target.GetComponent<T>(); return component != null; }
    public static void GetComponentsInChildren<T>(this IMapObjectTarget target, bool includeInactive, System.Collections.Generic.List<T> results) where T : Component
    { results.Clear(); if (target?.SceneObject != null) target.SceneObject.GetComponentsInChildren(includeInactive, results); }
    // Unity destroyed-object semantics must be explicit at interface/object boundaries.
    public static bool IsAlive(this IMapObjectTarget target) => target is ResourceInstance resource
        ? resource.IsRuntimeActive : target is RobotArmInstance arm ? arm.IsRuntimeActive : target is MapObject component && component != null;
    public static bool IsAliveTarget(object target) => target is ResourceInstance resource
        ? resource.IsRuntimeActive : target is RobotArmInstance arm ? arm.IsRuntimeActive : target is Object unityObject && unityObject != null;
}
