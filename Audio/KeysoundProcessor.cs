using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using BmsTrainer.Bms;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace BmsTrainer.Audio;

public readonly record struct ProcessProgress(int Done, int Total, string File);

/// <summary>
/// キー音を倍率に合わせて変換する。
/// ピッチを変える: リサンプリング（FREQ と同じく再生速度と音程が一緒に変わる）
/// ピッチを変えない: SoundStretch で長さだけ変える（osu-trainer と同じ方式）
/// 出力は全て 16bit PCM WAV。
/// </summary>
public sealed class KeysoundProcessor
{
    static readonly string[] AltExtensions = [".wav", ".ogg", ".mp3", ".flac"];

    readonly string soundStretchPath = Path.Combine(AppContext.BaseDirectory, "binaries", "soundstretch.exe");

    /// <returns>譜面の #WAV の値 → 変換後の #WAV の値</returns>
    public Dictionary<string, string> Process(BmsChart chart, RateSettings s, IProgress<ProcessProgress>? progress, CancellationToken ct)
    {
        Errors = [];
        if (!s.ChangesSpeed) return new(StringComparer.OrdinalIgnoreCase); // 判定だけ変える場合は元のキー音をそのまま使う
        if (!s.ChangePitch && !File.Exists(soundStretchPath))
            throw new FileNotFoundException(L.SoundStretchMissing, soundStretchPath);

        var chartDir = Path.GetDirectoryName(chart.FilePath)!;
        var outRelDir = BmsRateConverter.KeysoundDir(s);

        // #RANDOM の全分岐を含めて全ての #WAV を対象にする
        var wavValues = chart.Lines
            .Select(l => BmsChart.TryParseHeader(l, out var k, out var v) && k.Length == 5 && k.StartsWith("WAV") ? v : null)
            .Where(v => !string.IsNullOrEmpty(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var jobs = new List<(string Value, string Source, string OutRel)>();
        var usedOut = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in wavValues)
        {
            var source = ResolveSource(chartDir, value!);
            if (source == null) continue; // 元から存在しないキー音はそのまま
            var rel = SanitizeRelative(Path.ChangeExtension(value!, ".wav"));
            var outRel = $"{outRelDir}/{rel}";
            for (int i = 2; !usedOut.Add(outRel); i++)
                outRel = $"{outRelDir}/{Path.ChangeExtension(rel, null)}_{i}.wav";
            jobs.Add((value!, source, outRel));
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "bmstrainer", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var result = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var errors = new ConcurrentBag<string>();
        int done = 0;
        try
        {
            Parallel.ForEach(jobs, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, job =>
            {
                var outPath = Path.Combine(chartDir, job.OutRel.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    // 同じ倍率で作成済みのキー音は使い回す（同じフォルダの別差分など）
                    if (!File.Exists(outPath))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                        var (data, sr, ch) = Decode(job.Source);
                        var processed = s.ChangePitch ? Resample(data, sr, ch, s.Rate) : Stretch(data, sr, ch, s.Rate, tempDir);
                        var tmp = outPath + ".tmp";
                        WriteWav(tmp, processed, sr, ch);
                        File.Move(tmp, outPath, true);
                    }
                    result[job.Value] = job.OutRel;
                }
                catch (Exception ex)
                {
                    errors.Add($"{job.Value}: {ex.Message}");
                }
                var n = Interlocked.Increment(ref done);
                progress?.Report(new ProcessProgress(n, jobs.Count, job.Value));
            });
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
        Errors = errors.ToList();
        return new Dictionary<string, string>(result, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>直前の Process で変換に失敗したキー音（元のファイルのまま参照される）</summary>
    public IReadOnlyList<string> Errors { get; private set; } = [];

    static string? ResolveSource(string chartDir, string value)
    {
        var path = Path.Combine(chartDir, value);
        if (File.Exists(path)) return path;
        // BMS プレイヤーと同様、拡張子違いのファイルも探す
        foreach (var ext in AltExtensions)
        {
            var alt = Path.ChangeExtension(path, ext);
            if (File.Exists(alt)) return alt;
        }
        return null;
    }

    static string SanitizeRelative(string value)
    {
        var parts = value.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p == ".." ? "__" : p.Replace(":", "_"));
        return string.Join('/', parts);
    }

    static (float[] Data, int SampleRate, int Channels) Decode(string path)
    {
        var head = new byte[4];
        using (var fs = File.OpenRead(path)) fs.ReadExactly(head, 0, Math.Min(4, (int)fs.Length));
        var magic = System.Text.Encoding.ASCII.GetString(head);
        try
        {
            // 拡張子と中身が違うファイルがあるので先頭のバイトで判定する
            if (magic == "OggS")
            {
                using var ogg = new VorbisWaveReader(path);
                return ReadAll(ogg);
            }
            if (magic == "RIFF")
            {
                using var wav = new WaveFileReader(path);
                if (wav.WaveFormat.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.IeeeFloat)
                    return ReadAll(wav.ToSampleProvider());
                using var pcm = WaveFormatConversionStream.CreatePcmStream(wav); // ADPCM など
                return ReadAll(pcm.ToSampleProvider());
            }
        }
        catch (Exception) when (magic == "RIFF")
        {
            // 壊れ気味の WAV は Media Foundation に任せる
        }
        using var mf = new MediaFoundationReader(path);
        return ReadAll(mf.ToSampleProvider());
    }

    static (float[], int, int) ReadAll(ISampleProvider sp)
    {
        var list = new List<float>();
        var buf = new float[sp.WaveFormat.SampleRate * sp.WaveFormat.Channels];
        int n;
        while ((n = sp.Read(buf, 0, buf.Length)) > 0) list.AddRange(buf.AsSpan(0, n));
        return (list.ToArray(), sp.WaveFormat.SampleRate, sp.WaveFormat.Channels);
    }

    /// <summary>再生速度と音程を同時に rate 倍にする（サンプリング周波数は元のまま）</summary>
    static float[] Resample(float[] data, int sr, int ch, double rate)
    {
        var src = new ArraySampleProvider(data, WaveFormat.CreateIeeeFloatWaveFormat((int)Math.Round(sr * rate), ch));
        return ReadAll(new WdlResamplingSampleProvider(src, sr)).Item1;
    }

    /// <summary>音程を保ったまま長さを 1/rate にする</summary>
    float[] Stretch(float[] data, int sr, int ch, double rate, string tempDir)
    {
        var id = Guid.NewGuid().ToString("N");
        var inPath = Path.Combine(tempDir, id + "_in.wav");
        var outPath = Path.Combine(tempDir, id + "_out.wav");
        WriteWav(inPath, data, sr, ch);
        try
        {
            var tempo = ((rate - 1) * 100).ToString("+0.####;-0.####", CultureInfo.InvariantCulture);
            var psi = new ProcessStartInfo(soundStretchPath, $"\"{inPath}\" \"{outPath}\" -tempo={tempo}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using (var p = System.Diagnostics.Process.Start(psi)!)
            {
                p.StandardOutput.ReadToEndAsync();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
            }
            if (File.Exists(outPath))
            {
                using var reader = new WaveFileReader(outPath);
                var (result, _, _) = ReadAll(reader.ToSampleProvider());
                if (result.Length > 0) return result;
            }
        }
        finally
        {
            try { File.Delete(inPath); } catch { }
            try { File.Delete(outPath); } catch { }
        }
        // 極端に短い音などで SoundStretch が何も出力しなかった場合はリサンプリングで代用
        return Resample(data, sr, ch, rate);
    }

    static void WriteWav(string path, float[] data, int sr, int ch)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(sr, 16, ch));
        var pcm = new byte[data.Length * 2];
        for (int i = 0; i < data.Length; i++)
        {
            var v = (short)Math.Round(Math.Clamp(data[i], -1f, 1f) * short.MaxValue);
            pcm[i * 2] = (byte)v;
            pcm[i * 2 + 1] = (byte)(v >> 8);
        }
        writer.Write(pcm, 0, pcm.Length);
    }

    sealed class ArraySampleProvider(float[] data, WaveFormat format) : ISampleProvider
    {
        int position;
        public WaveFormat WaveFormat { get; } = format;
        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, data.Length - position);
            Array.Copy(data, position, buffer, offset, n);
            position += n;
            return n;
        }
    }
}
