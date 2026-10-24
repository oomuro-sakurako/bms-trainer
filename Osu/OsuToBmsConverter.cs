using System.Globalization;
using System.Text;
using BmsTrainer.Bms;
using NAudio.Wave;

namespace BmsTrainer.Osu;

/// <summary>キー音を割り当てるレーンの順番（無音ノーツ自動置換ツールの「置換レーン順」と同じ）</summary>
public sealed record LaneOrder(string Keys, Func<string> Describe)
{
    public static readonly LaneOrder[] All =
    [
        new("1234567", () => L.LaneFromLeft),
        new("7654321", () => L.LaneFromRight),
        new("4352617", () => L.LaneFromCenter(1)),
        new("4536271", () => L.LaneFromCenter(2)),
    ];

    public string Label => L.Current == Language.Japanese ? $"{Keys}（{Describe()}）" : $"{Keys} ({Describe()})";

    /// <summary>鍵盤 1～7 の優先順位。スクラッチ（0）は最後</summary>
    public int Rank(int key) => key == 0 ? 7 : Keys.IndexOf((char)('0' + key));
}

public sealed record OsuConvertResult(string OutputPath, int NoteCount, int KeysoundedNotes, int SilentNotes);

/// <summary>
/// osu!mania（7K/8K）→ BMS 変換（om2bms の移植）。
/// 本体 BMS を指定した場合は、本体の小節・BPM・STOP を土台にして osu! のノーツを配置し、
/// 本体の全キー音を BGM レーンに移したうえで、無音ノーツ自動置換ツールと同じく
/// 同じ位置にある BGM レーンのキー音をノーツへ移す（キー音の補完）。
/// </summary>
public static class OsuToBmsConverter
{
    public const string Marker = "*bmstrainer-osu:";
    /// <summary>鍵盤 1～7 とスクラッチ（0）のチャンネル</summary>
    static readonly string[] KeyChannel = ["16", "11", "12", "13", "14", "15", "18", "19"];
    /// <summary>キー音を探す範囲（ms）</summary>
    const double SnapTolerance = 5;

    /// <summary>osu! のレーン → BMS の鍵盤番号（0 = スクラッチ）。om2bms と同じく 8K は左端がスクラッチ</summary>
    static int KeyOf(OsuBeatmap osu, int column) => osu.KeyCount == 8 ? column : column + 1;

    static double DefaultTotal(int notes) => Math.Round(7.605 * notes / (0.01 * notes + 6.5), 2);

    // ───────── オフセット検出 ─────────

    /// <summary>本体 BMS 上の時刻 = osu! の時刻 + offset となる offset と、そのときのキー音一致率</summary>
    public static (double Offset, double MatchRate) DetectOffset(OsuBeatmap osu, BmsChart body)
    {
        var objects = ReadBody(body).Objects.Select(o => o.Time).Order().ToArray();
        var notes = osu.Notes.Select(n => (double)n.Time).ToArray();
        if (objects.Length == 0 || notes.Length == 0) return (0, 0);

        // 時刻差のヒストグラムで最も多い差を探す
        int min = (int)Math.Floor(objects[0] - notes[^1]) - 3, max = (int)Math.Ceiling(objects[^1] - notes[0]) + 3;
        var hist = new int[max - min + 1];
        int step = Math.Max(1, notes.Length / 400);
        for (int i = 0; i < notes.Length; i += step)
            foreach (var o in objects) hist[(int)Math.Round(o - notes[i]) - min]++;
        int best = 0, bestScore = -1;
        for (int i = 2; i < hist.Length - 2; i++)
        {
            int score = hist[i - 2] + hist[i - 1] + hist[i] + hist[i + 1] + hist[i + 2];
            if (score > bestScore) { bestScore = score; best = i + min; }
        }
        // 一致したノーツの平均で微調整
        var diffs = notes.Select(n => Nearest(objects, n + best) - n).Where(d => Math.Abs(d - best) <= 4).ToList();
        double offset = diffs.Count > 0 ? Math.Round(diffs.Average(), 1) : best;
        return (offset, MatchRate(osu, body, offset));
    }

