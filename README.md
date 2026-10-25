# Tangerine for Windows — 끌어서 바로 바꾸는 파일 변환기

맥용 파일 변환 앱 [Tangerine](https://tangerineformac.com)(Leap Studio)의 핵심 동작을 Windows 에서 쓰려고 만든 프로그램입니다.
원본 앱과는 관계가 없는 개인 프로젝트입니다. 참고한 소개 글: [AI매터스 — Tangerine](https://aimatters.co.kr/ai-tool/56353/)

## 사용 방법

1. `Tangerine.exe` 를 실행하면 창 없이 **알림 영역(작업 표시줄 오른쪽)** 에 귤 아이콘이 생깁니다.
2. 탐색기나 바탕화면에서 파일을 **끄는 도중 `Shift`** 를 누르면 커서 아래에 원형 메뉴가 뜹니다.
3. 원하는 형식 칸 위에 놓으면 **원본과 같은 폴더에 새 파일**이 생깁니다. 원본은 그대로 둡니다.
   - 같은 이름이 있으면 `사진 (1).jpg` 처럼 번호를 붙입니다.
   - 가운데나 메뉴 바깥에 놓으면 취소입니다. `Esc` 로 끌기 자체를 취소해도 됩니다.
4. 다 되면 Windows 알림이 뜨고, 알림을 누르면 탐색기에서 결과 파일을 보여 줍니다.

여러 파일을 한꺼번에 끌어 와도 됩니다. 모든 파일이 바뀔 수 있는 형식만 메뉴에 나오고,
이미 그 형식인 파일은 건너뜁니다 (JPG·PNG 를 같이 끌어 와 JPG 를 고르면 PNG 만 바뀜).

모든 변환은 이 PC 안에서만 합니다. 파일을 어디에도 올리지 않습니다.

### 바꿀 수 있는 형식

| 원본 | 바꿀 수 있는 형식 | 엔진 |
|---|---|---|
| 이미지 — JPG, PNG, WEBP, HEIC, AVIF, GIF, BMP, TIFF, ICO | JPG, PNG, WEBP, AVIF, GIF, BMP, TIFF, ICO(여러 크기), PDF | Magick.NET |
| GIF (위에 더해) | MP4, WEBM | FFmpeg |
| 영상 — MP4, MOV, M4V, MKV, AVI, WEBM, WMV, FLV, MPG, 3GP, TS, MTS | MP4, MOV, MKV(H.264), WEBM(VP9), AVI, GIF(움짤), MP3·WAV·M4A(소리만) | FFmpeg |
| 오디오 — MP3, WAV, M4A, AAC, FLAC, OGG, OPUS, WMA, AIFF | MP3, WAV, M4A, FLAC, OGG, OPUS, AIFF | FFmpeg |

### 알림 영역 메뉴 (아이콘 오른쪽 클릭)

| 항목 | 설명 |
|---|---|
| Shift 끌기로 변환 켜기 | 끄면 Shift 를 눌러도 메뉴가 뜨지 않습니다 |
| 탐색기·바탕화면에서만 작동 | 기본 켜짐. 끄면 브라우저 등 다른 프로그램에서 끌어 온 파일에도 반응합니다 (글자를 Shift+끌어 선택할 때 메뉴가 같이 뜰 수 있음) |
| Windows 시작 시 실행 | 로그인할 때 자동으로 켭니다 (`HKCU\...\Run`) |
| 기록 폴더 열기 | `%LOCALAPPDATA%\Tangerine\tangerine.log` — 실패한 변환의 이유가 남습니다 |

설정은 `%APPDATA%\Tangerine\settings.json` 에 저장됩니다.

## 설치

[Releases](../../releases) 에서 `Tangerine-v*-win-x64.zip` 을 받아 원하는 폴더에 풀고 `Tangerine.exe` 를 실행합니다.
.NET 설치는 필요 없습니다. 압축 파일 안의 `tools\ffmpeg.exe` 는 영상·오디오 변환에 쓰므로 같이 두세요.

- Windows 10/11 64비트
- 관리자 권한으로 실행한 프로그램에서 끌어 온 파일은 Windows 보안 정책 때문에 받을 수 없습니다.

## 개발

```powershell
dotnet test tests/Tangerine.Core.Tests     # 형식 목록·파일 이름·ffmpeg 인자 테스트 (맥에서도 실행됨)
.\scripts\fetch-ffmpeg.ps1                  # src\Tangerine\tools\ffmpeg.exe 받기 (처음 한 번)
dotnet run --project src/Tangerine          # 실행
```

- .NET 10 SDK 필요. 맥에서도 `dotnet build src/Tangerine/Tangerine.csproj` 로 컴파일까지는 확인할 수 있습니다 (실행은 Windows 에서만).
- `main` 에 올리면 GitHub Actions 가 Windows 에서 테스트·빌드합니다.
- `v0.1.0` 처럼 태그를 올리면 단일 exe + ffmpeg 를 묶은 zip 을 Releases 에 올립니다.

### 구조

| 위치 | 내용 |
|---|---|
| `src/Tangerine.Core/` | Windows 와 무관한 규칙 — 형식 목록(`FormatCatalog`), 결과 파일 이름(`OutputPaths`), ffmpeg 인자(`FfmpegArguments`) |
| `src/Tangerine/Interop/DragWatcher.cs` | 전역 저수준 마우스 훅. 왼쪽 버튼으로 끄는 중 Shift 가 눌리면 메뉴를 띄움 |
| `src/Tangerine/Ui/RadialMenuWindow.*` | 투명 원형 메뉴 창. 이 창이 드롭 대상이며, 항상 "복사" 로 답해 탐색기가 원본을 옮기지 않게 함 |
| `src/Tangerine/Ui/TrayIcon.cs` | 알림 영역 아이콘·메뉴·알림 |
| `src/Tangerine/Conversion/` | Magick.NET 이미지 변환, ffmpeg.exe 실행, 동시 변환 관리 |

### 다음에 할 일

원본 Tangerine 에 있는 아래 기능은 아직 없습니다.

- `Ctrl` + 끌기: 파일 도구 — 압축(비율·목표 용량), 자르기, 영상 자르기·나누기, PDF 합치기·나누기, 메타데이터 삭제, QR 읽기
- `Alt` + 끌기: 파일 동작 — 복사, 이동, 경로 복사, 터미널 열기
- 문서 변환 (Word·PowerPoint·EPUB → PDF 등, LibreOffice/Pandoc)
- 탐색기 오른쪽 클릭 "변환" 메뉴

## 라이선스 참고

- 배포 zip 의 `tools\ffmpeg.exe` 는 [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) 의 GPL 빌드이며 라이선스는 `tools\FFMPEG-LICENSE.txt` 에 있습니다.
- 이미지 변환은 [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache-2.0) 을 씁니다.
