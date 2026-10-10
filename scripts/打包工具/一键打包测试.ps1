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
$packageName = "MineRailMonitor-v1.0.0-Test-$timestamp"
$packageOutputRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\打包结果'))
$testOutputRoot = Join-Path $packageOutputRoot '测试包'
New-Item -ItemType Directory -Force -Path $testOutputRoot | Out-Null
$stagingRoot = Join-Path (Join-Path $packageOutputRoot '暂存\测试包') "desktop-package-test-$timestamp"
$packageRoot = Join-Path $testOutputRoot $packageName
$zipPath = Join-Path $testOutputRoot "$packageName.zip"
foreach ($newOutputPath in @($stagingRoot, $packageRoot, $zipPath)) {
    if (Test-Path -LiteralPath $newOutputPath) {
        throw "测试打包目标已存在，为避免覆盖请稍后重试：$newOutputPath"
    }
}

$siteProjectSource = Join-Path $repoRoot 'Projects\Default'
if (-not (Test-Path -LiteralPath (Join-Path $siteProjectSource 'project.json') -PathType Leaf)) {
    throw "缺少现场项目配置：$(Join-Path $siteProjectSource 'project.json')"
}
$station620SourcePath = Join-Path $siteProjectSource 'stations\620.json'
if (-not (Test-Path -LiteralPath $station620SourcePath -PathType Leaf)) {
    throw "缺少 620 站场点位配置：$station620SourcePath"
}
$station620Source = Get-Content -LiteralPath $station620SourcePath -Raw -Encoding UTF8 | ConvertFrom-Json
$boundRfidPoints620 = @($station620Source.Devices | Where-Object {
    $_.Type -eq 'RfidStation' -and
    -not [string]::IsNullOrWhiteSpace($_.RfidStationId) -and
    $null -ne $_.CadX -and
    $null -ne $_.CadY
})
$map620SourcePath = Join-Path $siteProjectSource ($station620Source.BackgroundImage -replace '/', '\')
if ($boundRfidPoints620.Count -eq 0) {
    throw "620 站场没有绑定 RFID 点位：$station620SourcePath"
}
if (-not (Test-Path -LiteralPath $map620SourcePath -PathType Leaf)) {
    throw "缺少 620 站场底图：$map620SourcePath"
}

Write-Host "现场项目：$siteProjectSource"
Write-Host "620 站场绑定点位：$($boundRfidPoints620.Count)"

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

$stationConfigDirectory = Join-Path $projectTarget 'stations'
$mapDirectory = Join-Path $projectTarget 'maps'
if (-not (Test-Path -LiteralPath $stationConfigDirectory -PathType Container) -or
    @(Get-ChildItem -LiteralPath $stationConfigDirectory -Filter '*.json' -File -Recurse).Count -eq 0) {
    throw "现场项目没有站点配置：$stationConfigDirectory"
}
if (-not (Test-Path -LiteralPath $mapDirectory -PathType Container) -or
    @(Get-ChildItem -LiteralPath $mapDirectory -File -Recurse).Count -eq 0) {
    throw "现场项目没有地图文件：$mapDirectory"
}
$packaged620Path = Join-Path $stationConfigDirectory '620.json'
$packaged620 = Get-Content -LiteralPath $packaged620Path -Raw -Encoding UTF8 | ConvertFrom-Json
$packaged620MapPath = Join-Path $projectTarget ($packaged620.BackgroundImage -replace '/', '\')
$packaged620PointCount = @($packaged620.Devices | Where-Object {
    $_.Type -eq 'RfidStation' -and
    -not [string]::IsNullOrWhiteSpace($_.RfidStationId) -and
    $null -ne $_.CadX -and
    $null -ne $_.CadY
}).Count
if ($packaged620PointCount -eq 0 -or -not (Test-Path -LiteralPath $packaged620MapPath -PathType Leaf)) {
    throw "打包内容缺少 620 底图或绑定点位：$projectTarget"
}
$databaseFiles = @(Get-ChildItem -LiteralPath $packageRoot -File -Recurse -Force | Where-Object {
    $_.Extension -in @('.db', '.sqlite', '.sqlite3')
})
if ($databaseFiles.Count -gt 0) {
    throw "测试包不应包含数据库或历史记录：$($databaseFiles[0].FullName)"
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

$readme = @"
MineRailMonitor v1.0.0 测试部署包

用途
本目录用于现场测试，不是正式安装包。

启动
双击 App\MineRailMonitor.exe。

配置
现场项目配置位于 Projects\Default：
- Projects\Default\project.json
- Projects\Default\stations
- Projects\Default\maps

数据
- 本包不含数据库和通行/报警历史记录。
- 首次启动后程序会在 Data 目录创建新的运行数据库。

运行目录
- App：程序和运行依赖
- Projects：测试项目配置
- Data：运行数据库目录
- Backups：SQLite 备份目录
- Logs：运行日志目录
- Docs：部署说明

注意
- 本包包含当前 Projects\Default 现场项目、底图、站点配置及绑定点位；启动时使用该项目。
- 620 站场包含 $packaged620PointCount 个绑定 RFID 点位。
- 本包不包含现场日志或 Git 文件。
- 目标电脑需要安装 .NET Framework 4.8。

构建信息
- Configuration: Release
- Source commit: $sourceCommit
- Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')
"@
Set-Content -LiteralPath (Join-Path $packageRoot 'README.txt') -Value $readme -Encoding UTF8

$manifestLines = [System.Collections.Generic.List[string]]::new()
$manifestLines.Add("MineRailMonitor v1.0.0 test package")
$manifestLines.Add("Source commit: $sourceCommit")
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
Write-Host "Output directory: $testOutputRoot"
Write-Host "Package directory: $packageRoot"
Write-Host "Package: $zipPath"
Write-Host "Size: $($zipInfo.Length) bytes ($zipSizeMb MB)"
Write-Host "SHA256: $zipHash"
