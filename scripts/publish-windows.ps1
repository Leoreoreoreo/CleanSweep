#!/usr/bin/env pwsh
# Publish a self-contained, single-file Windows (x64) build of CleanSweep.
# Output: publish/win-x64/CleanSweep.exe  (no .NET install required to run)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src/CleanSweep/CleanSweep.csproj"
$out  = Join-Path $root "publish/win-x64"

Write-Host "Publishing CleanSweep for win-x64 (self-contained, single file)..." -ForegroundColor Cyan

dotnet publish $proj -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $out

# Drop stray native debug symbols so the single .exe is the only artifact.
Get-ChildItem $out -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host ""
Write-Host "Done. Run: $out\CleanSweep.exe" -ForegroundColor Green
