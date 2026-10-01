using System;
using System.Collections.Generic;
using UnityEngine;

// Prefab geometry and fixed machine ports are fixture boundaries. Variant
// selection, rotation, fluid gates and required-port handling use production code.
public partial class InstallationPlacementController
{
    private readonly List<Vector2Int> fixedPorts = new();
    public void SetFixedPort(Vector2Int direction) => fixedPorts.Add(direction);
    public int AnchorNetworkFluid = -1;
    public Pipe ResolveVariant(Pipe prototype, int turns, int baseline, out int resolvedTurns, bool preview = false)
    {
        bool resolved = TryResolvePipePlacementVariant(prototype, Vector2Int.zero, turns, preview ? new Pipe { Preview = true } : null,
            out MapObject selected, out resolvedTurns, baseline);
        if (!resolved) throw new InvalidOperationException("variant could not be resolved");
        return (Pipe)selected;
    }
    public Pipe ResolvePreviewVariant(Pipe prototype, Pipe preview, int turns, out int resolvedTurns)
    {
        if (!TryResolvePipePlacementVariantWithCompatibleAdjacency(prototype, Vector2Int.zero, turns, preview, 0,
                out MapObject selected, out resolvedTurns)) throw new InvalidOperationException("preview resolution failed");
        return (Pipe)selected;
    }
    public Pipe ForceVariant(Pipe existing, int turns, Vector2Int direction, out Quaternion rotation)
    {
        if (!TryResolvePipeVariantForForcedConnection(Vector2Int.zero, existing, new Quaternion(turns),
            direction, out Pipe resolved, out rotation)) throw new InvalidOperationException("forced variant could not be resolved");
        return resolved;
    }
    public bool CompatibleVariant(Pipe pipe, int turns)
        => CanPipePlacementFluidConnectionsMatch(Vector2Int.zero, pipe, new Quaternion(turns), null);
    private sealed class InstallationEditSession { public Pipe prototype; }
    private sealed class PackedInstallationSession
    {
        public InstallationEditSession editSession;
        public Vector2Int anchorCoordinate;
        public int quarterTurns, conveyorVariantKind;
    }
    private readonly Stack<PackedInstallationSession> packedInstallationHistory = new();
    private bool mapEditModeActive = true;
    private int restoredMask;
    private bool IsEditingInstallation() => false;
    private static bool TryRemovePackedSessionItem(PackedInstallationSession session) => true;
    private static void RefreshTrainInstallPreviewTints() { }
    // Runtime activation/persistence are boundaries; actual undo dispatch and
    // prefab selection must preserve the packed shape and rotation together.
    private void RestoreEditedInstallation(InstallationEditSession session, Vector2Int coordinate,
        int turns, int variant = -1)
    {
        Pipe pipe = variant >= 0 ? (Pipe)ResolvePipeVariantPrefab(session.prototype, variant)
            : ResolveVariant(session.prototype, turns, 0, out turns);
        restoredMask = pipe.GetConnectionMask(new Quaternion(turns));
    }
    public int RestorePackedShape(Pipe prototype, int variant, int turns, bool wholeHistory)
    {
        packedInstallationHistory.Push(new PackedInstallationSession
        { editSession = new InstallationEditSession { prototype = prototype }, anchorCoordinate = Vector2Int.zero,
            quarterTurns = turns, conveyorVariantKind = variant });
        if (wholeHistory) RestorePackedInstallationHistory();
        else if (!TryUndoPackedInstallation()) throw new InvalidOperationException("packed undo failed");
        return restoredMask;
    }
    private static bool TryResolvePlacementQuarterTurnsFromRotation(MapObject obj, Quaternion rotation, out int turns)
    { turns = rotation.Turns; return true; }
    private static int NormalizePlacementQuarterTurns(int turns) => ((turns % 4) + 4) % 4;
    private static int NormalizePlacementQuarterTurnsForObject(MapObject obj, int turns) => NormalizePlacementQuarterTurns(turns);
    private static Quaternion GetPlacementObjectRotation(MapObject obj, int turns) => new(turns);
    private static int CountFenceNeighborDirections(IReadOnlyList<Vector2Int> directions) => directions?.Count ?? 0;
    private void AppendFixedFluidPipeConnectionDirections(Vector2Int coordinate, MapObject ignored, List<Vector2Int> directions)
        => directions.AddRange(fixedPorts);
    private List<Vector2Int> GetBoilerFixedFluidPipeConnectionDirections(Vector2Int coordinate, MapObject ignored) => new();
    private static bool TryResolveBoilerTeePipeVariant(Pipe pipe, IReadOnlyList<Vector2Int> neighbors,
        IReadOnlyList<Vector2Int> fixedDirections, int turns, out int resolvedTurns) { resolvedTurns = turns; return false; }
    private static bool TryResolveBoilerCornerPipeVariant(Pipe pipe, IReadOnlyList<Vector2Int> neighbors,
        IReadOnlyList<Vector2Int> fixedDirections, int turns, out int resolvedTurns) { resolvedTurns = turns; return false; }
    private static bool PipeHasConnectionTowardsAnyDirection(Pipe pipe, Quaternion rotation, IReadOnlyList<Vector2Int> directions)
    { foreach (var direction in directions) if (pipe.HasConnectionTowards(rotation, direction)) return true; return false; }
    private bool TryGetPipeVariantAnchorFluidItemId(Vector2Int coordinate, MapObject ignored, out int fluid)
    { fluid = EndpointFluid >= 0 ? EndpointFluid : AnchorNetworkFluid; return fluid >= 0; }
    private bool TryGetManualPipeConnectionMask(Vector2Int coordinate, MapObject ignored, out int mask)
    { mask = -1; return false; }
    private void NormalizePipeVariantsAroundCoordinates(IReadOnlyList<Vector2Int> coordinates,
        bool includeSelf, MapObject ignored, IReadOnlyCollection<Vector2Int> protectedCoordinates)
    {
        LastLegacyAnchors = new(coordinates);
        LastProtectedAnchors = new(protectedCoordinates);
    }
    public HashSet<Vector2Int> LastLegacyAnchors = new(), LastProtectedAnchors = new();
    public Pipe ResolveSavedPipe(Pipe prototype, int turns, int kind, int mask, out int restoredTurns)
    {
        if (!TryResolvePipeLoadPlacement(new ItemDefinition { mapObject = prototype }, Vector2Int.zero,
                turns, kind, mask, out MapObject restored, out restoredTurns, out _))
            throw new InvalidOperationException("saved pipe restoration failed");
        return (Pipe)restored;
    }
    public void SetSavedShape(Vector2Int coordinate, int kind, int mask)
    {
        var state = new BlockStateStore.InstallationSaveState
            { anchorCoordinate = coordinate, conveyorVariantKind = kind, pipeConnectionMask = mask };
        terrain.Saved[coordinate] = state;
        terrain.Blocks[coordinate].Record.First = coordinate;
    }
    private enum PipeNeighborConnectionSearchMode { PreviewOnly, NonPreviewOnly }
    private bool TryGetPipeOnlyConnectionAtCoordinate(Pipe prototype, Vector2Int coordinate, MapObject ignored,
        Vector2Int direction, bool allowPotential, HashSet<int> acceptedFluids, ref bool constrained,
        PipeNeighborConnectionSearchMode mode)
    {
        return neighbors.TryGetValue(coordinate, out Pipe pipe)
            && (mode == PipeNeighborConnectionSearchMode.PreviewOnly) == pipe.Preview
            && ShouldPipeNeighborContributeToVariant(coordinate, pipe, default, direction, allowPotential)
            && CanPipeNeighborContributeToVariant(prototype, coordinate + direction, coordinate,
                pipe, default, direction, ignored, allowPotential, acceptedFluids, ref constrained);
    }
    private static bool TryCollectSameCoordinateFixedFluidConstraintsForDirection(Vector2Int coordinate,
        Vector2Int direction, MapObject ignored, HashSet<int> fluids, ref bool constrained) => true;
    private bool CanPipePlacementFluidConnectionsMatch(Vector2Int coordinate, Pipe pipe, int turns,
        MapObject ignored, int baseline = 0) => CanPipePlacementFluidConnectionsMatch(coordinate, pipe, new Quaternion(turns), ignored, baseline);
}

