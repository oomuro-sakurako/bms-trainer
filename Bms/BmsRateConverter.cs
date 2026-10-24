using System.Globalization;
using System.Text;

namespace BmsTrainer.Bms;

/// <param name="Rank">変更後の #RANK（null なら元のまま）</param>
public sealed record RateSettings(double Rate, bool ChangePitch, int? FreqStep = null, int? Rank = null)
{
    /// <summary>LR2 の FREQ 1 段あたりの倍率（半音）。OpenLR2 の En_audio.cpp と同じ定数・計算方法</summary>
    const double FreqStepMultiplier = 1.0594631433486938;

    public static double FreqStepRate(int step)
    {
        double rate = 1.0;
        for (int i = 0; i < Math.Abs(step); i++)
            rate = step > 0 ? rate * FreqStepMultiplier : rate / FreqStepMultiplier;
        return rate;
    }

    public static RateSettings FromFreq(int step, bool changePitch, int? rank = null) => new(FreqStepRate(step), changePitch, step, rank);

    public bool ChangesSpeed => Math.Abs(Rate - 1.0) > 1e-9;
    public string RateText => Rate.ToString("0.####", CultureInfo.InvariantCulture);
    public string SpeedText => FreqStep is { } step ? $"FREQ{FreqText(step)}" : $"x{RateText}";
    /// <summary>キー音フォルダ名に使うタグ（例: x1.2 / FREQ+1P）</summary>
    public string Tag => $"{SpeedText}{(ChangePitch ? "P" : "")}";
    /// <summary>譜面ファイル名に使うタグ（例: x1.2 EASY / EASY）</summary>
    public string ChartTag => string.Join(" ", new[] { ChangesSpeed ? Tag : null, Rank is { } r ? RankName(r) : null }.OfType<string>());

    public static string FreqText(int step) => step == 0 ? "±0" : step.ToString("+0;-0");

    public static string RankName(int rank) => rank switch
    {
        0 => "VERY HARD",
        1 => "HARD",
        2 => "NORMAL",
        3 => "EASY",
        4 => "VERY EASY",
        _ => rank.ToString(),
    };
}

/// <summary>
/// 譜面テキストを倍率に合わせて書き換える。
/// BPM は全て rate 倍。#STOP は 1/192 小節単位なので BPM に追従して自動的に縮む。
/// チャンネル 03（16進 BPM, 1～255）は小数や 255 超を表せないため、#BPMxx を新設してチャンネル 08 に変換する。
/// 判定を指定した場合は #RANK を書き換える。
/// </summary>
public static class BmsRateConverter
{
    public const string KeysoundRootDir = "_bmstrainer";

    /// <summary>自動で付ける名前。サブタイトルとファイル名の末尾に [FREQ+1] のようなタグを付ける</summary>
    public static ChartNames DefaultNames(BmsChart chart, RateSettings s)
    {
        var tag = s.ChartTag.Length > 0 ? $" [{s.ChartTag}]" : "";
        return new(chart.Title, (chart.SubTitle + tag).Trim(), Path.GetFileNameWithoutExtension(chart.FilePath) + tag);
    }

    /// <summary>譜面フォルダからの相対パス（BMS 内の記述用に '/' 区切り）</summary>
    public static string KeysoundDir(RateSettings s) => $"{KeysoundRootDir}/{s.Tag}";

    public static void Write(BmsChart chart, RateSettings s, ChartNames names, IReadOnlyDictionary<string, string> wavReplacements, string outputPath)
    {
        var text = BuildText(chart, s, names, wavReplacements);
        var bytes = chart.Encoding.GetBytes(text);
        using var fs = File.Create(outputPath);
        if (chart.HasUtf8Bom) fs.Write([0xEF, 0xBB, 0xBF]);
        fs.Write(bytes);
    }

