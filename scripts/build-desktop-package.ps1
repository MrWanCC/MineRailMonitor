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

git -C $repoRoot diff --quiet -- "Projects/Example"
if ($LASTEXITCODE -ne 0) {
    throw "Tracked Projects/Example files contain uncommitted changes. Commit and review them before building a release package."
}

git -C $repoRoot diff --cached --quiet -- "Projects/Example"
if ($LASTEXITCODE -ne 0) {
    throw "Tracked Projects/Example files contain uncommitted changes. Commit and review them before building a release package."
}

$trackedExampleFiles = @(git -C $repoRoot ls-files -- "Projects/Example")
if ($LASTEXITCODE -ne 0) {
    throw "Failed to enumerate tracked Example project files."
}
if ($trackedExampleFiles.Count -eq 0) {
    throw "No tracked Example project files were found."
}
if ($trackedExampleFiles -notcontains "Projects/Example/project.json") {
    throw "Missing tracked sanitized Example project: Projects/Example/project.json"
}

foreach ($trackedPath in $trackedExampleFiles) {
    if (-not $trackedPath.StartsWith("Projects/Example/", [StringComparison]::Ordinal)) {
        throw "Unexpected tracked Example path: $trackedPath"
    }

    $relativeExamplePath = $trackedPath.Substring("Projects/Example/".Length).Replace("/", "\")
    $source = Join-Path $repoRoot ($trackedPath.Replace("/", "\"))
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Tracked Example source file is missing: $source"
    }

    $target = Join-Path $exampleTarget $relativeExamplePath
    $targetDirectory = Split-Path -Parent $target
    New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
}

Write-Host "Desktop package created at $stagingRoot"
