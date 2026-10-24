using System.Globalization;

namespace BmsTrainer.Osu;

public sealed record OsuTimingPoint(double Time, double BeatLength, int Meter)
{
    public double Bpm => 60000.0 / BeatLength;
    public double MeasureMs => BeatLength * Meter;
}

/// <param name="Column">0 始まりのレーン番号</param>
/// <param name="EndTime">LN の終端（通常ノーツは null）</param>
/// <param name="Sample">ノーツ固有のキー音ファイル（無ければ null）</param>
public sealed record OsuNote(int Column, int Time, int? EndTime, string? Sample);

/// <summary>ストーリーボードの Sample イベント（BGM として鳴る音）</summary>
public sealed record OsuSampleEvent(int Time, string File);

/// <summary>.osu ファイル（osu!mania）の読み込み。om2bms と同じ情報を取り出す</summary>
public sealed class OsuBeatmap
{
    public string FilePath { get; }
    public string Title { get; private set; } = "";
    public string TitleUnicode { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string ArtistUnicode { get; private set; } = "";
    public string Creator { get; private set; } = "";
    public string Version { get; private set; } = "";
    public string AudioFilename { get; private set; } = "";
    public string? Background { get; private set; }
    public int Mode { get; private set; }
    public int KeyCount { get; private set; }
    public double OverallDifficulty { get; private set; }
    public List<OsuTimingPoint> TimingPoints { get; } = [];
    public List<OsuNote> Notes { get; } = [];
    public List<OsuSampleEvent> Samples { get; } = [];

    public bool IsSupportedMania => Mode == 3 && KeyCount is 7 or 8;
    public string DisplayName => $"[{KeyCount}K] {Version}  ({Path.GetFileName(FilePath)})";

    public OsuBeatmap(string path)
    {
        FilePath = path;
        var section = "";
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }
            switch (section)
            {
                case "General": ParseGeneral(line); break;
                case "Metadata": ParseMetadata(line); break;
                case "Difficulty": ParseDifficulty(line); break;
                case "Events": ParseEvent(line); break;
                case "TimingPoints": ParseTimingPoint(line); break;
                case "HitObjects": if (KeyCount > 0) ParseHitObject(line); break;
            }
        }
        Notes.Sort((a, b) => a.Time != b.Time ? a.Time.CompareTo(b.Time) : a.Column.CompareTo(b.Column));
        Samples.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    static (string Key, string Value) Property(string line)
    {
        var i = line.IndexOf(':');
        return i < 0 ? (line, "") : (line[..i].Trim(), line[(i + 1)..].Trim());
    }

    static double D(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    static int I(string s) => (int)Math.Floor(D(s));

    void ParseGeneral(string line)
    {
        var (k, v) = Property(line);
        if (k == "AudioFilename") AudioFilename = v;
        else if (k == "Mode") Mode = int.TryParse(v, out var m) ? m : 0;
    }

    void ParseMetadata(string line)
    {
        var (k, v) = Property(line);
        switch (k)
        {
            case "Title": Title = v; break;
            case "TitleUnicode": TitleUnicode = v; break;
            case "Artist": Artist = v; break;
            case "ArtistUnicode": ArtistUnicode = v; break;
            case "Creator": Creator = v; break;
            case "Version": Version = v; break;
        }
    }

    void ParseDifficulty(string line)
    {
        var (k, v) = Property(line);
        if (k == "CircleSize") KeyCount = (int)Math.Round(D(v));
        else if (k == "OverallDifficulty") OverallDifficulty = D(v);
    }

    void ParseEvent(string line)
    {
        var p = line.Split(',');
        if (p.Length >= 3 && p[0] == "0" && Background == null)
            Background = p[2].Trim().Trim('"');
        else if (p.Length >= 4 && (p[0] == "Sample" || p[0] == "5"))
            Samples.Add(new OsuSampleEvent(I(p[1]), p[3].Trim().Trim('"')));
    }

    void ParseTimingPoint(string line)
    {
        var p = line.Split(',');
        if (p.Length < 2) return;
        bool uninherited = p.Length < 7 || p[6].Trim() == "1";
        double beatLength = D(p[1]);
        if (!uninherited || beatLength <= 0) return; // 緑線（SV）は使わない
        int meter = p.Length > 2 && int.TryParse(p[2], out var mt) && mt > 0 ? mt : 4;
        var tp = new OsuTimingPoint(D(p[0]), beatLength, meter);
        // 数 ms しか続かない赤線は後の赤線で置き換える（om2bms と同じ）
        if (TimingPoints.Count > 0 && tp.Time <= TimingPoints[^1].Time + 2) TimingPoints[^1] = tp;
        else TimingPoints.Add(tp);
    }

    void ParseHitObject(string line)
    {
        var p = line.Split(',');
        if (p.Length < 5) return;
        int column = Math.Clamp((int)Math.Floor(D(p[0]) * KeyCount / 512), 0, KeyCount - 1);
        int time = I(p[2]);
        int type = int.Parse(p[3]);
        int? end = null;
        string? extras = p.Length > 5 ? p[5] : null;
        if ((type & 128) != 0 && extras != null)
        {
            var colon = extras.IndexOf(':');
            end = I(colon < 0 ? extras : extras[..colon]);
            extras = colon < 0 ? null : extras[(colon + 1)..];
        }
        else if ((type & 1) == 0) return;
        // hitSample = normalSet:additionSet:index:volume:filename
        string? sample = null;
        if (extras != null)
        {
            var s = extras.Split(':');
            if (s.Length >= 5 && s[4].Trim().Length > 0) sample = s[4].Trim();
        }
        Notes.Add(new OsuNote(column, time, end, sample));
    }
}
