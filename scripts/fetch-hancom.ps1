<#
.SYNOPSIS
  한글 자동화 보안 승인 모듈(FilePathCheckerModuleExample.dll)을 한컴 공개 저장소에서 받아 둔다.

.DESCRIPTION
  hancom-io/devcenter-archive 의 hwp-automation/보안모듈(Automation).zip 에서 DLL 만 꺼낸다.
  저장소에 바이너리를 두지 않으려고 빌드할 때 받는다. 커밋과 해시를 고정해 같은 파일만 받는다.
  기본 위치는 src\Tangerine\hancom 이며, 빌드하면 실행 파일 옆 hancom\ 으로 복사된다.

.EXAMPLE
  .\scripts\fetch-hancom.ps1
#>
param([string]$Destination = (Join-Path $PSScriptRoot '..\src\Tangerine\hancom'))

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$commit = 'ec0c158c69195070dabcb086e11dd197c336bf8e'
$sha256 = '9AC5B97C47AC8AED1E8BCA27A3EEF39411361D8F68C262509F0C40A8F9D21BB6'
$url = "https://raw.githubusercontent.com/hancom-io/devcenter-archive/$commit/hwp-automation/" + [Uri]::EscapeDataString('보안모듈(Automation).zip')
$work = Join-Path ([IO.Path]::GetTempPath()) ("tangerine-hancom-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $work | Out-Null

try {
    Write-Host "내려받는 중: $url"
    Invoke-WebRequest -Uri $url -OutFile (Join-Path $work 'module.zip')
    Expand-Archive -Path (Join-Path $work 'module.zip') -DestinationPath $work

    $dll = Join-Path $work 'FilePathCheckerModuleExample.dll'
    if (-not (Test-Path $dll)) { throw '압축 파일에서 FilePathCheckerModuleExample.dll 을 찾지 못했습니다.' }
    if ((Get-FileHash $dll).Hash -ne $sha256) { throw '받은 DLL 의 해시가 예상과 다릅니다.' }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item $dll -Destination $Destination -Force
    Write-Host "완료: $(Resolve-Path $Destination)"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
