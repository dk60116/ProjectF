using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Connects the two PipePass cells and starts a new pressure-loss section.
/// The pump does not own fluid; producers and consumers still transfer the
/// authoritative fluid directly through the connected network.
/// </summary>
public class Pump : InputOutputModule
{
    public bool TryGetRuntimePipePass(
        Vector2Int coordinate,
        out Vector2Int otherCoordinate,
        out Vector2Int externalDirection)
    {
        otherCoordinate = default;
        externalDirection = default;
        if (!TryGetPlacementRuntime(out Vector2Int anchorCoordinate, out int quarterTurns)
            || !TryGetPipePassExternalDirection(
                this,
                anchorCoordinate,
                quarterTurns,
                coordinate,
                out externalDirection))
        {
            return false;
        }

        IReadOnlyList<RectGridBlockPlacement> placements = RectGridPlacements;
        for (int i = 0; i < placements.Count; i++)
        {
            RectGridBlockPlacement placement = placements[i];
            if (placement.blockType != RectGridBlockType.PipeInput
                || !TryGetRectGridPlacementCoordinate(
                    this,
                    anchorCoordinate,
                    quarterTurns,
                    placement,
                    out Vector2Int candidateCoordinate)
                || candidateCoordinate == coordinate)
            {
                continue;
            }

            otherCoordinate = candidateCoordinate;
            return true;
        }

        return false;
    }

    public bool TryGetPipePassExternalDirection(
        MapObject footprintSource,
        Vector2Int anchorCoordinate,
        int quarterTurns,
        Vector2Int coordinate,
        out Vector2Int externalDirection)
    {
        externalDirection = Vector2Int.zero;
        MapObject anchorSource = footprintSource != null ? footprintSource : this;
        return TryGetRectGridBlockTypeAtCoordinate(
                   anchorSource,
                   anchorCoordinate,
                   quarterTurns,
                   coordinate,
                   out RectGridBlockType blockType)
               && blockType == RectGridBlockType.PipeInput
               && TryGetNearestRectGridObjectDirection(
                   anchorSource,
                   anchorCoordinate,
                   quarterTurns,
                   coordinate,
                   out Vector2Int inwardDirection)
               && (externalDirection = -inwardDirection) != Vector2Int.zero;
    }

    internal static bool TryResolvePipePassConnectionDirection(
        Vector2Int pipeCoordinate,
        Vector2Int endpointCoordinate,
        Vector2Int externalDirection,
        out Vector2Int pipeConnectionDirection)
    {
        pipeConnectionDirection = endpointCoordinate - pipeCoordinate;
        if (pipeConnectionDirection == Vector2Int.zero)
        {
            pipeConnectionDirection = -externalDirection;
        }

        return externalDirection != Vector2Int.zero
               && pipeConnectionDirection == -externalDirection;
    }
}
