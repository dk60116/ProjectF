using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectF.Rendering;

// Only component/object lifetime and renderer scheduling cross native engine boundaries.
sealed class PreviewObject { public bool activeInHierarchy = true; }
sealed class PreviewTransform { public Vector3 position; public Quaternion rotation = Quaternion.identity; }
sealed class UtilityPole
{
    public bool isActiveAndEnabled;
    public readonly PreviewObject gameObject = new();
    public readonly PreviewTransform transform = new();
    public void Configure(ProjectF.Power.UtilityPoleRuntime runtime) { }
}
sealed class PreviewPlacement { public Vector3 worldPosition; public Quaternion worldRotation = Quaternion.identity; }
sealed class VirtualObjectWorld
{
    public static VirtualObjectWorld Current;
    public bool Alive = true;
    public bool IsHandleAlive(int handle) => Alive;
}
namespace ProjectF.Rendering
{
    static class UtilityPoleWireRenderer
    {
        internal static readonly HashSet<UtilityPoleWire> Wires = new();
        internal static void Register(UtilityPoleWire wire) => Wires.Add(wire);
        internal static void Unregister(UtilityPoleWire wire) => Wires.Remove(wire);
        internal static void Invalidate() { }
    }
}
namespace ProjectF.Power
{
    partial class UtilityPoleRuntime
    {
        private readonly UtilityPole presentation;
        private PreviewPlacement Placement;
        private bool Registered;
        private int Handle;
        private bool PlacementPresentationSuppressed;
        private float presentationScale = 1f, lineWidth = .025f, lineSagDepth = .06f, connectionLineSagDepth = .18f;
        private int lineCurveSegments = 8, usedConnectionUtilityPoleWireCount, linePointAConnectionCount, linePointBConnectionCount;
        private Color lineColor = new(.05f, .04f, .035f, 1f);
        private int ConnectionRadiusCells => 5;
        private UtilityPoleLinePoint linePointCenter, linePointA, linePointB;
        private UtilityPoleWire lineCenterToA, lineCenterToB;
        private readonly List<UtilityPoleWire> connectionUtilityPoleWires = new();
        private static readonly Dictionary<UtilityPoleRuntime, PreviewPoleRuntime> previewPoleRuntimes = new();
        private static readonly HashSet<UtilityPoleRuntime> activePoles = new();
        private static readonly List<UtilityPoleRuntime> visualPoleScratch = new();
        private static readonly Dictionary<object, object> previewConsumerRuntimes = new();
        private static readonly List<object> previewPoleConnections = new();
        private static bool previewPoleConnectionsDirty, connectionLineVisualsDirty, previewConsumerLineVisualsDirty;
        private static bool deferredPreviewConsumerLineVisualRefreshRequested;
        private static int refreshRequests, checks;
        private static void RequestDeferredConnectionLineVisualRefresh() => refreshRequests++;
        private static void HidePreviewConsumerUtilityPoleWires() { }

