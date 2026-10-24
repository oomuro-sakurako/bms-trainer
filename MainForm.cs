using BmsTrainer.Audio;
using BmsTrainer.Bms;

namespace BmsTrainer;

public sealed class MainForm : Form
{
    // 他の欄より先に読み込み、表示言語を決める
    readonly Settings settings = LoadSettings();
    readonly KeysoundProcessor processor = new();
    readonly List<BmsChart> charts = [];
    BmsChart? chart;
    const int MaxFreqStep = 12;
    double rate = 1.0;
    int? freqStep = 0; // FREQ 指定のときの段数
    bool syncing;
    CancellationTokenSource? cts;

    readonly PathLabel folderLabel = new();
    readonly ComboBox diffCombo = new();
    readonly Label titleLabel = new();
    readonly Label artistLabel = new();
    readonly Label levelLabel = new();
    readonly Label infoLabel = new();
    readonly RadioButton bpmRadio = new();
    readonly RadioButton freqRadio = new();
    readonly NumericUpDown bpmInput = new();
    readonly ComboBox freqCombo = new();
    readonly TrackBar rateSlider = new();
    readonly CheckBox pitchCheck = new();
    readonly ComboBox rankCombo = new();
    readonly Label previewLabel = new();
    readonly ChartNameFields nameFields = new(520, showArtist: false);
    readonly Button createButton = new();
    readonly Button cleanButton = new();
    readonly ProgressBar progressBar = new();
    readonly Label statusLabel = new();
    readonly ComboBox languageCombo = new();
    readonly ToolTip tip = new();

    static Settings LoadSettings()
    {
        var s = Settings.Load();
        L.Current = L.Parse(s.Language) ?? L.Detect();
        return s;
    }

