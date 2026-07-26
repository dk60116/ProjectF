using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public partial class TerrainGenerator
{
    internal BlockStateStore ConveyorDiagnosticsStateStore => resourceStateStore;
}

public partial class BlockStateStore
{
    internal void CopySavedConveyorItemCoordinates(List<Vector2Int> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        foreach (KeyValuePair<Vector2Int, ConveyorItemBlockState> pair in savedConveyorItemStates)
        {
            if (pair.Value != null && pair.Value.lanes.Count > 0)
            {
                results.Add(pair.Key);
            }
        }
    }
}

namespace ProjectF.Diagnostics
{
    public sealed class ConveyorDiagnosticReport
    {
        internal ConveyorDiagnosticReport()
        {
        }

        public int LoadedConveyorBlocks { get; internal set; }
        public int SavedConveyorBlocks { get; internal set; }
        public int LiveItems { get; internal set; }
        public int SavedItems { get; internal set; }
        public int DistinctItems { get; internal set; }
        public int AuthoritativeItems { get; internal set; }
        public int ErrorCount { get; internal set; }
        public int WarningCount { get; internal set; }
        public int DuplicateResidencyCount { get; internal set; }
        public int InvalidLaneStateCount { get; internal set; }
        public int InvalidLinkCount { get; internal set; }
        public string FirstIssue { get; internal set; } = string.Empty;

        public bool IsHealthy => ErrorCount == 0;

