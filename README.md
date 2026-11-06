# Tangerine for Windows — 끌어서 바로 바꾸는 파일 변환기

맥용 파일 변환 앱 [Tangerine](https://tangerineformac.com)(Leap Studio)의 핵심 동작을 Windows 에서 쓰려고 만든 프로그램입니다.
원본 앱과는 관계가 없는 개인 프로젝트입니다. 참고한 소개 글: [AI매터스 — Tangerine](https://aimatters.co.kr/ai-tool/56353/)

## 사용 방법

1. `Tangerine.exe` 를 실행하면 창 없이 **알림 영역(작업 표시줄 오른쪽)** 에 귤 아이콘이 생깁니다.
2. 탐색기나 바탕화면에서 파일을 **끌어오는 도중 `Shift`** 를 누르면 커서 아래에 원형 메뉴가 뜹니다.
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
| 이미지 — JPG, PNG, WEBP, HEIC, AVIF, GIF, BMP, TIFF, ICO, SVG | JPG, PNG, WEBP, AVIF, GIF, BMP, TIFF, ICO(여러 크기), PDF | Magick.NET |
| GIF (위에 더해) | MP4, WEBM | FFmpeg |
| 영상 — MP4, MOV, M4V, MKV, AVI, WEBM, WMV, FLV, MPG, 3GP, TS, MTS | MP4, MOV, MKV(H.264), WEBM(VP9), AVI, GIF(움짤), MP3·WAV·M4A(소리만) | FFmpeg |
| 오디오 — MP3, WAV, M4A, AAC, FLAC, OGG, OPUS, WMA, AIFF | MP3, WAV, M4A, FLAC, OGG, OPUS, AIFF | FFmpeg |
| 문서 — HWP, HWPX | PDF | 설치된 한글 |
| 문서 — DOC, DOCX, TXT | PDF | 설치된 Word |
| 엑셀 — XLS, XLSX | PDF, CSV(시트마다, UTF-8) | 설치된 Excel |
| CSV | XLSX | 설치된 Excel |
| 발표 — PPT, PPTX | PDF, JPG·PNG(장마다 한 장) | 설치된 PowerPoint |
| 자막 — SRT, VTT | SRT, VTT, TXT(대사만) | 탠저린 자체 |
| PDF | 나누기 — 쪽마다 PDF 한 개 | PDFsharp |
| PDF | JPG, PNG — 쪽마다 한 장 | Windows 기본 PDF 렌더러 |
| PDF 여러 개 | 합치기 — PDF 하나로 | PDFsharp |
| 무엇이든 (파일·폴더, 여러 개) | ZIP — 끌어 온 것을 모두 묶어 하나로 | 반디집 (없으면 Windows 기본 ZIP) |
| 무엇이든 (파일·폴더, 여러 개) | 경로 복사 — 전체 경로를 한 줄에 하나씩 클립보드로 | 탠저린 자체 |

- 메뉴의 마지막 두 조각은 늘 **ZIP → 경로** 입니다. 압축은 파일 하나면 `그이름.zip`, 여럿이면 들어 있던 `폴더이름.zip` 을 만듭니다.
- 발표 → 그림은 300dpi(16:9 면 4000×2250px) 로 `발표 1장.jpg`, `발표 2장.jpg` … 를 만듭니다.
- 엑셀 → CSV 는 시트가 하나면 `매출.csv`, 여럿이면 `매출 시트이름.csv` 를 시트마다 만듭니다 (UTF-8, 한글이 깨지지 않음).
  CSV → XLSX 는 UTF-8·ANSI(CP949) 를 가려 엽니다. Excel 이 숫자로 읽는 칸(예: 앞자리 0)은 Excel 규칙을 따릅니다.
- 자막은 UTF-8·ANSI 를 가려 읽고, SRT 는 BOM 을 붙인 UTF-8, VTT 는 BOM 없는 UTF-8 로 씁니다. TXT 는 꼬리표를 지운 대사만 모읍니다.
- SVG 는 300dpi 로 그리고, PNG·WEBP 등은 바탕을 투명하게 둡니다 (JPG 는 흰 바탕).
- 문서 변환은 한글·Office 를 보이지 않게 띄워 PDF 로 저장합니다. 매크로는 실행하지 않고, 암호가 걸린 문서는 변환하지 않습니다.
  Excel 은 모든 시트를, TXT 는 맑은 고딕으로 PDF 를 만듭니다. 문서는 한 번에 하나씩 차례로 바꿉니다.
- PDF → 그림은 300dpi(A4 면 2481×3509px), 흰 바탕입니다. 한 쪽이면 `문서.jpg`, 여러 쪽이면 `문서 1쪽.jpg`, `문서 2쪽.jpg` … 를 만듭니다.
  큰 도면도 긴 변이 10,000px 를 넘지 않게 줄입니다.
- PDF 합치기는 PDF 만 2개 이상 끌어 왔을 때 메뉴 첫 조각에 나옵니다. 선택한 순서대로 합쳐 `첫파일 외 N개.pdf` 를 만듭니다.
  탐색기는 끌기를 시작할 때 잡은 파일을 맨 앞에 두므로, 처음 고른 파일을 잡고 끌어오면 고른 순서 그대로 합쳐집니다. 실제 순서는 기록 파일에 남습니다.
  쪽을 그림으로 바꾸지 않아 글자 선택과 벡터 품질이 그대로입니다. 책갈피(목차)는 옮기지 않습니다. 열 때 암호가 필요한 PDF 는 합칠 수 없습니다.
- PDF 나누기는 원본 옆에 `문서 1쪽.pdf`, `문서 2쪽.pdf` … 를 만듭니다. 여러 PDF 를 끌어 오면 각각 나누고, 1쪽짜리는 건너뜁니다.
  합치기와 같이 쪽을 그대로 옮겨 글자와 품질이 유지됩니다.

### 알림 영역 메뉴 (아이콘 오른쪽 클릭)

| 항목 | 설명 |
|---|---|
| 변환 취소 | 변환하는 동안에만 보입니다. 영상·오디오·PDF 는 바로 멈추고, Office 문서는 지금 바꾸는 파일까지만 마칩니다 |
| Shift 끌기로 변환 켜기 | 끄면 Shift 를 눌러도 메뉴가 뜨지 않습니다 |
| 탐색기·바탕화면에서만 작동 | 기본 켜짐. 끄면 브라우저 등 다른 프로그램에서 끌어 온 파일에도 반응합니다 (글자를 Shift+끌어 선택할 때 메뉴가 같이 뜰 수 있음) |
| Windows 시작 시 실행 | 로그인할 때 자동으로 켭니다 (`HKCU\...\Run`) |
| 기록 폴더 열기 | `%LOCALAPPDATA%\Tangerine\tangerine.log` — 실패한 변환의 이유가 남습니다 |

변환하는 동안 알림 영역 아이콘이 진행된 만큼 시계 방향으로 밝게 차오르고, 마우스를 올리면 `변환 중 37%` 처럼 보입니다.

설정은 `%APPDATA%\Tangerine\settings.json` 에 저장됩니다.

## 설치

[Releases](../../releases) 에서 `Tangerine-v*-win-x64.zip` 을 받아 원하는 폴더에 풀고 `Tangerine.exe` 를 실행합니다.
.NET 설치는 필요 없습니다. 압축 파일 안의 `tools\ffmpeg.exe` 는 영상·오디오 변환에 쓰므로 같이 두세요.

- Windows 10 (2004 이상) / 11, 64비트
- 관리자 권한으로 실행한 프로그램에서 끌어 온 파일은 Windows 보안 정책 때문에 받을 수 없습니다.

## 개발

```powershell
dotnet test tests/Tangerine.Core.Tests     # 형식 목록·파일 이름·ffmpeg 인자 테스트 (맥에서도 실행됨)
.\scripts\fetch-ffmpeg.ps1                  # src\Tangerine\tools\ffmpeg.exe 받기 (처음 한 번)
dotnet run --project src/Tangerine          # 실행
.\scripts\install-local.ps1                 # 이 PC 에 설치: %LOCALAPPDATA%\Programs\Tangerine, 로그인 시 자동 실행, 바탕화면·시작 메뉴 바로가기
```

- .NET 10 SDK 필요. 맥에서도 `dotnet build src/Tangerine/Tangerine.csproj` 로 컴파일까지는 확인할 수 있습니다 (실행은 Windows 에서만).
- `main` 에 올리면 GitHub Actions 가 Windows 에서 테스트·빌드합니다.
- `v0.1.0` 처럼 태그를 올리면 단일 exe + ffmpeg 를 묶은 zip 을 Releases 에 올립니다.

### 구조

| 위치 | 내용 |
|---|---|
| `src/Tangerine.Core/` | Windows 와 무관한 규칙 — 형식 목록(`FormatCatalog`), 결과 파일 이름(`OutputPaths`), ffmpeg 인자(`FfmpegArguments`), 자막 변환(`SubtitleConverter`) |
| `src/Tangerine/Interop/DragWatcher.cs` | 전역 저수준 마우스 훅. 왼쪽 버튼으로 끌어오는 중 Shift 가 눌리면 메뉴를 띄움 |
| `src/Tangerine/Ui/RadialMenuWindow.*` | 귤 단면 모양 원형 메뉴 창 (Liquid Glass 풍 유리). 이 창이 드롭 대상이며, 항상 "복사" 로 답해 탐색기가 원본을 옮기지 않게 함 |
| `src/Tangerine/Ui/TrayIcon.cs` | 알림 영역 아이콘·메뉴·알림 |
| `src/Tangerine/Conversion/` | Magick.NET 이미지 변환, ffmpeg.exe 실행, 한글·Office 문서 → PDF (`DocumentConverter`), PDF → 그림 (`PdfPages`), PDF 합치기·나누기 (`PdfMerger`, `PdfSplitter`), 반디집 압축 (`Archiver`), 동시 변환 관리 |

### 다음에 할 일

원본 Tangerine 에 있는 아래 기능은 아직 없습니다.

- `Ctrl` + 끌기: 파일 도구 — 압축(비율·목표 용량), 자르기, 영상 자르기·나누기, PDF 쪽 골라 빼기, 메타데이터 삭제, QR 읽기
- `Alt` + 끌기: 파일 동작 — 복사, 이동, 경로 복사, 터미널 열기
- 한글·Office 가 없는 PC 를 위한 문서 변환 (LibreOffice 등), EPUB
- 탐색기 오른쪽 클릭 "변환" 메뉴

## 라이선스 참고

- 배포 zip 의 `tools\ffmpeg.exe` 는 [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) 의 GPL 빌드이며 라이선스는 `tools\FFMPEG-LICENSE.txt` 에 있습니다.
- 이미지 변환은 [Magick.NET](https://github.com/dlemstra/Magick.NET) (Apache-2.0) 을 씁니다.
- PDF 합치기·나누기는 [PDFsharp](https://docs.pdfsharp.net/) 6.2.4 (MIT) 를 씁니다. 6.5.0 부터 AGPL-3.0 이라 올리기 전에 라이선스를 확인해야 합니다.
