# bmstrainer

[日本語](README.md) | [English](README.en.md) | 한국어

BMS 채보 연습용 도구입니다 (Windows).

<img width="560" height="750" alt="3" src="https://github.com/user-attachments/assets/0a712e07-0410-4e8e-bc74-a0385f30a30a" />

- 채보의 속도 변경 (BPM 지정 / LR2와 같은 FREQ 지정, 피치 변경 여부 선택)
- 판정 (EASY / NORMAL / HARD) 변경
- osu!mania (7K / 8K) 채보를 BMS로 변환
  - 원본 BMS가 있으면 해당 키음을 노트에 할당

원본 채보는 수정하지 않고, 같은 폴더에 별도의 채보로 만듭니다.

## 다운로드

[Releases](https://github.com/oomuro-sakurako/bmstrainer/releases)에서 최신 버전 zip을 다운로드하고 압축을 푼 뒤 `bmstrainer.exe`를 실행하세요.

- Windows 10 / 11 (64비트)
- .NET 등을 따로 설치할 필요가 없습니다
- `binaries` 폴더는 `bmstrainer.exe`와 같은 위치에 두세요
- 서명되지 않은 exe이므로, 처음 실행할 때 "Windows의 PC 보호" 화면이 나오면 "추가 정보" → "실행"을 눌러 주세요

## 사용 방법

화면은 일본어·영어·한국어를 지원합니다. 처음에는 Windows 언어에 맞춰 표시되며, 메인 화면 오른쪽 아래의 선택란에서 바꿀 수 있습니다 (다시 시작한 후 적용).

### 채보의 속도·판정 변경

1. "채보 열기…", "폴더 열기…" 또는 드래그 앤 드롭으로 채보를 열고 난이도 선택
2. 속도 지정
   - **BPM 지정**: 목표 BPM 입력 (가장 오래 지속되는 BPM 기준)
   - **FREQ 지정**: -12 ~ +12 중에서 선택 (1단 = 반음. LR2와 같은 배율)
3. **피치 변경**
   - 켜기: 속도와 함께 음정도 오르내림 (LR2의 FREQ와 동일)
   - 끄기: 음정은 유지하고 속도만 변경
4. 필요하면 판정 선택
5. "생성할 채보 이름"에서 제목·부제 (난이도명)·파일 이름 변경 가능 (기본 상태에서는 `[FREQ+1]` 같은 태그가 자동으로 붙음)
6. "채보 만들기" 클릭

채보는 원본과 같은 폴더에, 키음은 채보 폴더 안의 `_bmstrainer` 폴더에 WAV 형식으로 만들어집니다.
"생성한 채보 삭제…"로 그 폴더에 만든 채보와 키음을 한꺼번에 삭제할 수 있습니다.

### osu! 채보를 BMS로 변환

메인 화면 오른쪽 위의 "osu! 채보 변환…"에서 사용합니다.

1. osu! 채보 (`.osu` / `.osz`)를 열고 난이도 선택
2. 원본 BMS 폴더 (선택)
   - **지정 안 함**: osu! 음원을 BGM으로 재생하는 채보를 "출력 폴더"에 만듭니다
   - **지정함**: 원본 BMS의 키음을 노트에 할당한 채보를 원본 폴더에 만듭니다.
     폴더 안의 난이도 중 키음이 가장 잘 일치하는 것을 자동으로 기준으로 삼고, 오프셋 (osu! 음원과 원본의 차이)도 자동으로 감지합니다
3. 판정·이름을 확인하고 "변환" 클릭

원본에 소리가 없는 위치의 노트는 무음이 됩니다.

## 주의 사항

- 만들어진 키음은 WAV 형식이므로 폴더 용량이 늘어납니다
- BGA 동영상은 속도가 바뀌지 않으므로 영상이 어긋납니다
- osu! 변환은 7K / 8K만 지원합니다 (8K는 맨 왼쪽이 스크래치). SV (초록 선)는 반영되지 않습니다
- 테스트 버전입니다. 만들어진 채보는 혹시 모르니 확인한 후 플레이해 주세요

설정은 `%APPDATA%\bmstrainer\settings.json`에 저장됩니다.

## 소스에서 빌드

[.NET 10 SDK](https://dotnet.microsoft.com/download)가 필요합니다.

```
dotnet build
```

배포용 폴더 (`..\dist\bmstrainer-v<버전>`)를 만드는 경우:

```
powershell -ExecutionPolicy Bypass -File package.ps1 -NoZip
```

버전은 `BmsTrainer.csproj`의 `<Version>`으로 지정합니다.

## 라이선스

Copyright (C) 2026 oomuro-sakurako

[GNU General Public License v3.0](LICENSE.txt)으로 공개합니다.

osu!mania → BMS 변환 처리는 [o!m2bms](https://github.com/mashimycota/om2bms) (GPLv3)를, 무음 노트 치환 처리는 "無音ノーツ自動置換ツール" (제작자: @E_BMS_2025)를 C#으로 이식했습니다.
화면 구성 등은 [osu-trainer](https://github.com/FunOrange/osu-trainer)를 참고했습니다 (코드는 사용하지 않았습니다).

사용한 라이브러리 (SoundTouch / SoundStretch, NAudio, NVorbis 등)와 그 라이선스는 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)와 `licenses` 폴더를 참고하세요.

osu!는 ppy Pty Ltd.의 상표입니다. bmstrainer는 osu! 및 LR2와 관계없는 비공식 도구입니다.