    public static double MatchRate(OsuBeatmap osu, BmsChart body, double offset)
    {
        var objects = ReadBody(body).Objects.Select(o => o.Time).Order().ToArray();
        if (objects.Length == 0 || osu.Notes.Count == 0) return 0;
        return osu.Notes.Count(n => Math.Abs(Nearest(objects, n.Time + offset) - (n.Time + offset)) <= SnapTolerance) / (double)osu.Notes.Count;
    }

    static double Nearest(double[] sorted, double t)
    {
        int i = Array.BinarySearch(sorted, t);
        if (i >= 0) return sorted[i];
        i = ~i;
        if (i == 0) return sorted[0];
        if (i == sorted.Length) return sorted[^1];
        return t - sorted[i - 1] < sorted[i] - t ? sorted[i - 1] : sorted[i];
    }

    // ───────── 本体 BMS の読み取り ─────────

    sealed class BodyLine(int lineIndex, int measure, string[] cells, int priority)
    {
        public int LineIndex = lineIndex, Measure = measure, Priority = priority;
        public string[] Cells = cells;
        public override string ToString() => $"#{Measure:D3}01:{string.Concat(Cells)}";
    }

    sealed class BodyObject(BodyLine line, int cell, double time, BmsPos pos, int order)
    {
        public BodyLine Line = line;
        public int Cell = cell, Order = order;
        public double Time = time;
        public BmsPos Pos = pos;
        public string Id => Line.Cells[Cell];
        public bool Consumed;
    }

    sealed record Body(Dictionary<int, BodyLine> Lines, HashSet<int> DroppedLines, List<BodyObject> Objects);

    static bool IsNoteChannel(string ch) => ch[1] is >= '1' and <= '9' && ch[0] is '1' or '2' or '5' or '6';
    static bool IsDroppedChannel(string ch) => ch[1] is >= '1' and <= '9' && ch[0] is '3' or '4' or 'D' or 'E';

    /// <summary>
    /// 本体のノーツレーン（1x/2x/5x/6x）を BGM レーン（01）へ移した状態を作る。
    /// LN の終端と #LNOBJ は鳴らないので除き、見えないノーツ・地雷は削除する。
    /// </summary>
    static Body ReadBody(BmsChart body)
    {
        var lines = new Dictionary<int, BodyLine>();
        var dropped = new HashSet<int>();
        var lnObjects = new List<(BodyLine Line, int Cell, string Channel, double Time, bool Active)>();
        for (int i = 0; i < body.Lines.Count; i++)
        {
            if (!BmsChart.TryParseChannel(body.Lines[i], out int m, out var ch, out var data)) continue;
            if (IsDroppedChannel(ch)) { dropped.Add(i); continue; }
            if (ch != "01" && !IsNoteChannel(ch)) continue;
            int n = data.Length / 2;
            var cells = Enumerable.Range(0, n).Select(k => data.Substring(k * 2, 2)).ToArray();
            var line = new BodyLine(i, m, cells, ch == "01" ? 1 : 0);
            lines[i] = line;
            for (int k = 0; k < n; k++)
            {
                if (cells[k] == "00") continue;
                if (ch[0] is '1' or '2' && string.Equals(cells[k], body.LnObj, StringComparison.OrdinalIgnoreCase)) cells[k] = "00";
                else if (ch[0] is '5' or '6') lnObjects.Add((line, k, ch, body.Timeline.TimeOf(m, (double)k / n), body.LineActive[i]));
            }
        }
        // 5x/6x は時刻順に開始・終端が交互に並ぶ
        foreach (var group in lnObjects.GroupBy(o => (o.Channel, o.Active)))
        {
            int index = 0;
            foreach (var o in group.OrderBy(o => o.Time))
                if (index++ % 2 == 1) o.Line.Cells[o.Cell] = "00";
        }

        var objects = new List<BodyObject>();
        foreach (var line in lines.Values.Where(l => body.LineActive[l.LineIndex]).OrderBy(l => l.Priority).ThenBy(l => l.LineIndex))
        {
            int n = line.Cells.Length;
            for (int k = 0; k < n; k++)
                if (line.Cells[k] != "00")
                    objects.Add(new BodyObject(line, k, body.Timeline.TimeOf(line.Measure, (double)k / n), BmsPos.Create(line.Measure, k, n), objects.Count));
        }
        objects.Sort((a, b) => a.Time.CompareTo(b.Time));
        return new Body(lines, dropped, objects);
    }

