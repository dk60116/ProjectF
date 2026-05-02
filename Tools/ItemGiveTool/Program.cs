using System.Drawing;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace ProjectF.Tools.EditorTool;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new EditorToolForm());
    }
}

internal sealed class EditorToolForm : Form
{
    private const string ToolTitle = "ProjectF EditorTool";
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 50877;
    private const int TimeoutMilliseconds = 5000;

    private readonly List<ItemCatalogEntry> allItems = new List<ItemCatalogEntry>();
    private readonly ComboBox itemComboBox = new ComboBox();
    private readonly PictureBox iconPreview = new PictureBox();
    private readonly TextBox searchTextBox = new TextBox();
    private readonly TextBox hostTextBox = new TextBox();
    private readonly NumericUpDown portInput = new NumericUpDown();
    private readonly NumericUpDown itemIdInput = new NumericUpDown();
    private readonly NumericUpDown countInput = new NumericUpDown();
    private readonly NumericUpDown saveSlotInput = new NumericUpDown();
    private readonly Button giveButton = new Button();
    private readonly Button pingButton = new Button();
    private readonly Button saveButton = new Button();
    private readonly Button loadButton = new Button();
    private readonly Button resetButton = new Button();
    private readonly Button reloadButton = new Button();
    private readonly CheckBox showConveyorSlotDotsCheckBox = new CheckBox();
    private readonly CheckBox showSleepAwakeCheckBox = new CheckBox();
    private readonly TextBox logTextBox = new TextBox();
    private readonly Label statusLabel = new Label();
    private readonly Label catalogLabel = new Label();
    private readonly Label fpsLabel = new Label();
    private readonly Label runtimeStatsLabel = new Label();
    private readonly TextBox runtimeStatsTextBox = new TextBox();
    private readonly System.Windows.Forms.Timer statusTimer = new System.Windows.Forms.Timer();
    private bool refreshingItems;
    private bool pollingStatus;
    private bool applyingRuntimeDebugState;

    public EditorToolForm()
    {
        Text = ToolTitle;
        MinimumSize = new Size(900, 792);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(31, 34, 29);

        Panel shellPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            BackColor = Color.FromArgb(31, 34, 29)
        };

