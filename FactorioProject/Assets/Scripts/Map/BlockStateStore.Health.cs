using System.Collections.Generic;
using UnityEngine;

public partial class BlockStateStore
{
    // Healthy placements need no additional record or per-frame work.
    internal readonly Dictionary<Vector2Int, InstallationSaveState> DamagedInstallations = new();
    internal readonly List<InstallationSaveState> Wreckage = new();
    internal int WreckageRevision { get; private set; }
    internal IReadOnlyList<PortableItemHealthSaveState> GetFloorInstallationHealth(Vector2Int coordinate) =>
        savedFloorObjectStates.TryGetValue(coordinate, out var state) ? state.InstallationHealth : null;

    private void TrackInstallationHealth(Vector2Int key, InstallationSaveState state)
    {
        if (state.hasCombatHealth) DamagedInstallations[key] = state;
        else DamagedInstallations.Remove(key);
    }

    public bool SetInstallationHealth(long sequence, int itemId, float current)
    {
        if (float.IsNaN(current) || float.IsInfinity(current) || current <= 0f
            || !TryGetInstallationStorageKey(sequence, itemId, out var key)
            || !savedInstallationStates.TryGetValue(key, out var state)) return false;
        ItemDefinition definition = null;
        GameManager.Instance?.ItemManger?.TryGetItemDefinitionById(itemId, out definition);
        float maximum = definition != null ? definition.MaximumHealth : 100f;
        state.hasCombatHealth = current < maximum;
        state.combatHealth = Mathf.Min(current, maximum);
        TrackInstallationHealth(key, state);
        return true;
    }

    public void AddWreckage(InstallationSaveState source, Vector3 position, Quaternion rotation)
    {
        // Wreckage is not an installation: no topology, inventory, fluid or simulation binding survives.
        Wreckage.Add(new InstallationSaveState
        {
            itemId = source.itemId, itemName = source.itemName,
            anchorCoordinate = TerrainGenerator.GetWorldBlockCoordinate(position), quarterTurns = source.quarterTurns,
            placementSequence = source.placementSequence, conveyorVariantKind = source.conveyorVariantKind,
            railVisualPathPoints = source.railVisualPathPoints != null ? new List<Vector2>(source.railVisualPathPoints) : new List<Vector2>(),
            occupiedCoordinates = source.occupiedCoordinates != null ? new List<Vector2Int>(source.occupiedCoordinates) : new List<Vector2Int>(),
            hasWorldPose = true, worldPosition = position, worldRotation = rotation,
            hasCombatHealth = true, combatHealth = 0f
        });
        WreckageRevision++;
    }

    public bool RemoveWreckage(long sequence)
    {
        for (int i = 0; i < Wreckage.Count; i++)
        {
            if (Wreckage[i].placementSequence != sequence) continue;
            Wreckage.RemoveAt(i);
            WreckageRevision++;
            return true;
        }
        return false;
    }

    internal int RemoveWreckageOverlapping(InstallationSaveState placement)
    {
        if (placement == null || Wreckage.Count == 0) return 0;
        int removed = 0;
        for (int i = Wreckage.Count - 1; i >= 0; i--)
        {
            var wreck = Wreckage[i];
            if (!WreckageOverlapsPlacement(wreck, placement)) continue;
            Wreckage.RemoveAt(i);
            removed++;
        }
        if (removed > 0) WreckageRevision++;
        return removed;
    }

    private static bool WreckageOverlapsPlacement(InstallationSaveState wreck, InstallationSaveState placement)
    {
        if (wreck == null) return false;
        var placedCells = placement.occupiedCoordinates;
        var wreckCells = wreck.occupiedCoordinates;
        int placedCount = placedCells?.Count ?? 0;
        int wreckCount = wreckCells?.Count ?? 0;
        // Old/single-cell records can have no footprint; only then use their anchor.
        for (int i = 0; i < Mathf.Max(1, placedCount); i++)
        {
            var cell = placedCount > 0 ? placedCells[i] : placement.anchorCoordinate;
            if (wreckCount == 0)
            {
                if (cell == wreck.anchorCoordinate) return true;
            }
            else if (wreckCells.Contains(cell)) return true;
        }
        return false;
    }

    private void ClearHealthState()
    {
        DamagedInstallations.Clear();
        Wreckage.Clear();
        WreckageRevision++;
    }
}
