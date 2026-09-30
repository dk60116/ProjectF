using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

static class Checks
{
    private static int passed;
    private static readonly Sprite Icon = new Sprite { texture = new Texture() };
    private static readonly Sprite Background = new Sprite { texture = new Texture() };
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); passed++; }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static void Call(object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    private static void Tick(AreaMarkerRenderer renderer) { Graphics.Calls.Clear(); Call(renderer, "LateUpdate"); }
    private static void Near(Vector3 actual, Vector3 expected, string message)
    {
        Require(Math.Abs(actual.x - expected.x) < .0001f && Math.Abs(actual.y - expected.y) < .0001f
            && Math.Abs(actual.z - expected.z) < .0001f, message + $" actual=({actual.x},{actual.y},{actual.z})");
    }
    private static List<AreaMarkerSpawnRequest> Requests(int count, Vector3 position = default, Sprite icon = null, float rotation = 0)
    {
        var result = new List<AreaMarkerSpawnRequest>();
        for (int i = 0; i < count; i++) result.Add(new AreaMarkerSpawnRequest(position, icon ?? Icon, rotation));
        return result;
    }
    private static AreaMarkerRenderer Setup()
    {
        var template = new AreaMarker();
        var icon = new SpriteRenderer { sprite = Icon, sortingOrder = 10, color = new Color(1, 1, 1, .5882353f) };
        icon.transform.parent = template.transform;
        icon.transform.localRotation = Quaternion.Euler(90, 0, 0);
        icon.transform.localScale = new Vector3(2, 3, 1); // Non-uniform scale catches rotation/scale composition errors.
        var background = new SpriteRenderer { sprite = Background, sortingOrder = 11, color = new Color(0, 0, 0, .39215687f) };
        background.transform.parent = template.transform;
        background.transform.localRotation = Quaternion.Euler(90, 0, 0);
        template.Renderers = new[] { background, icon };
        Set(template, "icon", icon);
        Resources.Template = template;
        GameManager.Instance = new GameManager { Player = new Player() };
        return new AreaMarkerRenderer();
    }
    public static int Main()
    {
        VisibilityRules();
        BatchAndLifecycle();
        PreviewIsolationAndGeometry();
        RenderModesAndPartitions();
        ProductionOutputIcons();
        LargeBatch();
        Console.WriteLine($"PASS AreaMarker harness: {passed} checks (CPU facade; no Unity/GPU execution)");
        return 0;
    }

    private static void VisibilityRules()
    {
        Vector3 origin = Vector3.zero;
        Require(AreaMarkerVisibilityContext.ShouldShow(5, false, false, false, true, new Vector3(3, 100, 4), origin), "Range includes boundary and ignores elevation");
        Require(!AreaMarkerVisibilityContext.ShouldShow(5, false, false, false, true, new Vector3(3.01f, 0, 4), origin), "Outside range hidden");
        Require(!AreaMarkerVisibilityContext.ShouldShow(5, false, false, false, false, origin, origin), "Missing player hidden");
        Require(AreaMarkerVisibilityContext.ShouldShow(0, false, false, false, false, origin, origin), "Unlimited range visible without player");
        Require(AreaMarkerVisibilityContext.ShouldShow(5, true, false, false, false, origin, origin), "Forced preview visible without player");
        Require(AreaMarkerVisibilityContext.ShouldShow(5, false, true, false, false, origin, origin), "Selected object visible without player");
        Require(AreaMarkerVisibilityContext.ShouldShow(5, false, false, true, false, origin, origin), "Edit/placement mode visible without player");
    }

    private static AreaMarkerSpawnRequest Marker(InputOutputModuleAreaMarkerController owner) =>
        ((List<AreaMarkerSpawnRequest>)typeof(InputOutputModuleAreaMarkerController)
            .GetField("requests",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner))[0];

    private static void ProductionOutputIcons()
    {
        AreaMarkerRenderer renderer = Setup();
        var inputIcon = new Sprite { texture=new Texture() };
        var lubricantIcon = new Sprite { texture=new Texture() };
        var nextIcon = new Sprite { texture=new Texture() };
        var solidIcon = new Sprite { texture=new Texture() };
        var fallbackIcon = new Sprite { texture=new Texture() };
        InputOutputModule.Definitions.Clear();
        InputOutputModule.Definitions[7]=new ItemDefinition { icon=inputIcon };
        InputOutputModule.Definitions[8]=new ItemDefinition { icon=lubricantIcon };
        InputOutputModule.Definitions[9]=new ItemDefinition { icon=nextIcon };
        InputOutputModule.Definitions[10]=new ItemDefinition();
        InputOutputModule.Definitions[31]=new ItemDefinition { icon=solidIcon, fluid=false };
        var outputCoordinate = new Vector2Int(1,0);
        var inputCoordinate = new Vector2Int(-1,0);
        var machine = new ProductionMachine { SelectedOutput=8, StoredFluidItemId=7 };
        machine.ConfiguredOutputIds.UnionWith(new[] {7,8,9});
        machine.OutputCoordinates.Add(outputCoordinate);
        var builder = new PlacementMarkerProbe { FallbackIcon=fallbackIcon };
        builder.Cells.Add(new PlacementMarkerProbe.RectGridPlacementCell
        {
            coordinate=outputCoordinate,
            placement=new PlacementMarkerProbe.CellPlacement { itemDefinition=InputOutputModule.Definitions[7] }
        });
        var requests=builder.Build(machine,Icon);
        Require(requests.Count==1 && requests[0].Icon==lubricantIcon,
            "output builder chooses selected Lubricant instead of lower-ID Heavy Oil or stale cell input");
        Require(requests[0].UsesRuntimeFluidIcon && requests[0].RuntimeFluidCoordinate==outputCoordinate,
            "production output icon has a live coordinate binding");
        Require(requests[0].OverlayIcon==Icon && requests[0].OverlayIconRotationZ==90f,
            "output arrow still points away from the machine");
        Require(requests[0].FallbackIcon==fallbackIcon,"cleared recipe uses a neutral fallback instead of its old output icon");
        var preview=builder.Build(machine,Icon,true);
        Require(!preview[0].UsesRuntimeFluidIcon && preview[0].Icon==lubricantIcon,
            "placement preview shows the recipe output without sampling the world at its unregistered port");
        PipeWorld.Current=new PipeWorld();
        var wrongOutputPipe=new PipeRuntimeRecord { FluidItemId=7 };
        PipeWorld.Current.Records[outputCoordinate]=wrongOutputPipe;
        PipeWorld.Current.Records[inputCoordinate]=new PipeRuntimeRecord { FluidItemId=7 };
        var owner=new InputOutputModuleAreaMarkerController();
        owner.Configure(renderer,requests,true);
        Set(owner,"runtimeFluidModule",machine);
        Time.unscaledTime=1f; Tick(renderer);
        Require(Marker(owner).RuntimeFluidItemId==8 && Marker(owner).Icon==lubricantIcon,
            "output controller uses its recipe despite input reserve and wrong overlapping pipe fluid");
        Require(wrongOutputPipe.Queries==0,"output marker cannot import input identity from the pipe network");
        machine.SelectedOutput=9;
        Time.unscaledTime+=.21f; Tick(renderer);
        Require(Marker(owner).RuntimeFluidItemId==9 && Marker(owner).Icon==nextIcon,
            "selected output changes update the existing marker without placement rebuild");
        machine.ActiveOutput=8;
        Time.unscaledTime+=.21f; Tick(renderer);
        Require(Marker(owner).RuntimeFluidItemId==8 && Marker(owner).Icon==lubricantIcon,
            "active batch keeps its output icon while another target is selected");
        machine.ActiveOutput=-1;
        Time.unscaledTime+=.21f; Tick(renderer);
        Require(Marker(owner).RuntimeFluidItemId==9,"after the batch drains the selected output resumes");
        machine.SelectedOutput=-1;
        Time.unscaledTime+=.21f; Tick(renderer);
        Require(Marker(owner).RuntimeFluidItemId==-1 && Marker(owner).Icon==fallbackIcon,
            "cleared recipe cannot fall back to stored Heavy Oil or a previous output");
        machine.SelectedOutput=10;
        Time.unscaledTime+=.21f; Tick(renderer);
        Require(Marker(owner).RuntimeFluidItemId==10 && Marker(owner).Icon==fallbackIcon,
            "missing output sprite uses the neutral fallback");
        machine.SelectedOutput=31;
        var solidRequests=builder.Build(machine,Icon);
        Require(solidRequests[0].Icon==fallbackIcon && solidRequests[0].OverlayIcon==Icon,
            "solid recipe uses a neutral PipeOutput icon and keeps its direction arrow");
        var solidPreview=builder.Build(machine,Icon,true);
        Require(solidPreview[0].Icon==fallbackIcon,
            "solid recipe cannot display an item icon on the PipeOutput preview");
        Time.unscaledTime+=.21f; Tick(renderer);
        Require(Marker(owner).RuntimeFluidItemId==-1 && Marker(owner).Icon==fallbackIcon,
            "existing PipeOutput replaces fluid icons with neutral fallback for solid recipes");
        machine.ActiveOutput=8;
        Time.unscaledTime+=.21f; Tick(renderer);
        Require(Marker(owner).RuntimeFluidItemId==8 && Marker(owner).Icon==lubricantIcon,
            "active fluid batch retains its icon when the next selected recipe is solid");
        machine.ActiveOutput=31; machine.SelectedOutput=8;
        Time.unscaledTime+=.21f; Tick(renderer);
        Require(Marker(owner).RuntimeFluidItemId==-1 && Marker(owner).Icon==fallbackIcon,
            "active solid batch cannot display an item icon even with a fluid recipe selected");
        machine.ActiveOutput=-1;
        Time.unscaledTime+=.21f; Tick(renderer);
        Require(Marker(owner).RuntimeFluidItemId==8 && Marker(owner).Icon==lubricantIcon,
            "PipeOutput restores fluid icons after the solid batch finishes");
        var inputRequest=AreaMarkerSpawnRequest.CreateRuntimeFluid(new Vector3(-1,0,0),inputCoordinate,fallbackIcon);
        owner.Configure(renderer,new[] {inputRequest},true);
        Set(owner,"runtimeFluidModule",machine);
        Time.unscaledTime+=.21f; Tick(renderer);
        Require(Marker(owner).RuntimeFluidItemId==7 && Marker(owner).Icon==inputIcon,
            "input area still displays its incoming fluid");
        var ordinary=new InputOutputModule(); ordinary.ConfiguredOutputIds.Add(8);
        var ordinaryRequests=builder.Build(ordinary,Icon);
        Require(!ordinaryRequests[0].UsesRuntimeFluidIcon && ordinaryRequests[0].Icon==inputIcon,
            "other installations retain their explicit per-port item icon");
        owner.Configure(null,null); Call(renderer,"OnDestroy");
        PipeWorld.Current=null; InputOutputModule.Definitions.Clear();
    }

    private static void BatchAndLifecycle()
    {
        AreaMarkerRenderer renderer = Setup();
        var owner = new InputOutputModuleAreaMarkerController();
        List<AreaMarkerSpawnRequest> requests = Requests(1000);
        owner.Configure(renderer, requests);
        Tick(renderer);
        Require(renderer.RegisteredMarkerCount == 1000 && renderer.VisibleMarkerCount == 1000, "1000 markers registered and visible");
        Require(renderer.BatchCount == 2 && Graphics.Calls.Count == 2, "1000 markers batch into two texture layers");
        Require(Graphics.Calls.All(c => c.Mesh.Vertices.Count == 4000 && c.Mesh.Triangles.Count == 6000), "All geometry and triangle offsets preserved");
        int rebuilds = renderer.MeshRebuildCount;
        Mesh[] meshes = Graphics.Calls.Select(c => c.Mesh).ToArray();
        int iconReads = Icon.GeometryReads;
        for (int i = 0; i < 50; i++)
        {
            owner.Configure(renderer, requests);
            Tick(renderer);
        }
        Require(renderer.MeshRebuildCount == rebuilds && meshes.All(m => m.Uploads == 1), "Unchanged configure/frame does not rebuild/upload");
        Require(Icon.GeometryReads == iconReads, "Sprite geometry arrays cached");
        requests.Clear();
        Tick(renderer);
        Require(renderer.RegisteredMarkerCount == 1000, "Caller scratch list copied");
        GameManager.Instance.Player.transform.position = new Vector3(1, 0, 0);
        Tick(renderer);
        Require(renderer.MeshRebuildCount == rebuilds, "Player movement inside same visibility range avoids rebuild");
        GameManager.Instance.Player.transform.position = new Vector3(20, 0, 0);
        Tick(renderer);
        Require(renderer.VisibleMarkerCount == 0 && Graphics.Calls.Count == 0 && renderer.BatchCount == 0, "Out-of-range meshes removed");
        Require(meshes.All(m => m.Destroyed), "Unused native meshes disposed");
        owner.SetSelectionVisibilityRequested(true);
        Require(owner.ShouldShowLinkedUi(), "Selection immediately updates linked UI predicate");
        Tick(renderer);
        Require(renderer.VisibleMarkerCount == 1000, "Selected distant markers restored");
        owner.isActiveAndEnabled = false;
        Call(owner, "OnDisable");
        Tick(renderer);
        Require(renderer.RegisteredMarkerCount == 0 && Graphics.Calls.Count == 0, "Disable unregisters and removes submissions");
        owner.isActiveAndEnabled = true;
        Call(owner, "OnEnable");
        Call(owner, "OnEnable");
        Tick(renderer);
        Require(renderer.RegisteredMarkerCount == 1000, "Re-enable restores retained data; no duplicate registration");
        owner.SetSelectionVisibilityRequested(false);
        GameManager.Instance.MapEditActive = true;
        Tick(renderer);
        Require(renderer.VisibleMarkerCount == 1000, "Map edit shows all");
        GameManager.Instance.MapEditActive = false;
        GameManager.Instance.InstallationPlacementActive = true;
        Tick(renderer);
        Require(renderer.VisibleMarkerCount == 1000, "Placement mode shows all");
        owner.Configure(null, null);
        Tick(renderer);
        Require(renderer.RegisteredMarkerCount == 0 && renderer.BatchCount == 0 && !owner.ShouldShowLinkedUi(), "Clear removes data and linked UI");
        Call(owner, "OnDestroy");
        Call(renderer, "OnDestroy");
    }

    private static void PreviewIsolationAndGeometry()
    {
        AreaMarkerRenderer renderer = Setup();
        var placed = new InputOutputModuleAreaMarkerController();
        placed.Configure(renderer, Requests(1), true);
        Tick(renderer);
        Mesh[] staticMeshes = Graphics.Calls.Select(c => c.Mesh).ToArray();
        var parent = new Transform { localPosition = new Vector3(10, 0, 0) };
        var preview = new InputOutputModuleAreaMarkerController();
        preview.Configure(renderer, Requests(1, new Vector3(11, 0, 0), rotation: 90), true, 6000, true, parent, .15f);
        Tick(renderer);
        Require(staticMeshes.All(m => m.Uploads == 1), "Adding preview does not rebuild placed meshes");
        var iconCall = Graphics.Calls.Single(c => c.Parameters.material.mainTexture == Icon.texture && c.Parameters.material.DepthTest == 8);
        Near(iconCall.Mesh.Vertices[0], new Vector3(11, .15f, 2), "Icon rotates before non-uniform scale is applied in parent plane");
        Require(iconCall.Mesh.UV[0].Equals(new Vector2(.2f, .3f)), "Atlas UV preserved");
        Require(Math.Abs(iconCall.Mesh.Colors[0].a - .5882353f) < .0001f, "Template icon alpha preserved");
        parent.localPosition = new Vector3(12, 0, 0);
        Tick(renderer);
        iconCall = Graphics.Calls.Single(c => c.Parameters.material.mainTexture == Icon.texture && c.Parameters.material.DepthTest == 8);
        Near(iconCall.Mesh.Vertices[0], new Vector3(13, .15f, 2), "Preview follows parent delta rather than double-transforming world position");
        Require(staticMeshes.All(m => m.Uploads == 1), "Moving preview keeps placed uploads unchanged");
        int rebuilds = renderer.MeshRebuildCount;
        Tick(renderer);
        Require(renderer.MeshRebuildCount == rebuilds, "Stationary preview reuses mesh");
        preview.Configure(renderer, Requests(1, new Vector3(13, 0, 0), rotation: 90), true, 6000, true, parent, .15f);
        Tick(renderer);
        iconCall = Graphics.Calls.Single(c => c.Parameters.material.mainTexture == Icon.texture && c.Parameters.material.DepthTest == 8);
        Near(iconCall.Mesh.Vertices[0], new Vector3(13, .15f, 2), "Reconfiguration rebases moving parent correctly");
        preview.Configure(renderer, Requests(1, new Vector3(13, 0, 0)), true);
        Tick(renderer);
        Require(Graphics.Calls.All(c => c.Parameters.material.DepthTest == 4), "Moving-to-static transition removes old preview batches");
        preview.Configure(null, null);
        placed.Configure(null, null);
        Tick(renderer);
        Require(renderer.BatchCount == 0, "All owner removals release batches");
        Call(renderer, "OnDestroy");
    }

    private static void RenderModesAndPartitions()
    {
        AreaMarkerRenderer renderer = Setup();
        var normal = new InputOutputModuleAreaMarkerController();
        var station = new InputOutputModuleAreaMarkerController();
        var preview = new InputOutputModuleAreaMarkerController();
        normal.Configure(renderer, Requests(1), true);
        station.Configure(renderer, Requests(1), true, 6000, false);
        preview.Configure(renderer, Requests(1), true, 6000, true);
        Tick(renderer);
        int[] queues = Graphics.Calls.Select(c => c.Parameters.material.renderQueue).OrderBy(q => q).ToArray();
        Require(queues.SequenceEqual(new[] { 3000, 3001, 4000, 4001, 4999, 5000 }), "Normal/station/preview layers have distinct valid render queues");
        Require(Graphics.Calls.Where(c => c.Parameters.material.renderQueue < 4999).All(c => c.Parameters.material.DepthTest == 4), "World and station markers retain depth testing");
        Require(Graphics.Calls.Where(c => c.Parameters.material.renderQueue >= 4999).All(c => c.Parameters.material.DepthTest == 8), "Preview ignores depth");
        Material[] materials = Graphics.Calls.Select(c => c.Parameters.material).ToArray();
        Mesh[] meshes = Graphics.Calls.Select(c => c.Mesh).ToArray();
        normal.Configure(null, null); station.Configure(null, null); preview.Configure(null, null);
        var sparse = new InputOutputModuleAreaMarkerController();
        sparse.Configure(renderer, new[] {
            new AreaMarkerSpawnRequest(new Vector3(-.1f, 0, 0), null),
            new AreaMarkerSpawnRequest(new Vector3(.1f, 0, 0), null),
            new AreaMarkerSpawnRequest(new Vector3(32, 0, 0), null) }, true);
        Tick(renderer);
        Require(renderer.BatchCount == 3 && Graphics.Calls.All(c => c.Parameters.material.mainTexture == Background.texture), "Negative/positive/distant chunks separate; null icon still draws background");
        Require(meshes.All(m => m.Destroyed || Graphics.Calls.Any(c => c.Mesh == m)), "Unused mode meshes released");
        Call(renderer, "OnDestroy");
        Require(materials.All(m => m.Destroyed), "Runtime material cache disposed with manager");
        Require(!Resources.Source.Destroyed && !Resources.Template.Destroyed, "Source assets remain intact");
    }

    private static void LargeBatch()
    {
        AreaMarkerRenderer renderer = Setup();
        var owner = new InputOutputModuleAreaMarkerController();
        owner.Configure(renderer, Requests(17000), true);
        Tick(renderer);
        Require(renderer.VisibleMarkerCount == 17000 && renderer.BatchCount == 2, "Large same-chunk population still batches");
        Require(Graphics.Calls.All(c => c.Mesh.Vertices.Count == 68000 && c.Mesh.Triangles.Max() == 67999), "Indices cross 16-bit vertex limit correctly");
        Require(Graphics.Calls.All(c => c.Mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32), "32-bit mesh indices selected");
        Call(renderer, "OnDestroy");
    }
}