        TableLayoutPanel layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 214f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 284f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        Label titleLabel = new Label
        {
            Text = "EditorTool",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 22f, FontStyle.Bold),
            ForeColor = Color.FromArgb(243, 234, 206),
            Location = new Point(0, 0)
        };

        Label descriptionLabel = new Label
        {
            Text = "아이템 지급과 런타임 디버그 토글을 조작합니다.",
            AutoSize = true,
            ForeColor = Color.FromArgb(176, 177, 158),
            Location = new Point(2, 42)
        };

        Panel headerPanel = new Panel { Dock = DockStyle.Fill };
        fpsLabel.Text = "FPS: --";
        fpsLabel.AutoSize = true;
        fpsLabel.Font = new Font(Font.FontFamily, 13f, FontStyle.Bold);
        fpsLabel.ForeColor = Color.FromArgb(176, 177, 158);
        fpsLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        headerPanel.Controls.Add(titleLabel);
        headerPanel.Controls.Add(descriptionLabel);
        headerPanel.Controls.Add(fpsLabel);
        headerPanel.Resize += (_, _) => PositionFpsLabel(headerPanel);
        PositionFpsLabel(headerPanel);
        layout.Controls.Add(headerPanel, 0, 0);
        layout.SetColumnSpan(headerPanel, 2);

        Panel iconCard = CreateCardPanel();
        iconCard.Margin = new Padding(0, 0, 18, 0);
        iconPreview.Dock = DockStyle.Fill;
        iconPreview.SizeMode = PictureBoxSizeMode.Zoom;
        iconPreview.BackColor = Color.FromArgb(43, 46, 39);
        iconPreview.Padding = new Padding(24);
        iconCard.Controls.Add(iconPreview);
        layout.Controls.Add(iconCard, 0, 1);

        TableLayoutPanel formGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8
        };
        formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96f));
        formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (int i = 0; i < 8; i++)
        {
            formGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 7 ? 36f : 34f));
        }

        searchTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        searchTextBox.PlaceholderText = "이름 또는 ID 검색";
        searchTextBox.TextChanged += (_, _) => RefreshItemDropDown();

        itemComboBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        itemComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        itemComboBox.DrawMode = DrawMode.OwnerDrawFixed;
        itemComboBox.ItemHeight = 28;
        itemComboBox.DrawItem += DrawItemComboItem;
        itemComboBox.SelectedIndexChanged += (_, _) => ApplySelectedItem();

        itemIdInput.Minimum = 0;
        itemIdInput.Maximum = int.MaxValue;
        itemIdInput.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        itemIdInput.ValueChanged += (_, _) => SelectCatalogItemById(Decimal.ToInt32(itemIdInput.Value));

        countInput.Minimum = 1;
        countInput.Maximum = 1000;
        countInput.Value = 1;
        countInput.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        hostTextBox.Text = DefaultHost;
        hostTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        portInput.Minimum = 1;
        portInput.Maximum = 65535;
        portInput.Value = DefaultPort;
        portInput.Anchor = AnchorStyles.Left | AnchorStyles.Right;

        catalogLabel.AutoSize = true;
        catalogLabel.ForeColor = Color.FromArgb(176, 177, 158);
        catalogLabel.Anchor = AnchorStyles.Left;

        reloadButton.Text = "목록 새로고침";
        reloadButton.Anchor = AnchorStyles.Left;
        reloadButton.Width = 128;
        reloadButton.Height = 30;
        reloadButton.FlatStyle = FlatStyle.Flat;
        reloadButton.ForeColor = Color.FromArgb(243, 234, 206);
        reloadButton.BackColor = Color.FromArgb(68, 72, 59);
        reloadButton.Click += (_, _) => LoadCatalog();

        AddRow(formGrid, 0, "Search", searchTextBox);
        AddRow(formGrid, 1, "Item", itemComboBox);
        AddRow(formGrid, 2, "Item ID", itemIdInput);
        AddRow(formGrid, 3, "Count", countInput);
        AddRow(formGrid, 4, "Host", hostTextBox);
        AddRow(formGrid, 5, "Port", portInput);
        AddRow(formGrid, 6, "Catalog", catalogLabel);
        AddRow(formGrid, 7, "", reloadButton);
        layout.Controls.Add(formGrid, 1, 1);

        FlowLayoutPanel buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 10, 0, 0)
        };

        StylePrimaryButton(giveButton, "Give");
        giveButton.Click += async (_, _) => await SendGiveAsync();

        StyleSecondaryButton(pingButton, "연결 확인");
        pingButton.Click += async (_, _) => await SendPingAsync();

        statusLabel.Text = "대기 중";
        statusLabel.AutoSize = true;
        statusLabel.ForeColor = Color.FromArgb(176, 177, 158);
        statusLabel.Padding = new Padding(12, 8, 0, 0);

        buttonPanel.Controls.Add(giveButton);
        buttonPanel.Controls.Add(pingButton);
        buttonPanel.Controls.Add(statusLabel);
        layout.Controls.Add(buttonPanel, 0, 2);
        layout.SetColumnSpan(buttonPanel, 2);

        FlowLayoutPanel saveLoadPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 6, 0, 0),
            WrapContents = false
        };

        Label saveSlotLabel = new Label
        {
            Text = "Slot",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Color.FromArgb(204, 199, 176),
            Margin = new Padding(0, 8, 8, 0)
        };

        saveSlotInput.Minimum = 1;
        saveSlotInput.Maximum = 10;
        saveSlotInput.Value = 1;
        saveSlotInput.Width = 64;
        saveSlotInput.Height = 30;
        saveSlotInput.TextAlign = HorizontalAlignment.Right;
        saveSlotInput.Anchor = AnchorStyles.Left;
        saveSlotInput.Margin = new Padding(0, 2, 14, 0);
        saveSlotInput.BackColor = Color.FromArgb(43, 46, 39);
        saveSlotInput.ForeColor = Color.FromArgb(243, 234, 206);

        StyleSecondaryButton(saveButton, "Save");
        saveButton.Click += async (_, _) => await SendSaveAsync();

        StyleSecondaryButton(loadButton, "Load");
        loadButton.Click += async (_, _) => await SendLoadAsync();

        StyleSecondaryButton(resetButton, "초기화");
        resetButton.Click += async (_, _) => await SendResetAsync();

        saveLoadPanel.Controls.Add(saveSlotLabel);
        saveLoadPanel.Controls.Add(saveSlotInput);
        saveLoadPanel.Controls.Add(saveButton);
        saveLoadPanel.Controls.Add(loadButton);
        saveLoadPanel.Controls.Add(resetButton);
        layout.Controls.Add(saveLoadPanel, 0, 3);
        layout.SetColumnSpan(saveLoadPanel, 2);

        FlowLayoutPanel debugTogglePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 4, 0, 0)
        };

        StyleDebugCheckBox(showConveyorSlotDotsCheckBox, "Show ConveyorSlotDot");
        showConveyorSlotDotsCheckBox.CheckedChanged += async (_, _) =>
            await SendDebugToggleAsync(
                "showConveyorSlotDots",
                showConveyorSlotDotsCheckBox.Checked,
                "Show ConveyorSlotDot");

        StyleDebugCheckBox(showSleepAwakeCheckBox, "Show SleepAwake");
        showSleepAwakeCheckBox.CheckedChanged += async (_, _) =>
            await SendDebugToggleAsync(
                "showSleepAwake",
                showSleepAwakeCheckBox.Checked,
                "Show SleepAwake");

        debugTogglePanel.Controls.Add(showConveyorSlotDotsCheckBox);
        debugTogglePanel.Controls.Add(showSleepAwakeCheckBox);
        layout.Controls.Add(debugTogglePanel, 0, 4);
        layout.SetColumnSpan(debugTogglePanel, 2);

        Panel runtimeStatsCard = CreateCardPanel();
        runtimeStatsCard.Padding = new Padding(14, 10, 14, 10);
        runtimeStatsCard.Margin = new Padding(0, 0, 0, 14);
        TableLayoutPanel runtimeStatsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        runtimeStatsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28f));
        runtimeStatsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        runtimeStatsLabel.Text = "Runtime Stats: --";
        runtimeStatsLabel.Dock = DockStyle.Fill;
        runtimeStatsLabel.ForeColor = Color.FromArgb(243, 234, 206);
        runtimeStatsLabel.Font = new Font(Font.FontFamily, 11.5f, FontStyle.Bold);

        runtimeStatsTextBox.Dock = DockStyle.Fill;
        runtimeStatsTextBox.Multiline = true;
        runtimeStatsTextBox.ScrollBars = ScrollBars.Vertical;
        runtimeStatsTextBox.ReadOnly = true;
        runtimeStatsTextBox.BorderStyle = BorderStyle.None;
        runtimeStatsTextBox.BackColor = Color.FromArgb(43, 46, 39);
        runtimeStatsTextBox.ForeColor = Color.FromArgb(231, 224, 200);
        runtimeStatsTextBox.Font = new Font("Consolas", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        runtimeStatsTextBox.Text = "설치 오브젝트 종류: --";

        runtimeStatsLayout.Controls.Add(runtimeStatsLabel, 0, 0);
        runtimeStatsLayout.Controls.Add(runtimeStatsTextBox, 0, 1);
        runtimeStatsCard.Controls.Add(runtimeStatsLayout);
        layout.Controls.Add(runtimeStatsCard, 0, 5);
        layout.SetColumnSpan(runtimeStatsCard, 2);

        Panel logCard = CreateCardPanel();
        logCard.Dock = DockStyle.Fill;
        logTextBox.Dock = DockStyle.Fill;
        logTextBox.Multiline = true;
        logTextBox.ScrollBars = ScrollBars.Vertical;
        logTextBox.ReadOnly = true;
        logTextBox.BorderStyle = BorderStyle.None;
        logTextBox.BackColor = Color.FromArgb(43, 46, 39);
        logTextBox.ForeColor = Color.FromArgb(231, 224, 200);
        logTextBox.Font = new Font("Consolas", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        logCard.Controls.Add(logTextBox);
        layout.Controls.Add(logCard, 0, 6);
        layout.SetColumnSpan(logCard, 2);

        shellPanel.Controls.Add(layout);
        Controls.Add(shellPanel);
        AcceptButton = giveButton;

        LoadCatalog();
        statusTimer.Interval = 1000;
        statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        Shown += async (_, _) => await RefreshStatusAsync();
        FormClosed += (_, _) => statusTimer.Stop();
        statusTimer.Start();
    }

    private static Panel CreateCardPanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            BackColor = Color.FromArgb(43, 46, 39)
        };
    }

    private static void AddRow(TableLayoutPanel panel, int row, string labelText, Control input)
    {
        Label label = new Label
        {
            Text = labelText,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Color.FromArgb(204, 199, 176)
        };

        input.Margin = new Padding(0, 2, 0, 2);
        panel.Controls.Add(label, 0, row);
        panel.Controls.Add(input, 1, row);
    }

    private static void StylePrimaryButton(Button button, string text)
    {
        button.Text = text;
        button.Width = 132;
        button.Height = 36;
        button.Margin = new Padding(0, 0, 10, 0);
        button.BackColor = Color.FromArgb(232, 121, 63);
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
    }

    private static void StyleSecondaryButton(Button button, string text)
    {
        button.Text = text;
        button.Width = 132;
        button.Height = 36;
        button.Margin = new Padding(0, 0, 10, 0);
        button.BackColor = Color.FromArgb(68, 72, 59);
        button.ForeColor = Color.FromArgb(243, 234, 206);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(101, 105, 84);
    }

    private void StyleDebugCheckBox(CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        checkBox.AutoSize = true;
        checkBox.Margin = new Padding(0, 8, 28, 0);
        checkBox.ForeColor = Color.FromArgb(243, 234, 206);
        checkBox.FlatStyle = FlatStyle.Flat;
        checkBox.CheckedChanged += (_, _) =>
        {
            checkBox.ForeColor = checkBox.Checked
                ? Color.FromArgb(126, 218, 126)
                : Color.FromArgb(243, 234, 206);
        };
    }

    private void PositionFpsLabel(Control parent)
    {
        fpsLabel.Location = new Point(
            Math.Max(0, parent.ClientSize.Width - fpsLabel.Width),
            8);
    }

    private void LoadCatalog()
    {
        allItems.Clear();

        string catalogPath = GetCatalogPath();
        if (!File.Exists(catalogPath))
        {
            catalogLabel.Text = "목록 없음";
            itemComboBox.Enabled = false;
            itemComboBox.Items.Clear();
            SetPlaceholderIcon();
            AppendLog($"Catalog not found: {catalogPath}");
            return;
        }

        try
        {
            string json = File.ReadAllText(catalogPath, Encoding.UTF8);
            ItemCatalog? catalog = JsonSerializer.Deserialize<ItemCatalog>(json);
            if (catalog?.Items != null)
            {
                string catalogDirectory = Path.GetDirectoryName(catalogPath) ?? AppContext.BaseDirectory;
                foreach (ItemCatalogEntry item in catalog.Items)
                {
                    item.ResolveIconPath(catalogDirectory);
                    allItems.Add(item);
                }
            }

            allItems.Sort((left, right) => left.Id.CompareTo(right.Id));
            catalogLabel.Text = $"{allItems.Count} items";
            itemComboBox.Enabled = allItems.Count > 0;
            RefreshItemDropDown();
            AppendLog($"Catalog loaded: {allItems.Count} items");
        }
        catch (Exception exception) when (exception is IOException || exception is JsonException)
        {
            catalogLabel.Text = "목록 오류";
            itemComboBox.Enabled = false;
            SetPlaceholderIcon();
            AppendLog($"Catalog load failed: {exception.Message}");
        }
    }

    private static string GetCatalogPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Data", "item_catalog.json");
    }

    private void RefreshItemDropDown()
    {
        if (refreshingItems)
        {
            return;
        }

        refreshingItems = true;
        string filter = searchTextBox.Text.Trim();
        int previousItemId = Decimal.ToInt32(itemIdInput.Value);
        itemComboBox.BeginUpdate();
        itemComboBox.Items.Clear();

        foreach (ItemCatalogEntry item in allItems)
        {
            if (!item.Matches(filter))
            {
                continue;
            }

            itemComboBox.Items.Add(item);
        }

        itemComboBox.EndUpdate();
        refreshingItems = false;

        SelectCatalogItemById(previousItemId);
        if (itemComboBox.SelectedIndex < 0 && itemComboBox.Items.Count > 0)
        {
            itemComboBox.SelectedIndex = 0;
        }

        if (itemComboBox.SelectedIndex < 0)
        {
            SetPlaceholderIcon();
        }
    }

    private void ApplySelectedItem()
    {
        if (refreshingItems || itemComboBox.SelectedItem is not ItemCatalogEntry item)
        {
            return;
        }

        if (itemIdInput.Value != item.Id)
        {
            itemIdInput.Value = item.Id;
        }

        SetIcon(item);
    }

    private void SelectCatalogItemById(int itemId)
    {
        if (refreshingItems)
        {
            return;
        }

        for (int i = 0; i < itemComboBox.Items.Count; i++)
        {
            if (itemComboBox.Items[i] is ItemCatalogEntry item && item.Id == itemId)
            {
                if (itemComboBox.SelectedIndex != i)
                {
                    itemComboBox.SelectedIndex = i;
                }

                return;
            }
        }
    }

    private void SetIcon(ItemCatalogEntry item)
    {
        Image? previous = iconPreview.Image;
        iconPreview.Image = null;
        previous?.Dispose();

        if (!string.IsNullOrWhiteSpace(item.ResolvedIconPath) && File.Exists(item.ResolvedIconPath))
        {
            using FileStream stream = File.OpenRead(item.ResolvedIconPath);
            using Image loadedImage = Image.FromStream(stream);
            iconPreview.Image = new Bitmap(loadedImage);
            return;
        }

        SetPlaceholderIcon();
    }

    private void SetPlaceholderIcon()
    {
        Image? previous = iconPreview.Image;
        iconPreview.Image = null;
        previous?.Dispose();

        Bitmap bitmap = new Bitmap(128, 128);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(54, 58, 49));
        using Pen pen = new Pen(Color.FromArgb(112, 116, 94), 4f);
        graphics.DrawEllipse(pen, 30, 30, 68, 68);
        graphics.DrawLine(pen, 48, 80, 80, 48);
        iconPreview.Image = bitmap;
    }

    private void DrawItemComboItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index < 0 || e.Index >= itemComboBox.Items.Count)
        {
            return;
        }

        if (itemComboBox.Items[e.Index] is not ItemCatalogEntry item)
        {
            return;
        }

        Rectangle bounds = e.Bounds;
        Color textColor = (e.State & DrawItemState.Selected) != 0
            ? Color.White
            : Color.FromArgb(35, 38, 32);

        Rectangle iconRect = new Rectangle(bounds.Left + 4, bounds.Top + 3, 22, 22);
        string? iconPath = item.ResolvedIconPath;
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            try
            {
                using Image icon = Image.FromFile(iconPath);
                e.Graphics.DrawImage(icon, iconRect);
            }
            catch (IOException)
            {
                e.Graphics.FillEllipse(Brushes.Gray, iconRect);
            }
        }
        else
        {
            e.Graphics.FillEllipse(Brushes.Gray, iconRect);
        }

        using Brush brush = new SolidBrush(textColor);
        e.Graphics.DrawString(item.DisplayText, e.Font ?? Font, brush, bounds.Left + 34, bounds.Top + 5);
        e.DrawFocusRectangle();
    }

    private async Task SendGiveAsync()
    {
        int itemId = Decimal.ToInt32(itemIdInput.Value);
        int count = Decimal.ToInt32(countInput.Value);
        await SendCommandAsync($"give {itemId} {count}", $"Give itemId={itemId}, count={count}");
    }

    private async Task SendPingAsync()
    {
        await SendCommandAsync("ping", "Ping");
    }

    private async Task SendSaveAsync()
    {
        int slotNumber = SelectedSaveSlotNumber;
        await SendCommandAsync($"save {slotNumber}", $"Save Slot {slotNumber}");
        await RefreshStatusAsync();
    }

    private async Task SendLoadAsync()
    {
        int slotNumber = SelectedSaveSlotNumber;
        await SendCommandAsync($"load {slotNumber}", $"Load Slot {slotNumber}");
        await RefreshStatusAsync();
    }

    private async Task SendResetAsync()
    {
        int slotNumber = SelectedSaveSlotNumber;
        DialogResult result = MessageBox.Show(
            this,
            $"{slotNumber}번 저장 파일과 런타임 월드 상태를 초기화합니다.",
            "초기화",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);
        if (result != DialogResult.OK)
        {
            return;
        }

        await SendCommandAsync($"reset {slotNumber}", $"Reset Slot {slotNumber}");
        await RefreshStatusAsync();
    }

    private int SelectedSaveSlotNumber => Decimal.ToInt32(saveSlotInput.Value);

    private async Task SendDebugToggleAsync(string toggleName, bool value, string displayName)
    {
        if (applyingRuntimeDebugState)
        {
            return;
        }

        await SendCommandAsync(
            $"debug {toggleName} {(value ? 1 : 0)}",
            $"{displayName} {(value ? "ON" : "OFF")}");
        await RefreshStatusAsync();
    }

    private async Task RefreshStatusAsync()
    {
        if (pollingStatus)
        {
            return;
        }

        pollingStatus = true;
        try
        {
            string host = string.IsNullOrWhiteSpace(hostTextBox.Text) ? DefaultHost : hostTextBox.Text.Trim();
            int port = Decimal.ToInt32(portInput.Value);
            string response = await SendProtocolLineAsync(host, port, "status");
            if (response.StartsWith("ok ", StringComparison.OrdinalIgnoreCase)
                && TryReadProtocolFloat(response, "fps", out float fps)
                && TryReadProtocolFloat(response, "frameMs", out float frameMs))
            {
                fpsLabel.Text = $"FPS: {fps:0.0}  ({frameMs:0.0} ms)";
                fpsLabel.ForeColor = fps >= 50f
                    ? Color.FromArgb(126, 218, 126)
                    : fps >= 30f
                        ? Color.FromArgb(235, 189, 92)
                        : Color.FromArgb(236, 104, 94);
                UpdateRuntimeStatsFromResponse(response);
            }
            else
            {
                fpsLabel.Text = "FPS: --";
                fpsLabel.ForeColor = Color.FromArgb(176, 177, 158);
                SetRuntimeStatsUnavailable("상태 응답 없음");
            }

            PositionFpsLabel(fpsLabel.Parent ?? this);
        }
        catch (Exception exception) when (exception is SocketException || exception is IOException || exception is TimeoutException)
        {
            fpsLabel.Text = "FPS: offline";
            fpsLabel.ForeColor = Color.FromArgb(236, 104, 94);
            SetRuntimeStatsUnavailable("게임 연결 안 됨");
            PositionFpsLabel(fpsLabel.Parent ?? this);
        }
        finally
        {
            pollingStatus = false;
        }
    }

    private void UpdateRuntimeStatsFromResponse(string response)
    {
        if (!TryReadProtocolInt(response, "installTotal", out int installTotal)
            || !TryReadProtocolInt(response, "beltItems", out int beltItems))
        {
            SetRuntimeStatsUnavailable("통계 응답 없음");
            return;
        }

        TryReadProtocolToken(response, "installTypes", out string installTypes);
        runtimeStatsLabel.Text = $"Runtime Stats: 설치 {installTotal:N0}개    벨트 아이템 {beltItems:N0}개";
        runtimeStatsTextBox.Text = FormatInstallTypeCounts(installTypes);

        if (TryReadProtocolBool(response, "showConveyorSlotDots", out bool showConveyorSlotDots))
        {
            ApplyRuntimeCheckBoxState(showConveyorSlotDotsCheckBox, showConveyorSlotDots);
        }

        if (TryReadProtocolBool(response, "showSleepAwake", out bool showSleepAwake))
        {
            ApplyRuntimeCheckBoxState(showSleepAwakeCheckBox, showSleepAwake);
        }
    }

    private void SetRuntimeStatsUnavailable(string message)
    {
        runtimeStatsLabel.Text = $"Runtime Stats: {message}";
        runtimeStatsTextBox.Text = "설치 오브젝트 종류: --";
    }

    private void ApplyRuntimeCheckBoxState(CheckBox checkBox, bool isChecked)
    {
        applyingRuntimeDebugState = true;
        try
        {
            checkBox.Checked = isChecked;
        }
        finally
        {
            applyingRuntimeDebugState = false;
        }
    }

    private string FormatInstallTypeCounts(string installTypes)
    {
        if (string.IsNullOrWhiteSpace(installTypes) || installTypes == "-")
        {
            return "설치 오브젝트 종류: 없음";
        }

        string[] entries = installTypes.Split(',', StringSplitOptions.RemoveEmptyEntries);
        List<InstallTypeCount> counts = new List<InstallTypeCount>();
        foreach (string entry in entries)
        {
            string[] parts = entry.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2
                || !int.TryParse(parts[0], out int itemId)
                || !int.TryParse(parts[1], out int count)
                || itemId < 0
                || count <= 0)
            {
                continue;
            }

            counts.Add(new InstallTypeCount(itemId, count));
        }

        if (counts.Count <= 0)
        {
            return "설치 오브젝트 종류: 없음";
        }

        counts.Sort((left, right) =>
        {
            int countComparison = right.Count.CompareTo(left.Count);
            return countComparison != 0 ? countComparison : left.ItemId.CompareTo(right.ItemId);
        });

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("설치 오브젝트 종류별 갯수");
        for (int i = 0; i < counts.Count; i++)
        {
            InstallTypeCount count = counts[i];
            builder.Append(ResolveCatalogDisplayName(count.ItemId));
            builder.Append(" [");
            builder.Append(count.ItemId);
            builder.Append("]  x ");
            builder.AppendLine(count.Count.ToString());
        }

        return builder.ToString().TrimEnd();
    }

    private string ResolveCatalogDisplayName(int itemId)
    {
        for (int i = 0; i < allItems.Count; i++)
        {
            ItemCatalogEntry item = allItems[i];
            if (item != null && item.Id == itemId)
            {
                return item.DisplayName;
            }
        }

        return $"Item {itemId}";
    }

    private async Task SendCommandAsync(string command, string displayName)
    {
        SetBusy(true, $"{displayName} 전송 중...");
        try
        {
            string host = string.IsNullOrWhiteSpace(hostTextBox.Text) ? DefaultHost : hostTextBox.Text.Trim();
            int port = Decimal.ToInt32(portInput.Value);
            string response = await SendProtocolLineAsync(host, port, command);
            AppendLog($"> {command}");
            AppendLog(response);
            statusLabel.Text = response.StartsWith("ok ", StringComparison.OrdinalIgnoreCase)
                ? "성공"
                : "실패";
        }
        catch (Exception exception) when (exception is SocketException || exception is IOException || exception is TimeoutException)
        {
            statusLabel.Text = "연결 실패";
            AppendLog($"{displayName} failed: {exception.Message}");
        }
        finally
        {
            SetBusy(false, statusLabel.Text);
        }
    }

    private static async Task<string> SendProtocolLineAsync(string host, int port, string command)
    {
        using TcpClient client = new TcpClient();
        await client.ConnectAsync(host, port).WaitAsync(TimeSpan.FromMilliseconds(TimeoutMilliseconds));

        client.ReceiveTimeout = TimeoutMilliseconds;
        client.SendTimeout = TimeoutMilliseconds;

        await using NetworkStream stream = client.GetStream();
        await using StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true)
        {
            AutoFlush = true
        };
        using StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true);

        await writer.WriteLineAsync(command);
        string? response = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromMilliseconds(TimeoutMilliseconds));
        return string.IsNullOrWhiteSpace(response) ? "error no response from game" : response;
    }

    private static bool TryReadProtocolFloat(string response, string key, out float value)
    {
        value = 0f;
        string prefix = key + "=";
        string[] parts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            if (!part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return float.TryParse(
                part.Substring(prefix.Length),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        }

        return false;
    }

    private static bool TryReadProtocolInt(string response, string key, out int value)
    {
        value = 0;
        if (!TryReadProtocolToken(response, key, out string token))
        {
            return false;
        }

        return int.TryParse(token, out value);
    }

    private static bool TryReadProtocolBool(string response, string key, out bool value)
    {
        value = false;
        if (!TryReadProtocolToken(response, key, out string token))
        {
            return false;
        }

        if (string.Equals(token, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "on", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(token, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "off", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool TryReadProtocolToken(string response, string key, out string value)
    {
        value = string.Empty;
        string prefix = key + "=";
        string[] parts = response.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            if (!part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = part.Substring(prefix.Length).Trim('"');
            return true;
        }

        return false;
    }

    private void SetBusy(bool busy, string status)
    {
        giveButton.Enabled = !busy;
        pingButton.Enabled = !busy;
        saveSlotInput.Enabled = !busy;
        saveButton.Enabled = !busy;
        loadButton.Enabled = !busy;
        resetButton.Enabled = !busy;
        reloadButton.Enabled = !busy;
        showConveyorSlotDotsCheckBox.Enabled = !busy;
        showSleepAwakeCheckBox.Enabled = !busy;
        statusLabel.Text = status;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void AppendLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        logTextBox.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
    }
}

internal sealed class ItemCatalog
{
    [JsonPropertyName("items")]
    public List<ItemCatalogEntry>? Items { get; set; }
}

internal readonly struct InstallTypeCount
{
    public InstallTypeCount(int itemId, int count)
    {
        ItemId = itemId;
        Count = count;
    }

    public int ItemId { get; }
    public int Count { get; }
}

internal sealed class ItemCatalogEntry
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonIgnore]
    public string? ResolvedIconPath { get; private set; }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Item {Id}" : Name!;

    [JsonIgnore]
    public string DisplayText => $"[{Id}] {DisplayName}";

    public override string ToString()
    {
        return DisplayText;
    }

    public void ResolveIconPath(string catalogDirectory)
    {
        ResolvedIconPath = string.IsNullOrWhiteSpace(Icon)
            ? null
            : Path.GetFullPath(Path.Combine(catalogDirectory, Icon));
    }

    public bool Matches(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Id.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)
               || DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
