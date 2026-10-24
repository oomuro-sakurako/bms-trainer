using System.Globalization;

namespace BmsTrainer;

public enum Language { Japanese, English, Korean }

/// <summary>画面・メッセージの文字（日本語 / 英語 / 韓国語）</summary>
public static class L
{
    public static Language Current { get; set; } = Detect();

    /// <summary>Windows の表示言語から選ぶ（日本語・韓国語以外は英語）</summary>
    public static Language Detect() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch
    {
        "ja" => Language.Japanese,
        "ko" => Language.Korean,
        _ => Language.English,
    };

    public static Language? Parse(string? code) => code switch
    {
        "ja" => Language.Japanese,
        "en" => Language.English,
        "ko" => Language.Korean,
        _ => null,
    };

    public static string Code(Language lang) => lang switch { Language.Japanese => "ja", Language.Korean => "ko", _ => "en" };

    /// <summary>言語の選択肢（その言語自身の表記）</summary>
    public static readonly (Language Lang, string Name)[] Choices =
        [(Language.Japanese, "日本語"), (Language.English, "English"), (Language.Korean, "한국어")];

    public static Font UiFont(float size = 9.5f, FontStyle style = FontStyle.Regular) => new(Current switch
    {
        Language.Japanese => "Yu Gothic UI",
        Language.Korean => "Malgun Gothic",
        _ => "Segoe UI",
    }, size, style);

    static string T(string ja, string en, string ko) => Current switch
    {
        Language.Japanese => ja,
        Language.Korean => ko,
        _ => en,
    };

    static string F(string format, params object[] args) => string.Format(CultureInfo.InvariantCulture, format, args);

    // ───────── 共通 ─────────
    public static string Open => T("開く…", "Open…", "열기…");
    public static string Judge => T("判定", "Judge rank", "판정");
    public static string Notes => T("ノーツ", "Notes", "노트");
    public static string Length => T("長さ", "Length", "길이");
    public static string BmsFilter => T("BMS 譜面", "BMS charts", "BMS 채보") + "|*.bms;*.bme;*.bml;*.pms|" + AllFiles + "|*.*";
    static string AllFiles => T("すべてのファイル", "All files", "모든 파일");
    public static string Created(string file) => F(T("作成しました: {0}", "Created: {0}", "생성했습니다: {0}"), file);
    public static string LanguageLabel => T("言語", "Language", "언어");
    public static string RestartToApply => T(
        "言語の変更は再起動後に反映されます。今すぐ再起動しますか？",
        "The language change takes effect after restarting. Restart now?",
        "언어 변경은 다시 시작한 후에 적용됩니다. 지금 다시 시작하시겠습니까?");

