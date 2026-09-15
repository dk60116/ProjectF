using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.Persistence
{
    /// <summary>
    /// All saved records remain authoritative. Materialize terrain for the player
    /// and every saved activity, including off-screen factories and their IO areas.
    /// Explored terrain with no activity can be generated when visited again.
    /// </summary>
    public sealed class SavedWorldChunkPlan
    {
        private const int InteractionMarginCells = 2;
        private readonly HashSet<Vector2Int> chunks = new HashSet<Vector2Int>();
        private readonly int chunkSize;
        private readonly Vector2Int minimumChunk;
        private readonly Vector2Int maximumChunk;

        public SavedWorldChunkPlan(int chunkSize, Vector2Int minimumChunk, Vector2Int maximumChunk)
        {
            this.chunkSize = Math.Max(1, chunkSize);
            this.minimumChunk = minimumChunk;
            this.maximumChunk = maximumChunk;
        }

        public List<Vector2Int> Build(
            MapSaveData map, Vector2Int center, int loadRadius,
            Func<int, int> getFacilityRadius = null)
        {
            chunks.Clear();
            AddChunkRectangle(center.x - loadRadius, center.y - loadRadius,
                center.x + loadRadius, center.y + loadRadius);

            var radiiByItem = new Dictionary<int, int>();
            if (map?.installations != null)
            {
                foreach (InstallationSaveEntry entry in map.installations)
                {
                    BlockStateStore.InstallationSaveState state = entry?.state;
                    if (state == null) continue;
                    if (!radiiByItem.TryGetValue(state.itemId, out int radius))
                    {
                        radius = Math.Max(InteractionMarginCells, getFacilityRadius?.Invoke(state.itemId) ?? 0);
                        radiiByItem.Add(state.itemId, radius);
                    }
                    AddTile(state.anchorCoordinate, radius);
                    AddTiles(state.occupiedCoordinates, radius);
                    if (state.hasWorldPose) AddPosition(state.worldPosition, radius);

                    InputOutputModule.PersistentState io = state.inputOutputState;
                    if (io == null) continue;
                    AddTiles(io.inputEnergyCoordinates);
                    AddTiles(io.outputCoordinates);
                    AddTiles(io.pipeInputCoordinates);
                    AddTiles(io.gridCoordinates);
                    AddTiles(io.focusCoordinates);
                    if (io.inputItemAreas != null)
                        foreach (var area in io.inputItemAreas) AddTile(area.coordinate);
                    if (io.seedPlanterHasLoadedSeed) AddTile(io.seedPlanterLoadedSeedInputCoordinate);
                }
            }

            if (map?.floorObjects != null)
                foreach (var entry in map.floorObjects)
                    if (entry?.itemIds != null && entry.itemIds.Count > 0) AddTile(entry.coordinate);
            if (map?.conveyorItems != null)
                foreach (var entry in map.conveyorItems)
                    if (entry != null) AddTile(entry.coordinate);
            if (map?.conveyorItemRuns != null)
                foreach (var entry in map.conveyorItemRuns)
                    if (entry != null) { AddTile(entry.startCoordinate); AddTile(entry.endCoordinate); }

            AddTiles(map?.farmlandCoordinates);
            if (map?.farmlandFertilizer != null)
                foreach (var entry in map.farmlandFertilizer)
                    if (entry != null) AddTile(entry.coordinate);
            if (map?.plantedResources != null)
                foreach (var entry in map.plantedResources)
                    if (entry != null) AddTile(entry.coordinate);
            if (map?.animals != null)
                foreach (var entry in map.animals)
                {
                    if (entry == null || entry.removed) continue;
                    AddPosition(entry.position);
                    if (entry.hasTarget) AddPosition(entry.targetPosition);
                    AddPosition(entry.herdCenter, Math.Max(InteractionMarginCells, (int)Math.Ceiling(entry.herdRadius)));
                }

            var result = new List<Vector2Int>(chunks);
            result.Sort((left, right) =>
            {
                long leftX = (long)left.x - center.x, leftY = (long)left.y - center.y;
                long rightX = (long)right.x - center.x, rightY = (long)right.y - center.y;
                int distance = (leftX * leftX + leftY * leftY).CompareTo(rightX * rightX + rightY * rightY);
                if (distance != 0) return distance;
                int y = left.y.CompareTo(right.y);
                return y != 0 ? y : left.x.CompareTo(right.x);
            });
            return result;
        }

        private void AddTiles(IReadOnlyList<Vector2Int> coordinates, int margin = InteractionMarginCells)
        {
            if (coordinates == null) return;
            for (int i = 0; i < coordinates.Count; i++) AddTile(coordinates[i], margin);
        }

        private void AddPosition(Vector3 position, int margin = InteractionMarginCells)
            => AddTile(new Vector2Int((int)Math.Floor(position.x), (int)Math.Floor(position.z)), margin);

        private void AddTile(Vector2Int coordinate, int margin = InteractionMarginCells)
        {
            AddChunkRectangle(
                (int)Math.Floor(((double)coordinate.x - margin) / chunkSize),
                (int)Math.Floor(((double)coordinate.y - margin) / chunkSize),
                (int)Math.Floor(((double)coordinate.x + margin) / chunkSize),
                (int)Math.Floor(((double)coordinate.y + margin) / chunkSize));
        }

        private void AddChunkRectangle(int minX, int minY, int maxX, int maxY)
        {
            minX = Math.Max(minX, minimumChunk.x); minY = Math.Max(minY, minimumChunk.y);
            maxX = Math.Min(maxX, maximumChunk.x); maxY = Math.Min(maxY, maximumChunk.y);
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++) chunks.Add(new Vector2Int(x, y));
        }
    }
}