    // ───────── 本体ありの変換 ─────────

    /// <summary>
    /// 自動で付ける名前。差分名（サブタイトル）は osu! の差分名、
    /// 本体ありならタイトル・アーティストは本体のもの、なしなら osu! のもの（Shift_JIS で表せなければローマ字表記）
    /// </summary>
    public static ChartNames DefaultNames(OsuBeatmap osu, BmsChart? body) => body != null
        ? new ChartNames(body.Title, osu.Version, $"{Path.GetFileNameWithoutExtension(body.FilePath)} {osu.Version}", body.Artist)
        : new ChartNames(Pick(osu.TitleUnicode, osu.Title), osu.Version, $"{osu.Title} {osu.Version}", Pick(osu.ArtistUnicode, osu.Artist));

    static string Pick(string unicode, string romanized)
    {
        if (string.IsNullOrEmpty(unicode)) return romanized;
        var sjis = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        try { sjis.GetBytes(unicode); return unicode; } catch (EncoderFallbackException) { return romanized; }
    }

    public static OsuConvertResult ConvertWithBody(OsuBeatmap osu, BmsChart body, double offset, LaneOrder laneOrder, int rank, ChartNames names)
    {
        var data = ReadBody(body);
        var objects = data.Objects;
        var times = objects.Select(o => o.Time).ToArray();
        var timeline = body.Timeline;
        var definedWavs = body.Lines
            .Select(l => BmsChart.TryParseHeader(l, out var k, out _) && k.Length == 5 && k.StartsWith("WAV") ? k[3..] : null)
            .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var silentId = SilentId(definedWavs);

        var builder = new ChannelLineBuilder();
        int keysounded = 0, silent = 0;
        // 同時押しは置換レーン順に処理する
        var ordered = osu.Notes.OrderBy(n => n.Time).ThenBy(n => laneOrder.Rank(KeyOf(osu, n.Column)));
        foreach (var note in ordered)
        {
            int key = KeyOf(osu, note.Column);
            double t = note.Time + offset;
            var (pos, obj) = Snap(t);
            string id;
            if (obj != null)
            {
                obj.Consumed = true;
                id = obj.Id;
                obj.Line.Cells[obj.Cell] = "00"; // BGM レーンから取り除く
                keysounded++;
            }
            else
            {
                id = silentId;
                silent++;
            }
            if (note.EndTime is { } end)
            {
                var ch = "5" + KeyChannel[key][1];
                builder.Add(pos, ch, id);
                builder.Add(Snap(end + offset, onlyPosition: true).Pos, ch, id);
            }
            else builder.Add(pos, KeyChannel[key], id);
        }

        (BmsPos Pos, BodyObject? Obj) Snap(double t, bool onlyPosition = false)
        {
            int i = Array.BinarySearch(times, t - SnapTolerance);
            if (i < 0) i = ~i;
            BodyObject? nearest = null, nearestFree = null;
            for (; i < times.Length && times[i] <= t + SnapTolerance; i++)
            {
                var o = objects[i];
                if (nearest == null || Math.Abs(o.Time - t) < Math.Abs(nearest.Time - t) - 0.01) nearest = o;
                if (!o.Consumed && (nearestFree == null
                    || Math.Abs(o.Time - t) < Math.Abs(nearestFree.Time - t) - 0.01
                    || (Math.Abs(o.Time - nearestFree.Time) < 0.01 && (o.Line.Priority, o.Order).CompareTo((nearestFree.Line.Priority, nearestFree.Order)) < 0)))
                    nearestFree = o;
            }
            if (!onlyPosition && nearestFree != null) return (nearestFree.Pos, nearestFree);
            if (nearest != null) return (nearest.Pos, null);
            return (BmsPos.Quantize(timeline, t), null);
        }

        // 出力
        bool hasRank = body.Lines.Any(l => BmsChart.TryParseHeader(l, out var k, out _) && k == "RANK");
        var output = new List<string>
        {
            $"{Marker} source=\"{Path.GetFileName(osu.FilePath)}\" body=\"{Path.GetFileName(body.FilePath)}\" offset={offset.ToString(CultureInfo.InvariantCulture)}",
        };
        if (!hasRank) output.Add($"#RANK {rank}");
        bool totalWritten = false;
        for (int i = 0; i < body.Lines.Count; i++)
        {
            var line = body.Lines[i];
            if (data.DroppedLines.Contains(i)) continue;
            if (data.Lines.TryGetValue(i, out var bodyLine))
            {
                if (bodyLine.Cells.Any(c => c != "00")) output.Add(bodyLine.ToString());
                continue;
            }
            if (BmsChart.TryParseHeader(line, out var key, out var value))
            {
                var token = line.TrimStart().Split((char[]?)null, 2)[0];
                switch (key)
                {
                    case "RANK": output.Add($"{token} {rank}"); continue;
                    case "DEFEXRANK": case "LNOBJ": case "LNTYPE": continue; // ノーツを作り直すので不要
                    case "TOTAL":
                        if (!totalWritten) output.Add($"{token} {BmsRateConverter.Fmt(DefaultTotal(osu.Notes.Count))}");
                        totalWritten = true;
                        continue;
                }
                if (names.TryRewrite(key, token, line, body.Lines, output)) continue;
            }
            output.Add(line);
        }
        if (!totalWritten) output.Insert(1, $"#TOTAL {BmsRateConverter.Fmt(DefaultTotal(osu.Notes.Count))}");
        output.Add("");
        output.Add("*---------------------- osu! NOTES");
        output.AddRange(builder.Build());

        var outPath = names.OutputPath(body.FilePath);
        var bytes = body.Encoding.GetBytes(string.Join("\r\n", output));
        using (var fs = File.Create(outPath))
        {
            if (body.HasUtf8Bom) fs.Write([0xEF, 0xBB, 0xBF]);
            fs.Write(bytes);
        }
        return new OsuConvertResult(outPath, osu.Notes.Count, keysounded, silent);
    }