public static class VariantChecks
{
    public static Pipe Prototypes()
    {
        var straight = new Pipe { VariantKind = PipeVariantKind.Straight, Mask = 10 };
        straight.StraightVariantPrefab = straight;
        straight.CornerVariantPrefab = new Pipe { VariantKind = PipeVariantKind.Corner, Mask = 3 };
        straight.TeeVariantPrefab = new Pipe { VariantKind = PipeVariantKind.Tee, Mask = 11 };
        straight.CrossVariantPrefab = new Pipe { VariantKind = PipeVariantKind.Cross, Mask = 15 };
        return straight;
    }
    public static void Run(Action<bool, string> require)
    {
        for (int turns = 0; turns < 4; turns++)
        {
            var directions = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            var controller = new InstallationPlacementController();
            Pipe prototype = Prototypes();
            int baseline = prototype.GetConnectionMask(new Quaternion(turns));
            int neighborDirection = (1 + turns) & 3;
            controller.SetNeighbor(neighborDirection, new Pipe { Mask = baseline, Fluid = 113, VariantKind = PipeVariantKind.Straight });
            controller.SetFixedPort(directions[turns]);
            Pipe resolved = controller.ResolveVariant(prototype, turns, baseline, out int resolvedTurns);
            require((resolved.GetConnectionMask(new Quaternion(resolvedTurns)) & baseline) == baseline,
                $"restored straight retains both ports beside a perpendicular machine output, rotation {turns}");
            require(resolved.VariantKind == PipeVariantKind.Tee,
                "a compatible side output extends the existing straight to a tee");
            Pipe fresh = controller.ResolveVariant(prototype, turns, 0, out _);
            require(fresh.VariantKind == PipeVariantKind.Corner,
                "a new two-direction placement can still choose a corner");
            Pipe forced = controller.ForceVariant(prototype, turns, directions[turns], out Quaternion forcedRotation);
            require((forced.GetConnectionMask(forcedRotation) & baseline) == baseline,
                "alignment of a restored neighbour must retain both straight ports too");

            foreach (bool wholeHistory in new[] { false, true })
            for (int kind = 0; kind < 4; kind++)
            {
                Pipe stored = (Pipe)InstallationPlacementController.ResolvePipeVariantPrefab(prototype, kind);
                require(controller.RestorePackedShape(prototype, kind, turns, wholeHistory)
                        == stored.GetConnectionMask(new Quaternion(turns)),
                    "single undo and history rollback pass packed shape and rotation together");
            }

            var mixed = new InstallationPlacementController();
            mixed.SetNeighbor(neighborDirection, new Pipe { Mask = baseline, Fluid = 113, VariantKind = PipeVariantKind.Straight });
            mixed.SetNeighbor((neighborDirection + 2) & 3,
                new Pipe { Mask = baseline, Fluid = 114, VariantKind = PipeVariantKind.Straight });
            Pipe repaired = mixed.ResolveVariant(prototype, turns, baseline, out int repairedTurns);
            require((repaired.GetConnectionMask(new Quaternion(repairedTurns)) & baseline) != baseline
                    && mixed.CompatibleVariant(repaired, repairedTurns),
                "mixed-fluid straight baseline remains repairable rather than being protected");

            var shrinking = new InstallationPlacementController();
            shrinking.SetNeighbor(neighborDirection, new Pipe { Mask = baseline, Fluid = 113, VariantKind = PipeVariantKind.Straight });
            Pipe shrunk = shrinking.ResolveVariant(prototype, turns,
                prototype.TeeVariantPrefab.GetConnectionMask(new Quaternion(turns)), out _);
            require(shrunk.VariantKind == PipeVariantKind.Straight,
                "a removed junction branch can still shrink a tee to a straight");

            var parallel = new InstallationPlacementController { AnchorNetworkFluid = 113 };
            parallel.SetNeighbor(neighborDirection,
                new Pipe { Mask = baseline, Fluid = 113, VariantKind = PipeVariantKind.Straight, Sequence = 1 });
            parallel.SetNeighbor(turns,
                new Pipe { Mask = (~baseline) & 15, Fluid = 114, VariantKind = PipeVariantKind.Straight,
                    Preview = true, Sequence = 50 });
            Pipe previewVariant = parallel.ResolveVariant(prototype, turns, 0, out int previewTurns, preview: true);
            Pipe commitVariant = parallel.ResolveVariant(prototype, turns, 0, out int commitTurns);
            require(previewVariant.GetConnectionMask(new Quaternion(previewTurns))
                    == commitVariant.GetConnectionMask(new Quaternion(commitTurns))
                    && previewVariant.GetConnectionMask(new Quaternion(previewTurns)) == baseline,
                "actual fluid selection and shape resolution keep parallel foreign previews out of the connected straight");
        }
    }
}