    // ───────── メイン画面 ─────────
    public static string OpenChart => T("譜面を開く…", "Open chart…", "채보 열기…");
    public static string OpenFolder => T("フォルダを開く…", "Open folder…", "폴더 열기…");
    public static string ConvertOsu => T("osu! 譜面を変換…", "Convert osu!…", "osu! 채보 변환…");
    public static string Speed => T("速度", "Speed", "속도");
    public static string ByBpm => T("BPM 指定", "By BPM", "BPM 지정");
    public static string ByFreq => T("FREQ 指定", "By FREQ", "FREQ 지정");
    public static string ChangePitch => T(
        "ピッチを変える（音程も上がる／下がる。LR2 の FREQ と同じ）",
        "Change pitch (pitch rises/falls with speed, like LR2's FREQ)",
        "피치 변경 (속도와 함께 음정도 변경. LR2의 FREQ와 동일)");
    public static string NoChange => T("変更しない", "No change", "변경 안 함");
    public static string CreateChart => T("譜面を作成", "Create chart", "채보 만들기");
    public static string Cancel => T("キャンセル", "Cancel", "취소");
    public static string DeleteGenerated => T("生成した譜面を削除…", "Delete generated charts…", "생성한 채보 삭제…");
    public static string LoadFailed(string file, string message) => F(T("読み込み失敗: {0} ({1})", "Failed to load: {0} ({1})", "불러오기 실패: {0} ({1})"), file, message);
    public static string NoBmsInFolder => T("このフォルダに BMS 譜面がありません。", "No BMS charts in this folder.", "이 폴더에 BMS 채보가 없습니다.");
    public static string NoChartSelected => T("譜面が選択されていません", "No chart selected", "채보가 선택되지 않았습니다");
    public static string PitchChanged => T("ピッチ: 変える", "Pitch: Shifted", "피치: 변경");
    public static string PitchKept => T("ピッチ: そのまま", "Pitch: Kept", "피치: 유지");
    public static string NothingToCreate => T(
        "倍率が x1.00 で、判定も変更しない設定です。",
        "The rate is x1.00 and the judge rank is unchanged, so there is nothing to create.",
        "배율이 x1.00이고 판정도 변경하지 않는 설정입니다.");
    public static string ConvertingKeysounds => T("キー音を変換中…", "Converting keysounds…", "키음 변환 중…");
    public static string KeysoundErrors(int count) => F(T(
        "{0} 個のキー音を変換できず、元のファイルのままになっています。",
        "{0} keysounds could not be converted and still use the original files.",
        "키음 {0}개를 변환하지 못해 원본 파일을 그대로 사용합니다."), count);
    public static string Canceled => T("キャンセルしました。", "Canceled.", "취소했습니다.");
    public static string CreateFailed => T("作成に失敗しました。", "Failed to create the chart.", "채보 생성에 실패했습니다.");
    public static string NoGenerated => T("このフォルダに生成した譜面はありません。", "There are no generated charts in this folder.", "이 폴더에 생성한 채보가 없습니다.");
    public static string DeleteConfirm(int charts) => F(T("次を削除します。\n\n譜面 {0} 個", "The following will be deleted.\n\nCharts: {0}", "다음을 삭제합니다.\n\n채보 {0}개"), charts);
    public static string KeysoundFolder => T("キー音フォルダ", "Keysound folder", "키음 폴더");
    public static string Deleted(int charts) => F(T("{0} 個の譜面とキー音を削除しました。", "Deleted {0} charts and their keysounds.", "채보 {0}개와 키음을 삭제했습니다."), charts);

    // ───────── 名前の入力欄 ─────────
    public static string NamesHeading => T("作成する譜面の名前", "New chart names", "생성할 채보 이름");
    public static string ResetNames => T("自動に戻す", "Reset", "자동으로 되돌리기");
    public static string FieldTitle => T("タイトル", "Title", "제목");
    public static string FieldSubtitle => T("サブタイトル（差分名）", "Subtitle (difficulty)", "부제 (난이도명)");
    public static string FieldArtist => T("アーティスト", "Artist", "아티스트");
    public static string FieldFileName => T("ファイル名", "File name", "파일 이름");
    public static string OverwriteConfirm(string file) => F(T("{0} は既にあります。上書きしますか？", "{0} already exists. Overwrite it?", "{0}이(가) 이미 있습니다. 덮어쓰시겠습니까?"), file);
    public static string EmptyFileName => T("ファイル名が空です。", "The file name is empty.", "파일 이름이 비어 있습니다.");
    public static string SameAsSource => T(
        "元の譜面と同じファイル名です。別の名前にしてください。",
        "The file name is the same as the original chart. Please choose another name.",
        "원본 채보와 같은 파일 이름입니다. 다른 이름을 지정해 주세요.");

