<#
.SYNOPSIS
  이 PC 에 탠저린을 설치한다: 단일 exe 로 만들어 내 계정 프로그램 폴더에 두고,
  로그인할 때 자동 실행 + 바탕화면·시작 메뉴 바로가기를 만든 뒤 실행한다.

.DESCRIPTION
  .NET 이 함께 든 실행 파일이라 .NET 을 따로 깔 필요가 없다. 관리자 권한도 필요 없다.
  코드를 고친 뒤 다시 실행하면 켜져 있던 탠저린을 끄고 새 버전으로 바꿔 깐다.

.EXAMPLE
  .\scripts\install-local.ps1
#>
param(
    [string]$Destination = (Join-Path $env:LOCALAPPDATA 'Programs\Tangerine')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot

# SDK 를 내 계정에만 깔아 둔 경우 (dotnet-install.ps1) 그쪽 dotnet 을 쓴다
$userDotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
$dotnet = if (Test-Path $userDotnet) { $userDotnet } else { 'dotnet' }

if (-not (Test-Path (Join-Path $root 'src\Tangerine\tools\ffmpeg.exe'))) {
    & (Join-Path $PSScriptRoot 'fetch-ffmpeg.ps1')
}
if (-not (Test-Path (Join-Path $root 'src\Tangerine\hancom\FilePathCheckerModuleExample.dll'))) {
    & (Join-Path $PSScriptRoot 'fetch-hancom.ps1')
}

# 켜져 있으면 파일이 잠겨 바꿀 수 없다
Get-Process Tangerine -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

& $dotnet publish (Join-Path $root 'src\Tangerine\Tangerine.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $Destination -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "빌드에 실패했습니다 (코드 $LASTEXITCODE)" }

$exe = Join-Path $Destination 'Tangerine.exe'

# 로그인할 때 자동 실행. 알림 영역 메뉴의 "Windows 시작 시 실행" 과 같은 자리라 그 체크로 끌 수 있다.
Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'Tangerine' -Value "`"$exe`""

$shell = New-Object -ComObject WScript.Shell
foreach ($folder in [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs')) {
    $link = $shell.CreateShortcut((Join-Path $folder '탠저린.lnk'))
    $link.TargetPath = $exe
    $link.WorkingDirectory = $Destination
    $link.Description = '끌어서 바로 바꾸는 파일 변환기'
    $link.Save()
}

Start-Process $exe
Write-Host "설치 완료: $exe"
