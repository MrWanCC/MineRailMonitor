[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][scriptblock]$Command
    )

    Write-Host "[$Label]"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Label 失败，退出码：$LASTEXITCODE。"
    }
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    return $Path.Substring($Root.Length + 1).Replace('\', '/')
}

function Get-Sha256Hex {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha256.ComputeHash([IO.File]::ReadAllBytes($Path))
        return ([BitConverter]::ToString($bytes)).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Set-Location -LiteralPath $repoRoot

if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'MineRailMonitor.sln') -PathType Leaf)) {
    throw "当前目录不是 MineRailMonitor 仓库根目录：$repoRoot"
}

$dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    throw '未找到 dotnet.exe，请先安装 .NET SDK。'
}

$buildScript = Join-Path $PSScriptRoot 'build-desktop-package.ps1'
$verifyScript = Join-Path $PSScriptRoot 'verify-desktop-package.ps1'
if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
    throw "缺少现有桌面打包脚本：$buildScript"
}
if (-not (Test-Path -LiteralPath $verifyScript -PathType Leaf)) {
    throw "缺少现有桌面包验证脚本：$verifyScript"
}

$configuration = 'Release'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$packageName = "MineRailMonitor-v1.0.0-Production-$timestamp"
$packageOutputRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\打包结果'))
$productionRoot = Join-Path $packageOutputRoot '正式包'
New-Item -ItemType Directory -Force -Path $productionRoot | Out-Null
$stagingRoot = Join-Path (Join-Path $packageOutputRoot '暂存\正式包') "desktop-package-production-$timestamp"
$packageRoot = Join-Path $productionRoot $packageName
$zipPath = Join-Path $productionRoot "$packageName.zip"

foreach ($newOutputPath in @($stagingRoot, $packageRoot, $zipPath)) {
    if (Test-Path -LiteralPath $newOutputPath) {
        throw "正式打包目标已存在，为避免覆盖请稍后重试：$newOutputPath"
    }
}

