using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace ProjectF.Tools.MapObjectProfiler;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new ProfilerForm());
    }
}

internal sealed class ProfilerForm : Form
{
    private const string ToolTitle = "ProjectF MapObject Profiler";
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 50877;
    private const int TimeoutMilliseconds = 5000;

    private readonly TextBox hostTextBox = new TextBox();
    private readonly NumericUpDown portInput = new NumericUpDown();
    private readonly NumericUpDown intervalInput = new NumericUpDown();
    private readonly NumericUpDown maxRowsInput = new NumericUpDown();
    private readonly CheckBox enableProfilingCheckBox = new CheckBox();
    private readonly Button refreshButton = new Button();
    private readonly Button copyTextButton = new Button();
    private readonly Label fpsLabel = new Label();
    private readonly Label summaryLabel = new Label();
    private readonly Panel chartPanel = new Panel();
    private readonly DataGridView rowsGrid = new DataGridView();
    private readonly TextBox logTextBox = new TextBox();
    private readonly System.Windows.Forms.Timer pollTimer = new System.Windows.Forms.Timer();
    private readonly List<ItemCatalogEntry> catalogItems = new List<ItemCatalogEntry>();
    private readonly Dictionary<int, Image> iconCache = new Dictionary<int, Image>();
    private readonly List<ProfileRow> profileRows = new List<ProfileRow>();

    private ProfileSnapshot? lastSnapshot;
    private bool applyingRuntimeState;
    private bool polling;

