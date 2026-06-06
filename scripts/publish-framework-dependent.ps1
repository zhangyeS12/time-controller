$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "AppTimeTracker\AppTimeTracker.csproj"
$output = Join-Path $root "release\framework-dependent"

Write-Host "Cleaning project..."
dotnet clean $project -c Release

if (-not (Test-Path $output)) {
    New-Item -ItemType Directory -Path $output | Out-Null
}

Write-Host "Publishing framework-dependent build..."
dotnet publish $project -c Release -r win-x64 --self-contained false -o $output

$exePath = Join-Path $output "AppTimeTracker.exe"
Write-Host ""
Write-Host "Publish completed."
Write-Host "Executable: $exePath"