public partial class PlacementMarkerProbe
{
    public sealed class CellPlacement { public ItemDefinition itemDefinition; }
    public struct RectGridPlacementCell { public Vector2Int coordinate; public CellPlacement placement; }
    public readonly List<RectGridPlacementCell> Cells = new();
    private readonly List<RectGridPlacementCell> areaMarkerPipeOutputCellScratch = new();
    private readonly HashSet<int> areaMarkerOutputItemIdsScratch = new();
    private static readonly int[] PipeOutputMarkerRectGridBlockTypes = Array.Empty<int>();
    public Sprite FallbackIcon;
    private Sprite ResolveFallbackPipePassMarkerIcon() => FallbackIcon;
    private static ItemDefinition ResolveItemDefinition(int id) => InputOutputModule.ResolveItemDefinition(id);
    private static bool TryGetInputOutputModule(MapObject source,out InputOutputModule module)
    { module=source as InputOutputModule; return module!=null; }
    private bool TryBuildRectGridPlacementCells(Vector2Int anchor,MapObject source,int turns,int[] types,List<RectGridPlacementCell> cells)
    { cells.AddRange(Cells); return cells.Count>0; }
    private static Vector3 GetAreaMarkerWorldPosition(Vector2Int coordinate) => new(coordinate.x,0,coordinate.y);
    private static Vector3 ResolveNearestAreaMarkerReferenceWorldPosition(Vector3 marker,IReadOnlyList<Vector3> references) => references[0];
    private static float GetArrowMarkerRotationZ(Vector3 start,Vector3 end) => end.x>start.x ? 90f : 270f;
    public List<AreaMarkerSpawnRequest> Build(MapObject source,Sprite arrow,bool preview=false)
    {
        var requests=new List<AreaMarkerSpawnRequest>();
        AddPipeOutputAreaMarkerRequests(requests,default,source,0,arrow,new[] {Vector3.zero},preview);
        return requests;
    }
}
