# Runs the real-desktop tests against the published build. Takes about 5 minutes.
#
# DON'T USE THE PC WHILE THIS RUNS. These tests move the real mouse, open menus and windows,
# and take focus, so anything you type or click can land in the wrong place and fail a test.
# Your quotes and settings are safe: the tests use throwaway data folders.
#
#   powershell -ExecutionPolicy Bypass -File tests\desktop\run-all.ps1

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$exe = Join-Path $here "..\..\publish\WindowsQuotes.exe" | Resolve-Path -ErrorAction SilentlyContinue
if (-not $exe) {
    throw "Publish first: dotnet publish src/WindowsQuotes -c Release -o publish"
}

Write-Host "This takes over the mouse and focus for about 5 minutes. Don't use the PC until it finishes."
Write-Host "It closes Windows Quotes first, including the manager, then restarts the widget at the end."
Read-Host "Press Enter to start, or Ctrl+C to cancel" | Out-Null

$root = Join-Path ([IO.Path]::GetTempPath()) "WindowsQuotesDesktopTests"
if (Test-Path $root) { Remove-Item $root -Recurse -Force }
New-Item -ItemType Directory $root | Out-Null
$results = Join-Path $root "results.txt"

$wasRunning = @(Get-Process WindowsQuotes -ErrorAction SilentlyContinue)
$wasRunning | Stop-Process -Force
Start-Sleep -Seconds 1

try {
    foreach ($test in "e2e", "mouse", "placement") {
        "=== $test" | Tee-Object -FilePath $results -Append
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $here "$test.ps1") -Exe $exe.Path -Root $root 2>&1 |
            Tee-Object -FilePath $results -Append
    }
}
finally {
    Get-Process WindowsQuotes -ErrorAction SilentlyContinue | Stop-Process -Force
    if ($wasRunning.Count -gt 0) {
        Start-Process -FilePath $exe.Path
    }
}

Write-Host ""
Write-Host "Summary (placement passes if the bottom edge number is zero or negative):"
Select-String -Path $results -Pattern '^FAIL|^RESULT|bottom edge' | ForEach-Object { Write-Host "  $($_.Line)" }
Write-Host "Full results: $results"
