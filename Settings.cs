using System.Text.Json;

namespace BmsTrainer;

public sealed class Settings
{
    public string LastPath { get; set; } = "";
    public bool UseFreq { get; set; }
    public int FreqStep { get; set; }
    public bool ChangePitch { get; set; }
    public int? Rank { get; set; }
    /// <summary>表示言語（ja / en / ko）。null なら Windows の言語に合わせる</summary>
    public string? Language { get; set; }
    public string OsuOutputDir { get; set; } = "";
    public int OsuLaneOrder { get; set; }
    /// <summary>om2bms と同じく既定は EASY</summary>
    public int OsuRank { get; set; } = 3;
    /// <summary>ウィンドウの幅（0 なら既定）</summary>
    public int MainWindowWidth { get; set; }
    public int OsuWindowWidth { get; set; }

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "bmstrainer", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
