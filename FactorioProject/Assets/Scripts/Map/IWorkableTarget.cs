using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.MapObjects
{
    // Crafting remains owned by the player. A workable supplies access and geometry only.
    public interface IWorkableTarget : IMapObjectTarget
    {
        long WorkablePlacementSequence { get; }
        Vector2Int AnchorCoordinate { get; }
        IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates { get; }
        bool ShowWorkableRange { get; }
        bool GlobalRangeVisualSuppressed { get; }
        float RangeVisualYOffset { get; }
        bool TryGetWorkableRangeBounds(out Bounds bounds);
        void SetSelectedRangeVisualRequested(bool requested);
    }

    public static class WorkableTargetExtensions
    {
        public static bool ContainsWorldPositionInOwnWorkableRange(this IWorkableTarget target, Vector3 position) =>
            target != null && target.IsTargetActive && target.TryGetWorkableRangeBounds(out var bounds)
            && WorkableRangeIndex.Contains(bounds, position);
        public static bool ContainsWorldPositionInWorkableRange(this IWorkableTarget target, Vector3 position) =>
            target.ContainsWorldPositionInOwnWorkableRange(position);
        public static bool ContainsWorldPositionInConnectedWorkableRange(this IWorkableTarget target, Vector3 position) =>
            WorkableObject.RangeIndex.ContainsConnected(target, position);
    }
}
