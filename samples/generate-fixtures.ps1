# Generates sample fixture files: sample.evtx, sample.xml, sample.etl
# Run from project root: pwsh samples\generate-fixtures.ps1
$ErrorActionPreference = "Stop"
$root = Join-Path $PSScriptRoot "."
$evtxPath = Join-Path $root "sample.evtx"
$xmlPath  = Join-Path $root "sample.xml"
$etlPath  = Join-Path $root "sample.etl"

Write-Host "Generating sample.evtx (errors+warnings from Application log)..."
# Limit to error+warning events so the file stays small and is universally available
$query = '*[System[(Level=2 or Level=3)]]'
& wevtutil epl Application $evtxPath /ow:true "/q:$query"
if (Test-Path $evtxPath) {
    $size = (Get-Item $evtxPath).Length
    Write-Host "  → sample.evtx ($size bytes)"
} else {
    Write-Warning "  Failed to create sample.evtx"
}

Write-Host "Generating sample.xml (max 100 events as XML)..."
# wevtutil qe writes one <Event> per line; wrap in a root <Events> so it parses
$header = '<?xml version="1.0" encoding="utf-8"?>' + [Environment]::NewLine + '<Events>'
$footer = [Environment]::NewLine + '</Events>'
$body = & wevtutil qe Application "/q:$query" /f:xml /c:100 /rd:true
Set-Content -Path $xmlPath -Value ($header + [Environment]::NewLine + ($body -join [Environment]::NewLine) + $footer) -Encoding UTF8
if (Test-Path $xmlPath) {
    $size = (Get-Item $xmlPath).Length
    Write-Host "  → sample.xml ($size bytes)"
}

Write-Host "Generating sample.etl (brief Kernel Process trace)..."
# Use logman to capture ~3 seconds of Microsoft-Windows-Kernel-Process events.
# This produces a real Event Tracing for Windows (ETL) file.
$session = "SimpleEventViewerSample"
try { & logman stop $session -ets 2>$null | Out-Null } catch { }
try { Remove-Item $etlPath -ErrorAction SilentlyContinue } catch { }

& logman create trace $session -p "Microsoft-Windows-Kernel-Process" 0xffffffffffffffff 0xff `
    -o $etlPath -ow -ets | Out-Null

Start-Sleep -Seconds 3

& logman stop $session -ets | Out-Null

if (Test-Path $etlPath) {
    $size = (Get-Item $etlPath).Length
    Write-Host "  → sample.etl ($size bytes)"
} else {
    Write-Warning "  Failed to create sample.etl (logman may need admin rights)"
}

Write-Host ""
Write-Host "Done. Fixture files are in: $root"
