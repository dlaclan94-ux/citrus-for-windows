<#
.SYNOPSIS
  영상·오디오 변환에 쓰는 ffmpeg.exe 를 받아 둔다.

.DESCRIPTION
  BtbN/FFmpeg-Builds 의 Windows 64비트 GPL 빌드(정식 릴리스 줄기)에서 ffmpeg.exe 와 라이선스만 꺼낸다.
  기본 위치는 src\Citrus\tools 이며, 빌드하면 실행 파일 옆 tools\ 로 복사된다.

.EXAMPLE
  .\scripts\fetch-ffmpeg.ps1
  .\scripts\fetch-ffmpeg.ps1 -Destination out\Citrus\tools
#>
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\Citrus\tools'),
    [string]$Version = '8.1'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$name = "ffmpeg-n$Version-latest-win64-gpl-$Version"
$url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/$name.zip"
$work = Join-Path ([IO.Path]::GetTempPath()) ("citrus-ffmpeg-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $work | Out-Null

try {
    Write-Host "내려받는 중: $url"
    Invoke-WebRequest -Uri $url -OutFile (Join-Path $work 'ffmpeg.zip')
    Expand-Archive -Path (Join-Path $work 'ffmpeg.zip') -DestinationPath $work

    $exe = Get-ChildItem -Path $work -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
    if (-not $exe) { throw "압축 파일에서 ffmpeg.exe 를 찾지 못했습니다." }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item $exe.FullName -Destination $Destination -Force

    $license = Get-ChildItem -Path $work -Recurse -Filter 'LICENSE*' | Select-Object -First 1
    if ($license) { Copy-Item $license.FullName -Destination (Join-Path $Destination 'FFMPEG-LICENSE.txt') -Force }

    & (Join-Path $Destination 'ffmpeg.exe') -hide_banner -version | Select-Object -First 1
    Write-Host "완료: $(Resolve-Path $Destination)"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
