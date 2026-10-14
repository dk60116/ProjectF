using System.Collections.Generic;
using ProjectF.Simulation.Monsters;
using UnityEngine;

// Installation health and teardown share the terrain's canonical persistence owner.
public partial class TerrainGenerator
{
    public readonly struct InstallationCombatTarget
    {
        internal readonly IMapObjectTarget Target;
        internal readonly Vector2Int Coordinate;
        internal readonly Vector2Int StorageKey;
        internal readonly long Sequence;
        internal readonly BlockStateStore.InstallationSaveState State;

        internal InstallationCombatTarget(IMapObjectTarget target, Vector2Int coordinate,
            Vector2Int storageKey, BlockStateStore.InstallationSaveState state)
        {
            Target = target;
            Coordinate = coordinate;
            StorageKey = storageKey;
            Sequence = state.placementSequence;
            State = state;
        }

        public Vector3 ClosestPoint(Vector3 origin)
        {
            Vector3 nearest = Target.WorldPosition;
            if (Target.BoundItemDefinition?.mapObject is Vehicle)
            {
                nearest.y = origin.y;
                return nearest;
            }
            float distance = float.MaxValue;
            var coordinates = State.occupiedCoordinates;
            for (int i = 0; coordinates != null && i < coordinates.Count; i++)
            {
                Vector2Int cell = coordinates[i];
                Vector3 point = new Vector3(Mathf.Clamp(origin.x, cell.x - 0.5f, cell.x + 0.5f),
                    origin.y, Mathf.Clamp(origin.z, cell.y - 0.5f, cell.y + 0.5f));
                float candidate = (point - origin).sqrMagnitude;
                if (candidate < distance) { distance = candidate; nearest = point; }
            }
            nearest.y = origin.y;
            return nearest;
        }
    }

    public bool TryGetInstallationCombatTarget(Vector2Int coordinate, out InstallationCombatTarget result)
    {
        result = default;
        if (resourceStateStore == null || !TryGetLoadedBlock(coordinate, out Block block) || block == null) return false;
        if (ProjectF.Railway.TrainWorld.Shared.TryGetTargetAtCoordinate(coordinate, out var train)
            && TryGetInstallationCombatTarget(train, coordinate, out result)) return true;
        if (TryGetInstallationCombatTarget(block.MapObject, coordinate, out result)) return true;
        // Traversability is a navigation decision, not a combat exclusion. I/O owners can hide pipes/belts.
        if (PipeWorld.Current != null && PipeWorld.Current.TryGetAtCoordinate(coordinate, out var pipe)
            && TryGetInstallationCombatTarget(pipe, coordinate, out result)) return true;
        return ConveyorWorld.Current != null && ConveyorWorld.Current.TryGetAtCoordinate(coordinate, out var belt)
            && TryGetInstallationCombatTarget(belt, coordinate, out result);
    }