        public string BuildProtocolTokens()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "beltCheckErrors={0} beltCheckWarnings={1} beltLoadedBlocks={2} beltSavedBlocks={3} beltLiveItems={4} beltSavedItems={5} beltDistinctItems={6} beltAuthoritativeItems={7} beltDuplicateResidencies={8} beltInvalidLanes={9} beltInvalidLinks={10}",
                ErrorCount,
                WarningCount,
                LoadedConveyorBlocks,
                SavedConveyorBlocks,
                LiveItems,
                SavedItems,
                DistinctItems,
                AuthoritativeItems,
                DuplicateResidencyCount,
                InvalidLaneStateCount,
                InvalidLinkCount);
        }
    }

    public static class ConveyorRuntimeDiagnostics
    {
        private const float MotionProgressEpsilon = 0.0001f;

        public static ConveyorDiagnosticReport Run(TerrainGenerator terrain)
        {
            if (terrain == null)
            {
                throw new ArgumentNullException(nameof(terrain));
            }

            ConveyorDiagnosticReport report = new ConveyorDiagnosticReport();
            List<Block> loadedBlocks = new List<Block>();
            List<Vector2Int> savedCoordinates = new List<Vector2Int>();
            HashSet<Vector2Int> auditedSavedCoordinates = new HashSet<Vector2Int>();
            HashSet<int> liveItemLanes = new HashSet<int>();

            terrain.CopyLoadedBlocks(loadedBlocks);
            BlockStateStore stateStore = terrain.ConveyorDiagnosticsStateStore;
            if (stateStore != null)
            {
                stateStore.CopySavedConveyorItemCoordinates(savedCoordinates);
            }

            for (int i = 0; i < loadedBlocks.Count; i++)
            {
                Block block = loadedBlocks[i];
                if (block == null || !block.IsRuntimeConveyor)
                {
                    continue;
                }

                report.LoadedConveyorBlocks++;
                AuditLoadedConveyor(block, report, liveItemLanes);

                if (stateStore != null
                    && stateStore.TryGetConveyorItems(block.Coordinate, out List<ConveyorItemLaneSaveState> savedLanes))
                {
                    auditedSavedCoordinates.Add(block.Coordinate);
                    AuditSavedConveyor(
                        block.Coordinate,
                        savedLanes,
                        block,
                        liveItemLanes,
                        report);
                }
            }

            if (stateStore != null)
            {
                for (int i = 0; i < savedCoordinates.Count; i++)
                {
                    Vector2Int coordinate = savedCoordinates[i];
                    if (auditedSavedCoordinates.Contains(coordinate)
                        || !stateStore.TryGetConveyorItems(coordinate, out List<ConveyorItemLaneSaveState> savedLanes))
                    {
                        continue;
                    }

                    Block loadedBlock = null;
                    terrain.TryGetLoadedBlock(coordinate, out loadedBlock);
                    AuditSavedConveyor(
                        coordinate,
                        savedLanes,
                        loadedBlock,
                        null,
                        report);
                }
            }

            report.DistinctItems = Math.Max(
                0,
                report.LiveItems + report.SavedItems - report.DuplicateResidencyCount);
            report.AuthoritativeItems = terrain.GetConveyorItemCount();
            if (report.AuthoritativeItems != report.DistinctItems)
            {
                AddGlobalError(report, "item_total_mismatch");
            }

            return report;
        }

        private static void AuditLoadedConveyor(
            Block block,
            ConveyorDiagnosticReport report,
            HashSet<int> liveItemLanes)
        {
            liveItemLanes.Clear();
            int laneCount = block.GetRuntimeConveyorLaneCount();
            if (laneCount <= 0 || laneCount > Block.ConveyorCellItemUnit)
            {
                report.InvalidLaneStateCount++;
                AddError(report, "invalid_runtime_lane_count", block.Coordinate, laneCount);
                return;
            }

            for (int laneIndex = 0; laneIndex < laneCount; laneIndex++)
            {
                if (!block.TryGetRuntimeConveyorItemSlotIdAtLane(laneIndex, out int itemId))
                {
                    continue;
                }

                if (itemId >= 0)
                {
                    liveItemLanes.Add(laneIndex);
                    report.LiveItems++;
                }

                if (!block.TryGetRuntimeConveyorSuccessorLane(
                        laneIndex,
                        out Block destinationBlock,
                        out int destinationLaneIndex))
                {
                    continue;
                }

                if (destinationBlock == null
                    || !destinationBlock.IsRuntimeConveyor
                    || destinationLaneIndex < 0
                    || !destinationBlock.TryGetRuntimeConveyorItemSlotIdAtLane(destinationLaneIndex, out _)
                    || (destinationBlock == block && destinationLaneIndex == laneIndex))
                {
                    report.InvalidLinkCount++;
                    AddError(report, "invalid_runtime_link", block.Coordinate, laneIndex);
                }
            }
        }

        private static void AuditSavedConveyor(
            Vector2Int coordinate,
            IReadOnlyList<ConveyorItemLaneSaveState> lanes,
            Block loadedBlock,
            ISet<int> liveItemLanes,
            ConveyorDiagnosticReport report)
        {
            if (lanes == null || lanes.Count <= 0)
            {
                return;
            }

            report.SavedConveyorBlocks++;
            HashSet<int> savedItemLanes = new HashSet<int>();
            bool hasLoadedRuntimeConveyor = loadedBlock != null && loadedBlock.IsRuntimeConveyor;

            if (loadedBlock != null && !loadedBlock.IsRuntimeConveyor)
            {
                AddWarning(report, "saved_items_on_non_conveyor", coordinate, -1);
            }

            for (int i = 0; i < lanes.Count; i++)
            {
                ConveyorItemLaneSaveState lane = lanes[i];
                if (lane == null
                    || lane.itemId < 0
                    || lane.laneIndex < 0
                    || lane.laneIndex >= Block.ConveyorCellItemUnit
                    || (hasLoadedRuntimeConveyor
                        && !loadedBlock.TryGetRuntimeConveyorItemSlotIdAtLane(lane.laneIndex, out _)))
                {
                    report.InvalidLaneStateCount++;
                    AddError(
                        report,
                        "invalid_saved_lane",
                        coordinate,
                        lane != null ? lane.laneIndex : -1);
                    continue;
                }

                report.SavedItems++;
                if (!savedItemLanes.Add(lane.laneIndex))
                {
                    report.InvalidLaneStateCount++;
                    AddError(report, "duplicate_saved_lane", coordinate, lane.laneIndex);
                }

                if (liveItemLanes != null && liveItemLanes.Contains(lane.laneIndex))
                {
                    report.DuplicateResidencyCount++;
                    AddError(report, "live_saved_lane_overlap", coordinate, lane.laneIndex);
                }

                AuditSavedMotion(coordinate, lane, report);
            }
        }

        private static void AuditSavedMotion(
            Vector2Int coordinate,
            ConveyorItemLaneSaveState lane,
            ConveyorDiagnosticReport report)
        {
            if (!lane.hasMotion)
            {
                return;
            }

            bool invalidMotion = !IsFinite(lane.progress)
                || lane.progress < -MotionProgressEpsilon
                || lane.progress > 1f + MotionProgressEpsilon
                || !IsFinite(lane.pathLength)
                || lane.pathLength <= MotionProgressEpsilon
                || lane.destinationLaneIndex < 0
                || lane.destinationLaneIndex >= Block.ConveyorCellItemUnit;

            if (lane.useCornerMotion)
            {
                invalidMotion |= lane.sourceLaneIndex < 0
                    || lane.sourceLaneIndex >= Block.ConveyorCellItemUnit;
            }

            if (lane.cornerContinuationActive)
            {
                invalidMotion |= !IsFinite(lane.cornerContinuationStartProgress)
                    || lane.cornerContinuationStartProgress < -MotionProgressEpsilon
                    || lane.cornerContinuationStartProgress > 1f + MotionProgressEpsilon
                    || !IsFinite(lane.cornerContinuationPathLength)
                    || lane.cornerContinuationPathLength <= MotionProgressEpsilon
                    || lane.cornerContinuationSourceLaneIndex < 0
                    || lane.cornerContinuationSourceLaneIndex >= Block.ConveyorCellItemUnit
                    || lane.cornerContinuationDestinationLaneIndex < 0
                    || lane.cornerContinuationDestinationLaneIndex >= Block.ConveyorCellItemUnit;
            }

            if (!invalidMotion)
            {
                return;
            }

            report.InvalidLaneStateCount++;
            AddError(report, "invalid_saved_motion", coordinate, lane.laneIndex);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static void AddError(
            ConveyorDiagnosticReport report,
            string code,
            Vector2Int coordinate,
            int laneIndex)
        {
            report.ErrorCount++;
            if (report.ErrorCount == 1)
            {
                report.FirstIssue = FormatIssue(code, coordinate, laneIndex);
            }
        }

        private static void AddGlobalError(ConveyorDiagnosticReport report, string code)
        {
            report.ErrorCount++;
            if (report.ErrorCount == 1)
            {
                report.FirstIssue = code;
            }
        }

        private static void AddWarning(
            ConveyorDiagnosticReport report,
            string code,
            Vector2Int coordinate,
            int laneIndex)
        {
            report.WarningCount++;
            if (!string.IsNullOrEmpty(report.FirstIssue))
            {
                return;
            }

            report.FirstIssue = FormatIssue(code, coordinate, laneIndex);
        }

        private static string FormatIssue(string code, Vector2Int coordinate, int laneIndex)
        {
            return laneIndex >= 0
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}@{1},{2}:lane{3}",
                    code,
                    coordinate.x,
                    coordinate.y,
                    laneIndex)
                : string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}@{1},{2}",
                    code,
                    coordinate.x,
                    coordinate.y);
        }
    }
}