    // ───────── 本体なしの変換（om2bms と同じく osu! の音源を BGM として鳴らす） ─────────

    public static OsuConvertResult ConvertStandalone(OsuBeatmap osu, string outputParentDir, int rank, ChartNames names)
    {
        var fileName = ChartNames.SanitizeFileName(names.FileName);
        if (fileName.Length == 0) throw new InvalidOperationException(L.EmptyFileName);
        var tps = osu.TimingPoints;
        if (tps.Count == 0) throw new InvalidOperationException(L.NoTimingPoints);
        var srcDir = Path.GetDirectoryName(osu.FilePath)!;

        // 小節を作る。0 小節目は 1 小節分の空白、音源は 1 小節目の頭から鳴らす。
        // om2bms と同じく、赤線は必ず小節の頭になるようにし、途中に赤線があれば小節を切り詰める。
        var tp0 = tps[0];
        double lead = 4 * tp0.BeatLength;
        double endTime = osu.Notes.Select(n => (double)(n.EndTime ?? n.Time)).Concat(osu.Samples.Select(s => (double)s.Time)).DefaultIfEmpty(0).Max() + 1;
        var lengths = new Dictionary<int, double>();
        var bpms = new List<(int Measure, double Pos, double Bpm)>();
        int measure = 1;
        double first = ((tp0.Time % tp0.MeasureMs) + tp0.MeasureMs) % tp0.MeasureMs;
        if (first < 1 || first > tp0.MeasureMs - 1) first = 0;
        if (first > 0) lengths[measure++] = first / (4 * tp0.BeatLength);
        double t = first, currentBpm = tp0.Bpm;
        int tpIndex = 0;
        while (t < endTime)
        {
            if (measure > BmsTimeline.MaxMeasure) throw new InvalidOperationException(L.TooManyMeasures);
            while (tpIndex + 1 < tps.Count && tps[tpIndex + 1].Time <= t + 2) tpIndex++;
            var tp = tps[tpIndex];
            double next = tpIndex + 1 < tps.Count ? tps[tpIndex + 1].Time : double.MaxValue;
            double duration = t + tp.MeasureMs > next + 2 ? next - t : tp.MeasureMs;
            if (Math.Abs(duration - 4 * tp.BeatLength) > 1e-6) lengths[measure] = duration / (4 * tp.BeatLength);
            if (Math.Abs(tp.Bpm - currentBpm) > 1e-9) { bpms.Add((measure, 0, tp.Bpm)); currentBpm = tp.Bpm; }
            t += duration;
            if (Math.Abs(t - next) <= 2) t = next;
            measure++;
        }
        var timeline = new BmsTimeline(tp0.Bpm, lengths, bpms, []);

        // キー音
        var outDir = Path.Combine(outputParentDir, Sanitize($"{osu.Artist} - {osu.Title}"));
        Directory.CreateDirectory(outDir);
        var wavIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var wavDefs = new List<string>();
        string WavId(string file)
        {
            if (!wavIds.TryGetValue(file, out var id))
            {
                id = Base36(wavIds.Count + 1);
                wavIds[file] = id;
                wavDefs.Add($"#WAV{id} {file}");
            }
            return id;
        }
        var audio = CopyAudio(srcDir, osu.AudioFilename, outDir);
        if (audio != null) WavId(audio);
        var silentId = SilentId(wavIds.Values.ToHashSet());

        var builder = new ChannelLineBuilder();
        if (audio != null) builder.Add(new BmsPos(1, 0, 1), "01", wavIds[audio]);
        foreach (var s in osu.Samples)
        {
            var file = CopyFile(srcDir, s.File, outDir);
            if (file != null) builder.Add(BmsPos.Quantize(timeline, s.Time + lead), "01", WavId(file));
        }
        int keysounded = 0;
        foreach (var note in osu.Notes)
        {
            int key = KeyOf(osu, note.Column);
            var file = note.Sample != null ? CopyFile(srcDir, note.Sample, outDir) : null;
            var id = file != null ? WavId(file) : silentId;
            if (file != null) keysounded++;
            var pos = BmsPos.Quantize(timeline, note.Time + lead);
            if (note.EndTime is { } end)
            {
                var ch = "5" + KeyChannel[key][1];
                builder.Add(pos, ch, id);
                builder.Add(BmsPos.Quantize(timeline, end + lead), ch, id);
            }
            else builder.Add(pos, KeyChannel[key], id);
        }
        string? stage = osu.Background != null ? CopyFile(srcDir, osu.Background, outDir) : null;

        var bpmIds = bpms.Select(b => b.Bpm).Distinct().Select((b, i) => (Bpm: b, Id: Base36(i + 1))).ToDictionary(x => x.Bpm, x => x.Id);
        foreach (var b in bpms) builder.Add(new BmsPos(b.Measure, 0, 1), "08", bpmIds[b.Bpm]);
        var lines = new List<string>
        {
            $"{Marker} source=\"{Path.GetFileName(osu.FilePath)}\"",
            "",
            "*---------------------- HEADER FIELD",
            "#PLAYER 1",
            $"#GENRE {osu.Creator}",
            $"#TITLE {names.Title}",
            $"#SUBTITLE {names.SubTitle}",
            $"#ARTIST {names.Artist}",
            $"#BPM {BmsRateConverter.Fmt(tp0.Bpm)}",
            "#DIFFICULTY 5",
            $"#RANK {rank}",
            $"#TOTAL {BmsRateConverter.Fmt(DefaultTotal(osu.Notes.Count))}",
        };
        if (stage != null) lines.Add($"#STAGEFILE {stage}");
        lines.Add("");
        lines.AddRange(wavDefs);
        lines.AddRange(bpmIds.Select(kv => $"#BPM{kv.Value} {BmsRateConverter.Fmt(kv.Key)}"));
        lines.Add("");
        lines.Add("*---------------------- MAIN DATA FIELD");
        lines.AddRange(lengths.OrderBy(kv => kv.Key).Select(kv => $"#{kv.Key:D3}02:{BmsRateConverter.Fmt(kv.Value)}"));
        lines.AddRange(builder.Build());

        var outPath = Path.Combine(outDir, fileName + ".bms");
        File.WriteAllText(outPath, string.Join("\r\n", lines), Encoding.GetEncoding(932));
        return new OsuConvertResult(outPath, osu.Notes.Count, keysounded, osu.Notes.Count - keysounded);
    }

