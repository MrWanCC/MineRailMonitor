param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$OutputRoot = (Join-Path $PSScriptRoot "..\artifacts\desktop-package")
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$stagingRoot = [IO.Path]::GetFullPath($OutputRoot)
$appDirectory = Join-Path $stagingRoot "App"
$projectsDirectory = Join-Path $stagingRoot "Projects"

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path @(
    $appDirectory,
    $projectsDirectory,
    (Join-Path $stagingRoot "Data"),
    (Join-Path $stagingRoot "Backups\SQLite"),
    (Join-Path $stagingRoot "Logs\BlackBox"),
    (Join-Path $stagingRoot "Docs")
) | Out-Null

dotnet publish (Join-Path $repoRoot "src\MineRailMonitor\MineRailMonitor.csproj") `
    -c $Configuration -o $appDirectory --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$publishedProjects = Join-Path $appDirectory "Projects"
if (Test-Path $publishedProjects) {
    throw "Publish output must not contain App\Projects: $publishedProjects"
}

$managedSqlite = Join-Path $appDirectory "System.Data.SQLite.dll"
if (-not (Test-Path -LiteralPath $managedSqlite)) {
    throw "Missing managed SQLite runtime: $managedSqlite"
}

$nativeBuildRoot = Join-Path $repoRoot "src\MineRailMonitor\bin\$Configuration\net48"
$nativeFiles = @{
    x86 = Join-Path $nativeBuildRoot "x86\SQLite.Interop.dll"
    x64 = Join-Path $nativeBuildRoot "x64\SQLite.Interop.dll"
}
foreach ($architecture in $nativeFiles.Keys) {
    $nativeSource = $nativeFiles[$architecture]
    if (-not (Test-Path -LiteralPath $nativeSource)) {
        throw "Missing System.Data.SQLite native runtime: $nativeSource"
    }

    $nativeTargetDirectory = Join-Path $appDirectory $architecture
    New-Item -ItemType Directory -Force -Path $nativeTargetDirectory | Out-Null
    Copy-Item -LiteralPath $nativeSource -Destination (Join-Path $nativeTargetDirectory "SQLite.Interop.dll") -Force
}

$exampleSource = Join-Path $repoRoot "Projects\Example"
$exampleTarget = Join-Path $projectsDirectory "Example"
if (-not (Test-Path -LiteralPath (Join-Path $exampleSource "project.json"))) {
    throw "Missing tracked sanitized Example project: $exampleSource"
}
New-Item -ItemType Directory -Force -Path $exampleTarget | Out-Null
Copy-Item -Path (Join-Path $exampleSource "*") -Destination $exampleTarget -Recurse -Force

Write-Host "Desktop package created at $stagingRoot"
