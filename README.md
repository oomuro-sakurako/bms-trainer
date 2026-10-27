# bmstrainer

日本語 | [English](README.en.md) | [한국어](README.ko.md)

BMS 譜面の練習用ツールです（Windows）。

<img width="554" height="754" alt="1" src="https://github.com/user-attachments/assets/d6215e1e-1941-4ef3-b625-3fb0925bad42" />

- 譜面の速度を変える（BPM 指定 / LR2 と同じ FREQ 指定、ピッチを変えるかどうかを選択）
- 判定（EASY / NORMAL / HARD）を変える
- osu!mania（7K / 8K）の譜面を BMS に変換する
  - 本体の BMS があれば、そのキー音をノーツに割り当てる

元の譜面は書き換えず、同じフォルダに別の譜面として作成します。

## ダウンロード

[Releases](https://github.com/oomuro-sakurako/bmstrainer/releases) から最新版の zip をダウンロードし、展開して `bmstrainer.exe` を実行してください。

- Windows 10 / 11（64bit）
- .NET などを別途インストールする必要はありません
- `binaries` フォルダは `bmstrainer.exe` と同じ場所に置いたままにしてください
- 署名していない exe のため、初回起動時に「Windows によって PC が保護されました」と表示された場合は「詳細情報」→「実行」で起動できます

## 使い方

画面は日本語・英語・韓国語に対応しています。最初は Windows の言語に合わせて表示され、メイン画面右下の選択欄で切り替えられます（再起動後に反映）。

### 譜面の速度・判定を変える

1. 「譜面を開く…」「フォルダを開く…」またはドラッグ＆ドロップで譜面を開き、差分を選ぶ
2. 速度を指定する
   - **BPM 指定**: 目標の BPM（一番長く続く BPM 基準）を入力
   - **FREQ 指定**: -12 ～ +12 から選択（1 段 = 半音。LR2 と同じ倍率）
3. **ピッチを変える**
   - オン: 音程も一緒に上がる／下がる（LR2 の FREQ と同じ）
   - オフ: 音程を保ったまま速さだけ変える
4. 必要なら判定を選ぶ
5. 「作成する譜面の名前」でタイトル・サブタイトル（差分名）・ファイル名を変更できる。何も触らなければ `[FREQ+1]` のような名前が自動で付く
6. 「譜面を作成」を押す

譜面は元の譜面と同じフォルダに、キー音は譜面フォルダ内の `_bmstrainer` フォルダに WAV 形式で作成します。
「生成した譜面を削除…」で、そのフォルダに作成した譜面とキー音をまとめて削除できます。

### osu! 譜面を BMS に変換する

メイン画面右上の「osu! 譜面を変換…」から使います。

1. osu! 譜面（`.osu` / `.osz`）を開き、差分を選ぶ
2. 本体フォルダ（任意）
   - **指定しない**: osu! の音源を BGM として鳴らす譜面を、「出力先」のフォルダに作成
   - **指定する**: 本体 BMS のキー音をノーツに割り当てた譜面を、本体フォルダに作成。
     本体フォルダの差分のうちキー音が一番一致するものを自動で基準にし、オフセット（osu! の音源と本体のずれ）も自動で検出する
3. 判定・名前を確認して「変換」を押す

本体に音が無い位置のノーツは無音になります。

## 注意

- 作成したキー音は WAV 形式のため、フォルダの容量が増えます
- BGA の動画は速度が変わらないため、映像がずれます
- osu! 変換は 7K / 8K のみ対応です（8K は左端がスクラッチ）。SV（緑線）は反映しません
- テスト版です。作成した譜面は念のため確認してから遊んでください

設定は `%APPDATA%\bmstrainer\settings.json` に保存されます。

## ソースからビルドする

[.NET 10 SDK](https://dotnet.microsoft.com/download) が必要です。

```
dotnet build
```

配布用のフォルダ（`..\dist\bmstrainer-v<バージョン>`）を作る場合:

```
powershell -ExecutionPolicy Bypass -File package.ps1 -NoZip
```

バージョンは `BmsTrainer.csproj` の `<Version>` で指定します。

## ライセンス

Copyright (C) 2026 oomuro-sakurako

[GNU General Public License v3.0](LICENSE.txt) で公開しています。

osu!mania → BMS 変換の処理は [o!m2bms](https://github.com/mashimycota/om2bms)（GPLv3）を、無音ノーツの置換処理は「無音ノーツ自動置換ツール」（作者: @E_BMS_2025）を C# に移植しています。
画面構成などは [osu-trainer](https://github.com/FunOrange/osu-trainer) を参考にしています（コードは使用していません）。

使用しているライブラリ（SoundTouch / SoundStretch、NAudio、NVorbis など）とそのライセンスは [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) と `licenses` フォルダを参照してください。

osu! は ppy Pty Ltd. の商標です。bmstrainer は osu! および LR2 とは関係のない非公式のツールです。