    /// <summary>音源をコピーする。mp3 は LR2 で鳴らないので wav に変換する</summary>
    static string? CopyAudio(string srcDir, string file, string outDir)
    {
        var src = Path.Combine(srcDir, file);
        if (string.IsNullOrEmpty(file) || !File.Exists(src)) return null;
        if (!Path.GetExtension(file).Equals(".mp3", StringComparison.OrdinalIgnoreCase)) return CopyFile(srcDir, file, outDir);
        var name = Path.ChangeExtension(Path.GetFileName(file), ".wav");
        var dst = Path.Combine(outDir, name);
        if (!File.Exists(dst))
        {
            using var reader = new MediaFoundationReader(src);
            WaveFileWriter.CreateWaveFile16(dst + ".tmp", reader.ToSampleProvider());
            File.Move(dst + ".tmp", dst, true);
        }
        return name;
    }

    static string? CopyFile(string srcDir, string file, string outDir)
    {
        var src = Path.Combine(srcDir, file);
        if (!File.Exists(src)) return null;
        var rel = file.Replace('\\', '/');
        var dst = Path.Combine(outDir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        if (!File.Exists(dst)) File.Copy(src, dst);
        return rel;
    }

    static string SilentId(ISet<string> used)
    {
        for (int i = 36 * 36 - 1; i > 0; i--)
        {
            var id = Base36(i);
            if (!used.Contains(id)) return id;
        }
        throw new InvalidOperationException(L.NoSilentId);
    }

    static string Base36(int n)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        return $"{digits[n / 36 % 36]}{digits[n % 36]}";
    }

