using BmsTrainer.Bms;

namespace BmsTrainer;

/// <summary>
/// 作成する譜面のタイトル・サブタイトル（差分名）・アーティスト・ファイル名の入力欄。
/// 手で書き換えていない欄は、設定に合わせて自動の名前に追従する。
/// </summary>
public sealed class ChartNameFields : UserControl
{
    const int RowHeight = 32, LabelWidth = 150;

    readonly TextBox title = new(), subtitle = new(), artist = new(), file = new();
    readonly Label extLabel = new();
    readonly HashSet<TextBox> edited = [];
    readonly bool showArtist;
    ChartNames? defaults;
    bool syncing;

    public ChartNameFields(int width, bool showArtist)
    {
        this.showArtist = showArtist;
        BackColor = Colors.FormBg;
        ForeColor = Colors.TextBoxFg;
        Width = width;
        Font = L.UiFont();

        var heading = new Label { Text = L.NamesHeading, ForeColor = Colors.AccentPink, Bounds = new Rectangle(0, 0, width - 160, 22) };
        heading.Font = L.UiFont(9.5f, FontStyle.Bold);
        Controls.Add(heading);
        var reset = new Button { Text = L.ResetNames, Bounds = new Rectangle(width - 150, -2, 150, 26) };
        reset.FlatStyle = FlatStyle.Flat;
        reset.FlatAppearance.BorderColor = Colors.PaleBlue;
        reset.BackColor = Colors.TextBoxBg;
        reset.Cursor = Cursors.Hand;
        reset.Click += (_, _) => { edited.Clear(); Apply(); };
        Controls.Add(reset);
        reset.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        int y = 30;
        AddRow(L.FieldTitle, title, ref y);
        AddRow(L.FieldSubtitle, subtitle, ref y);
        if (showArtist) AddRow(L.FieldArtist, artist, ref y);
        AddRow(L.FieldFileName, file, ref y);
        file.Width -= 50;
        extLabel.Bounds = new Rectangle(width - 45, y - RowHeight + 3, 45, 22);
        extLabel.ForeColor = Colors.Disabled;
        Controls.Add(extLabel);
        extLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        Height = y;
    }

    void AddRow(string text, TextBox box, ref int y)
    {
        Controls.Add(new Label { Text = text, Bounds = new Rectangle(0, y + 3, LabelWidth, 22) });
        box.Bounds = new Rectangle(LabelWidth, y, Width - LabelWidth, 26);
        box.BackColor = Colors.TextBoxBg;
        box.ForeColor = Colors.TextBoxFg;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.TextChanged += (_, _) => { if (!syncing) edited.Add(box); };
        Controls.Add(box);
        box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        y += RowHeight;
    }

    /// <summary>自動の名前を更新する（手で書き換えた欄はそのまま）</summary>
    public void SetDefaults(ChartNames? names, string extension)
    {
        defaults = names;
        extLabel.Text = extension;
        Apply();
    }

    /// <summary>別の譜面を選んだときなど、手での書き換えを捨てて自動の名前に戻す</summary>
    public void ResetEdits() => edited.Clear();

    void Apply()
    {
        syncing = true;
        try
        {
            Set(title, defaults?.Title);
            Set(subtitle, defaults?.SubTitle);
            if (showArtist) Set(artist, defaults?.Artist);
            Set(file, defaults?.FileName);
        }
        finally { syncing = false; }

        void Set(TextBox box, string? value)
        {
            if (!edited.Contains(box)) box.Text = value ?? "";
        }
    }

    /// <summary>bmstrainer が作ったのではない既存の譜面を上書きする場合は確認する</summary>
    public static bool ConfirmOverwrite(IWin32Window owner, string path)
    {
        if (!File.Exists(path)) return true;
        var first = File.ReadLines(path).FirstOrDefault() ?? "";
        if (first.StartsWith("*bmstrainer", StringComparison.OrdinalIgnoreCase)) return true;
        return MessageBox.Show(owner, L.OverwriteConfirm(Path.GetFileName(path)), "bmstrainer",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    public ChartNames Current => new(title.Text.Trim(), subtitle.Text.Trim(), file.Text.Trim(), showArtist ? artist.Text.Trim() : null);
}
