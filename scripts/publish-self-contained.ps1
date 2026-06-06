$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "AppTimeTracker\AppTimeTracker.csproj"
$output = Join-Path $root "release\self-contained"

Write-Host "Cleaning project..."
dotnet clean $project -c Release

if (-not (Test-Path $output)) {
    New-Item -ItemType Directory -Path $output | Out-Null
}

Write-Host "Publishing self-contained single-file build..."
dotnet publish $project -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o $output

$exePath = Join-Path $output "AppTimeTracker.exe"
Write-Host ""
Write-Host "Publish completed."
Write-Host "Executable: $exePath"