    public bool TryGetMonsterPathBlocker(Vector3 start, Vector3 end, out InstallationCombatTarget blocker)
    {
        blocker = default;
        int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(start, end) * 5f));
        for (int i = 1; i <= steps; i++)
        {
            Vector3 point = Vector3.Lerp(start, end, (float)i / steps);
            if (CanMonsterMoveTo(point)) continue;
            // Never target something behind water, a resource or a fully excluded installation.
            return TryGetInstallationCombatTarget(GetWorldBlockCoordinate(point), out blocker);
        }
        return false;
    }

    // Proximity scans need every identity at an I/O overlap, not only the cell's foreground owner.
    public void CollectInstallationCombatTargets(Vector2Int coordinate, List<InstallationCombatTarget> results)
    {
        results.Clear();
        if (resourceStateStore == null || !TryGetLoadedBlock(coordinate, out Block block) || block == null) return;
        if (ProjectF.Railway.TrainWorld.Shared.TryGetTargetAtCoordinate(coordinate, out var train))
            AppendInstallationCombatTarget(train, coordinate, results);
        AppendInstallationCombatTarget(block.MapObject, coordinate, results);
        if (PipeWorld.Current != null && PipeWorld.Current.TryGetAtCoordinate(coordinate, out var pipe))
            AppendInstallationCombatTarget(pipe, coordinate, results);
        if (ConveyorWorld.Current != null && ConveyorWorld.Current.TryGetAtCoordinate(coordinate, out var belt))
            AppendInstallationCombatTarget(belt, coordinate, results);
    }

    private void AppendInstallationCombatTarget(IMapObjectTarget target, Vector2Int coordinate,
        List<InstallationCombatTarget> results)
    {
        for (int i = 0; i < results.Count; i++)
            if (ReferenceEquals(results[i].Target, target)) return;
        if (TryGetInstallationCombatTarget(target, coordinate, out var candidate)) results.Add(candidate);
    }

    private bool TryGetInstallationCombatTarget(IMapObjectTarget target, Vector2Int coordinate,
        out InstallationCombatTarget result)
    {
        result = default;
        if (target == null || !target.IsTargetActive) return false;
        ItemDefinition definition = target.BoundItemDefinition;
        if (!(definition != null && definition.mapObject is InstallationObject) || definition.excludeFromMonsterTargets) return false;
        long sequence = GetMapObjectPlacementSequence(target);
        // I/O areas can overlap other installations. A coordinate-only key can belong to a pipe/belt.
        if (sequence <= 0L || !resourceStateStore.TryGetInstallationStorageKey(sequence, definition.id, out var key))
            return false;
        if (!resourceStateStore.TryGetInstallationStateReadOnly(key, out var state)
            && !resourceStateStore.TryGetLiveInstallationReadOnly(key, out _, out state)) return false;
        if (state == null || state.placementSequence != sequence || state.itemId != definition.id) return false;
        result = new InstallationCombatTarget(target, coordinate, key, state);
        return true;
    }

    public bool TryRefreshCombatTarget(in InstallationCombatTarget target, out InstallationCombatTarget current)
    {
        current = default;
        return target.Target != null && TryGetInstallationCombatTarget(target.Target, target.Coordinate, out current)
            && ReferenceEquals(current.Target, target.Target) && current.StorageKey == target.StorageKey
            && current.Sequence == target.Sequence;
    }

    internal bool IsCombatTargetOccupant(Vector2Int coordinate, IMapObjectTarget target)
    {
        if (target == null || !TryGetLoadedBlock(coordinate, out Block block) || block == null
            || !IsCombatTargetBlockOwner(block, target)) return false;
        // Blocks bind infrastructure prototypes, but combat owns individual runtime identities.
        // Sharing a prototype must not allow attacking through another pipe or conveyor.
        if (target is ConveyorRuntimeRecord)
            return ConveyorWorld.Current != null && ConveyorWorld.Current.TryGetAtCoordinate(coordinate, out var conveyor)
                && ReferenceEquals(conveyor, target);
        if (target is PipeRuntimeRecord)
            return PipeWorld.Current != null && PipeWorld.Current.TryGetAtCoordinate(coordinate, out var pipe)
                && ReferenceEquals(pipe, target);
        return true;
    }

    public float DamageInstallation(in InstallationCombatTarget target, float damage)
    {
        if (!TryRefreshCombatTarget(target, out _) || MapObjectTickManager.SimulationPaused
            || SaveManager.GameplayInputBlocked || float.IsNaN(damage) || float.IsInfinity(damage)
            || damage <= 0f) return 0f;
        if (!resourceStateStore.TryGetInstallationStateReadOnly(target.StorageKey, out var state)) return 0f;
        float maximum = target.Target.BoundItemDefinition.MaximumHealth;
        CombatHealth health = state.hasCombatHealth
            ? new CombatHealth(maximum, state.combatHealth) : new CombatHealth(maximum);
        float applied = health.Damage(damage);
        if (!health.IsDead)
        {
            resourceStateStore.SetInstallationHealth(target.Sequence, state.itemId, health.Current);
        }
        else
        {
            resourceStateStore.TryGetLiveInstallationReadOnly(target.StorageKey, out var live, out _);
            Vector3 position = target.Target.WorldPosition;
            Quaternion rotation = live != null ? live.transform.rotation : state.hasWorldPose
                ? state.worldRotation : Quaternion.Euler(0f, state.quarterTurns * 90f, 0f);
            MapObject source = target.Target.BoundItemDefinition.mapObject;
            var coordinates = state.occupiedCoordinates;
            bool conveyorTarget = target.Target is ConveyorRuntimeRecord || source is ConveyorBelt;
            for (int i = 0; coordinates != null && i < coordinates.Count; i++)
                if (TryGetLoadedBlock(coordinates[i], out var contentBlock) && contentBlock != null
                    && (conveyorTarget || IsCombatTargetBlockOwner(contentBlock, target.Target)))
                {
                    contentBlock.DiscardInstallationContents(conveyorTarget);
                    resourceStateStore.SaveFloorObjects(coordinates[i], contentBlock, VirtualObjectResidency.Live);
                    if (conveyorTarget) resourceStateStore.SetConveyorItems(coordinates[i], null);
                }
            // Remove topology first, so block refresh cannot rediscover the destroyed infrastructure.
            resourceStateStore.RemoveInstallation(target.StorageKey);
            for (int i = 0; coordinates != null && i < coordinates.Count; i++)
            {
                Vector2Int coordinate = coordinates[i];
                if (TryGetLoadedBlock(coordinate, out Block block) && block != null)
                {
                    block.ForgetDestroyedInfrastructure(target.Target);
                    // Do not delete a different installation's overlapping I/O inventory.
                    if (IsCombatTargetBlockOwner(block, target.Target))
                    {
                        block.SetMapObject(null);
                    }
                    else if (target.Target is ConveyorRuntimeRecord || target.Target is PipeRuntimeRecord)
                        block.SetMapObject(block.MapObject);
                }
            }
            resourceStateStore.AddWreckage(state, position, rotation);
            if (live != null) ReleaseInstallationObject(live, source);
        }
        return applied;
    }

    public bool TryGetMapObjectHealth(IMapObjectTarget target, out float current, out float maximum)
    {
        maximum = target?.BoundItemDefinition != null ? target.BoundItemDefinition.MaximumHealth : 100f;
        current = maximum;
        if (target == null || !target.IsTargetActive) return false;
        // Harvest quantity/work gauges remain independent from combat HP.
        if (target is ResourceInstance resource) { current = resource.CurrentHealth; maximum = resource.MaximumHealth; return true; }
        if (target is Resource) return true;
        long sequence = GetMapObjectPlacementSequence(target);
        if (resourceStateStore == null || !resourceStateStore.TryGetInstallationStorageKey(sequence, target.ResolvedItemId, out var key)
            || !resourceStateStore.TryGetInstallationStateReadOnly(key, out var state)) return false;
        if (state.hasCombatHealth) current = Mathf.Clamp(state.combatHealth, 0f, maximum);
        return true;
    }

    public bool SetMapObjectHealth(IMapObjectTarget target, float health) => target != null
        && resourceStateStore != null && resourceStateStore.SetInstallationHealth(
            GetMapObjectPlacementSequence(target), target.ResolvedItemId, health);

    private static bool IsCombatTargetBlockOwner(Block block, IMapObjectTarget target) =>
        ReferenceEquals(block.MapObject, target)
        || target is ConveyorRuntimeRecord conveyor && ReferenceEquals(block.MapObject, conveyor.Prototype)
        || target is PipeRuntimeRecord pipe && ReferenceEquals(block.MapObject, pipe.Prototype);

    private static long GetMapObjectPlacementSequence(IMapObjectTarget target) =>
        target is IMapObjectSimulationIdentity identity ? identity.SimulationId
            : target is RobotArmInstance arm ? arm.SimulationId
            : target is ProjectF.Power.UtilityPoleRuntime pole ? pole.SimulationId
            : target is ProjectF.MapObjects.IWorkableTarget workable ? workable.WorkablePlacementSequence
            : target is ProjectF.Railway.RailwayInstance railway ? railway.SimulationId : 0L;
}
