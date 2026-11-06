<#
.SYNOPSIS
  docs\guide.html 로 설명서 PDF(docs\citrus-guide.pdf, A4)를 만든다. Microsoft Edge 가 필요하다.

.EXAMPLE
  .\scripts\build-guide.ps1
#>
$ErrorActionPreference = 'Stop'
$docs = Join-Path (Split-Path $PSScriptRoot) 'docs'
$edge = @(
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $edge) { throw 'Microsoft Edge 를 찾지 못했습니다' }

$pdf = Join-Path $docs 'citrus-guide.pdf'
$profile = Join-Path ([IO.Path]::GetTempPath()) ('citrus-edge-' + [guid]::NewGuid())
try {
    # 글꼴(Google Fonts)을 받을 틈을 주고 인쇄한다
    & $edge --headless=new --disable-gpu --no-first-run --user-data-dir="$profile" --no-pdf-header-footer `
        --virtual-time-budget=10000 --print-to-pdf="$pdf" ([Uri](Join-Path $docs 'guide.html')).AbsoluteUri 2>$null | Out-Null
    Start-Sleep 2
    if (-not (Test-Path $pdf)) { throw 'PDF 를 만들지 못했습니다' }
    Write-Host "만듦: $pdf ($((Get-Item $pdf).Length) bytes)"
}
finally {
    Remove-Item -Recurse -Force $profile -ErrorAction SilentlyContinue
}