        private UtilityPoleRuntime(UtilityPole source)
        {
            presentation = source;
            linePointCenter = new(this) { Local = new Vector3(0, 2, 0) };
            linePointA = new(this) { Local = new Vector3(.4f, 2, 0) };
            linePointB = new(this) { Local = new Vector3(-.4f, 2, 0) };
        }
        private static void Check(bool pass, string message)
        {
            checks++;
            if (!pass) throw new Exception(message);
        }
        internal static void Run()
        {
            var component = new UtilityPole(); // ConfigureInstallPreview disables MonoBehaviours.
            var pole = new UtilityPoleRuntime(component);
            Check(!pole.IsRuntimeActive, "A disabled preview must not join simulation");
            for (int q = 0; q < 4; q++)
            {
                component.transform.position = new Vector3(q * 3, 0, -q);
                component.transform.rotation = new Quaternion(0, MathF.Sin(q * MathF.PI / 4), 0, MathF.Cos(q * MathF.PI / 4));
                RegisterBlueprintPreview(pole, new Vector2Int(q * 3, -q), q, q == 3);
                CleanupPreviewPoleRuntimes();
                Check(IsValidPreviewPole(pole), "Disabled component removed from preview registry");
                Check(!pole.IsRuntimeActive && activePoles.Count == 0, "Preview entered the actual power network");
                BuildVisualPoleScratch();
                Check(visualPoleScratch.Count == 1 && visualPoleScratch[0] == pole, "Preview missing from wire refresh");
                Check(pole.lineCenterToA.Visible && pole.lineCenterToB.Visible, "Internal preview wires hidden");
                Check(pole.lineCenterToA.Start == pole.linePointCenter.Position && pole.lineCenterToA.End == pole.linePointA.Position,
                    "Moving/rotating preview did not refresh endpoints");
                Check(HasTopologyReplacementPreview() == (q == 3), "Disabled replacement preview ignored");
                pole.usedConnectionUtilityPoleWireCount = 0;
                pole.RenderConnectionLine(pole.linePointA, new Vector3(5, 2, 5));
                Check(pole.connectionUtilityPoleWires[0].Visible, "External preview wire hidden");
                int requestCount = refreshRequests, wireCount = UtilityPoleWireRenderer.Wires.Count;
                RegisterBlueprintPreview(pole, new Vector2Int(q * 3, -q), q, q == 3);
                Check(refreshRequests == requestCount && UtilityPoleWireRenderer.Wires.Count == wireCount,
                    "Unchanged registration rebuilt wires");
            }
            pole.PlacementPresentationSuppressed = true;
            pole.RefreshUtilityPoleWires();
            pole.RenderConnectionLine(pole.linePointA, Vector3.zero);
            Check(!pole.lineCenterToA.Visible && !pole.connectionUtilityPoleWires[1].Visible, "Suppressed preview visible");
            pole.PlacementPresentationSuppressed = false;
            pole.lineWidth = 0;
            pole.RefreshUtilityPoleWires();
            Check(!pole.lineCenterToA.Visible, "Zero-width wire visible");
            pole.lineWidth = .025f;
            pole.RefreshUtilityPoleWires();
            UnregisterBlueprintPreview(pole);
            Check(!IsValidPreviewPole(pole) && !pole.lineCenterToA.Visible && !pole.connectionUtilityPoleWires[0].Visible,
                "Unregistered preview left visible wires");
            RegisterBlueprintPreview(pole, Vector2Int.zero, 0);
            component.gameObject.activeInHierarchy = false;
            CleanupPreviewPoleRuntimes();
            Check(previewPoleRuntimes.Count == 0 && !pole.lineCenterToA.Visible, "Inactive preview was not removed/hidden");
            component.gameObject.activeInHierarchy = true;
            RegisterBlueprintPreview(pole, Vector2Int.zero, 0);
            var second = new UtilityPoleRuntime(new UtilityPole());
            RegisterBlueprintPreview(second, Vector2Int.one, 0);
            ClearBlueprintPreviews();
            Check(previewPoleRuntimes.Count == 0 && !pole.lineCenterToA.Visible && !second.lineCenterToB.Visible,
                "ClearBlueprintPreviews left internal wires visible");

            var installed = new UtilityPoleRuntime(null) { Placement = new PreviewPlacement(), Registered = true };
            VirtualObjectWorld.Current = new VirtualObjectWorld();
            installed.EnsureUtilityPoleWires();
            installed.RefreshUtilityPoleWires();
            Check(installed.IsRuntimeActive && installed.lineCenterToA.Visible, "Data-only installed pole wire hidden");
            Check(!installed.IsPreviewPresentationActive, "Installed pole treated as a preview");
            VirtualObjectWorld.Current.Alive = false;
            installed.RefreshUtilityPoleWires();
            Check(!installed.IsRuntimeActive && !installed.lineCenterToA.Visible, "Dead installed handle kept wire visible");
            Console.WriteLine($"{checks} blueprint wire lifetime/visibility checks passed.");
        }
    }
}
static class Program { static void Main() => ProjectF.Power.UtilityPoleRuntime.Run(); }