    static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "");
        return name.Trim();
    }
}

/// <summary>小節番号と小節内の位置（既約分数）</summary>
public readonly record struct BmsPos(int Measure, int Num, int Den)
{
    public double Frac => (double)Num / Den;

    public static BmsPos Create(int measure, int num, int den)
    {
        if (num >= den) return new BmsPos(measure + num / den, num % den, den);
        int g = (int)System.Numerics.BigInteger.GreatestCommonDivisor(num, den);
        return num == 0 ? new BmsPos(measure, 0, 1) : new BmsPos(measure, num / g, den / g);
    }

    static readonly int[] Denominators = [1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 192, 384, 768];

    /// <summary>時刻を、誤差 1ms 以内に収まる一番粗い分割の位置に丸める</summary>
    public static BmsPos Quantize(BmsTimeline timeline, double time)
    {
        var (m, f) = timeline.PositionOf(time);
        BmsPos best = default;
        double bestError = double.MaxValue;
        foreach (var den in Denominators)
        {
            var p = Create(m, (int)Math.Round(f * den), den);
            var error = Math.Abs(timeline.TimeOf(p.Measure, p.Frac) - time);
            if (error <= 1) return p;
            if (error < bestError) { bestError = error; best = p; }
        }
        return best;
    }
}

/// <summary>オブジェクトを小節・チャンネルごとの行にまとめる。同じ位置に重なる場合は行を分ける</summary>
sealed class ChannelLineBuilder
{
    const int MaxCells = 3072;
    readonly Dictionary<(int Measure, string Channel), List<(BmsPos Pos, string Id)>> objects = [];

    public void Add(BmsPos pos, string channel, string id)
    {
        if (!objects.TryGetValue((pos.Measure, channel), out var list)) objects[(pos.Measure, channel)] = list = [];
        list.Add((pos, id));
    }

    public IEnumerable<string> Build()
    {
        foreach (var ((measure, channel), list) in objects.OrderBy(kv => kv.Key.Measure).ThenBy(kv => kv.Key.Channel))
        {
            long cells = 1;
            foreach (var (pos, _) in list) cells = cells / Gcd(cells, pos.Den) * pos.Den;
            if (cells > MaxCells) cells = MaxCells;
            var rows = new List<string[]>();
            foreach (var (pos, id) in list)
            {
                int cell = (int)Math.Min(cells - 1, Math.Round(pos.Frac * cells));
                var row = rows.FirstOrDefault(r => r[cell] == "00");
                if (row == null) rows.Add(row = Enumerable.Repeat("00", (int)cells).ToArray());
                row[cell] = id;
            }
            foreach (var row in rows) yield return $"#{measure:D3}{channel}:{string.Concat(row)}";
        }
    }

    static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);
}