    // ───────── osu! 変換 ─────────
    public static string OsuTitle => T("osu! 譜面を BMS に変換", "Convert osu! chart to BMS", "osu! 채보를 BMS로 변환");
    public static string OsuChart => T("osu! 譜面（.osu / .osz）", "osu! chart (.osu / .osz)", "osu! 채보 (.osu / .osz)");
    public static string OsuFilter => T("osu! 譜面", "osu! charts", "osu! 채보") + "|*.osu;*.osz|" + AllFiles + "|*.*";
    public static string BodyFolder => T("本体フォルダ（任意・キー音の補完に使う）", "Original BMS folder (optional, for keysounds)", "원본 BMS 폴더 (선택, 키음 보완용)");
    public static string Clear => T("解除", "Clear", "해제");
    public static string BaseChart => T("基準の譜面", "Base chart", "기준 채보");
    public static string Offset => T("オフセット", "Offset", "오프셋");
    public static string AutoDetect => T("自動検出", "Detect", "자동 감지");
    public static string LaneOrderLabel => T("置換レーン順", "Lane order", "치환 레인 순서");
    public static string Common => T("共通", "General", "일반");
    public static string OutputFolder => T("出力先", "Output", "출력 폴더");
    public static string Change => T("変更…", "Change…", "변경…");
    public static string Convert => T("変換", "Convert", "변환");
    public static string NoManiaCharts => T("7K / 8K の osu!mania 譜面が見つかりません。", "No 7K / 8K osu!mania charts were found.", "7K / 8K osu!mania 채보를 찾을 수 없습니다.");
    public static string CouldNotLoad(string message) => F(T("読み込めませんでした: {0}", "Could not load: {0}", "불러올 수 없습니다: {0}"), message);
    public static string NoBmsInBodyFolder => T("本体フォルダに BMS 譜面がありません。", "There are no BMS charts in the original folder.", "원본 폴더에 BMS 채보가 없습니다.");
    public static string MatchShort(double rate) => F(T("一致率 {0:0.0}%", "Match {0:0.0}%", "일치율 {0:0.0}%"), rate * 100);
    public static string KeysoundMatch(double rate) => F(T("キー音の一致率 {0:0.0}%", "Keysound match: {0:0.0}%", "키음 일치율: {0:0.0}%"), rate * 100);
    public static string NoBody => T("指定なし（osu! の音源を BGM として鳴らす）", "None (plays osu! audio as BGM)", "지정 안 함 (osu! 음원을 BGM으로 재생)");
    public static string SameAsBody => T("本体と同じフォルダ", "Same as the original folder", "원본과 같은 폴더");
    public static string NotSet => T("（未設定）", "(not set)", "(미설정)");
    public static string Converting => T("変換中…", "Converting…", "변환 중…");
    public static string KeysoundedNotes(int keysounded, int total, int silent) => F(T(
        "キー音を割り当てたノーツ {0} / {1}（無音 {2}）",
        "Notes with keysounds: {0} / {1} (silent: {2})",
        "키음을 할당한 노트 {0} / {1} (무음 {2})"), keysounded, total, silent);
    public static string NoteCount(int notes) => F(T("ノーツ {0}", "Notes: {0}", "노트 {0}"), notes);
    public static string OpenInMain => T("作成した譜面をメイン画面で開きますか？", "Open the new chart in the main window?", "생성한 채보를 메인 화면에서 여시겠습니까?");
    public static string ConvertFailed => T("変換に失敗しました。", "Conversion failed.", "변환에 실패했습니다.");
    public static string LaneFromLeft => T("左側レーンから順に置換", "Left to right", "왼쪽 레인부터 치환");
    public static string LaneFromRight => T("右側レーンから順に置換", "Right to left", "오른쪽 레인부터 치환");
    public static string LaneFromCenter(int n) => F(T("中央レーンから順に置換{0}", "Center outward {0}", "가운데 레인부터 치환 {0}"), n);

    // ───────── 変換処理のエラー ─────────
    public static string NoFreeBpmId => T("#BPMxx の空き ID が足りません。", "Not enough free #BPMxx IDs.", "사용 가능한 #BPMxx ID가 부족합니다.");
    public static string SoundStretchMissing => T("soundstretch.exe が見つかりません。", "soundstretch.exe was not found.", "soundstretch.exe를 찾을 수 없습니다.");
    public static string NoTimingPoints => T("赤線（タイミングポイント）がありません。", "The chart has no timing points (red lines).", "타이밍 포인트(빨간 선)가 없습니다.");
    public static string TooManyMeasures => T("999 小節を超えました。", "The chart exceeds 999 measures.", "999마디를 초과했습니다.");
    public static string NoSilentId => T("無音ノーツ用の ID がありません。", "No free ID is left for silent notes.", "무음 노트용 ID가 없습니다.");
}