    public ProfilerForm()
    {
        Text = ToolTitle;
        MinimumSize = new Size(1080, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(28, 31, 34);

        TableLayoutPanel shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18),
            BackColor = Color.FromArgb(28, 31, 34)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 58f));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 54f));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 120f));

        Panel headerPanel = new Panel { Dock = DockStyle.Fill };
        Label titleLabel = new Label
        {
            Text = "MapObject Profiler",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 22f, FontStyle.Bold),
            ForeColor = Color.FromArgb(238, 232, 212),
            Location = new Point(0, 0)
        };
        Label descriptionLabel = new Label
        {
            Text = "MapObject Tick 비용을 타입/아이템별로 측정합니다.",
            AutoSize = true,
            ForeColor = Color.FromArgb(165, 174, 178),
            Location = new Point(2, 36)
        };
        fpsLabel.Text = "FPS: --";
        fpsLabel.AutoSize = true;
        fpsLabel.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
        fpsLabel.ForeColor = Color.FromArgb(165, 174, 178);
        headerPanel.Controls.Add(titleLabel);
        headerPanel.Controls.Add(descriptionLabel);
        headerPanel.Controls.Add(fpsLabel);
        headerPanel.Resize += (_, _) => PositionFpsLabel(headerPanel);
        shell.Controls.Add(headerPanel, 0, 0);

        FlowLayoutPanel controlPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };
        ConfigureTextInput(hostTextBox, DefaultHost, 130);
        ConfigureNumberInput(portInput, 1, 65535, DefaultPort, 76);
        ConfigureNumberInput(intervalInput, 100, 10000, 1000, 86);
        ConfigureNumberInput(maxRowsInput, 1, 256, 64, 70);
        intervalInput.Increment = 100;
        intervalInput.ValueChanged += (_, _) => pollTimer.Interval = Decimal.ToInt32(intervalInput.Value);

        StyleCheckBox(enableProfilingCheckBox, "Measure");
        enableProfilingCheckBox.CheckedChanged += async (_, _) => await SetProfilingEnabledAsync(enableProfilingCheckBox.Checked);

        StyleButton(refreshButton, "Refresh");
        refreshButton.Click += async (_, _) => await RefreshNowAsync();

        StyleButton(copyTextButton, "Copy Text");
        copyTextButton.Margin = new Padding(8, 2, 0, 0);
        copyTextButton.Enabled = false;
        copyTextButton.Click += (_, _) => CopyCurrentSnapshotText();

        AddLabeledControl(controlPanel, "Host", hostTextBox);
        AddLabeledControl(controlPanel, "Port", portInput);
        AddLabeledControl(controlPanel, "Interval ms", intervalInput);
        AddLabeledControl(controlPanel, "Rows", maxRowsInput);
        controlPanel.Controls.Add(enableProfilingCheckBox);
        controlPanel.Controls.Add(refreshButton);
        controlPanel.Controls.Add(copyTextButton);
        shell.Controls.Add(controlPanel, 0, 1);

        summaryLabel.Text = "측정 꺼짐";
        summaryLabel.Dock = DockStyle.Fill;
        summaryLabel.ForeColor = Color.FromArgb(238, 232, 212);
        summaryLabel.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        shell.Controls.Add(summaryLabel, 0, 2);

        SplitContainer split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 280,
            BackColor = Color.FromArgb(28, 31, 34)
        };
        chartPanel.Dock = DockStyle.Fill;
        chartPanel.BackColor = Color.FromArgb(34, 38, 41);
        chartPanel.Paint += DrawChart;
        split.Panel1.Controls.Add(chartPanel);

        ConfigureGrid();
        split.Panel2.Controls.Add(rowsGrid);
        shell.Controls.Add(split, 0, 3);

        logTextBox.Dock = DockStyle.Fill;
        logTextBox.Multiline = true;
        logTextBox.ScrollBars = ScrollBars.Vertical;
        logTextBox.ReadOnly = true;
        logTextBox.BorderStyle = BorderStyle.None;
        logTextBox.BackColor = Color.FromArgb(34, 38, 41);
        logTextBox.ForeColor = Color.FromArgb(218, 224, 222);
        logTextBox.Font = new Font("Consolas", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        shell.Controls.Add(logTextBox, 0, 4);

        Controls.Add(shell);

        LoadCatalog();
        pollTimer.Interval = Decimal.ToInt32(intervalInput.Value);
        pollTimer.Tick += async (_, _) => await PollAsync(false);
        Shown += async (_, _) =>
        {
            PositionFpsLabel(headerPanel);
            await PollAsync(true);
        };
        FormClosed += (_, _) =>
        {
            pollTimer.Stop();
            ClearIconCache();
        };
        pollTimer.Start();
    }

    private static void ConfigureTextInput(TextBox input, string text, int width)
    {
        input.Text = text;
        input.Width = width;
        input.Height = 28;
        input.Margin = new Padding(0, 4, 16, 0);
    }

    private static void ConfigureNumberInput(NumericUpDown input, int min, int max, int value, int width)
    {
        input.Minimum = min;
        input.Maximum = max;
        input.Value = value;
        input.Width = width;
        input.Height = 28;
        input.Margin = new Padding(0, 3, 16, 0);
    }

    private static void AddLabeledControl(FlowLayoutPanel panel, string labelText, Control input)
    {
        Label label = new Label
        {
            Text = labelText,
            AutoSize = true,
            ForeColor = Color.FromArgb(190, 199, 201),
            Margin = new Padding(0, 9, 6, 0)
        };
        panel.Controls.Add(label);
        panel.Controls.Add(input);
    }

    private void StyleCheckBox(CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        checkBox.AutoSize = true;
        checkBox.Margin = new Padding(6, 7, 18, 0);
        checkBox.ForeColor = Color.FromArgb(238, 232, 212);
        checkBox.FlatStyle = FlatStyle.Flat;
        checkBox.CheckedChanged += (_, _) =>
        {
            checkBox.ForeColor = checkBox.Checked
                ? Color.FromArgb(119, 218, 151)
                : Color.FromArgb(238, 232, 212);
        };
    }

    private static void StyleButton(Button button, string text)
    {
        button.Text = text;
        button.Width = 108;
        button.Height = 32;
        button.Margin = new Padding(0, 2, 0, 0);
        button.BackColor = Color.FromArgb(70, 86, 94);
        button.ForeColor = Color.FromArgb(245, 246, 238);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(96, 112, 120);
    }

    private void ConfigureGrid()
    {
        rowsGrid.Dock = DockStyle.Fill;
        rowsGrid.AllowUserToAddRows = false;
        rowsGrid.AllowUserToDeleteRows = false;
        rowsGrid.AllowUserToResizeRows = false;
        rowsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        rowsGrid.BackgroundColor = Color.FromArgb(34, 38, 41);
        rowsGrid.BorderStyle = BorderStyle.None;
        rowsGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        rowsGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        rowsGrid.EnableHeadersVisualStyles = false;
        rowsGrid.ReadOnly = true;
        rowsGrid.RowHeadersVisible = false;
        rowsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        rowsGrid.GridColor = Color.FromArgb(53, 59, 62);
        rowsGrid.DefaultCellStyle.BackColor = Color.FromArgb(34, 38, 41);
        rowsGrid.DefaultCellStyle.ForeColor = Color.FromArgb(222, 228, 226);
        rowsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(52, 74, 83);
        rowsGrid.DefaultCellStyle.SelectionForeColor = Color.White;
        rowsGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(48, 55, 59);
        rowsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(238, 232, 212);
        rowsGrid.RowTemplate.Height = 34;
        rowsGrid.Columns.Add(new DataGridViewImageColumn
        {
            Name = "Icon",
            HeaderText = "",
            FillWeight = 8,
            ImageLayout = DataGridViewImageCellLayout.Zoom
        });
        rowsGrid.Columns.Add(CreateTextColumn("Rank", "#", 7));
        rowsGrid.Columns.Add(CreateTextColumn("Item", "Item", 24));
        rowsGrid.Columns.Add(CreateTextColumn("Type", "Type", 17));
        rowsGrid.Columns.Add(CreateTextColumn("Kind", "Tick", 8));
        rowsGrid.Columns.Add(CreateTextColumn("Active", "Active", 10));
        rowsGrid.Columns.Add(CreateTextColumn("Samples", "Samples", 11));
        rowsGrid.Columns.Add(CreateTextColumn("TotalMs", "Total ms", 11));
        rowsGrid.Columns.Add(CreateTextColumn("AvgUs", "Avg us", 11));
        rowsGrid.Columns.Add(CreateTextColumn("MaxUs", "Max us", 11));
    }

    private static DataGridViewTextBoxColumn CreateTextColumn(string name, string headerText, float fillWeight)
    {
        return new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = headerText,
            FillWeight = fillWeight
        };
    }

    private void PositionFpsLabel(Control parent)
    {
        fpsLabel.Location = new Point(Math.Max(0, parent.ClientSize.Width - fpsLabel.Width), 8);
    }

    private async Task SetProfilingEnabledAsync(bool enabled)
    {
        if (applyingRuntimeState)
        {
            return;
        }

        SetBusy(true);
        try
        {
            string response = await SendProtocolLineAsync(BuildHost(), BuildPort(), $"debug mapObjectTickProfiling {(enabled ? 1 : 0)}");
            AppendLog($"> debug mapObjectTickProfiling {(enabled ? 1 : 0)}");
            AppendLog(response);
            if (!enabled)
            {
                ApplyEmptyState("측정 꺼짐");
            }

            await PollAsync(true);
        }
        catch (Exception exception) when (IsProtocolException(exception))
        {
            ApplyOfflineState(exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshNowAsync()
    {
        await PollAsync(true);
    }

    private async Task PollAsync(bool logFailure)
    {
        if (polling)
        {
            return;
        }

        polling = true;
        try
        {
            string statusResponse = await SendProtocolLineAsync(BuildHost(), BuildPort(), "status");
            ApplyStatus(statusResponse);
            if (!enableProfilingCheckBox.Checked)
            {
                ApplyEmptyState("측정 꺼짐");
                return;
            }

            int maxRows = Decimal.ToInt32(maxRowsInput.Value);
            string perfResponse = await SendProtocolLineAsync(BuildHost(), BuildPort(), $"perf {maxRows}");
            if (!perfResponse.StartsWith("ok ", StringComparison.OrdinalIgnoreCase)
                || !TryReadProtocolToken(perfResponse, "perfData", out string perfDataToken))
            {
                if (logFailure)
                {
                    AppendLog($"perf failed: {perfResponse}");
                }

                ApplyEmptyState("측정 응답 없음");
                return;
            }

            string json = Encoding.UTF8.GetString(Convert.FromBase64String(perfDataToken));
            ProfileSnapshot? snapshot = JsonSerializer.Deserialize<ProfileSnapshot>(json);
            ApplySnapshot(snapshot);
        }
        catch (Exception exception) when (IsProtocolException(exception))
        {
            if (logFailure)
            {
                AppendLog($"poll failed: {exception.Message}");
            }

            ApplyOfflineState(exception.Message);
        }
        finally
        {
            polling = false;
        }
    }

    private void ApplyStatus(string response)
    {
        if (response.StartsWith("ok ", StringComparison.OrdinalIgnoreCase)
            && TryReadProtocolFloat(response, "fps", out float fps)
            && TryReadProtocolFloat(response, "frameMs", out float frameMs))
        {
            fpsLabel.Text = $"FPS: {fps:0.0} ({frameMs:0.0} ms)";
            fpsLabel.ForeColor = fps >= 50f
                ? Color.FromArgb(119, 218, 151)
                : fps >= 30f
                    ? Color.FromArgb(235, 189, 92)
                    : Color.FromArgb(236, 104, 94);
        }
        else
        {
            fpsLabel.Text = "FPS: --";
            fpsLabel.ForeColor = Color.FromArgb(165, 174, 178);
        }

        if (TryReadProtocolBool(response, "mapObjectTickProfiling", out bool enabled))
        {
            ApplyRuntimeCheckBox(enabled);
        }

        PositionFpsLabel(fpsLabel.Parent ?? this);
    }

    private void ApplyRuntimeCheckBox(bool enabled)
    {
        applyingRuntimeState = true;
        try
        {
            enableProfilingCheckBox.Checked = enabled;
        }
        finally
        {
            applyingRuntimeState = false;
        }
    }

    private void ApplySnapshot(ProfileSnapshot? snapshot)
    {
        lastSnapshot = snapshot;
        profileRows.Clear();
        if (snapshot?.Rows != null)
        {
            profileRows.AddRange(snapshot.Rows.Where(row => row.Samples > 0 || row.ActiveCount > 0));
        }

        if (snapshot == null)
        {
            summaryLabel.Text = "측정 응답 없음";
        }
        else if (!snapshot.Enabled)
        {
            summaryLabel.Text = "측정 꺼짐";
        }
        else
        {
            summaryLabel.Text =
                $"Window {snapshot.WindowMs:0.#} ms / Frames {snapshot.BeltLoopProfileFrames:N0} / Active Update {snapshot.ActiveUpdateTicks:N0} / Belts {snapshot.ActiveBeltTicks:N0} / Loops/f {snapshot.BeltItemLoopIterations:N1} / Data {snapshot.BeltDataMotionLoopIterations:N1} / Queue {snapshot.BeltActiveLoopIterations:N1} / Line {snapshot.BeltStraightLineBlockLoopIterations:N1} / Visual {snapshot.BeltVisualLoopIterations:N1} / Try {snapshot.BeltTryMoveAttempts:N1}:{snapshot.BeltTryMoveSuccesses:N1} / St {snapshot.BeltStraightMoveAttempts:N1}:{snapshot.BeltStraightMoveSuccesses:N1} / Plan {snapshot.BeltPlanMoveCalls:N1} / Apply {snapshot.BeltPlannedMoveApplications:N1} / Touch {snapshot.BeltTouchedBlockRefreshes:N1} / Wake {snapshot.BeltWakeAroundCalls:N1} / Ref {snapshot.BeltActivityRefreshCalls:N1} / Rows {profileRows.Count:N0}";
        }

        RefreshGrid();
        chartPanel.Invalidate();
        UpdateCopyButtonEnabled();
    }

    private void ApplyEmptyState(string message)
    {
        lastSnapshot = null;
        profileRows.Clear();
        summaryLabel.Text = message;
        rowsGrid.Rows.Clear();
        chartPanel.Invalidate();
        UpdateCopyButtonEnabled();
    }

    private void ApplyOfflineState(string message)
    {
        fpsLabel.Text = "FPS: offline";
        fpsLabel.ForeColor = Color.FromArgb(236, 104, 94);
        ApplyEmptyState($"게임 연결 안 됨: {message}");
    }

    private void RefreshGrid()
    {
        rowsGrid.Rows.Clear();
        for (int i = 0; i < profileRows.Count; i++)
        {
            ProfileRow row = profileRows[i];
            rowsGrid.Rows.Add(
                ResolveIcon(row.ItemId)!,
                row.Rank,
                ResolveRowDisplayName(row),
                row.Type,
                row.Kind,
                row.ActiveCount.ToString("N0", CultureInfo.InvariantCulture),
                row.Samples.ToString("N0", CultureInfo.InvariantCulture),
                (row.TotalUs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture),
                row.AvgUs.ToString("0.#", CultureInfo.InvariantCulture),
                row.MaxUs.ToString("0.#", CultureInfo.InvariantCulture));
        }
    }

    private void DrawChart(object? sender, PaintEventArgs e)
    {
        Rectangle bounds = chartPanel.ClientRectangle;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using SolidBrush backgroundBrush = new SolidBrush(Color.FromArgb(34, 38, 41));
        e.Graphics.FillRectangle(backgroundBrush, bounds);

        if (profileRows.Count <= 0)
        {
            DrawCenteredText(e.Graphics, bounds, "측정 데이터 없음");
            return;
        }

        Rectangle inner = Rectangle.Inflate(bounds, -14, -12);
        int visibleRows = Math.Min(profileRows.Count, Math.Max(1, inner.Height / 34));
        int rowHeight = Math.Max(30, inner.Height / visibleRows);
        double maxTotalUs = Math.Max(1.0, profileRows[0].TotalUs);

        using SolidBrush textBrush = new SolidBrush(Color.FromArgb(232, 238, 235));
        using SolidBrush dimBrush = new SolidBrush(Color.FromArgb(158, 169, 172));
        using SolidBrush barBackBrush = new SolidBrush(Color.FromArgb(52, 58, 62));
        using SolidBrush updateBrush = new SolidBrush(Color.FromArgb(89, 183, 216));
        using SolidBrush lateBrush = new SolidBrush(Color.FromArgb(177, 132, 224));
        using SolidBrush beltBrush = new SolidBrush(Color.FromArgb(235, 189, 92));
        using Pen dividerPen = new Pen(Color.FromArgb(55, 62, 66));
        Font graphFont = (Font ?? SystemFonts.MessageBoxFont)!;

        for (int i = 0; i < visibleRows; i++)
        {
            ProfileRow row = profileRows[i];
            int y = inner.Top + i * rowHeight;
            Rectangle rowRect = new Rectangle(inner.Left, y, inner.Width, rowHeight - 2);
            Rectangle iconRect = new Rectangle(rowRect.Left, rowRect.Top + 3, 26, 26);
            Image? icon = ResolveIcon(row.ItemId);
            if (icon != null)
            {
                e.Graphics.DrawImage(icon, iconRect);
            }
            else
            {
                using SolidBrush placeholderBrush = new SolidBrush(Color.FromArgb(82, 92, 98));
                e.Graphics.FillEllipse(placeholderBrush, iconRect);
            }

            int textLeft = iconRect.Right + 10;
            int metricWidth = 300;
            int barLeft = Math.Max(textLeft + 270, rowRect.Right - metricWidth);
            int barWidth = Math.Max(80, rowRect.Right - barLeft);
            Rectangle nameRect = new Rectangle(textLeft, rowRect.Top + 2, Math.Max(80, barLeft - textLeft - 12), 18);
            Rectangle detailRect = new Rectangle(textLeft, rowRect.Top + 19, nameRect.Width, 16);
            Rectangle barRect = new Rectangle(barLeft, rowRect.Top + 6, barWidth - 10, 12);
            Rectangle metricRect = new Rectangle(barLeft, rowRect.Top + 20, barWidth - 10, 16);

            using StringFormat textFormat = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };

            e.Graphics.DrawString($"{row.Rank}. {ResolveRowDisplayName(row)}", graphFont, textBrush, nameRect, textFormat);
            e.Graphics.DrawString($"{row.Kind} / {row.Type}", graphFont, dimBrush, detailRect, textFormat);
            e.Graphics.FillRectangle(barBackBrush, barRect);
            int filledWidth = Math.Max(1, (int)Math.Round(barRect.Width * Math.Clamp(row.TotalUs / maxTotalUs, 0.0, 1.0)));
            e.Graphics.FillRectangle(
                ResolveRowBrush(row, updateBrush, lateBrush, beltBrush),
                new Rectangle(barRect.Left, barRect.Top, filledWidth, barRect.Height));

            string metrics = $"{row.TotalUs / 1000.0:0.###} ms   avg {row.AvgUs:0.#} us   max {row.MaxUs:0.#} us   x{row.Samples:N0}   active {row.ActiveCount:N0}";
            e.Graphics.DrawString(metrics, graphFont, dimBrush, metricRect, textFormat);
            e.Graphics.DrawLine(dividerPen, rowRect.Left, rowRect.Bottom, rowRect.Right, rowRect.Bottom);
        }
    }

    private static SolidBrush ResolveRowBrush(ProfileRow row, SolidBrush updateBrush, SolidBrush lateBrush, SolidBrush beltBrush)
    {
        if (string.Equals(row.Kind, "Belt", StringComparison.OrdinalIgnoreCase))
        {
            return beltBrush;
        }

        return string.Equals(row.Kind, "Late", StringComparison.OrdinalIgnoreCase) ? lateBrush : updateBrush;
    }

    private static void DrawCenteredText(Graphics graphics, Rectangle bounds, string text)
    {
        using SolidBrush brush = new SolidBrush(Color.FromArgb(158, 169, 172));
        using StringFormat format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(text, SystemFonts.MessageBoxFont!, brush, bounds, format);
    }

    private string ResolveRowDisplayName(ProfileRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.ItemName))
        {
            return row.ItemName;
        }

        ItemCatalogEntry? catalogEntry = FindCatalogEntry(row.ItemId);
        return catalogEntry != null ? catalogEntry.DisplayName : row.Type;
    }

    private Image? ResolveIcon(int itemId)
    {
        if (itemId < 0)
        {
            return null;
        }

        if (iconCache.TryGetValue(itemId, out Image? cachedIcon))
        {
            return cachedIcon;
        }

        ItemCatalogEntry? item = FindCatalogEntry(itemId);
        if (item == null || string.IsNullOrWhiteSpace(item.ResolvedIconPath) || !File.Exists(item.ResolvedIconPath))
        {
            return null;
        }

        try
        {
            using Image sourceIcon = Image.FromFile(item.ResolvedIconPath);
            Image cached = new Bitmap(sourceIcon);
            iconCache[itemId] = cached;
            return cached;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private ItemCatalogEntry? FindCatalogEntry(int itemId)
    {
        for (int i = 0; i < catalogItems.Count; i++)
        {
            if (catalogItems[i].Id == itemId)
            {
                return catalogItems[i];
            }
        }

        return null;
    }

    private void LoadCatalog()
    {
        string catalogPath = Path.Combine(AppContext.BaseDirectory, "Data", "item_catalog.json");
        if (!File.Exists(catalogPath))
        {
            AppendLog($"Catalog not found: {catalogPath}");
            return;
        }

        try
        {
            string json = File.ReadAllText(catalogPath, Encoding.UTF8);
            ItemCatalog? catalog = JsonSerializer.Deserialize<ItemCatalog>(json);
            string catalogDirectory = Path.GetDirectoryName(catalogPath) ?? AppContext.BaseDirectory;
            if (catalog?.Items == null)
            {
                return;
            }

            catalogItems.Clear();
            foreach (ItemCatalogEntry item in catalog.Items)
            {
                item.ResolveIconPath(catalogDirectory);
                catalogItems.Add(item);
            }

            catalogItems.Sort((left, right) => left.Id.CompareTo(right.Id));
            AppendLog($"Catalog loaded: {catalogItems.Count:N0} items");
        }
        catch (Exception exception) when (exception is IOException || exception is JsonException)
        {
            AppendLog($"Catalog load failed: {exception.Message}");
        }
    }

    private void ClearIconCache()
    {
        foreach (Image icon in iconCache.Values)
        {
            icon.Dispose();
        }

        iconCache.Clear();
    }

    private string BuildHost()
    {
        return string.IsNullOrWhiteSpace(hostTextBox.Text) ? DefaultHost : hostTextBox.Text.Trim();
    }

    private int BuildPort()
    {
        return Decimal.ToInt32(portInput.Value);
    }

    private void SetBusy(bool busy)
    {
        hostTextBox.Enabled = !busy;
        portInput.Enabled = !busy;
        intervalInput.Enabled = !busy;
        maxRowsInput.Enabled = !busy;
        enableProfilingCheckBox.Enabled = !busy;
        refreshButton.Enabled = !busy;
        UpdateCopyButtonEnabled(busy);
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void UpdateCopyButtonEnabled(bool busy = false)
    {
        copyTextButton.Enabled = !busy && lastSnapshot != null && lastSnapshot.Enabled;
    }

    private void CopyCurrentSnapshotText()
    {
        if (lastSnapshot == null)
        {
            return;
        }

        try
        {
            Clipboard.SetText(BuildSnapshotClipboardText(lastSnapshot));
            AppendLog("Profile snapshot copied to clipboard");
        }
        catch (Exception exception) when (exception is ExternalException || exception is ThreadStateException)
        {
            AppendLog($"clipboard copy failed: {exception.Message}");
        }
    }

    private string BuildSnapshotClipboardText(ProfileSnapshot snapshot)
    {
        StringBuilder builder = new StringBuilder(4096);
        builder.AppendLine("MapObject Profiler Snapshot");
        builder.AppendLine($"CopiedAt\t{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Enabled\t{snapshot.Enabled}");
        builder.AppendLine($"Frame\t{snapshot.Frame}");
        builder.AppendLine($"WindowMs\t{snapshot.WindowMs.ToString("0.###", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"BeltLoopProfileFrames\t{snapshot.BeltLoopProfileFrames.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"ActiveUpdateTicks\t{snapshot.ActiveUpdateTicks.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"ActiveBeltTicks\t{snapshot.ActiveBeltTicks.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"ActiveBeltDataMotions\t{snapshot.ActiveBeltDataMotions.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"ActiveBeltVisualTicks\t{snapshot.ActiveBeltVisualTicks.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine();
        builder.AppendLine("BeltCountersPerFrame");
        AppendMetric(builder, "Loops", snapshot.BeltItemLoopIterations);
        AppendMetric(builder, "DataMotionLoops", snapshot.BeltDataMotionLoopIterations);
        AppendMetric(builder, "ActiveQueueLoops", snapshot.BeltActiveLoopIterations);
        AppendMetric(builder, "StraightLineBlockLoops", snapshot.BeltStraightLineBlockLoopIterations);
        AppendMetric(builder, "VisualLoops", snapshot.BeltVisualLoopIterations);
        AppendMetric(builder, "TryMoveAttempts", snapshot.BeltTryMoveAttempts);
        AppendMetric(builder, "TryMoveSuccesses", snapshot.BeltTryMoveSuccesses);
        AppendMetric(builder, "StraightMoveAttempts", snapshot.BeltStraightMoveAttempts);
        AppendMetric(builder, "StraightMoveSuccesses", snapshot.BeltStraightMoveSuccesses);
        AppendMetric(builder, "PlanMoveCalls", snapshot.BeltPlanMoveCalls);
        AppendMetric(builder, "PlannedMoveApplications", snapshot.BeltPlannedMoveApplications);
        AppendMetric(builder, "TouchedBlockRefreshes", snapshot.BeltTouchedBlockRefreshes);
        AppendMetric(builder, "WakeAroundCalls", snapshot.BeltWakeAroundCalls);
        AppendMetric(builder, "ActivityRefreshCalls", snapshot.BeltActivityRefreshCalls);
        builder.AppendLine();
        builder.AppendLine("Rows");
        builder.AppendLine("Rank\tKind\tItem\tType\tItemId\tActive\tSamples\tTotalMs\tAvgUs\tMaxUs");
        for (int i = 0; i < profileRows.Count; i++)
        {
            ProfileRow row = profileRows[i];
            builder.Append(row.Rank.ToString(CultureInfo.InvariantCulture)).Append('\t');
            builder.Append(SanitizeClipboardCell(row.Kind)).Append('\t');
            builder.Append(SanitizeClipboardCell(ResolveRowDisplayName(row))).Append('\t');
            builder.Append(SanitizeClipboardCell(row.Type)).Append('\t');
            builder.Append(row.ItemId.ToString(CultureInfo.InvariantCulture)).Append('\t');
            builder.Append(row.ActiveCount.ToString(CultureInfo.InvariantCulture)).Append('\t');
            builder.Append(row.Samples.ToString(CultureInfo.InvariantCulture)).Append('\t');
            builder.Append((row.TotalUs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)).Append('\t');
            builder.Append(row.AvgUs.ToString("0.###", CultureInfo.InvariantCulture)).Append('\t');
            builder.AppendLine(row.MaxUs.ToString("0.###", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static void AppendMetric(StringBuilder builder, string name, double value)
    {
        builder.Append(name).Append('\t').AppendLine(value.ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static string SanitizeClipboardCell(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
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

    private static bool TryReadProtocolBool(string response, string key, out bool value)
    {
        value = false;
        if (!TryReadProtocolToken(response, key, out string token))
        {
            return false;
        }

        if (token == "1" || token.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (token == "0" || token.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool TryReadProtocolFloat(string response, string key, out float value)
    {
        value = 0f;
        return TryReadProtocolToken(response, key, out string token)
               && float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsProtocolException(Exception exception)
    {
        return exception is SocketException
               || exception is IOException
               || exception is TimeoutException
               || exception is FormatException
               || exception is JsonException;
    }

    private void AppendLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        logTextBox.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
    }
}

internal sealed class ProfileSnapshot
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("frame")]
    public int Frame { get; set; }

    [JsonPropertyName("windowMs")]
    public double WindowMs { get; set; }

    [JsonPropertyName("activeUpdateTicks")]
    public int ActiveUpdateTicks { get; set; }

    [JsonPropertyName("activeLateTicks")]
    public int ActiveLateTicks { get; set; }

    [JsonPropertyName("activeBeltTicks")]
    public int ActiveBeltTicks { get; set; }

    [JsonPropertyName("activeBeltDataMotions")]
    public int ActiveBeltDataMotions { get; set; }

    [JsonPropertyName("activeBeltVisualTicks")]
    public int ActiveBeltVisualTicks { get; set; }

    [JsonPropertyName("beltLoopProfileFrames")]
    public int BeltLoopProfileFrames { get; set; }

    [JsonPropertyName("beltItemLoopIterations")]
    public double BeltItemLoopIterations { get; set; }

    [JsonPropertyName("beltDataMotionLoopIterations")]
    public double BeltDataMotionLoopIterations { get; set; }

    [JsonPropertyName("beltActiveLoopIterations")]
    public double BeltActiveLoopIterations { get; set; }

    [JsonPropertyName("beltStraightLineBlockLoopIterations")]
    public double BeltStraightLineBlockLoopIterations { get; set; }

    [JsonPropertyName("beltVisualLoopIterations")]
    public double BeltVisualLoopIterations { get; set; }

    [JsonPropertyName("beltTryMoveAttempts")]
    public double BeltTryMoveAttempts { get; set; }

    [JsonPropertyName("beltTryMoveSuccesses")]
    public double BeltTryMoveSuccesses { get; set; }

    [JsonPropertyName("beltStraightMoveAttempts")]
    public double BeltStraightMoveAttempts { get; set; }

    [JsonPropertyName("beltStraightMoveSuccesses")]
    public double BeltStraightMoveSuccesses { get; set; }

    [JsonPropertyName("beltPlanMoveCalls")]
    public double BeltPlanMoveCalls { get; set; }

    [JsonPropertyName("beltPlannedMoveApplications")]
    public double BeltPlannedMoveApplications { get; set; }

    [JsonPropertyName("beltTouchedBlockRefreshes")]
    public double BeltTouchedBlockRefreshes { get; set; }

    [JsonPropertyName("beltWakeAroundCalls")]
    public double BeltWakeAroundCalls { get; set; }

    [JsonPropertyName("beltActivityRefreshCalls")]
    public double BeltActivityRefreshCalls { get; set; }

    [JsonPropertyName("rowCount")]
    public int RowCount { get; set; }

    [JsonPropertyName("rows")]
    public List<ProfileRow>? Rows { get; set; }
}

internal sealed class ProfileRow
{
    [JsonPropertyName("rank")]
    public int Rank { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("itemId")]
    public int ItemId { get; set; }

    [JsonPropertyName("itemName")]
    public string ItemName { get; set; } = string.Empty;

    [JsonPropertyName("activeCount")]
    public int ActiveCount { get; set; }

    [JsonPropertyName("samples")]
    public long Samples { get; set; }

    [JsonPropertyName("totalUs")]
    public double TotalUs { get; set; }

    [JsonPropertyName("avgUs")]
    public double AvgUs { get; set; }

    [JsonPropertyName("maxUs")]
    public double MaxUs { get; set; }
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

    public void ResolveIconPath(string catalogDirectory)
    {
        ResolvedIconPath = string.IsNullOrWhiteSpace(Icon)
            ? null
            : Path.GetFullPath(Path.Combine(catalogDirectory, Icon));
    }
}
