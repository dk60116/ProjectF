using System.Collections.Generic;
using UnityEngine;

public interface IFacilityRuntimeWakeTarget
{
    bool IsFacilityRuntimeWakeTargetActive { get; }
    void WakeFacilityRuntimeTick();
}

/// <summary>
/// Spatial wake index for facilities that sleep while waiting for a world-coordinate mutation.
/// Registration is changed only with placement, so notifications stay proportional to nearby waiters.
/// </summary>
public static class FacilityRuntimeWakeRegistry
{
    private static readonly Dictionary<Vector2Int, HashSet<IFacilityRuntimeWakeTarget>> TargetsByCoordinate =
        new Dictionary<Vector2Int, HashSet<IFacilityRuntimeWakeTarget>>();
    private static readonly Dictionary<IFacilityRuntimeWakeTarget, List<Vector2Int>> CoordinatesByTarget =
        new Dictionary<IFacilityRuntimeWakeTarget, List<Vector2Int>>();
    private static readonly List<IFacilityRuntimeWakeTarget> WakeScratch =
        new List<IFacilityRuntimeWakeTarget>(8);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        TargetsByCoordinate.Clear();
        CoordinatesByTarget.Clear();
        WakeScratch.Clear();
    }

    public static void Register(
        IFacilityRuntimeWakeTarget target,
        IReadOnlyList<Vector2Int> coordinates)
    {
        Unregister(target);
        if (target == null || coordinates == null || coordinates.Count <= 0)
        {
            return;
        }

        var registeredCoordinates = new List<Vector2Int>(coordinates.Count);
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (registeredCoordinates.Contains(coordinate))
            {
                continue;
            }

            registeredCoordinates.Add(coordinate);
            if (!TargetsByCoordinate.TryGetValue(
                    coordinate,
                    out HashSet<IFacilityRuntimeWakeTarget> targets))
            {
                targets = new HashSet<IFacilityRuntimeWakeTarget>();
                TargetsByCoordinate.Add(coordinate, targets);
            }

            targets.Add(target);
        }

        if (registeredCoordinates.Count > 0)
        {
            CoordinatesByTarget[target] = registeredCoordinates;
        }
    }

    public static void Unregister(IFacilityRuntimeWakeTarget target)
    {
        if (target == null
            || !CoordinatesByTarget.TryGetValue(target, out List<Vector2Int> coordinates))
        {
            return;
        }

        CoordinatesByTarget.Remove(target);
        for (int i = 0; i < coordinates.Count; i++)
        {
            Vector2Int coordinate = coordinates[i];
            if (!TargetsByCoordinate.TryGetValue(
                    coordinate,
                    out HashSet<IFacilityRuntimeWakeTarget> targets))
            {
                continue;
            }

            targets.Remove(target);
            if (targets.Count <= 0)
            {
                TargetsByCoordinate.Remove(coordinate);
            }
        }
    }

    public static void NotifyCoordinateChanged(Vector2Int coordinate)
    {
        InputOutputModule.WakeRuntimeModulesAtCoordinate(coordinate);
        if (!TargetsByCoordinate.TryGetValue(
                coordinate,
                out HashSet<IFacilityRuntimeWakeTarget> targets)
            || targets.Count <= 0)
        {
            return;
        }

        WakeScratch.Clear();
        foreach (IFacilityRuntimeWakeTarget target in targets)
        {
            if (target != null && target.IsFacilityRuntimeWakeTargetActive)
            {
                WakeScratch.Add(target);
            }
        }

        for (int i = 0; i < WakeScratch.Count; i++)
        {
            WakeScratch[i].WakeFacilityRuntimeTick();
        }

        WakeScratch.Clear();
    }
}
