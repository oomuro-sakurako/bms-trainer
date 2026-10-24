namespace BmsTrainer;

/// <summary>osu-trainer と同じ配色</summary>
public static class Colors
{
    public static readonly Color FormBg = Color.FromArgb(38, 35, 53);
    public static readonly Color TextBoxBg = Color.FromArgb(36, 24, 38);
    public static readonly Color TextBoxFg = Color.FromArgb(224, 224, 224);
    public static readonly Color ReadOnlyBg = Color.FromArgb(38, 35, 53);
    public static readonly Color ReadOnlyFg = Color.Silver;
    public static readonly Color Disabled = Color.FromArgb(136, 134, 144);

    public static readonly Color Easier = Color.FromArgb(114, 241, 184);
    public static readonly Color Salmon = Color.FromArgb(249, 126, 114);
    public static readonly Color PaleBlue = Color.FromArgb(167, 154, 233);
    public static readonly Color AccentYellow = Color.FromArgb(254, 222, 93);
    public static readonly Color AccentCyan = Color.FromArgb(46, 226, 250);
    public static readonly Color AccentPink = Color.FromArgb(243, 114, 185);
    public static readonly Color AccentPink2 = Color.FromArgb(197, 51, 131);

    public static Color GetDifficultyColor(int difficulty) => difficulty switch
    {
        1 => Color.FromArgb(136, 179, 0),
        2 => Color.FromArgb(102, 204, 255),
        3 => Color.FromArgb(255, 204, 34),
        4 => Color.FromArgb(255, 102, 170),
        5 => Color.FromArgb(170, 136, 255),
        _ => ReadOnlyFg,
    };
}
