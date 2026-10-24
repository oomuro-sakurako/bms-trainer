using System.IO.Compression;
using BmsTrainer.Bms;
using BmsTrainer.Osu;

namespace BmsTrainer;

/// <summary>osu!mania 譜面を BMS に変換する画面。本体 BMS を指定するとキー音を補完する</summary>
public sealed class OsuConvertForm : Form
{
    static readonly int[] Ranks = [3, 2, 1, 0];

    readonly Settings settings;
    readonly List<OsuBeatmap> beatmaps = [];
    BmsChart? body;
    string? bodyFolder;
    /// <summary>本体フォルダの譜面と、それぞれの検出オフセット・キー音の一致率（一致率の高い順）</summary>
    readonly List<(BmsChart Chart, double Offset, double MatchRate)> bodyCharts = [];
    string? tempDir;
    bool detecting;

    readonly PathLabel osuLabel = new();
    readonly ComboBox diffCombo = new();
    readonly PathLabel bodyLabel = new();
    readonly ComboBox bodyCombo = new();
    readonly Button bodyClear = new();
    readonly NumericUpDown offsetInput = new();
    readonly Button detectButton = new();
    readonly Label matchLabel = new();
    readonly ComboBox laneCombo = new();
    readonly ComboBox rankCombo = new();
    readonly PathLabel outDirLabel = new();
    readonly Button outDirButton = new();
    readonly ChartNameFields nameFields = new(520, showArtist: true);
    readonly Button convertButton = new();
    readonly Label statusLabel = new();
    readonly ToolTip tip = new();

    /// <summary>変換した BMS のパス</summary>
    public string? ResultPath { get; private set; }

