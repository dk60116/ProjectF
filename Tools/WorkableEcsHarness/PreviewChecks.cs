using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.MapObjects;

// Run production selection/request construction with managed component and renderer boundaries.
public interface IMapObjectTarget { bool IsTargetActive { get; } int ResolveItemId(); }
public static class PreviewEngine { public static bool IsPlaying = true; }
public sealed class BagSlot { public bool IsCraftingExpanded; }
public sealed class GameManager
{
    public static readonly GameManager Instance = new();
    public bool InstallationPlacementActive = true, MapEditActive;
}
public sealed class TerrainGenerator
{
    public static TerrainGenerator Active;
    public bool IsBenchmarkPlacementInProgress;
}
public sealed class WorkableObject : IWorkableTarget
{
    internal static WorkableRangeIndex RangeIndex => WorkableVisualProbe.RangeIndex;
    public bool Active = true, isActiveAndEnabled = true;
    public bool IsTargetActive => Active;
    public bool ShowWorkableRange { get; set; } = true;
    public bool GlobalRangeVisualSuppressed { get; set; }
    public float RangeVisualYOffset => .04f;
    public long WorkablePlacementSequence { get; set; }
    public Vector2Int AnchorCoordinate => default;
    public IReadOnlyList<Vector2Int> RuntimeOccupiedCoordinates => Array.Empty<Vector2Int>();
    public Bounds Bounds;
    public int ResolveItemId() => 22;
    public bool TryGetWorkableRangeBounds(out Bounds bounds) { bounds = Bounds; return Bounds.size.x > 0; }
    public void SetSelectedRangeVisualRequested(bool selected) => WorkableVisualProbe.SetTargetSelected(this, selected);
}
public sealed class WorkableObjectRangeVisual
{
    public readonly List<WorkableObjectRangeVisualRequest> Requests = new();
    public bool Visible;
    public void Configure(IReadOnlyList<WorkableObjectRangeVisualRequest> requests)
    { Requests.Clear(); for (int i = 0; i < requests.Count; i++) Requests.Add(requests[i]); }
    public void SetVisible(bool visible) => Visible = visible;
}
public static partial class WorkableVisualProbe
{
    internal static void EnsureRangeVisualHost() => sharedRangeVisual ??= new();
    internal static WorkableObjectRangeVisual Visual => sharedRangeVisual;
}
static class PreviewChecks
{
    private static int checks;
    private static void Require(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    private static WorkableObject At(float x, bool enabled = true) => new()
    { isActiveAndEnabled = enabled, Bounds = new Bounds(new Vector3(x, 0, 0), new Vector3(2, .01f, 2)) };
    public static void Main()
    {
        var preview = At(10, false); preview.GlobalRangeVisualSuppressed = true;
        WorkableVisualProbe.RegisterTarget(preview); preview.SetSelectedRangeVisualRequested(true);
        WorkableVisualProbe.UpdateRangeVisual();
        Require(WorkableVisualProbe.RangeIndex.Targets.Count == 0, "disabled blueprint never enters crafting range index");
        Require(WorkableVisualProbe.Visual.Visible && WorkableVisualProbe.Visual.Requests.Count == 1,
            "selected disabled blueprint renders its own workable area without a registered group");
        Require(WorkableVisualProbe.Visual.Requests[0].Center == preview.Bounds.center, "preview area uses current pose");
        preview.Bounds = At(20).Bounds; WorkableVisualProbe.RegisterTarget(preview);
        WorkableVisualProbe.UpdateRangeVisual();
        Require(WorkableVisualProbe.Visual.Requests[0].Center.x == 20, "moving preview moves the rendered range");
        var second = At(40, false); second.GlobalRangeVisualSuppressed = true;
        WorkableVisualProbe.RegisterTarget(second); second.SetSelectedRangeVisualRequested(true);
        WorkableVisualProbe.UpdateRangeVisual();
        Require(WorkableVisualProbe.Visual.Requests.Count == 2, "multiple blueprint workables render independent areas");
        preview.SetSelectedRangeVisualRequested(false); WorkableVisualProbe.UpdateRangeVisual();
        Require(WorkableVisualProbe.Visual.Requests.Count == 1 && WorkableVisualProbe.Visual.Requests[0].Center.x == 40,
            "unselected globally suppressed preview disappears");
        second.Active = false; WorkableVisualProbe.UnregisterTarget(second); WorkableVisualProbe.UpdateRangeVisual();
        Require(!WorkableVisualProbe.Visual.Visible, "removed last blueprint hides shared range renderer");
        preview.isActiveAndEnabled = true; preview.GlobalRangeVisualSuppressed = false;
        var neighbor = At(22); WorkableVisualProbe.RegisterTarget(preview); WorkableVisualProbe.RegisterTarget(neighbor);
        preview.SetSelectedRangeVisualRequested(true); WorkableVisualProbe.UpdateRangeVisual();
        Require(WorkableVisualProbe.Visual.Visible && WorkableVisualProbe.Visual.Requests.Count == 2,
            "installed selection still renders connected group with no duplicate root");
        WorkableVisualProbe.SetInstallOrEditWorkableSelectionRangeVisualsRequested(true); WorkableVisualProbe.UpdateRangeVisual();
        Require(WorkableVisualProbe.Visual.Requests.Count == 2, "global and selected requests deduplicate installed areas");
        preview.isActiveAndEnabled = false; preview.GlobalRangeVisualSuppressed = true;
        WorkableVisualProbe.RegisterTarget(preview); WorkableVisualProbe.UpdateRangeVisual();
        Require(WorkableVisualProbe.RangeIndex.Targets.Count == 1 && WorkableVisualProbe.Visual.Requests.Count == 2,
            "disabled edit preview removes installed access but retains requested visual");
        WorkableVisualProbe.UnregisterTarget(preview); WorkableVisualProbe.UnregisterTarget(neighbor);
        WorkableVisualProbe.UpdateRangeVisual();
        Require(!WorkableVisualProbe.Visual.Visible, "pool cleanup removes preview selection and visual");
        Console.WriteLine($"Workable blueprint preview: {checks} checks passed (native lifecycle/rendering boundaries doubled).");
    }
}
