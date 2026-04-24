using System.Drawing;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace ProjectF.Tools.ItemGiveTool;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ItemGiveForm());
    }
}

internal sealed class ItemGiveForm : Form
{
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
    private readonly Button giveButton = new Button();
    private readonly Button pingButton = new Button();
    private readonly Button reloadButton = new Button();
    private readonly TextBox logTextBox = new TextBox();
    private readonly Label statusLabel = new Label();
    private readonly Label catalogLabel = new Label();
    private bool refreshingItems;

    public ItemGiveForm()
    {
        Text = "ProjectF Item Give Tool";
        MinimumSize = new Size(680, 560);
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
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 214f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 284f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        Label titleLabel = new Label
        {
            Text = "Item Give Tool",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 22f, FontStyle.Bold),
            ForeColor = Color.FromArgb(243, 234, 206),
            Location = new Point(0, 0)
        };

        Label descriptionLabel = new Label
        {
            Text = "아이템을 고르고 실행 중인 게임으로 바로 지급합니다.",
            AutoSize = true,
            ForeColor = Color.FromArgb(176, 177, 158),
            Location = new Point(2, 42)
        };

        Panel headerPanel = new Panel { Dock = DockStyle.Fill };
        headerPanel.Controls.Add(titleLabel);
        headerPanel.Controls.Add(descriptionLabel);
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
        layout.Controls.Add(logCard, 0, 3);
        layout.SetColumnSpan(logCard, 2);

        shellPanel.Controls.Add(layout);
        Controls.Add(shellPanel);
        AcceptButton = giveButton;

        LoadCatalog();
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

    private void SetBusy(bool busy, string status)
    {
        giveButton.Enabled = !busy;
        pingButton.Enabled = !busy;
        reloadButton.Enabled = !busy;
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
