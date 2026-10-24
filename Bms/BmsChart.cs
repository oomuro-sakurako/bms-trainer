using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BmsTrainer.Bms;

/// <summary>
/// BMS 譜面ファイルの読み込みと、表示用の情報（BPM・ノーツ数など）の計算。
/// 行はそのまま保持し、変換時はこの行を書き換えて出力する。
/// </summary>
public sealed class BmsChart
{
    public static readonly string[] Extensions = [".bms", ".bme", ".bml", ".pms"];
    public const string GeneratedMarker = "*bmstrainer:";

    static readonly Regex ChannelLine = new(@"^#(\d{3})([0-9A-Za-z]{2}):(.*)$", RegexOptions.Compiled);

    public string FilePath { get; }
    public Encoding Encoding { get; }
    public bool HasUtf8Bom { get; }
    public IReadOnlyList<string> Lines { get; }

    public string Title { get; private set; } = "";
    public string SubTitle { get; private set; } = "";
    public string Artist { get; private set; } = "";
    public string Genre { get; private set; } = "";
    public string PlayLevel { get; private set; } = "";
    public int Difficulty { get; private set; }
    public double? Total { get; private set; }
    /// <summary>#RANK（0=VERY HARD, 1=HARD, 2=NORMAL, 3=EASY）</summary>
    public int? Rank { get; private set; }
    public double? DefExRank { get; private set; }
    public double InitialBpm { get; private set; } = 130;
    public double MinBpm { get; private set; }
    public double MaxBpm { get; private set; }
    /// <summary>最も長く続く BPM（osu-trainer の BPM 表示と同じ考え方）</summary>
    public double MainBpm { get; private set; }
    public int NoteCount { get; private set; }
    public double LengthSeconds { get; private set; }
    public bool IsGenerated { get; private set; }
    public string? LnObj { get; private set; }
    /// <summary>#RANDOM で乱数 1 を引いたとき、その行が有効かどうか</summary>
    public bool[] LineActive { get; private set; } = [];
    public BmsTimeline Timeline { get; private set; } = null!;

    /// <summary>#WAVxx の ID（大文字）→ 相対パス</summary>
    public Dictionary<string, string> Wavs { get; } = new(StringComparer.OrdinalIgnoreCase);

    static BmsChart() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public BmsChart(string path)
    {
        FilePath = path;
        var bytes = File.ReadAllBytes(path);
        (Encoding, HasUtf8Bom) = DetectEncoding(bytes);
        var text = Encoding.GetString(bytes, HasUtf8Bom ? 3 : 0, bytes.Length - (HasUtf8Bom ? 3 : 0));
        Lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        Analyze();
    }

