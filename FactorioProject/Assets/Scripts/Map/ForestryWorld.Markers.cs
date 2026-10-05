using System.Collections.Generic;
using UnityEngine;

namespace ProjectF.MapObjects
{
public sealed partial class ForestryWorld
{
    private ForestryInstance selectedMarkerInstance;
    private readonly HashSet<ForestryInstance> visibleMarkers = new HashSet<ForestryInstance>();
    private readonly HashSet<ForestryInstance> markerCandidates = new HashSet<ForestryInstance>();
    private readonly List<ForestryInstance> markerScratch = new List<ForestryInstance>();
    private bool markersDirty = true;
    public void SetSelectedMarkerInstance(ForestryInstance instance)
    { if (!ReferenceEquals(selectedMarkerInstance, instance)) { selectedMarkerInstance = instance; markersDirty = true; } }
    internal bool RefreshAreaMarkers(in AreaMarkerVisibilityContext context)
    {
        bool changed = markersDirty; markersDirty = false; markerCandidates.Clear();
        foreach (var instance in visibleMarkers) markerCandidates.Add(instance);
        if (context.ShowAll) foreach (var instance in instances) markerCandidates.Add(instance);
        else if (context.HasPlayer)
        { BuildNearby(context.PlayerPosition, markerScratch); foreach (var instance in markerScratch) markerCandidates.Add(instance); }
        if (selectedMarkerInstance != null) markerCandidates.Add(selectedMarkerInstance);
        foreach (var instance in markerCandidates)
        {
            bool show = instance.IsRuntimeActive && !instance.PlacementPresentationSuppressed
                && AreaMarkerVisibilityContext.ShouldShow(3.6f, false, instance == selectedMarkerInstance,
                    context.ShowAll, context.HasPlayer, context.PlayerPosition, instance.WorldPosition);
            if (show) changed |= visibleMarkers.Add(instance); else changed |= visibleMarkers.Remove(instance);
        }
        return changed;
    }
    internal void AppendAreaMarkers(AreaMarkerRenderer renderer)
    {
        if (Terrain.IsBenchmarkPlacementInProgress) return;
        Sprite arrow = UIManager.Instance != null ? UIManager.Instance.ArrowImage : null;
        foreach (var instance in visibleMarkers)
        {
            if (instance.Prototype is LoggingMachine logger)
            {
                for (int i = 0; i < LoggingMachine.HarvestDirectionCount; i++)
                    AppendMarker(renderer, instance, LoggingMachine.GetHarvestCoordinate(instance.AnchorCoordinate,
                        instance.Placement.quarterTurns, i), logger.HarvestMarkerIcon, 0);
                continue;
            }
            if (!(instance.Prototype is SeedPlanter planter) || instance.Placement.inputOutputState == null) continue;
            foreach (var coordinate in instance.Placement.inputOutputState.gridCoordinates)
            {
                if (!planter.TryGetRectGridBlockPlacementAtCoordinate(planter, instance.AnchorCoordinate,
                    instance.Placement.quarterTurns, coordinate, out var placement)
                    || !InputOutputModule.IsInputOutputAreaBlockType(placement.blockType)) continue;
                bool output = InputOutputModule.IsOutputBlockType(placement.blockType);
                Sprite icon = InputOutputModule.IsInputEnergyBlockType(placement.blockType) ? instance.Template.EnergyMarkerIcon
                    : output && planter.OutputAreaMarkerIcon != null ? planter.OutputAreaMarkerIcon : arrow;
                Vector3 direction = new Vector3(coordinate.x, instance.WorldPosition.y, coordinate.y) - instance.WorldPosition;
                if (!output) direction = -direction;
                float rotation = icon == arrow ? -Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg : 0;
                AppendMarker(renderer, instance, coordinate, icon, rotation);
            }
        }
    }
    private static void AppendMarker(AreaMarkerRenderer renderer, ForestryInstance instance, Vector2Int coordinate, Sprite icon, float rotation)
    {
        if (icon == null) return;
        var position = new Vector3(coordinate.x, instance.WorldPosition.y + 0.08f, coordinate.y);
        renderer.Append(new AreaMarkerSpawnRequest(position, icon, rotation), Matrix4x4.Translate(position), 0, false, false);
    }
}
}
