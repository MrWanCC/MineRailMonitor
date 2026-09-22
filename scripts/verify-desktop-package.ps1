[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$StagingRoot
)

$ErrorActionPreference = 'Stop'

$root = [IO.Path]::GetFullPath($StagingRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Desktop package root does not exist: $root"
}

$requiredFiles = @(
    (Join-Path $root 'App\MineRailMonitor.exe'),
    (Join-Path $root 'App\MineRailMonitor.exe.config'),
    (Join-Path $root 'App\System.Data.SQLite.dll'),
    (Join-Path $root 'App\x86\SQLite.Interop.dll'),
    (Join-Path $root 'App\x64\SQLite.Interop.dll'),
    (Join-Path $root 'Projects\Example\project.json')
)

$requiredDirectories = @(
    (Join-Path $root 'Projects'),
    (Join-Path $root 'Data'),
    (Join-Path $root 'Backups'),
    (Join-Path $root 'Backups\SQLite'),
    (Join-Path $root 'Logs'),
    (Join-Path $root 'Logs\BlackBox'),
    (Join-Path $root 'Docs')
)

foreach ($path in $requiredFiles) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing desktop package file: $path"
    }
}

foreach ($path in $requiredDirectories) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        throw "Missing desktop package directory: $path"
    }
}

$nestedProjects = Join-Path $root 'App\Projects'
if (Test-Path -LiteralPath $nestedProjects) {
    throw "Projects must be a Root sibling, not App\Projects: $nestedProjects"
}

$defaultProject = Join-Path $root 'Projects\Default'
if (Test-Path -LiteralPath $defaultProject) {
    throw "Clean staging must not contain Projects\Default: $defaultProject"
}

$projectsRoot = Join-Path $root 'Projects'
$exampleRoot = Join-Path $projectsRoot 'Example'
$projectDirectories = @(Get-ChildItem -LiteralPath $projectsRoot -Directory -Force)
if ($projectDirectories.Count -ne 1 -or $projectDirectories[0].Name -cne 'Example') {
    throw 'Clean staging must contain exactly one project directory named Example'
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$trackedProjectFiles = @(& git -C $repoRoot ls-files -- 'Projects/Example')
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to read tracked Projects/Example files from Git'
}

$trackedProjectFiles = @($trackedProjectFiles | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($trackedProjectFiles.Count -eq 0) {
    throw 'Git contains no tracked Projects/Example files'
}

$trackedSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($trackedFile in $trackedProjectFiles) {
    if (-not $trackedFile.StartsWith('Projects/Example/', [StringComparison]::Ordinal)) {
        throw "Unexpected tracked project path: $trackedFile"
    }

    [void]$trackedSet.Add($trackedFile)
    $relativePath = $trackedFile.Substring('Projects/Example/'.Length).Replace('/', '\')
    $stagedPath = Join-Path $exampleRoot $relativePath
    if (-not (Test-Path -LiteralPath $stagedPath -PathType Leaf)) {
        throw "Tracked Example file is missing from staging: $trackedFile"
    }
}

$stagedProjectFiles = @(Get-ChildItem -LiteralPath $exampleRoot -File -Recurse -Force)
foreach ($stagedFile in $stagedProjectFiles) {
    $relativePath = $stagedFile.FullName.Substring($exampleRoot.Length + 1).Replace('\', '/')
    $repoRelativePath = "Projects/Example/$relativePath"
    if (-not $trackedSet.Contains($repoRelativePath)) {
        throw "Staging contains an untracked or ignored Example file: $repoRelativePath"
    }
}

if ($stagedProjectFiles.Count -ne $trackedSet.Count) {
    throw "Staged Example file count does not match tracked file count: staged=$($stagedProjectFiles.Count), tracked=$($trackedSet.Count)"
}

Write-Host "Desktop package layout verified: $root"
Write-Host "Tracked Example files verified: $($trackedSet.Count)"