    public string DisplayName
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(SubTitle) ? Title : $"{Title} {SubTitle}";
            var diff = Difficulty switch { 1 => "BEGINNER", 2 => "NORMAL", 3 => "HYPER", 4 => "ANOTHER", 5 => "INSANE", _ => "" };
            var level = string.IsNullOrEmpty(PlayLevel) ? "" : $"☆{PlayLevel}";
            var tag = (diff + " " + level).Trim();
            return $"{name}{(tag.Length > 0 ? $"  [{tag}]" : "")}  ({Path.GetFileName(FilePath)})";
        }
    }

    static (Encoding, bool) DetectEncoding(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(false), true);
        try
        {
            new UTF8Encoding(false, true).GetString(bytes);
            return (new UTF8Encoding(false), false);
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.GetEncoding(932), false); // Shift_JIS
        }
    }

    /// <summary>"#KEY value" 形式のヘッダ行を分解する。チャンネル行やコメントは false。</summary>
    public static bool TryParseHeader(string line, out string key, out string value)
    {
        key = value = "";
        var s = line.TrimStart();
        if (s.Length < 2 || s[0] != '#' || ChannelLine.IsMatch(s)) return false;
        int i = 1;
        while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
        key = s[1..i].ToUpperInvariant();
        value = s[i..].Trim();
        return true;
    }

    public static bool TryParseChannel(string line, out int measure, out string channel, out string data)
    {
        var m = ChannelLine.Match(line.Trim());
        measure = 0; channel = data = "";
        if (!m.Success) return false;
        measure = int.Parse(m.Groups[1].Value);
        channel = m.Groups[2].Value.ToUpperInvariant();
        data = m.Groups[3].Value.Trim();
        return true;
    }

    public static bool TryParseDouble(string s, out double v) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    void Analyze()
    {
        var bpmDefs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var stopDefs = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var measureLen = new Dictionary<int, double>();
        var events = new List<(int Measure, double Pos, int Kind, string Value)>(); // Kind: 0=BPM(hex) 1=BPM(ref) 2=STOP
        var lnActive = new HashSet<string>();
        string? lnObj = null;
        int notes = 0;
        (int Measure, double Pos) lastNote = (0, 0);

        // #RANDOM は乱数 1 を引いたものとして表示用の統計を出す
        var ifStack = new Stack<bool>();
        int random = 1;
        bool Active() => ifStack.All(x => x);

        LineActive = new bool[Lines.Count];
        for (int lineIndex = 0; lineIndex < Lines.Count; lineIndex++)
        {
            var line = Lines[lineIndex].Trim();
            LineActive[lineIndex] = Active();
            if (line.StartsWith(GeneratedMarker, StringComparison.OrdinalIgnoreCase)) IsGenerated = true;
            if (TryParseHeader(line, out var key, out var value))
            {
                switch (key)
                {
                    case "RANDOM": case "RONDAM": random = 1; continue;
                    case "SETRANDOM": random = int.TryParse(value, out var r) ? r : 1; continue;
                    case "IF": ifStack.Push(int.TryParse(value, out var c) && c == random); continue;
                    case "ENDIF": case "END IF": if (ifStack.Count > 0) ifStack.Pop(); continue;
                }
                if (!Active()) continue;
                switch (key)
                {
                    case "TITLE": Title = value; break;
                    case "SUBTITLE": SubTitle = value; break;
                    case "ARTIST": Artist = value; break;
                    case "GENRE": Genre = value; break;
                    case "PLAYLEVEL": PlayLevel = value; break;
                    case "DIFFICULTY": Difficulty = int.TryParse(value, out var d) ? d : 0; break;
                    case "TOTAL": if (TryParseDouble(value, out var t)) Total = t; break;
                    case "RANK": Rank = int.TryParse(value, out var rk) ? rk : null; break;
                    case "DEFEXRANK": if (TryParseDouble(value, out var dx)) DefExRank = dx; break;
                    case "BPM": if (TryParseDouble(value, out var b)) InitialBpm = b; break;
                    case "LNOBJ": lnObj = value.ToUpperInvariant(); break;
                    default:
                        if (key.Length == 5 && key.StartsWith("BPM") && TryParseDouble(value, out var bv)) bpmDefs[key[3..]] = bv;
                        else if (key.Length == 7 && key.StartsWith("EXBPM") && TryParseDouble(value, out var ev)) bpmDefs[key[5..]] = ev;
                        else if (key.Length == 6 && key.StartsWith("STOP") && TryParseDouble(value, out var sv)) stopDefs[key[4..]] = sv;
                        else if (key.Length == 5 && key.StartsWith("WAV")) Wavs[key[3..]] = value;
                        break;
                }
                continue;
            }
            if (!Active() || !TryParseChannel(line, out int measure, out var ch, out var data)) continue;
            if (ch == "02")
            {
                if (TryParseDouble(data, out var len) && len > 0) measureLen[measure] = len;
                continue;
            }
            int n = data.Length / 2;
            for (int i = 0; i < n; i++)
            {
                var obj = data.Substring(i * 2, 2).ToUpperInvariant();
                if (obj == "00") continue;
                double pos = (double)i / n;
                switch (ch)
                {
                    case "03": events.Add((measure, pos, 0, obj)); continue;
                    case "08": events.Add((measure, pos, 1, obj)); continue;
                    case "09": events.Add((measure, pos, 2, obj)); continue;
                }
                bool playable = (ch[0] == '1' || ch[0] == '2') && ch[1] >= '1' && ch[1] <= '9';
                bool longNote = (ch[0] == '5' || ch[0] == '6') && ch[1] >= '1' && ch[1] <= '9';
                if (playable)
                {
                    if (obj != lnObj) notes++; // #LNOBJ は LN の終端なので数えない
                }
                else if (longNote)
                {
                    // 5x/6x は開始と終了の 2 オブジェクトで 1 ノーツ
                    if (!lnActive.Remove(ch)) { lnActive.Add(ch); notes++; }
                }
                else continue;
                if (measure > lastNote.Measure || (measure == lastNote.Measure && pos > lastNote.Pos)) lastNote = (measure, pos);
            }
        }
        NoteCount = notes;
        LnObj = lnObj;

        Timeline = new BmsTimeline(InitialBpm, measureLen,
            events.Where(e => e.Kind != 2).Select(e => (e.Measure, e.Pos,
                e.Kind == 0 ? Convert.ToInt32(e.Value, 16) : bpmDefs.GetValueOrDefault(e.Value))),
            events.Where(e => e.Kind == 2).Select(e => (e.Measure, e.Pos, stopDefs.GetValueOrDefault(e.Value))));

        // 時間計算
        var bpmTime = new Dictionary<double, double>();
        double bpm = InitialBpm, time = 0, min = bpm, max = bpm;
        void Advance(double beats)
        {
            if (beats <= 0 || bpm <= 0) return;
            var sec = beats * 60 / bpm;
            time += sec;
            var keyBpm = Math.Round(bpm, 2);
            bpmTime[keyBpm] = bpmTime.GetValueOrDefault(keyBpm) + sec;
        }
        var byMeasure = events.GroupBy(e => e.Measure).ToDictionary(g => g.Key, g => g.OrderBy(e => e.Pos).ThenBy(e => e.Kind).ToList());
        for (int m = 0; m <= lastNote.Measure; m++)
        {
            double len = measureLen.GetValueOrDefault(m, 1.0);
            double endPos = m == lastNote.Measure ? lastNote.Pos : 1.0;
            double prev = 0;
            if (byMeasure.TryGetValue(m, out var list))
            {
                foreach (var e in list)
                {
                    if (e.Pos > endPos) break;
                    Advance((e.Pos - prev) * len * 4);
                    prev = e.Pos;
                    switch (e.Kind)
                    {
                        case 0: bpm = Convert.ToInt32(e.Value, 16); break;
                        case 1: if (bpmDefs.TryGetValue(e.Value, out var v)) bpm = v; break;
                        case 2: if (stopDefs.TryGetValue(e.Value, out var s) && bpm > 0) time += s / 48 * 60 / bpm; break;
                    }
                    if (e.Kind != 2 && bpm > 0) { min = Math.Min(min, bpm); max = Math.Max(max, bpm); }
                }
            }
            Advance((endPos - prev) * len * 4);
        }
        MinBpm = min;
        MaxBpm = max;
        MainBpm = bpmTime.Count > 0 ? bpmTime.MaxBy(kv => kv.Value).Key : InitialBpm;
        LengthSeconds = time;
    }
}