    public static string BuildText(BmsChart chart, RateSettings s, ChartNames names, IReadOnlyDictionary<string, string> wavReplacements)
    {
        double rate = s.Rate;
        var lines = chart.Lines;

        // チャンネル 03 の値 → 新しい #BPMxx の ID
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hexValues = new SortedSet<int>();
        foreach (var line in lines)
        {
            if (BmsChart.TryParseHeader(line, out var key, out _))
            {
                if (key.Length == 5 && key.StartsWith("BPM")) usedIds.Add(key[3..]);
                else if (key.Length == 7 && key.StartsWith("EXBPM")) usedIds.Add(key[5..]);
            }
            else if (BmsChart.TryParseChannel(line, out _, out var ch, out var data) && ch == "03")
            {
                for (int i = 0; i + 1 < data.Length; i += 2)
                {
                    var v = Convert.ToInt32(data.Substring(i, 2), 16);
                    if (v != 0) hexValues.Add(v);
                }
            }
        }
        var hexToId = new Dictionary<int, string>();
        using (var free = FreeIds(usedIds).GetEnumerator())
        {
            foreach (var v in hexValues)
            {
                if (!free.MoveNext()) throw new InvalidOperationException(L.NoFreeBpmId);
                hexToId[v] = free.Current;
            }
        }
        var newDefs = hexToId.Select(kv => $"#BPM{kv.Value} {Fmt(kv.Key * rate)}").ToList();

        bool defsInserted = newDefs.Count == 0;

        var output = new List<string>
        {
            $"{BmsChart.GeneratedMarker} source=\"{Path.GetFileName(chart.FilePath)}\" rate={s.RateText} pitch={(s.ChangePitch ? 1 : 0)} rank={s.Rank?.ToString() ?? "-"}",
        };
        // 元の譜面に #RANK が無い場合は先頭に追加する
        if (s.Rank is { } newRank && !lines.Any(l => BmsChart.TryParseHeader(l, out var k, out _) && k == "RANK"))
            output.Add($"#RANK {newRank}");
        foreach (var line in lines)
        {
            if (BmsChart.TryParseHeader(line, out var key, out var value))
            {
                var token = line.TrimStart().Split((char[]?)null, 2)[0]; // 元の大文字小文字を保つ
                if (key == "BPM" && BmsChart.TryParseDouble(value, out var bpm))
                {
                    output.Add($"{token} {Fmt(bpm * rate)}");
                    if (!defsInserted) { output.AddRange(newDefs); defsInserted = true; }
                    continue;
                }
                if (((key.Length == 5 && key.StartsWith("BPM")) || (key.Length == 7 && key.StartsWith("EXBPM")))
                    && BmsChart.TryParseDouble(value, out var def))
                {
                    output.Add($"{token} {Fmt(def * rate)}");
                    continue;
                }
                if (key.Length == 5 && key.StartsWith("WAV") && wavReplacements.TryGetValue(value, out var newWav))
                {
                    output.Add($"{token} {newWav}");
                    continue;
                }
                if (s.Rank is { } rank)
                {
                    if (key == "RANK")
                    {
                        output.Add($"{token} {rank}");
                        continue;
                    }
                    // #DEFEXRANK は #RANK より優先されるので消す
                    if (key == "DEFEXRANK") continue;
                }
                if (names.TryRewrite(key, token, line, lines, output)) continue;
                output.Add(line);
                continue;
            }
            if (BmsChart.TryParseChannel(line, out int measure, out var ch, out var data))
            {
                if (!defsInserted) { output.AddRange(newDefs); defsInserted = true; }
                if (ch == "03")
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i + 1 < data.Length; i += 2)
                    {
                        var v = Convert.ToInt32(data.Substring(i, 2), 16);
                        sb.Append(v == 0 ? "00" : hexToId[v]);
                    }
                    output.Add($"#{measure:D3}08:{sb}");
                    continue;
                }
            }
            output.Add(line);
        }
        if (!defsInserted) output.AddRange(newDefs);
        return string.Join("\r\n", output);
    }

    static IEnumerable<string> FreeIds(HashSet<string> used)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        for (int i = 1; i < 36 * 36; i++)
        {
            var id = $"{digits[i / 36]}{digits[i % 36]}";
            if (!used.Contains(id)) yield return id;
        }
    }

    public static string Fmt(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
}
