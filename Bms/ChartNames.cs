namespace BmsTrainer.Bms;

/// <summary>作成する譜面のタイトル・サブタイトル（差分名）・アーティスト・ファイル名（拡張子なし）</summary>
/// <param name="Artist">null なら元のまま</param>
public sealed record ChartNames(string Title, string SubTitle, string FileName, string? Artist = null)
{
    /// <summary>
    /// ヘッダ行のタイトル類を書き換えて output に追加する。書き換えた場合は true。
    /// 元の譜面に #SUBTITLE / #ARTIST が無い場合は #TITLE の直後に追加する。
    /// </summary>
    public bool TryRewrite(string key, string token, string line, IReadOnlyList<string> sourceLines, List<string> output)
    {
        switch (key)
        {
            case "TITLE":
                output.Add($"{token} {Title}");
                if (SubTitle.Length > 0 && !HasHeader(sourceLines, "SUBTITLE")) output.Add($"#SUBTITLE {SubTitle}");
                if (!string.IsNullOrEmpty(Artist) && !HasHeader(sourceLines, "ARTIST")) output.Add($"#ARTIST {Artist}");
                return true;
            case "SUBTITLE":
                if (SubTitle.Length > 0) output.Add($"{token} {SubTitle}");
                return true;
            case "ARTIST" when Artist != null:
                output.Add($"{token} {Artist}");
                return true;
        }
        return false;
    }

    static bool HasHeader(IReadOnlyList<string> lines, string key) =>
        lines.Any(l => BmsChart.TryParseHeader(l, out var k, out _) && k == key);

    public static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "");
        return name.Trim();
    }

    /// <summary>作成先のパス。空のファイル名や元の譜面への上書きはエラー</summary>
    public string OutputPath(string sourcePath, string? directory = null)
    {
        var name = SanitizeFileName(FileName);
        if (name.Length == 0) throw new InvalidOperationException(L.EmptyFileName);
        var path = Path.Combine(directory ?? Path.GetDirectoryName(sourcePath)!, name + Path.GetExtension(sourcePath));
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(L.SameAsSource);
        return path;
    }
}