$siteProjectSource = Join-Path $repoRoot 'Projects\Default'
function Assert-ProjectAssets {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $projectRootFull = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\') + '\'
    $manifestPath = Join-Path $ProjectRoot 'project.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "$Context 缺少项目配置：$manifestPath"
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $stationReferences = @($manifest.Stations)
    if ($stationReferences.Count -eq 0) {
        throw "$Context 没有站场配置引用：$manifestPath"
    }

    $stationCount = 0
    $boundPointCount = 0
    foreach ($reference in $stationReferences) {
        if ([string]::IsNullOrWhiteSpace($reference.ConfigFile)) {
            throw "$Context 存在空站场配置路径：$manifestPath"
        }

        $relativeConfigPath = $reference.ConfigFile.Replace('/', '\')
        $configPath = [IO.Path]::GetFullPath((Join-Path $ProjectRoot $relativeConfigPath))
        if (-not $configPath.StartsWith($projectRootFull, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Context 站场配置路径越出项目目录：$($reference.ConfigFile)"
        }
        if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
            throw "$Context 缺少站场配置：$configPath"
        }

        $station = Get-Content -LiteralPath $configPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([string]::IsNullOrWhiteSpace($station.BackgroundImage)) {
            throw "$Context 站场未配置底图：$configPath"
        }

        $relativeMapPath = $station.BackgroundImage.Replace('/', '\')
        $mapPath = [IO.Path]::GetFullPath((Join-Path $ProjectRoot $relativeMapPath))
        if (-not $mapPath.StartsWith($projectRootFull, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Context 底图路径越出项目目录：$($station.BackgroundImage)"
        }
        if (-not (Test-Path -LiteralPath $mapPath -PathType Leaf)) {
            throw "$Context 缺少站场底图：$mapPath"
        }

        $stationCount++
        $boundPointCount += @($station.Devices | Where-Object {
            $_.Type -eq 'RfidStation' -and
            -not [string]::IsNullOrWhiteSpace($_.RfidStationId) -and
            $null -ne $_.CadX -and
            $null -ne $_.CadY
        }).Count
    }

    return [pscustomobject]@{
        StationCount = $stationCount
        BoundPointCount = $boundPointCount
    }
}

$sourceProjectAssets = Assert-ProjectAssets -ProjectRoot $siteProjectSource -Context '现场项目'
Write-Host "现场项目：$siteProjectSource"
Write-Host "站场配置：$($sourceProjectAssets.StationCount) 个；绑定 RFID 点位：$($sourceProjectAssets.BoundPointCount) 个"

Invoke-CheckedCommand -Label 'Release Build' -Command {
    & $dotnet.Source build (Join-Path $repoRoot 'MineRailMonitor.sln') `
        -c $configuration --no-restore --nologo
}

Invoke-CheckedCommand -Label 'Core Tests' -Command {
    & $dotnet.Source test (Join-Path $repoRoot 'tests\MineRailMonitor.Core.Tests\MineRailMonitor.Core.Tests.csproj') `
        -c $configuration --no-build --no-restore --nologo
}

Invoke-CheckedCommand -Label 'Infrastructure Tests' -Command {
    & $dotnet.Source test (Join-Path $repoRoot 'tests\MineRailMonitor.Infrastructure.Tests\MineRailMonitor.Infrastructure.Tests.csproj') `
        -c $configuration --no-build --no-restore --nologo
}

Write-Host '[Desktop Package] 调用现有打包脚本'
& $buildScript -Configuration $configuration -OutputRoot $stagingRoot
if ($LASTEXITCODE -ne 0) {
    throw "桌面打包脚本失败，退出码：$LASTEXITCODE。"
}

Write-Host '[Desktop Package] 调用现有验证脚本'
& $verifyScript -StagingRoot $stagingRoot
if ($LASTEXITCODE -ne 0) {
    throw "桌面包验证脚本失败，退出码：$LASTEXITCODE。"
}

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
Get-ChildItem -LiteralPath $stagingRoot -Force |
    Where-Object { $_.Name -ne 'Projects' } |
    Copy-Item -Destination $packageRoot -Recurse -Force

$projectTarget = Join-Path $packageRoot 'Projects\Default'
New-Item -ItemType Directory -Force -Path $projectTarget | Out-Null
Get-ChildItem -LiteralPath $siteProjectSource -Force |
    Copy-Item -Destination $projectTarget -Recurse -Force

$projectDirectories = @(Get-ChildItem -LiteralPath (Join-Path $packageRoot 'Projects') -Directory -Force)
if ($projectDirectories.Count -ne 1 -or $projectDirectories[0].Name -cne 'Default') {
    throw '正式包必须只包含 Projects\Default 现场项目，不能混入 Example 或其它项目。'
}

foreach ($runtimeDirectoryName in @('Data', 'Backups', 'Logs', 'artifacts', 'TestResults')) {
    $runtimeDirectory = Join-Path $projectTarget $runtimeDirectoryName
    if (Test-Path -LiteralPath $runtimeDirectory) {
        throw "现场项目目录中混入运行或测试数据：$runtimeDirectory"
    }
}

$sourceProjectFiles = @(Get-ChildItem -LiteralPath $siteProjectSource -File -Recurse -Force)
$packagedProjectFiles = @(Get-ChildItem -LiteralPath $projectTarget -File -Recurse -Force)
if ($sourceProjectFiles.Count -ne $packagedProjectFiles.Count) {
    throw "现场项目文件未完整复制：source=$($sourceProjectFiles.Count), package=$($packagedProjectFiles.Count)"
}
$sourceProjectRelativeFiles = @($sourceProjectFiles | ForEach-Object {
    $_.FullName.Substring($siteProjectSource.Length + 1).Replace('\', '/')
} | Sort-Object)
$packagedProjectRelativeFiles = @($packagedProjectFiles | ForEach-Object {
    $_.FullName.Substring($projectTarget.Length + 1).Replace('\', '/')
} | Sort-Object)
if (Compare-Object -ReferenceObject $sourceProjectRelativeFiles -DifferenceObject $packagedProjectRelativeFiles) {
    throw '打包后的现场项目文件清单与源目录不一致。'
}

$packagedProjectAssets = Assert-ProjectAssets -ProjectRoot $projectTarget -Context '打包内容'
if ($packagedProjectAssets.StationCount -ne $sourceProjectAssets.StationCount -or
    $packagedProjectAssets.BoundPointCount -ne $sourceProjectAssets.BoundPointCount) {
    throw '打包后的站场或 RFID 绑定点位数量与现场项目源不一致。'
}
$databaseFiles = @(Get-ChildItem -LiteralPath $packageRoot -File -Recurse -Force | Where-Object {
    $_.Extension -in @('.db', '.sqlite', '.sqlite3', '.log', '.pdb', '.env', '.token') -or
    $_.Name -like '*.db-shm' -or
    $_.Name -like '*.db-wal' -or
    $_.Name -like '*.secrets.json'
})
if ($databaseFiles.Count -gt 0) {
    throw "正式包检测到数据库、运行日志、调试符号或敏感本地文件：$($databaseFiles[0].FullName)"
}

foreach ($directory in @('Data', 'Backups\SQLite', 'Logs\BlackBox', 'Docs')) {
    $directoryPath = Join-Path $packageRoot $directory
    New-Item -ItemType Directory -Force -Path $directoryPath | Out-Null
    Set-Content -LiteralPath (Join-Path $directoryPath '.keep') -Value 'Directory reserved for runtime data.' -Encoding UTF8
}

$sourceCommit = (& git -C $repoRoot rev-parse --short HEAD 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommit)) {
    $sourceCommit = 'unknown'
}
$trackedChanges = @(& git -C $repoRoot status --porcelain --untracked-files=no 2>$null)
if ($LASTEXITCODE -ne 0) {
    throw '无法读取 Git 工作区状态，不能生成正式包来源信息。'
}
$sourceTreeState = if ($trackedChanges.Count -eq 0) { 'clean' } else { 'modified' }

$readme = @"
MineRailMonitor v1.0.0 正式现场部署 ZIP

用途
本 ZIP 用于部署 MineRailMonitor 正式程序和当前现场项目配置，不包含安装器。

启动
双击 App\MineRailMonitor.exe。

配置
本包包含当前现场项目配置及地图，位于 Projects\Default：
- Projects\Default\project.json
- Projects\Default\stations
- Projects\Default\maps

数据
- 本包不含数据库、通行/报警历史记录、运行日志或备份。
- 首次启动后程序会在 Data 目录创建新的运行数据库。

运行目录
- App：程序和运行依赖
- Projects：现场项目配置
- Data：运行数据库目录
- Backups：SQLite 备份目录
- Logs：运行日志目录
- Docs：部署说明

注意
- 本包包含现场项目地址、站场配置、底图和点位绑定，请按现场资料管理要求保管。
- 当前包含 $($packagedProjectAssets.StationCount) 个站场和 $($packagedProjectAssets.BoundPointCount) 个绑定 RFID 点位。
- 已有现场升级时不要直接解压覆盖整个目录；先备份 Projects\Default、Data 和 Backups，通常只更新 App。
- 只有确认要更新现场配置时，才单独替换 Projects\Default，避免覆盖现场调整。
- 本包不包含 Git 文件、运行数据或现场运行日志。
- 目标电脑需要安装 .NET Framework 4.8。

构建信息
- Configuration: Release
- Source commit: $sourceCommit
- Tracked source tree: $sourceTreeState
- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
"@
Set-Content -LiteralPath (Join-Path $packageRoot 'README.txt') -Value $readme -Encoding UTF8

$manifestLines = [System.Collections.Generic.List[string]]::new()
$manifestLines.Add("MineRailMonitor v1.0.0 production field package (ZIP only)")
$manifestLines.Add("Source commit: $sourceCommit")
$manifestLines.Add("Tracked source tree: $sourceTreeState")
$manifestLines.Add("Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')")
$manifestLines.Add('')
$manifestLines.Add('SHA256  RelativePath')

$packageFiles = Get-ChildItem -LiteralPath $packageRoot -File -Recurse | Sort-Object FullName
foreach ($file in $packageFiles) {
    $relativePath = Get-RelativePath -Root $packageRoot -Path $file.FullName
    $hash = Get-Sha256Hex -Path $file.FullName
    $manifestLines.Add("$hash  $relativePath")
}
Set-Content -LiteralPath (Join-Path $packageRoot 'CHECKSUMS.txt') -Value $manifestLines -Encoding UTF8

Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    throw "压缩包未生成：$zipPath"
}

$zipInfo = Get-Item -LiteralPath $zipPath
$zipHash = Get-Sha256Hex -Path $zipPath
$zipSizeMb = [math]::Round($zipInfo.Length / 1MB, 2)

Write-Host ''
Write-Host 'Build Success' -ForegroundColor Green
Write-Host ''
Write-Host "Output directory: $productionRoot"
Write-Host "Package directory: $packageRoot"
Write-Host "Package: $zipPath"
Write-Host "Size: $($zipInfo.Length) bytes ($zipSizeMb MB)"
Write-Host "SHA256: $zipHash"
Write-Host "Source commit: $sourceCommit ($sourceTreeState tracked source tree)"