    public MainForm(string? initialPath)
    {
        Text = "bmstrainer";
        Font = L.UiFont();
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(560, 560);
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = Colors.FormBg;
        ForeColor = Colors.TextBoxFg;
        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) => { if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } p) OpenPath(p[0]); };

        BuildLayout();

        pitchCheck.Checked = settings.ChangePitch;
        rankCombo.SelectedIndex = Math.Max(0, Array.IndexOf(RankChoices, settings.Rank));
        (settings.UseFreq ? freqRadio : bpmRadio).Checked = true;
        SetFreq(Math.Clamp(settings.FreqStep, -MaxFreqStep, MaxFreqStep));
        UpdateInputMode();
        ShowChart(null);

        var path = initialPath ?? settings.LastPath;
        if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path))) OpenPath(path);
    }

    void BuildLayout()
    {
        int x = 20, w = ClientSize.Width - 40, y = 16;

        var openFile = MakeButton(L.OpenChart, new Rectangle(x, y, 130, 30));
        openFile.Click += (_, _) => BrowseFile();
        var openFolder = MakeButton(L.OpenFolder, new Rectangle(x + 140, y, 130, 30));
        openFolder.Click += (_, _) => BrowseFolder();
        var convertOsu = MakeButton(L.ConvertOsu, new Rectangle(x + w - 150, y, 150, 30));
        convertOsu.Click += (_, _) =>
        {
            using var form = new OsuConvertForm(settings);
            if (form.ShowDialog(this) == DialogResult.OK && form.ResultPath != null) OpenPath(form.ResultPath);
        };
        y += 40;

        Place(folderLabel, new Rectangle(x, y, w, 20));
        folderLabel.ForeColor = Colors.Disabled;
        folderLabel.AutoEllipsis = true;
        // 入り切らないパスはマウスを乗せると全体を表示する
        folderLabel.TextChanged += (_, _) => tip.SetToolTip(folderLabel, folderLabel.Text);
        y += 24;

        Place(diffCombo, new Rectangle(x, y, w, 28));
        diffCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        diffCombo.FlatStyle = FlatStyle.Flat;
        diffCombo.BackColor = Colors.TextBoxBg;
        diffCombo.ForeColor = Colors.TextBoxFg;
        diffCombo.SelectedIndexChanged += (_, _) => ShowChart(diffCombo.SelectedIndex >= 0 ? charts[diffCombo.SelectedIndex] : null);
        y += 40;

        Place(titleLabel, new Rectangle(x, y, w, 30));
        titleLabel.Font = L.UiFont(14f, FontStyle.Bold);
        titleLabel.AutoEllipsis = true;
        y += 32;
        Place(artistLabel, new Rectangle(x, y, w, 20));
        artistLabel.ForeColor = Colors.ReadOnlyFg;
        artistLabel.AutoEllipsis = true;
        y += 22;
        Place(levelLabel, new Rectangle(x, y, w, 22));
        levelLabel.Font = new Font(Font, FontStyle.Bold);
        y += 24;
        Place(infoLabel, new Rectangle(x, y, w, 40));
        infoLabel.ForeColor = Colors.ReadOnlyFg;
        y += 50;

        // 速度指定
        var speedTitle = MakeLabel(L.Speed, new Rectangle(x, y, 100, 22), Colors.AccentPink);
        speedTitle.Font = new Font(Font, FontStyle.Bold);
        y += 26;

        Place(bpmRadio, new Rectangle(x, y, 110, 26));
        bpmRadio.Text = L.ByBpm;
        Place(bpmInput, new Rectangle(x + 115, y, 100, 26));
        bpmInput.DecimalPlaces = 1;
        bpmInput.Maximum = 10000;
        bpmInput.Minimum = 1;
        Place(freqRadio, new Rectangle(x + 260, y, 110, 26));
        freqRadio.Text = L.ByFreq;
        Place(freqCombo, new Rectangle(x + 375, y, 80, 26));
        freqCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        freqCombo.FlatStyle = FlatStyle.Flat;
        freqCombo.MaxDropDownItems = 2 * MaxFreqStep + 1;
        for (int step = -MaxFreqStep; step <= MaxFreqStep; step++) freqCombo.Items.Add(RateSettings.FreqText(step));
        bpmInput.TextAlign = HorizontalAlignment.Right;
        foreach (Control n in new Control[] { bpmInput, freqCombo })
        {
            n.BackColor = Colors.TextBoxBg;
            n.ForeColor = Colors.TextBoxFg;
        }
        bpmRadio.CheckedChanged += (_, _) => UpdateInputMode();
        freqRadio.CheckedChanged += (_, _) => UpdateInputMode();
        bpmInput.ValueChanged += (_, _) => { if (!syncing && chart != null) SetRate((double)bpmInput.Value / chart.MainBpm); };
        freqCombo.SelectedIndexChanged += (_, _) => { if (!syncing && freqCombo.SelectedIndex >= 0) SetFreq(freqCombo.SelectedIndex - MaxFreqStep); };
        y += 34;

        Place(rateSlider, new Rectangle(x, y, w, 30));
        rateSlider.Minimum = 50;
        rateSlider.Maximum = 200;
        rateSlider.TickFrequency = 10;
        rateSlider.SmallChange = 1;
        rateSlider.LargeChange = 5;
        rateSlider.BackColor = Colors.FormBg;
        rateSlider.Scroll += (_, _) =>
        {
            if (freqRadio.Checked) SetFreq((int)Math.Round(12 * Math.Log2(rateSlider.Value / 100.0)));
            else SetRate(rateSlider.Value / 100.0);
        };
        y += rateSlider.Height + 6; // TrackBar は AutoSize で高さが変わる

        Place(pitchCheck, new Rectangle(x, y, w, 26));
        pitchCheck.Text = L.ChangePitch;
        pitchCheck.CheckedChanged += (_, _) => UpdatePreview();
        y += 34;

        var rankTitle = MakeLabel(L.Judge, new Rectangle(x, y + 3, 85, 22), Colors.AccentPink);
        rankTitle.Font = new Font(Font, FontStyle.Bold);
        Place(rankCombo, new Rectangle(x + 90, y, 130, 26));
        rankCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        rankCombo.FlatStyle = FlatStyle.Flat;
        rankCombo.BackColor = Colors.TextBoxBg;
        rankCombo.ForeColor = Colors.TextBoxFg;
        foreach (var r in RankChoices) rankCombo.Items.Add(r is { } v ? RateSettings.RankName(v) : L.NoChange);
        rankCombo.SelectedIndexChanged += (_, _) => UpdatePreview();
        y += 36;

        Place(previewLabel, new Rectangle(x, y, w, 44));
        previewLabel.ForeColor = Colors.Easier;
        y += 52;

        Place(nameFields, new Rectangle(x, y, w, nameFields.Height));
        y += nameFields.Height + 12;

        Place(createButton, new Rectangle(x, y, 200, 40));
        StyleButton(createButton, L.CreateChart);
        createButton.BackColor = Colors.AccentPink2;
        createButton.Font = new Font(Font, FontStyle.Bold);
        createButton.Click += async (_, _) => await CreateAsync();
        Place(cleanButton, new Rectangle(x + w - 190, y + 6, 190, 30));
        StyleButton(cleanButton, L.DeleteGenerated);
        cleanButton.Click += (_, _) => CleanGenerated();
        y += 50;

        Place(progressBar, new Rectangle(x, y, w, 12));
        y += 18;
        Place(statusLabel, new Rectangle(x, y + 2, w - 130, 20));
        statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        statusLabel.ForeColor = Colors.ReadOnlyFg;
        statusLabel.AutoEllipsis = true;
        statusLabel.TextChanged += (_, _) => tip.SetToolTip(statusLabel, statusLabel.Text);

        // 表示言語（変更は再起動後に反映）
        Place(languageCombo, new Rectangle(x + w - 120, y, 120, 26));
        languageCombo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        languageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        languageCombo.FlatStyle = FlatStyle.Flat;
        languageCombo.BackColor = Colors.TextBoxBg;
        languageCombo.ForeColor = Colors.TextBoxFg;
        foreach (var (_, name) in L.Choices) languageCombo.Items.Add(name);
        languageCombo.SelectedIndex = Array.FindIndex(L.Choices, c => c.Lang == L.Current);
        languageCombo.SelectedIndexChanged += (_, _) => ChangeLanguage(L.Choices[languageCombo.SelectedIndex].Lang);
        tip.SetToolTip(languageCombo, "言語 / Language / 언어");
        y += 36;

        ClientSize = new Size(ClientSize.Width, y);

        Label MakeLabel(string text, Rectangle bounds, Color color)
        {
            var l = new Label { Text = text, ForeColor = color };
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
        c.Anchor = AutoAnchor(c, bounds, ClientSize.Width - 20);
    }

    /// <summary>
    /// ウィンドウの幅を変えたとき用。右端まである欄は横に伸ばし、右端のボタンは右に寄せる
    /// </summary>
    public static AnchorStyles AutoAnchor(Control c, Rectangle bounds, int contentRight)
    {
        if (bounds.Right < contentRight - 1) return AnchorStyles.Top | AnchorStyles.Left;
        return c is Button ? AnchorStyles.Top | AnchorStyles.Right : AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
    }

    /// <summary>初期サイズを最小サイズにして、前回の幅を復元する</summary>
    public static void EnableResize(Form form, int savedWidth)
    {
        form.MinimumSize = form.Size;
        if (savedWidth > form.Width)
            form.Width = Math.Min(savedWidth, Screen.FromControl(form).WorkingArea.Width);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        EnableResize(this, settings.MainWindowWidth);
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

    void BrowseFile()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = L.BmsFilter,
            InitialDirectory = chart != null ? Path.GetDirectoryName(chart.FilePath) : "",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenPath(dlg.FileName);
    }

    void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            InitialDirectory = chart != null ? Path.GetDirectoryName(chart.FilePath)! : "",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenPath(dlg.SelectedPath);
    }

    /// <summary>譜面ファイルならそのフォルダの差分一覧を読み、そのファイルを選択する</summary>
    void OpenPath(string path)
    {
        var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
        charts.Clear();
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            if (!BmsChart.Extensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
            try
            {
                var c = new BmsChart(file);
                if (!c.IsGenerated || string.Equals(file, path, StringComparison.OrdinalIgnoreCase)) charts.Add(c);
            }
            catch (Exception ex)
            {
                statusLabel.Text = L.LoadFailed(Path.GetFileName(file), ex.Message);
            }
        }
        charts.Sort((a, b) => a.Difficulty != b.Difficulty ? a.Difficulty.CompareTo(b.Difficulty)
            : LevelOf(a).CompareTo(LevelOf(b)));
        folderLabel.Text = dir;
        diffCombo.Items.Clear();
        foreach (var c in charts) diffCombo.Items.Add(c.DisplayName);
        settings.LastPath = path;

        if (charts.Count == 0)
        {
            ShowChart(null);
            statusLabel.Text = L.NoBmsInFolder;
            return;
        }
        var index = charts.FindIndex(c => string.Equals(c.FilePath, path, StringComparison.OrdinalIgnoreCase));
        diffCombo.SelectedIndex = Math.Max(index, 0);

        static double LevelOf(BmsChart c) => BmsChart.TryParseDouble(c.PlayLevel, out var v) ? v : 0;
    }

    void ShowChart(BmsChart? c)
    {
        chart = c;
        createButton.Enabled = c != null;
        cleanButton.Enabled = c != null;
        if (c == null)
        {
            titleLabel.Text = L.NoChartSelected;
            artistLabel.Text = levelLabel.Text = infoLabel.Text = "";
            UpdatePreview();
            return;
        }
        titleLabel.Text = string.IsNullOrWhiteSpace(c.SubTitle) ? c.Title : $"{c.Title}  {c.SubTitle}";
        artistLabel.Text = $"{c.Artist}    {c.Genre}";
        var diff = c.Difficulty switch { 1 => "BEGINNER", 2 => "NORMAL", 3 => "HYPER", 4 => "ANOTHER", 5 => "INSANE", _ => "" };
        levelLabel.Text = $"{diff} {(string.IsNullOrEmpty(c.PlayLevel) ? "" : "☆" + c.PlayLevel)}".Trim();
        levelLabel.ForeColor = Colors.GetDifficultyColor(c.Difficulty);
        infoLabel.Text = $"BPM {BpmText(c, 1)}    {L.Notes} {c.NoteCount}    TOTAL {(c.Total is { } t ? BmsRateConverter.Fmt(t) : "-")}    {L.Judge} {RankText(c)}\n" +
                         $"{L.Length} {TimeText(c.LengthSeconds)}    {Path.GetFileName(c.FilePath)}";
        // 譜面を切り替えても倍率は維持し、BPM 欄を新しい譜面に合わせる
        nameFields.ResetEdits();
        SetRate(rate);
    }

    void UpdateInputMode()
    {
        bpmInput.Enabled = bpmRadio.Checked;
        freqCombo.Enabled = freqRadio.Checked;
        // BPM 指定は狙った BPM ぴったり、FREQ 指定は段数ちょうどの倍率に合わせ直す
        if (bpmRadio.Checked)
        {
            if (chart != null) SetRate((double)bpmInput.Value / chart.MainBpm);
            else { freqStep = null; UpdatePreview(); }
        }
        else if (freqRadio.Checked)
            SetFreq(freqCombo.SelectedIndex >= 0 ? freqCombo.SelectedIndex - MaxFreqStep : (int)Math.Round(12 * Math.Log2(rate)));
    }

    /// <summary>BPM 指定の倍率</summary>
    void SetRate(double value)
    {
        if (freqRadio.Checked)
        {
            SetFreq(freqStep ?? 0); // 譜面切り替え時など、FREQ 指定中は段数を保つ
            return;
        }
        freqStep = null;
        ApplyRate(Math.Round(Math.Clamp(value, 0.25, 3.0), 4));
    }

    void SetFreq(int step)
    {
        step = Math.Clamp(step, -MaxFreqStep, MaxFreqStep);
        freqStep = step;
        ApplyRate(RateSettings.FreqStepRate(step));
    }

    void ApplyRate(double value)
    {
        rate = value;
        syncing = true;
        try
        {
            if (chart != null && !bpmInput.Focused)
                bpmInput.Value = Math.Clamp((decimal)Math.Round(chart.MainBpm * rate, 1), bpmInput.Minimum, bpmInput.Maximum);
            // BPM 指定のときは、ちょうど一致する段数があればそれを表示する
            int nearest = (int)Math.Round(12 * Math.Log2(rate));
            bool exact = freqStep != null || Math.Abs(RateSettings.FreqStepRate(nearest) - rate) < 0.0005;
            freqCombo.SelectedIndex = exact && Math.Abs(nearest) <= MaxFreqStep ? nearest + MaxFreqStep : -1;
            rateSlider.Value = Math.Clamp((int)Math.Round(rate * 100), rateSlider.Minimum, rateSlider.Maximum);
        }
        finally { syncing = false; }
        UpdatePreview();
    }

    /// <summary>判定の選択肢（null = 変更しない）</summary>
    static readonly int?[] RankChoices = [null, 3, 2, 1];

    int? SelectedRank => rankCombo.SelectedIndex > 0 ? RankChoices[rankCombo.SelectedIndex] : null;

    static string RankText(BmsChart c) =>
        c.DefExRank is { } ex ? $"DEFEXRANK {BmsRateConverter.Fmt(ex)}"
        : c.Rank is { } r ? RateSettings.RankName(r)
        : "-";

    RateSettings CurrentSettings() => freqStep is { } step
        ? RateSettings.FromFreq(step, pitchCheck.Checked, SelectedRank)
        : new RateSettings(rate, pitchCheck.Checked, null, SelectedRank);

    void UpdatePreview()
    {
        var speed = freqStep is { } step ? $"FREQ{RateSettings.FreqText(step)}  (x{rate:0.0000})" : $"x{rate:0.00##}";
        if (chart == null)
        {
            previewLabel.Text = speed;
            nameFields.SetDefaults(null, "");
            return;
        }
        nameFields.SetDefaults(BmsRateConverter.DefaultNames(chart, CurrentSettings()), Path.GetExtension(chart.FilePath));
        previewLabel.Text =
            $"{speed}    BPM {BpmText(chart, 1)} → {BpmText(chart, rate)}\n" +
            $"{L.Length} {TimeText(chart.LengthSeconds)} → {TimeText(chart.LengthSeconds / rate)}    " +
            (pitchCheck.Checked ? L.PitchChanged : L.PitchKept) +
            $"    {L.Judge}: {(SelectedRank is { } r ? RateSettings.RankName(r) : RankText(chart))}";
    }

    static string BpmText(BmsChart c, double r)
    {
        string F(double v) => BmsRateConverter.Fmt(Math.Round(v * r, 1));
        return Math.Abs(c.MinBpm - c.MaxBpm) < 0.01 ? F(c.MainBpm) : $"{F(c.MainBpm)} ({F(c.MinBpm)}-{F(c.MaxBpm)})";
    }

    static string TimeText(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss");

    async Task CreateAsync()
    {
        if (chart == null) return;
        if (cts != null) { cts.Cancel(); return; }
        if (Math.Abs(rate - 1.0) < 1e-9 && SelectedRank == null)
        {
            statusLabel.Text = L.NothingToCreate;
            return;
        }
        var target = chart;
        var s = CurrentSettings();
        var names = nameFields.Current;
        string output;
        try
        {
            output = names.OutputPath(target.FilePath);
        }
        catch (InvalidOperationException ex)
        {
            statusLabel.Text = ex.Message;
            return;
        }
        if (!ChartNameFields.ConfirmOverwrite(this, output)) return;
        cts = new CancellationTokenSource();
        createButton.Text = L.Cancel;
        diffCombo.Enabled = false;
        progressBar.Value = 0;
        statusLabel.Text = L.ConvertingKeysounds;
        var progress = new Progress<ProcessProgress>(p =>
        {
            progressBar.Maximum = Math.Max(p.Total, 1);
            progressBar.Value = Math.Min(p.Done, progressBar.Maximum);
            statusLabel.Text = $"{L.ConvertingKeysounds} {p.Done}/{p.Total}  {p.File}";
        });
        try
        {
            var token = cts.Token;
            var map = await Task.Run(() => processor.Process(target, s, progress, token));
            BmsRateConverter.Write(target, s, names, map, output);
            statusLabel.Text = L.Created(Path.GetFileName(output));
            if (processor.Errors.Count > 0)
            {
                MessageBox.Show(this, L.KeysoundErrors(processor.Errors.Count) + "\n\n" +
                    string.Join("\n", processor.Errors.Take(20)), "bmstrainer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = L.Canceled;
        }
        catch (Exception ex)
        {
            statusLabel.Text = L.CreateFailed;
            MessageBox.Show(this, ex.Message, "bmstrainer", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            cts.Dispose();
            cts = null;
            createButton.Text = L.CreateChart;
            diffCombo.Enabled = true;
        }
    }

    /// <summary>このフォルダに bmstrainer が作った譜面とキー音を削除する</summary>
    void CleanGenerated()
    {
        if (chart == null) return;
        var dir = Path.GetDirectoryName(chart.FilePath)!;
        var files = Directory.EnumerateFiles(dir)
            .Where(f => BmsChart.Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .Where(f => { try { return new BmsChart(f).IsGenerated; } catch { return false; } })
            .ToList();
        var keysoundDir = Path.Combine(dir, BmsRateConverter.KeysoundRootDir);
        bool hasKeysounds = Directory.Exists(keysoundDir);
        if (files.Count == 0 && !hasKeysounds)
        {
            statusLabel.Text = L.NoGenerated;
            return;
        }
        var msg = L.DeleteConfirm(files.Count) + "\n" +
                  string.Join("\n", files.Take(15).Select(f => "  " + Path.GetFileName(f))) +
                  (hasKeysounds ? $"\n\n{L.KeysoundFolder}: {BmsRateConverter.KeysoundRootDir}\\" : "");
        if (MessageBox.Show(this, msg, "bmstrainer", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        foreach (var f in files) File.Delete(f);
        if (hasKeysounds) Directory.Delete(keysoundDir, true);
        statusLabel.Text = L.Deleted(files.Count);
    }

    /// <summary>表示言語を保存し、再起動して反映する</summary>
    void ChangeLanguage(Language lang)
    {
        if (lang == L.Current) return;
        settings.Language = L.Code(lang);
        if (MessageBox.Show(this, L.RestartToApply, "bmstrainer", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            Application.Restart();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        cts?.Cancel();
        settings.UseFreq = freqRadio.Checked;
        settings.FreqStep = freqCombo.SelectedIndex >= 0 ? freqCombo.SelectedIndex - MaxFreqStep : 0;
        settings.ChangePitch = pitchCheck.Checked;
        settings.Rank = SelectedRank;
        settings.MainWindowWidth = WindowState == FormWindowState.Normal ? Width : RestoreBounds.Width;
        settings.Save();
        base.OnFormClosing(e);
    }
}
