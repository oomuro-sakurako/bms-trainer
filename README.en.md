# bmstrainer

[日本語](README.md) | English | [한국어](README.ko.md)

A practice tool for BMS charts (Windows).

<img width="554" height="754" alt="1" src="https://github.com/user-attachments/assets/df2fc1ba-4818-4f4a-bada-fddc811a35ba" />


- Change the speed of a chart (by BPM, or by FREQ steps just like LR2), with or without changing the pitch
- Change the judge rank (EASY / NORMAL / HARD)
- Convert osu!mania (7K / 8K) charts to BMS
  - If you have the original BMS, its keysounds are assigned to the notes

The original chart is never modified. A new chart is created in the same folder.

## Download

Download the latest zip from [Releases](https://github.com/oomuro-sakurako/bmstrainer/releases), extract it, and run `bmstrainer.exe`.

- Windows 10 / 11 (64-bit)
- No need to install .NET or anything else
- Keep the `binaries` folder next to `bmstrainer.exe`
- The exe is not signed. If Windows shows "Windows protected your PC" on the first launch, click "More info" → "Run anyway"

## Usage

The UI is available in Japanese, English and Korean. It follows the Windows display language at first, and you can switch it with the selector at the bottom right of the main window (applied after a restart).

### Change the speed / judge rank of a chart

1. Open a chart with "Open chart…", "Open folder…" or drag & drop, and pick a difficulty
2. Set the speed
   - **By BPM**: enter the target BPM (based on the BPM that lasts the longest)
   - **By FREQ**: choose from -12 to +12 (1 step = 1 semitone, the same rate as LR2)
3. **Change pitch**
   - On: the pitch rises/falls together with the speed (same as LR2's FREQ)
   - Off: only the speed changes; the pitch is kept
4. Choose a judge rank if needed
5. Under "New chart names" you can change the title, subtitle (difficulty name) and file name. If you leave them alone, a name like `[FREQ+1]` is added automatically
6. Click "Create chart"

The chart is created in the same folder as the original, and the keysounds are written as WAV files to the `_bmstrainer` folder inside the chart folder.
"Delete generated charts…" deletes all charts and keysounds created by bmstrainer in that folder.

### Convert an osu! chart to BMS

Click "Convert osu!…" at the top right of the main window.

1. Open an osu! chart (`.osu` / `.osz`) and pick a difficulty
2. Original BMS folder (optional)
   - **None (not set)**: a chart that plays the osu! audio as BGM is created in the "Output" folder
   - **Specified**: a chart whose notes use the keysounds of the original BMS is created in the original BMS folder.
     The difficulty in that folder whose keysounds match best is chosen automatically as the base, and the offset (the gap between the osu! audio and the original) is detected automatically
3. Check the judge rank and names, then click "Convert"

Notes at positions where the original has no sound become silent.

## Notes

- The generated keysounds are WAV files, so the folder size increases
- BGA videos are not sped up, so they will be out of sync
- osu! conversion supports 7K / 8K only (the leftmost lane of 8K is the scratch). SV changes (green lines) are not applied
- This is a test version. Please check the generated charts before playing

Settings are saved to `%APPDATA%\bmstrainer\settings.json`.

## Building from source

The [.NET 10 SDK](https://dotnet.microsoft.com/download) is required.

```
dotnet build
```

To create the distribution folder (`..\dist\bmstrainer-v<version>`):

```
powershell -ExecutionPolicy Bypass -File package.ps1 -NoZip
```

The version is set by `<Version>` in `BmsTrainer.csproj`.

## License

Copyright (C) 2026 oomuro-sakurako

Released under the [GNU General Public License v3.0](LICENSE.txt).

The osu!mania → BMS conversion is ported to C# from [o!m2bms](https://github.com/mashimycota/om2bms) (GPLv3), and the silent-note replacement from "無音ノーツ自動置換ツール" (by @E_BMS_2025).
The UI is inspired by [osu-trainer](https://github.com/FunOrange/osu-trainer) (no code is used).

For the libraries used (SoundTouch / SoundStretch, NAudio, NVorbis, etc.) and their licenses, see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) and the `licenses` folder.

osu! is a trademark of ppy Pty Ltd. bmstrainer is an unofficial tool and is not affiliated with osu! or LR2.
