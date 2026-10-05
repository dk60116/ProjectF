using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ProjectF.Benchmark;

namespace ProjectF.Tools.BenchmarkTool;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new BenchmarkForm());
    }
}

internal sealed class BenchmarkForm : Form
{
    private readonly TextBox host = new() { Text = "127.0.0.1", Width = 140 };
    private readonly NumericUpDown port = Number(50877, 1, 65535);
    private readonly NumericUpDown rings = Number(10, 1, BenchmarkLayout.MaximumRings);
    private readonly NumericUpDown objectCount = Number(100, 1, BenchmarkLayout.MaximumObjects);
    private readonly NumericUpDown percent = Number(50, 0, 100);
    private readonly ComboBox belts = Picker(), items = Picker(), objects = Picker();
    private readonly TextBox itemSearch = new() { Width = 170, PlaceholderText = "이름 또는 ID 검색" };
    private readonly Label stats = TextLabel("FPS --   UPS --   설치물 --   벨트 아이템 --", 16);
    private readonly Label connection = TextLabel("연결 대기"), preview = TextLabel(""), gridPreview = TextLabel("");
    private readonly Label job = TextLabel("대기"), output = TextLabel(""), mode = TextLabel("Seed 0 맵을 불러와 시작해");
    private readonly Label itemStatus = TextLabel("아이템 목록 대기");
    private readonly CheckBox force = new() { Text = "전력·목표·자원·재료를 가정하고 모든 작동형 오브젝트 강제 작동", AutoSize = true };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Height = 140, Dock = DockStyle.Top };
    private readonly ProgressBar progress = new() { Width = 450, Height = 22, Maximum = 1000 };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1000 };
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<Button> mutations = new();
    private readonly List<CatalogItem> catalog = new();
    private Button cancel = null!, fill = null!, fillRandom = null!, spawn = null!;
    private bool polling, sending, applyingState, connected, worldReady, jobBusy, benchmarkMap;
    private int lastJob = -1;
    private string lastResult = "";
    private string catalogEndpoint = "";

    public BenchmarkForm()
    {
        Text = "ProjectF Benchmark Tool";
        Font = new Font("Segoe UI", 10f);
        Size = new Size(1060, 900); MinimumSize = new Size(850, 650);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(30, 33, 37); ForeColor = Color.Gainsboro;
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(layout); Controls.Add(scroll);

        void Section(string title, params Control[] controls)
        {
            var group = new GroupBox { Text = title, AutoSize = true, Dock = DockStyle.Top, ForeColor = ForeColor,
                Padding = new Padding(12, 22, 12, 12), Margin = new Padding(0, 0, 0, 12) };
            var rows = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Top };
            foreach (var control in controls) rows.Controls.Add(control);
            group.Controls.Add(rows); layout.Controls.Add(group);
        }
        Section("연결 / 측정", Row(TextLabel("Host"), host, TextLabel("Port"), port,
            Button("연결 / 목록 새로고침", async () => await ConnectAsync(), false)), stats, connection, mode);
        Section("1. 벤치마크 맵", Row(Button("새 Seed 0 맵 불러오기", () => CommandAsync("benchmark map")),
            TextLabel("현재 맵의 설치물과 내용을 지우고 새 빈 맵으로 교체해")));
        rings.ValueChanged += (_, _) => UpdatePreviews();
        Section("2. 사각 순환 벨트", Row(TextLabel("벨트"), belts, TextLabel("N줄"), rings), preview,
            Row(Button("N줄 생성", () => CommandAsync($"benchmark belts {SelectedId(belts, true)} {(int)rings.Value}")),
                TextLabel("플레이어 중심으로 붙어 있는 ㅁ 순환 벨트를 생성해. 기존 벨트는 모두 교체돼")));
        percent.DecimalPlaces = 1; percent.Increment = .5m;
        itemSearch.TextChanged += (_, _) => RefreshPickers();
        Section("3. 벨트 아이템", Row(itemSearch, items, TextLabel("채움 비율 %"), percent),
            Row(fill = Button("전체 벨트 채우기", () => CommandAsync($"benchmark fill {SelectedId(items)} {percent.Value.ToString(CultureInfo.InvariantCulture)}")),
                fillRandom = Button("랜덤 아이템으로 채우기", () => CommandAsync($"benchmark fill random {percent.Value.ToString(CultureInfo.InvariantCulture)}")),
                Button("벨트 아이템 전체 제거", () => CommandAsync("benchmark clearitems"))),
            TextLabel("양쪽 레인의 전체 슬롯 기준으로 채워. 랜덤은 전체 운반 가능한 아이템을 섞어. 기존 아이템은 교체돼"), itemStatus);
        objectCount.ValueChanged += (_, _) => UpdatePreviews();
        Section("4. 맵 오브젝트 격자 소환", Row(objects, TextLabel("N개"), objectCount), gridPreview,
            Row(spawn = Button("강제 소환", () => CommandAsync($"benchmark spawn {SelectedId(objects)} {(int)objectCount.Value}")),
                TextLabel("지형·자원 설치 조건을 무시해. 오브젝트 크기와 IO 영역만큼 간격을 둬")));
        force.CheckedChanged += async (_, _) =>
        {
            if (applyingState) return;
            await CommandAsync($"benchmark force {(force.Checked ? 1 : 0)} {SelectedId(items)}");
            await PollAsync();
        };
        Section("5–6. 초기화 / 강제 작동", Row(Button("모든 맵 오브젝트 클리어", () => CommandAsync("benchmark clearobjects"))),
            Row(force, Button("작동 진행도 랜덤화", () => CommandAsync("benchmark randomizeprogress"))),
            TextLabel("선택한 벨트 아이템은 채굴기·벌목기·로봇암의 가상 자원으로도 쓰여"),
            TextLabel("결과 아이템은 배출구 또는 바닥으로 나와. 연결되지 않은 유체는 넘침으로 집계돼"), output);
        cancel = Button("생성 중단", () => CommandAsync("benchmark cancel"), false);
        Section("작업 진행", Row(progress, cancel), job, log);
        timer.Tick += async (_, _) => await PollAsync();
        Shown += async (_, _) => { await ConnectAsync(); timer.Start(); };
        FormClosed += (_, _) => { timer.Stop(); timer.Dispose(); lifetime.Cancel(); lifetime.Dispose(); };
        UpdatePreviews(); RefreshPickers(); UpdateEnabled();
    }

    private static NumericUpDown Number(decimal value, decimal minimum, decimal maximum)
        => new() { Minimum = minimum, Maximum = maximum, Value = value, Width = 100, ThousandsSeparator = true };
    private static ComboBox Picker() => new() { Width = 330, DropDownStyle = ComboBoxStyle.DropDownList,
        DisplayMember = nameof(CatalogItem.Display), BackColor = Color.FromArgb(45, 48, 52), ForeColor = Color.Gainsboro };
    private static Label TextLabel(string text, float size = 10) => new() { Text = text, AutoSize = true, MaximumSize = new Size(900, 0),
        Font = new Font("Segoe UI", size), Margin = new Padding(3, 7, 3, 7) };
    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(920, 0) };
        foreach (var control in controls) row.Controls.Add(control);
        return row;
    }
    private Button Button(string text, Func<Task> action, bool mutation = true)
    {
        var button = new Button { Text = text, AutoSize = true, Height = 34, Padding = new Padding(8, 4, 8, 4) };
        button.Click += async (_, _) => { try { await action(); } catch (Exception exception) { Log(exception.Message); } };
        if (mutation) mutations.Add(button);
        return button;
    }
    private void UpdatePreviews()
    {
        int n = (int)rings.Value;
        preview.Text = $"총 {BenchmarkLayout.BeltCount(n):N0}개  ·  바깥 변 {n * 2 + 1:N0}칸  ·  4 × N × (N + 1)";
        int count = (int)objectCount.Value, columns = BenchmarkLayout.GridColumns(count);
        gridPreview.Text = $"총 {count:N0}개  ·  {columns:N0}열 × {(count + columns - 1) / columns:N0}행";
    }
    private static string SelectedId(ComboBox combo, bool auto = false)
        => combo.SelectedItem is CatalogItem item ? item.id.ToString(CultureInfo.InvariantCulture) : auto ? "auto" : "-1";
    private void RefreshPickers()
    {
        string query = itemSearch.Text.Trim();
        FillPicker(belts, catalog.Where(i => i.conveyor), true);
        FillPicker(items, catalog.Where(i => i.portable && Matches(i, query)), false);
        FillPicker(objects, catalog.Where(i => i.installation && Matches(i, query)), false);
        itemStatus.Text = catalog.Count == 0 ? "아이템 목록 대기" : $"벨트 아이템 {items.Items.Count:N0} / {catalog.Count(i => i.portable):N0}종 · 검색을 비우면 전체 목록이 보여";
        UpdateEnabled();
    }
    private static bool Matches(CatalogItem item, string query) => query.Length == 0 || item.Display.Contains(query, StringComparison.OrdinalIgnoreCase);
    private static void FillPicker(ComboBox combo, IEnumerable<CatalogItem> entries, bool auto)
    {
        int oldId = combo.SelectedItem is CatalogItem old ? old.id : -1;
        combo.BeginUpdate(); combo.Items.Clear();
        if (auto) combo.Items.Add("자동 선택");
        foreach (var item in entries.OrderBy(i => i.id)) combo.Items.Add(item);
        int selected = -1;
        for (int i = 0; i < combo.Items.Count; i++) if (combo.Items[i] is CatalogItem item && item.id == oldId) { selected = i; break; }
        combo.SelectedIndex = selected >= 0 ? selected : combo.Items.Count > 0 ? 0 : -1;
        combo.EndUpdate();
    }
    private async Task ConnectAsync()
    {
        catalogEndpoint = "";
        await PollAsync();
    }
    private async Task LoadCatalogAsync()
    {
        string response = await SendAsync("benchmark catalog");
        if (!response.StartsWith("ok ", StringComparison.Ordinal)) throw new IOException(response);
        var values = Tokens(response);
        if (!values.TryGetValue("benchmarkCatalog", out string? encoded)) throw new IOException("게임이 Benchmark Tool을 지원하지 않거나 목록을 받지 못했어");
        var data = JsonSerializer.Deserialize<Catalog>(Convert.FromBase64String(encoded));
        if (data?.items == null || data.items.Count == 0) throw new IOException("게임의 아이템 목록이 아직 준비되지 않았어");
        catalog.Clear(); catalog.AddRange(data.items);
        catalogEndpoint = $"{host.Text.Trim()}:{port.Value}";
        RefreshPickers(); Log($"아이템 {catalog.Count:N0}개 · 벨트에 올릴 수 있는 아이템 {catalog.Count(i => i.portable):N0}종을 불러왔어");
    }
    private async Task CommandAsync(string command)
    {
        if (sending) return;
        sending = true; UpdateEnabled();
        try { string response = await SendAsync(command); Log(command); Log(response); ApplyStatus(Tokens(response)); }
        catch (Exception exception) { Log("실패: " + exception.Message); }
        finally { sending = false; UpdateEnabled(); }
    }
    private async Task PollAsync()
    {
        if (polling || sending || IsDisposed) return;
        polling = true;
        try
        {
            string response = await SendAsync("benchmark status");
            if (!response.StartsWith("ok ", StringComparison.Ordinal)) throw new IOException(response);
            connected = true; connection.Text = "연결됨";
            ApplyStatus(Tokens(response));
            if (catalogEndpoint != $"{host.Text.Trim()}:{port.Value}")
            {
                try { await LoadCatalogAsync(); }
                catch (Exception exception)
                {
                    catalog.Clear(); RefreshPickers();
                    itemStatus.Text = "아이템 목록 재시도 중: " + exception.Message;
                }
            }
        }
        catch (Exception exception)
        {
            connected = false; connection.Text = "연결 실패: " + exception.Message;
            catalogEndpoint = "";
            catalog.Clear(); RefreshPickers();
            stats.Text = "FPS --   UPS --   설치물 --   벨트 아이템 --";
        }
        finally { polling = false; UpdateEnabled(); }
    }
    private void ApplyStatus(Dictionary<string, string> values)
    {
        if (values.ContainsKey("fps"))
        {
            stats.Text = $"FPS {Token(values, "fps")}   UPS {Token(values, "ups")}   설치물 {Integer(values, "installTotal"):N0}   벨트 아이템 {Integer(values, "beltItems"):N0}";
            benchmarkMap = Token(values, "seed") == "0"; worldReady = Token(values, "worldReady") == "1";
            mode.Text = benchmarkMap ? "Seed 0 · 무한 빈 땅" : "Seed 0 맵을 불러와 시작해";
        }
        if (values.ContainsKey("benchmarkBusy"))
        {
            jobBusy = Token(values, "benchmarkBusy") == "1";
            long done = Integer(values, "benchmarkDone"), total = Integer(values, "benchmarkTotal");
            long stageDone = Integer(values, "benchmarkStageDone"), stageTotal = Integer(values, "benchmarkStageTotal");
            long displayedDone = stageTotal > 0 && jobBusy ? stageDone : done, displayedTotal = stageTotal > 0 && jobBusy ? stageTotal : total;
            progress.Value = displayedTotal <= 0 ? 0 : (int)Math.Clamp(displayedDone * 1000d / displayedTotal, 0, 1000);
            string result = Encoding.UTF8.GetString(Convert.FromBase64String(Token(values, "benchmarkResult")));
            bool preparingWithoutCount = result.StartsWith("Preparing", StringComparison.Ordinal) && stageTotal <= 0;
            progress.Style = jobBusy && (displayedTotal <= 0 || preparingWithoutCount) ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            int jobId = (int)Integer(values, "benchmarkJob");
            string stage = stageTotal > 0 && jobBusy ? $" · 현재 단계 {stageDone:N0} / {stageTotal:N0}" : "";
            job.Text = $"작업 #{jobId} · 생성 {done:N0} / {total:N0}{stage} · {Token(values, "benchmarkSeconds")}초 · {result}";
            if (jobId != lastJob || result != lastResult) { Log(job.Text); lastJob = jobId; lastResult = result; }
            applyingState = true;
            force.Checked = Token(values, "benchmarkForce") == "1";
            applyingState = false;
            output.Text = $"생성된 아이템 {Integer(values, "benchmarkProduced"):N0}개 · 유체 넘침 {Token(values, "benchmarkSpilledLiters")} L";
        }
        UpdateEnabled();
    }
    private void UpdateEnabled()
    {
        foreach (var button in mutations) button.Enabled = connected && worldReady && !sending && !jobBusy
            && (benchmarkMap || button.Text == "새 Seed 0 맵 불러오기");
        force.Enabled = connected && benchmarkMap && worldReady && !sending && !jobBusy && (force.Checked || items.SelectedItem is CatalogItem);
        if (fill != null) fill.Enabled &= items.SelectedItem is CatalogItem;
        if (fillRandom != null) fillRandom.Enabled &= catalog.Any(i => i.portable);
        if (spawn != null) spawn.Enabled &= objects.SelectedItem is CatalogItem;
        if (cancel != null) cancel.Enabled = connected && jobBusy && !sending;
    }
    private async Task<string> SendAsync(string command)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(8000);
        using var client = new TcpClient();
        await client.ConnectAsync(host.Text.Trim(), (int)port.Value, timeout.Token);
        await using var stream = client.GetStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);
        await writer.WriteLineAsync(command.AsMemory(), timeout.Token);
        return await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("게임의 응답이 없어");
    }
    private void Log(string text)
    {
        if (IsDisposed) return;
        if (log.TextLength > 32000) log.Clear();
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    }
    private static Dictionary<string, string> Tokens(string line)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string part in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        { int separator = part.IndexOf('='); if (separator > 0) values[part[..separator]] = part[(separator + 1)..]; }
        return values;
    }
    private static string Token(Dictionary<string, string> values, string name) => values.GetValueOrDefault(name, "--");
    private static long Integer(Dictionary<string, string> values, string name) => long.TryParse(Token(values, name), out long result) ? result : 0;
    private sealed class Catalog { public List<CatalogItem>? items { get; set; } }
    private sealed class CatalogItem
    {
        public int id { get; set; }
        public string name { get; set; } = "";
        public bool installation { get; set; }
        public bool conveyor { get; set; }
        public bool portable { get; set; }
        public string Display => $"{id} · {name}";
        public override string ToString() => Display;
    }
}