    public OsuConvertForm(Settings settings)
    {
        this.settings = settings;
        Text = L.OsuTitle;
        Font = L.UiFont();
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 480);
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Colors.FormBg;
        ForeColor = Colors.TextBoxFg;
        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths) return;
            foreach (var p in paths)
            {
                // BMS 譜面か、.osu を含まないフォルダは本体フォルダとして開く
                if (BmsChart.Extensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
                    OpenBodyFolder(Path.GetDirectoryName(p)!, p);
                else if (Directory.Exists(p) && !Directory.EnumerateFiles(p, "*.osu").Any())
                    OpenBodyFolder(p);
                else OpenOsu(p);
            }
        };
        BuildLayout();
        UpdateState();
    }

    void BuildLayout()
    {
        int x = 20, w = ClientSize.Width - 40, y = 16;

        Title(L.OsuChart, y);
        var openOsu = MakeButton(L.Open, new Rectangle(x + w - 90, y - 4, 90, 28));
        openOsu.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = L.OsuFilter };
            if (dlg.ShowDialog(this) == DialogResult.OK) OpenOsu(dlg.FileName);
        };
        y += 28;
        Place(osuLabel, new Rectangle(x, y, w, 20));
        osuLabel.ForeColor = Colors.Disabled;
        osuLabel.AutoEllipsis = true;
        y += 24;
        Place(diffCombo, new Rectangle(x, y, w, 28));
        StyleCombo(diffCombo);
        diffCombo.SelectedIndexChanged += (_, _) =>
        {
            nameFields.ResetEdits();
            // osu! の差分ごとに、一番合う本体の譜面を選び直す
            if (bodyFolder != null) OpenBodyFolder(bodyFolder);
            UpdateState();
        };
        y += 42;

        Title(L.BodyFolder, y);
        var openBody = MakeButton(L.Open, new Rectangle(x + w - 190, y - 4, 90, 28));
        openBody.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        openBody.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { InitialDirectory = bodyFolder ?? "" };
            if (dlg.ShowDialog(this) == DialogResult.OK) OpenBodyFolder(dlg.SelectedPath);
        };
        Place(bodyClear, new Rectangle(x + w - 90, y - 4, 90, 28));
        StyleButton(bodyClear, L.Clear);
        bodyClear.Click += (_, _) =>
        {
            body = null;
            bodyFolder = null;
            bodyCharts.Clear();
            bodyCombo.Items.Clear();
            nameFields.ResetEdits();
            UpdateState();
        };
        y += 28;
        Place(bodyLabel, new Rectangle(x, y, w, 20));
        bodyLabel.ForeColor = Colors.Disabled;
        bodyLabel.AutoEllipsis = true;
        y += 24;
        MakeLabel(L.BaseChart, new Rectangle(x, y + 3, 110, 22));
        Place(bodyCombo, new Rectangle(x + 115, y, w - 115, 26));
        StyleCombo(bodyCombo);
        bodyCombo.SelectedIndexChanged += (_, _) =>
        {
            if (bodyCombo.SelectedIndex < 0) return;
            var (chart, offset, _) = bodyCharts[bodyCombo.SelectedIndex];
            body = chart;
            nameFields.ResetEdits();
            detecting = true;
            offsetInput.Value = Math.Clamp((decimal)offset, offsetInput.Minimum, offsetInput.Maximum);
            detecting = false;
            UpdateMatch();
            UpdateState();
        };
        y += 34;

        MakeLabel(L.Offset, new Rectangle(x, y + 3, 80, 22));
        Place(offsetInput, new Rectangle(x + 85, y, 110, 26));
        offsetInput.DecimalPlaces = 1;
        offsetInput.Minimum = -600000;
        offsetInput.Maximum = 600000;
        offsetInput.BackColor = Colors.TextBoxBg;
        offsetInput.ForeColor = Colors.TextBoxFg;
        offsetInput.TextAlign = HorizontalAlignment.Right;
        offsetInput.ValueChanged += (_, _) => { if (!detecting) UpdateMatch(); };
        MakeLabel("ms", new Rectangle(x + 198, y + 3, 30, 22));
        Place(detectButton, new Rectangle(x + 235, y - 1, 90, 28));
        StyleButton(detectButton, L.AutoDetect);
        detectButton.Click += (_, _) => AutoDetect();
        Place(matchLabel, new Rectangle(x + 335, y + 3, w - 335, 22));
        matchLabel.ForeColor = Colors.Easier;
        y += 34;

        MakeLabel(L.LaneOrderLabel, new Rectangle(x, y + 3, 110, 22));
        Place(laneCombo, new Rectangle(x + 115, y, 300, 26));
        StyleCombo(laneCombo);
        foreach (var o in LaneOrder.All) laneCombo.Items.Add(o.Label);
        laneCombo.SelectedIndex = Math.Clamp(settings.OsuLaneOrder, 0, LaneOrder.All.Length - 1);
        y += 42;

        Title(L.Common, y);
        y += 28;
        MakeLabel(L.Judge, new Rectangle(x, y + 3, 110, 22));
        Place(rankCombo, new Rectangle(x + 115, y, 140, 26));
        StyleCombo(rankCombo);
        foreach (var r in Ranks) rankCombo.Items.Add(RateSettings.RankName(r));
        rankCombo.SelectedIndex = Math.Max(0, Array.IndexOf(Ranks, settings.OsuRank));
        y += 34;
        MakeLabel(L.OutputFolder, new Rectangle(x, y + 3, 110, 22));
        Place(outDirLabel, new Rectangle(x + 115, y + 3, w - 215, 22));
        outDirLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        outDirLabel.AutoEllipsis = true;
        Place(outDirButton, new Rectangle(x + w - 90, y - 1, 90, 28));
        StyleButton(outDirButton, L.Change);
        outDirButton.Click += (_, _) =>
        {
            using var dlg = new FolderBrowserDialog { InitialDirectory = settings.OsuOutputDir };
            if (dlg.ShowDialog(this) == DialogResult.OK) { settings.OsuOutputDir = dlg.SelectedPath; UpdateState(); }
        };
        y += 44;

        Place(nameFields, new Rectangle(x, y, w, nameFields.Height));
        y += nameFields.Height + 12;

        Place(convertButton, new Rectangle(x, y, 200, 40));
        StyleButton(convertButton, L.Convert);
        convertButton.BackColor = Colors.AccentPink2;
        convertButton.Font = new Font(Font, FontStyle.Bold);
        convertButton.Click += async (_, _) => await ConvertAsync();
        y += 50;
        Place(statusLabel, new Rectangle(x, y, w, 40));
        statusLabel.ForeColor = Colors.ReadOnlyFg;
        y += 46;
        ClientSize = new Size(ClientSize.Width, y);
        // 入り切らないパスはマウスを乗せると全体を表示する
        foreach (var label in new Label[] { osuLabel, bodyLabel, outDirLabel, statusLabel })
            label.TextChanged += (_, _) => tip.SetToolTip(label, label.Text);

        void Title(string text, int top)
        {
            var l = MakeLabel(text, new Rectangle(x, top, w - 200, 22));
            l.ForeColor = Colors.AccentPink;
            l.Font = new Font(Font, FontStyle.Bold);
        }
        Label MakeLabel(string text, Rectangle bounds)
        {
            var l = new Label { Text = text };
            Place(l, bounds);
            return l;
        }
        Button MakeButton(string text, Rectangle bounds)
        {
            var b = new Button();
            Place(b, bounds);
            StyleButton(b, text);
            return b;
        }
    }

    void Place(Control c, Rectangle bounds)
    {
        c.Bounds = bounds;
        Controls.Add(c);
        c.Anchor = MainForm.AutoAnchor(c, bounds, ClientSize.Width - 20);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        MainForm.EnableResize(this, settings.OsuWindowWidth);
    }

    static void StyleButton(Button b, string text)
    {
        b.Text = text;
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = Colors.PaleBlue;
        b.BackColor = Colors.TextBoxBg;
        b.ForeColor = Colors.TextBoxFg;
        b.Cursor = Cursors.Hand;
    }

    static void StyleCombo(ComboBox c)
    {
        c.DropDownStyle = ComboBoxStyle.DropDownList;
        c.FlatStyle = FlatStyle.Flat;
        c.BackColor = Colors.TextBoxBg;
        c.ForeColor = Colors.TextBoxFg;
    }

    OsuBeatmap? Selected => diffCombo.SelectedIndex >= 0 ? beatmaps[diffCombo.SelectedIndex] : null;

    void OpenOsu(string path)
    {
        try
        {
            string dir;
            IEnumerable<string> files;
            if (Path.GetExtension(path).Equals(".osz", StringComparison.OrdinalIgnoreCase))
            {
                CleanTemp();
                tempDir = Path.Combine(Path.GetTempPath(), "bmstrainer", "osz_" + Guid.NewGuid().ToString("N"));
                ZipFile.ExtractToDirectory(path, tempDir);
                dir = tempDir;
                files = Directory.EnumerateFiles(dir, "*.osu");
            }
            else
            {
                dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
                files = Directory.Exists(path) ? Directory.EnumerateFiles(dir, "*.osu") : [path];
            }
            beatmaps.Clear();
            foreach (var f in files)
            {
                try
                {
                    var b = new OsuBeatmap(f);
                    if (b.IsSupportedMania) beatmaps.Add(b);
                }
                catch { }
            }
            beatmaps.Sort((a, b) => a.Notes.Count.CompareTo(b.Notes.Count));
            osuLabel.Text = path;
            diffCombo.Items.Clear();
            foreach (var b in beatmaps) diffCombo.Items.Add(b.DisplayName);
            if (beatmaps.Count > 0) diffCombo.SelectedIndex = 0;
            else statusLabel.Text = L.NoManiaCharts;
        }
        catch (Exception ex)
        {
            statusLabel.Text = L.CouldNotLoad(ex.Message);
        }
        UpdateState();
    }

    /// <summary>
    /// 本体フォルダの譜面を読み込み、osu! 譜面とキー音が一番一致する譜面を基準に選ぶ。
    /// preferred を指定した場合はその譜面を選ぶ。bmstrainer が作った譜面は除く。
    /// </summary>
    void OpenBodyFolder(string folder, string? preferred = null)
    {
        bodyFolder = folder;
        bodyCharts.Clear();
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            if (!BmsChart.Extensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
            try
            {
                var chart = new BmsChart(file);
                if (chart.Lines.Count > 0 && chart.Lines[0].StartsWith("*bmstrainer", StringComparison.OrdinalIgnoreCase)) continue;
                var (offset, rate) = Selected is { } osu ? OsuToBmsConverter.DetectOffset(osu, chart) : (0, 0);
                bodyCharts.Add((chart, offset, rate));
            }
            catch { }
        }
        bodyCharts.Sort((a, b) => b.MatchRate.CompareTo(a.MatchRate));
        bodyCombo.Items.Clear();
        foreach (var (chart, _, rate) in bodyCharts)
            bodyCombo.Items.Add(Selected != null ? $"{chart.DisplayName}  {L.MatchShort(rate)}" : chart.DisplayName);
        if (bodyCharts.Count == 0)
        {
            body = null;
            statusLabel.Text = L.NoBmsInBodyFolder;
            UpdateState();
            return;
        }
        var index = preferred != null ? bodyCharts.FindIndex(c => string.Equals(c.Chart.FilePath, preferred, StringComparison.OrdinalIgnoreCase)) : -1;
        bodyCombo.SelectedIndex = -1;
        bodyCombo.SelectedIndex = Math.Max(index, 0);
    }

    void AutoDetect()
    {
        if (body == null || Selected is not { } osu) return;
        var (offset, _) = OsuToBmsConverter.DetectOffset(osu, body);
        detecting = true;
        offsetInput.Value = Math.Clamp((decimal)offset, offsetInput.Minimum, offsetInput.Maximum);
        detecting = false;
        UpdateMatch();
    }

    void UpdateMatch()
    {
        if (body == null || Selected is not { } osu) { matchLabel.Text = ""; return; }
        var rate = OsuToBmsConverter.MatchRate(osu, body, (double)offsetInput.Value);
        matchLabel.Text = L.KeysoundMatch(rate);
        matchLabel.ForeColor = rate >= 0.8 ? Colors.Easier : Colors.Salmon;
    }

    void UpdateState()
    {
        bool hasBody = body != null;
        bodyLabel.Text = bodyFolder ?? L.NoBody;
        bodyClear.Enabled = bodyFolder != null;
        bodyCombo.Enabled = offsetInput.Enabled = detectButton.Enabled = laneCombo.Enabled = hasBody;
        if (!hasBody) matchLabel.Text = "";
        outDirButton.Enabled = !hasBody;
        outDirLabel.Text = hasBody ? L.SameAsBody : string.IsNullOrEmpty(settings.OsuOutputDir) ? L.NotSet : settings.OsuOutputDir;
        convertButton.Enabled = Selected != null && (hasBody || Directory.Exists(settings.OsuOutputDir));
        nameFields.SetDefaults(Selected is { } osu ? OsuToBmsConverter.DefaultNames(osu, body) : null,
            hasBody ? Path.GetExtension(body!.FilePath) : ".bms");
    }

    async Task ConvertAsync()
    {
        if (Selected is not { } osu) return;
        int rank = Ranks[Math.Max(0, rankCombo.SelectedIndex)];
        var lane = LaneOrder.All[Math.Max(0, laneCombo.SelectedIndex)];
        double offset = (double)offsetInput.Value;
        var target = body;
        var names = nameFields.Current;
        if (target != null)
        {
            try
            {
                if (!ChartNameFields.ConfirmOverwrite(this, names.OutputPath(target.FilePath))) return;
            }
            catch (InvalidOperationException ex)
            {
                statusLabel.Text = ex.Message;
                return;
            }
        }
        convertButton.Enabled = false;
        statusLabel.Text = L.Converting;
        try
        {
            var result = await Task.Run(() => target != null
                ? OsuToBmsConverter.ConvertWithBody(osu, target, offset, lane, rank, names)
                : OsuToBmsConverter.ConvertStandalone(osu, settings.OsuOutputDir, rank, names));
            ResultPath = result.OutputPath;
            settings.OsuRank = rank;
            settings.OsuLaneOrder = laneCombo.SelectedIndex;
            var detail = target != null
                ? L.KeysoundedNotes(result.KeysoundedNotes, result.NoteCount, result.SilentNotes)
                : L.NoteCount(result.NoteCount);
            statusLabel.Text = $"{L.Created(Path.GetFileName(result.OutputPath))}\n{detail}";
            if (MessageBox.Show(this, $"{statusLabel.Text}\n\n{L.OpenInMain}", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = L.ConvertFailed;
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UpdateState();
        }
    }

    void CleanTemp()
    {
        if (tempDir == null) return;
        try { Directory.Delete(tempDir, true); } catch { }
        tempDir = null;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        settings.OsuWindowWidth = WindowState == FormWindowState.Normal ? Width : RestoreBounds.Width;
        CleanTemp();
        base.OnFormClosed(e);
    }
}
