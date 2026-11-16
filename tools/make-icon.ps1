# Regenerates src\DiskMonitor\Assets\DiskMonitor.ico from the icon drawn in code (IconArt).
# Builds the project, then runs DiskMonitor.exe --make-icon with the asset path.

$ErrorActionPreference = "Stop"

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root "src\DiskMonitor\DiskMonitor.csproj"
$icon = Join-Path $root "src\DiskMonitor\Assets\DiskMonitor.ico"

& dotnet build $project -c Release -nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed"
}

$exe = Join-Path $root "src\DiskMonitor\bin\Release\net10.0-windows\win-x64\DiskMonitor.exe"
$process = Start-Process $exe -ArgumentList @("--make-icon", "`"$icon`"") -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0) {
    throw "DiskMonitor.exe --make-icon failed with exit code $($process.ExitCode)"
}

Write-Host "Wrote $icon" -ForegroundColor Green
Write-Host "Rebuild the project so the executable embeds the new icon."
