using System.Collections;
using System.Text;
using ProjectF.Benchmark;

// Engine, transport and controls are boundaries; methods under test are extracted
// from the real frontend and runtime. No Windows forms or Unity are launched.
public class ItemDefinition
{
    public int id;
    public string itemName = "Item";
    public bool isFluid;
    public object portableMesh = new(), portableMat = new();
    public static bool IsElectricityItemDefinition(ItemDefinition item) => item.itemName == "Electricity";
}
public class ItemManager { public List<ItemDefinition> ItemDefinitions = new(); }
public static class MapObjectTickManager
{
    public static bool SimulationPaused;
    public static void SetSimulationPaused(bool value) => SimulationPaused = value;
}
public class Block
{
    public int Capacity = 4;
    public bool Reject;
    public int Coordinate, WorldPosition;
    public List<int> Items = new();
    public int GetAvailableConveyorCapacity() => Capacity - Items.Count;
    public bool TryAddConveyorObjectAnimatedAtPlacement(int id, int start, int end, float time, out object result,
        object first, object second, float third, bool fourth, float fifth)
    { result = null; if (Reject || Items.Count >= Capacity) return false; Items.Add(id); return true; }
}
public class TerrainGenerator
{
    public List<Block> Blocks = new();
    public void ClearAllBeltItems(out int a, out int b)
    { a = b = 0; foreach (var block in Blocks) block?.Items.Clear(); }
    public void CopyLoadedBlocks(List<Block> result) => result.AddRange(Blocks);
}
public class Control { public string Text = ""; public bool Enabled; public bool Checked; public decimal Value; }
public enum ProgressBarStyle { Marquee, Continuous }
public class ProgressBar { public int Value; public ProgressBarStyle Style; }
public class ComboBox
{
    public List<object> Items = new();
    public int SelectedIndex = -1;
    public object SelectedItem => SelectedIndex >= 0 ? Items[SelectedIndex] : null;
    public void BeginUpdate() { }
    public void EndUpdate() { }
}
public partial class CatalogProbe
{
    private readonly ComboBox belts = new(), items = new(), objects = new();
    private readonly Control host = new() { Text = "127.0.0.1" }, port = new() { Value = 50877 }, itemSearch = new(), itemStatus = new(),
        connection = new(), stats = new(), force = new(), fill = new(), fillRandom = new(), spawn = new(), cancel = new(), mode = new(), job = new(), output = new();
    private readonly ProgressBar progress = new();
    private bool applyingState;
    private int lastJob = -1;
    private string lastResult = "";
    private readonly List<Control> mutations = new();
    private readonly List<CatalogItem> catalog = new();
    private string catalogEndpoint = "";
    private bool polling, sending, connected, worldReady = true, jobBusy, benchmarkMap = true, IsDisposed;
    public readonly Queue<string> Replies = new();
    public readonly List<string> Requests = new();
    private Task<string> SendAsync(string command)
    {
        Requests.Add(command);
        string reply = Replies.Dequeue();
        return reply == "DISCONNECT" ? Task.FromException<string>(new IOException("disconnected")) : Task.FromResult(reply);
    }
    private void Log(string text) { }
    public void SetStatus(string text) => ApplyStatus(Tokens(text));
    public int Progress => progress.Value;
    public ProgressBarStyle ProgressStyle => progress.Style;
    public string JobText => job.Text;
    public Task Poll() => PollAsync();
    public Task Refresh() => ConnectAsync();
    public int VisibleItems => items.Items.Count;
    public bool IsConnected => connected;
    public string ItemStatus => itemStatus.Text;
    public string SelectedDisplay => items.SelectedItem?.ToString();
    public void Search(string text) { itemSearch.Text = text; RefreshPickers(); }
    public void SetEndpoint(string text) => host.Text = text;
}
internal static class ItemChecks
{
    private static int checks;
    private static void Require(bool value, string description)
    { checks++; if (!value) throw new Exception(description); }
    private static BenchmarkCommand Parse(string command)
    { Require(BenchmarkCommand.TryParse(command.Split(' '), out var parsed, out _), "parse " + command); return parsed; }
    private static string Catalog(string json) => "ok benchmarkCatalog=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    private static async Task Main()
    {
        var frontend = new CatalogProbe();
        frontend.Replies.Enqueue("DISCONNECT"); await frontend.Poll();
        Require(!frontend.IsConnected && frontend.VisibleItems == 0, "initial disconnected catalog");
        frontend.Replies.Enqueue("ok fps=60"); frontend.Replies.Enqueue(Catalog("{\"items\":[]}")); await frontend.Poll();
        Require(frontend.IsConnected && frontend.ItemStatus.Contains("재시도"), "empty catalog remains pending while status stays connected");
        const string json = "{\"items\":[{\"id\":19,\"name\":\"Iron ore\",\"portable\":true},{\"id\":0,\"name\":\"Log\",\"portable\":true},{\"id\":80,\"name\":\"Water\",\"portable\":false}]}";
        frontend.Replies.Enqueue("ok fps=60"); frontend.Replies.Enqueue(Catalog(json)); await frontend.Poll();
        Require(frontend.VisibleItems == 2 && frontend.SelectedDisplay == "0 · Log", "portable names and IDs populate in sorted order");
        int catalogRequests = frontend.Requests.Count(c => c == "benchmark catalog");
        frontend.Replies.Enqueue("ok fps=60"); await frontend.Poll();
        Require(frontend.Requests.Count(c => c == "benchmark catalog") == catalogRequests, "loaded catalog is cached during normal polling");
        frontend.Search("Iron"); Require(frontend.VisibleItems == 1 && frontend.SelectedDisplay == "19 · Iron ore", "name search");
        frontend.Search("not found"); Require(frontend.VisibleItems == 0, "empty search result");
        frontend.Search(""); Require(frontend.VisibleItems == 2, "clear filter restores all items");
        frontend.Replies.Enqueue("DISCONNECT"); await frontend.Poll();
        Require(frontend.VisibleItems == 0, "disconnect clears stale items");
        frontend.Replies.Enqueue("ok fps=60"); frontend.Replies.Enqueue(Catalog(json)); await frontend.Poll();
        Require(frontend.VisibleItems == 2, "reconnect automatically reloads catalog");
        frontend.SetEndpoint("localhost"); frontend.Replies.Enqueue("ok fps=60"); frontend.Replies.Enqueue(Catalog(json)); await frontend.Poll();
        Require(frontend.Requests.Count(c => c == "benchmark catalog") == catalogRequests + 2, "endpoint change reloads catalog");
        frontend.Replies.Enqueue("ok fps=60"); frontend.Replies.Enqueue("error catalog unavailable"); await frontend.Refresh();
        Require(frontend.ItemStatus.Contains("재시도"), "catalog errors remain pending");
        frontend.Replies.Enqueue("ok fps=60"); frontend.Replies.Enqueue(Catalog(json)); await frontend.Poll();
        Require(frontend.VisibleItems == 2, "catalog error recovers next poll");
        string jobResult = Convert.ToBase64String(Encoding.UTF8.GetBytes("Generating terrain"));
        string jobStatus = $"benchmarkBusy=1 benchmarkJob=1 benchmarkDone=2500 benchmarkTotal=100000 benchmarkSeconds=10 benchmarkForce=0 benchmarkResult={jobResult}";
        frontend.SetStatus(jobStatus + " benchmarkStageDone=5 benchmarkStageTotal=20");
        Require(frontend.Progress == 250 && frontend.ProgressStyle == ProgressBarStyle.Continuous, "terrain progress uses current stage counters");
        Require(frontend.JobText.Contains("2,500 / 100,000") && frontend.JobText.Contains("5 / 20"), "overall creation and phase counts remain visible together");
        frontend.SetStatus(jobStatus + " benchmarkStageDone=0 benchmarkStageTotal=0");
        Require(frontend.Progress == 25, "object progress falls back to overall creation");
        frontend.SetStatus(jobStatus.Replace("benchmarkDone=2500 benchmarkTotal=100000", "benchmarkDone=0 benchmarkTotal=0"));
        Require(frontend.ProgressStyle == ProgressBarStyle.Marquee, "unknown total still shows active work");
        frontend.SetStatus(jobStatus.Replace(jobResult, Convert.ToBase64String(Encoding.UTF8.GetBytes("Preparing pipes render"))));
        Require(frontend.ProgressStyle == ProgressBarStyle.Marquee, "final render preparation stays visibly active after creation is complete");
        frontend.SetStatus(jobStatus.Replace("benchmarkBusy=1", "benchmarkBusy=0").Replace("benchmarkDone=2500", "benchmarkDone=100000") + " benchmarkStageDone=0 benchmarkStageTotal=20");
        Require(frontend.Progress == 1000 && frontend.ProgressStyle == ProgressBarStyle.Continuous, "completed job does not show stale terrain progress");

        var manager = new ItemManager();
        manager.ItemDefinitions.AddRange(new[] { new ItemDefinition { id = 0 }, new ItemDefinition { id = 19 },
            new ItemDefinition { id = 80, isFluid = true }, new ItemDefinition { id = 81, itemName = "Electricity" },
            new ItemDefinition { id = 82, portableMesh = null }, new ItemDefinition { id = -1 }, null });
        var pool = BenchmarkRuntime.CollectPortableItemIds(manager);
        Require(pool.SequenceEqual(new[] { 0, 19 }), "random pool excludes fluid, electricity, missing visuals, negative IDs and nulls");
        Require(BenchmarkRuntime.CollectPortableItemIds(null).Count == 0, "missing manager empty pool");
        foreach (double percentage in new[] { 0d, 12.5, 50, 100 })
        {
            var terrain = new TerrainGenerator();
            for (int i = 0; i < 997; i++) { var block = new Block { Capacity = i % 5 + 1 }; block.Items.Add(999); terrain.Blocks.Add(block); }
            foreach (bool random in new[] { false, true })
            {
                var fill = new FillProbe();
                bool pausedBefore = percentage == 50;
                MapObjectTickManager.SimulationPaused = pausedBefore;
                var work = fill.Run(terrain, Parse("benchmark fill " + (random ? "random" : "19") + " " + percentage.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    random ? null : manager.ItemDefinitions[1], random ? pool : null);
                while (work.MoveNext()) { }
                Require(MapObjectTickManager.SimulationPaused == pausedBefore, "fill restores previous pause state");
                var ids = terrain.Blocks.SelectMany(b => b.Items).ToArray();
                long expected = BenchmarkLayout.FilledSlotCount(terrain.Blocks.Sum(b => b.Capacity), percentage);
                Require(ids.Length == expected && fill.benchmarkDone == expected && fill.benchmarkTotal == expected, "exact percentage and job counters for either mode");
                Require(ids.All(id => random ? pool.Contains(id) : id == 19), "correct items replace previous content");
                if (random && expected > 100) Require(ids.Distinct().Count() == 2, "random fill mixes item kinds");
            }
        }
        var rejected = new TerrainGenerator(); rejected.Blocks.Add(new Block { Reject = true });
        MapObjectTickManager.SimulationPaused = false;
        try { new FillProbe().Run(rejected, Parse("benchmark fill random 100"), null, pool).MoveNext(); throw new Exception("expected rejection"); }
        catch (InvalidOperationException) { Require(!MapObjectTickManager.SimulationPaused, "failed fill restores pause"); }
        var cancelled = new TerrainGenerator(); for (int i = 0; i < 200; i++) cancelled.Blocks.Add(new Block());
        var iterator = new FillProbe().Run(cancelled, Parse("benchmark fill random 100"), null, pool);
        Require(iterator.MoveNext() && MapObjectTickManager.SimulationPaused, "fill yields with paused simulation");
        ((IDisposable)iterator).Dispose(); Require(!MapObjectTickManager.SimulationPaused, "cancelled fill restores pause");
        Console.WriteLine($"PASS: {checks} benchmark catalog recovery, item eligibility and belt fill checks");
    }
}
