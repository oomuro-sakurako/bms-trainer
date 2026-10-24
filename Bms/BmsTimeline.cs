namespace BmsTrainer.Bms;

/// <summary>
/// 小節と小節内の位置（0～1）⇔ 時刻（ms）の変換。
/// 小節長（チャンネル 02）、BPM 変化、STOP に対応する。
/// </summary>
public sealed class BmsTimeline
{
    public const int MaxMeasure = 999;

    readonly double[] lengths = new double[MaxMeasure + 2];
    readonly double[] startTime = new double[MaxMeasure + 2];
    readonly double[] startBpm = new double[MaxMeasure + 2];
    readonly List<Event>[] events = new List<Event>[MaxMeasure + 2];

    /// <param name="Stop">STOP の場合 true。Value は 1/192 小節単位</param>
    readonly record struct Event(double Pos, bool Stop, double Value);

    public BmsTimeline(double initialBpm, IReadOnlyDictionary<int, double> measureLengths,
        IEnumerable<(int Measure, double Pos, double Bpm)> bpmChanges,
        IEnumerable<(int Measure, double Pos, double Value)> stops)
    {
        for (int m = 0; m < lengths.Length; m++)
        {
            lengths[m] = measureLengths.TryGetValue(m, out var l) && l > 0 ? l : 1.0;
            events[m] = [];
        }
        foreach (var (m, pos, value) in bpmChanges)
            if (m <= MaxMeasure && value > 0) events[m].Add(new Event(pos, false, value));
        foreach (var (m, pos, value) in stops)
            if (m <= MaxMeasure && value > 0) events[m].Add(new Event(pos, true, value));
        // 同じ位置では BPM 変化を先に適用してから STOP する
        foreach (var list in events) list.Sort((a, b) => a.Pos != b.Pos ? a.Pos.CompareTo(b.Pos) : a.Stop.CompareTo(b.Stop));

        double t = 0, bpm = initialBpm;
        for (int m = 0; m < lengths.Length; m++)
        {
            startTime[m] = t;
            startBpm[m] = bpm;
            (t, bpm) = Walk(m, 1.0, inclusive: true);
        }
    }

    public double MeasureLength(int measure) => lengths[Math.Clamp(measure, 0, MaxMeasure + 1)];

    double BeatMs(double bpm) => 60000.0 / bpm;

    /// <summary>小節 m の先頭から pos まで進めた時刻と、その時点の BPM</summary>
    (double Time, double Bpm) Walk(int m, double pos, bool inclusive)
    {
        double t = startTime[m], bpm = startBpm[m], prev = 0, beats = lengths[m] * 4;
        foreach (var e in events[m])
        {
            if (e.Pos > pos || (!inclusive && e.Pos == pos)) break;
            t += (e.Pos - prev) * beats * BeatMs(bpm);
            prev = e.Pos;
            if (e.Stop) t += e.Value / 48 * BeatMs(bpm);
            else bpm = e.Value;
        }
        t += (pos - prev) * beats * BeatMs(bpm);
        return (t, bpm);
    }

    /// <summary>その位置のオブジェクトが鳴る時刻（同じ位置の STOP より前）</summary>
    public double TimeOf(int measure, double pos)
    {
        if (measure > MaxMeasure) return startTime[MaxMeasure + 1];
        return Walk(measure, pos, inclusive: false).Time;
    }

    public double MeasureStart(int measure) => startTime[Math.Clamp(measure, 0, MaxMeasure + 1)];

    /// <summary>時刻 → (小節, 位置)。STOP 中の時刻は STOP の位置になる</summary>
    public (int Measure, double Pos) PositionOf(double time)
    {
        int m = Array.BinarySearch(startTime, 0, MaxMeasure + 1, time);
        if (m < 0) m = ~m - 1;
        // 長さ 0 の小節が続く場合などは、時刻を含む最後の小節
        while (m < MaxMeasure && startTime[m + 1] <= time) m++;
        m = Math.Clamp(m, 0, MaxMeasure);

        double t = startTime[m], bpm = startBpm[m], prev = 0, beats = lengths[m] * 4;
        foreach (var e in events[m])
        {
            double next = t + (e.Pos - prev) * beats * BeatMs(bpm);
            if (time < next) break;
            t = next;
            prev = e.Pos;
            if (e.Stop)
            {
                var end = t + e.Value / 48 * BeatMs(bpm);
                if (time < end) return (m, e.Pos);
                t = end;
            }
            else bpm = e.Value;
        }
        var pos = prev + (time - t) / BeatMs(bpm) / beats;
        return (m, Math.Clamp(pos, 0, 1));
    }
}
