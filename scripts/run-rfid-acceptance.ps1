[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$KeepSuccessfulArtifacts,
    [string[]]$Scenario
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$mainBinaryDirectory = Join-Path $repoRoot "src\MineRailMonitor\bin\$Configuration\net48"
$simulatorBinaryDirectory = Join-Path $repoRoot "src\MineRailMonitor.Simulator\bin\$Configuration\net48"
$mainExecutable = Join-Path $mainBinaryDirectory 'MineRailMonitor.exe'
$simulatorExecutable = Join-Path $simulatorBinaryDirectory 'MineRailMonitor.Simulator.exe'
$sqliteAssembly = Join-Path $mainBinaryDirectory 'System.Data.SQLite.dll'
$runId = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$runRoot = Join-Path $repoRoot "artifacts\acceptance\$runId"
$reportPath = Join-Path $runRoot 'phase33a-result.json'
$acceptanceRunWatch = [System.Diagnostics.Stopwatch]::StartNew()
$simulatorPort560 = 62101
$simulatorPort620 = 62111
$upperPort560 = 62102
$upperPort620 = 62112
$scenarioNames = @(
    'Normal11',
    'Uncoupling10',
    'TwoConsecutiveTrains',
    'TwoStationsConcurrent',
    'ClearReappearingTags',
    'SparseSlots',
    'MultipleHeads',
    'NoHead'
)

function Write-AtomicText {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $directory = [System.IO.Path]::GetDirectoryName($fullPath)
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporaryPath = Join-Path $directory ('.' + [System.IO.Path]::GetFileName($fullPath) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
    $backupPath = $fullPath + '.' + [guid]::NewGuid().ToString('N') + '.bak'
    try {
        $encoding = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($temporaryPath, $Content, $encoding)
        if ([System.IO.File]::Exists($fullPath)) {
            [System.IO.File]::Replace($temporaryPath, $fullPath, $backupPath, $true)
            if ([System.IO.File]::Exists($backupPath)) {
                [System.IO.File]::Delete($backupPath)
            }
        }
        else {
            [System.IO.File]::Move($temporaryPath, $fullPath)
        }
    }
    finally {
        if ([System.IO.File]::Exists($temporaryPath)) {
            [System.IO.File]::Delete($temporaryPath)
        }
    }
}

function Write-AtomicJson {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )

    Write-AtomicText -Path $Path -Content ($Value | ConvertTo-Json -Depth 30)
}

function Quote-ProcessArgument {
    param([Parameter(Mandatory = $true)][string]$Value)
    return '"' + ($Value -replace '"', '\"') + '"'
}

function Start-AcceptanceProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$StandardOutput,
        [Parameter(Mandatory = $true)][string]$StandardError
    )

    $argumentText = ($Arguments | ForEach-Object { Quote-ProcessArgument $_ }) -join ' '
    return Start-Process -FilePath $FilePath `
        -ArgumentList $argumentText `
        -WorkingDirectory $WorkingDirectory `
        -WindowStyle Hidden `
        -RedirectStandardOutput $StandardOutput `
        -RedirectStandardError $StandardError `
        -PassThru
}

function Wait-ForFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not [System.IO.File]::Exists($Path)) {
        if ($null -ne $Process -and $Process.HasExited) {
            throw "$Description 进程已退出，ExitCode=$($Process.ExitCode)。"
        }
        if ([DateTime]::UtcNow -ge $deadline) {
            throw "等待 $Description 超时：$Path"
        }
        Start-Sleep -Milliseconds 100
    }
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        if ($null -ne $Process -and $Process.HasExited -and -not [System.IO.File]::Exists($Path)) {
            throw "$Description 进程已退出，且未生成 $Path，ExitCode=$($Process.ExitCode)。"
        }
        if ([System.IO.File]::Exists($Path)) {
            try {
                return (Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json)
            }
            catch {
                if ([DateTime]::UtcNow -ge $deadline) {
                    throw "读取 $Description JSON 超时：$Path；$($_.Exception.Message)"
                }
            }
        }
        if ([DateTime]::UtcNow -ge $deadline) {
            throw "等待 $Description JSON 超时：$Path"
        }
        Start-Sleep -Milliseconds 100
    }
}

function Wait-ForProcessExit {
    param(
        [System.Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if ($null -eq $Process) {
        return $true
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $Process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 100
    }
    if (-not $Process.HasExited) {
        return $false
    }
    return $true
}

function Stop-UpperProcess {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$StopFile
    )

    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    Write-AtomicText -Path $StopFile -Content '{"stop":true}'
    if (-not (Wait-ForProcessExit -Process $Process -TimeoutSeconds 12 -Description '上位机')) {
        $Process.CloseMainWindow() | Out-Null
        if (-not (Wait-ForProcessExit -Process $Process -TimeoutSeconds 3 -Description '上位机')) {
            $Process.Kill()
            $Process.WaitForExit()
        }
    }
}

function Stop-SimulatorProcess {
    param([System.Diagnostics.Process]$Process)

    if ($null -eq $Process -or $Process.HasExited) {
        return
    }

    if (-not (Wait-ForProcessExit -Process $Process -TimeoutSeconds 5 -Description 'Simulator')) {
        $Process.Kill()
        $Process.WaitForExit()
    }
}

function Invoke-DotnetCheck {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$OutputPath
    )

    Write-Host "[$Label] $($Arguments -join ' ')"
    $output = @(& dotnet @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    [System.IO.File]::WriteAllLines($OutputPath, [string[]]($output | ForEach-Object { $_.ToString() }))
    $output | ForEach-Object { Write-Host $_ }
    return [pscustomobject]@{
        Name = $Label
        Pass = ($exitCode -eq 0)
        ExitCode = $exitCode
        OutputFile = $OutputPath
    }
}

function Invoke-SqliteQuery {
    param(
        [Parameter(Mandatory = $true)][string]$DatabasePath,
        [Parameter(Mandatory = $true)][string]$Sql
    )

    $connection = New-Object System.Data.SQLite.SQLiteConnection("Data Source=$DatabasePath;Version=3;")
    $connection.Open()
    $command = $null
    $adapter = $null
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $adapter = New-Object System.Data.SQLite.SQLiteDataAdapter
        $adapter.SelectCommand = $command
        $table = New-Object System.Data.DataTable
        [void]$adapter.Fill($table)
        $rows = New-Object System.Collections.ArrayList
        foreach ($row in $table.Rows) {
            [void]$rows.Add($row)
        }
        return $rows.ToArray()
    }
    finally {
        if ($null -ne $adapter) {
            $adapter.Dispose()
        }
        if ($null -ne $command) {
            $command.Dispose()
        }
        $connection.Dispose()
    }
}

function Remove-SuccessfulArtifactDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq 10) {
                Write-Warning "成功场景目录暂时无法清理，已保留现场：$Path；$($_.Exception.Message)"
                return
            }
            Start-Sleep -Milliseconds 100
        }
    }
}

function Format-Rfid {
    param($Value)
    if ($null -eq $Value -or $Value -is [DBNull]) {
        return $null
    }
    return ('{0:X4}' -f [int]$Value)
}

function Get-DatabaseSnapshot {
    param([Parameter(Mandatory = $true)][string]$DatabasePath)

    $recordRows = @(Invoke-SqliteQuery -DatabasePath $DatabasePath -Sql @"
SELECT passage_id, station_id, station_address, head_rfid, expected_vehicle_count,
       detected_vehicle_count, result, clear_state, warning_message, alarm_message
FROM passage_record
ORDER BY completed_at ASC, created_at ASC;
"@
    )
    $records = New-Object System.Collections.ArrayList
    foreach ($row in $recordRows) {
        $details = @(Invoke-SqliteQuery -DatabasePath $DatabasePath -Sql ("SELECT rfid_value FROM passage_rfid WHERE passage_id = '{0}' ORDER BY sequence_no;" -f $row.passage_id))
        $rfids = @($details | ForEach-Object { Format-Rfid $_.rfid_value })
        $warningParts = New-Object System.Collections.Generic.List[string]
        if (-not $row.IsNull('warning_message')) {
            [void]$warningParts.Add([string]$row.warning_message)
        }
        if (-not $row.IsNull('alarm_message')) {
            [void]$warningParts.Add([string]$row.alarm_message)
        }
        $warning = if ($warningParts.Count -eq 0) { $null } else { $warningParts -join "`n" }
        [void]$records.Add([pscustomobject]@{
            PassageId = [string]$row.passage_id
            StationId = [string]$row.station_id
            StationAddress = [int]$row.station_address
            Station = ('RFID-{0:X2}' -f [int]$row.station_address)
            HeadRfid = Format-Rfid $row.head_rfid
            Expected = [int]$row.expected_vehicle_count
            Detected = [int]$row.detected_vehicle_count
            Result = if ([int]$row.result -eq 0) { 'Completed' } else { 'UncouplingAlarm' }
            ClearState = if ([int]$row.clear_state -eq 1) { 'Cleared' } else { 'PendingClear' }
            Warning = $warning
            Rfids = $rfids
        })
    }
    return $records.ToArray()
}

function Get-ScenarioExpectation {
    param([Parameter(Mandatory = $true)][string]$Name)

    switch ($Name) {
        'Normal11' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '0001'; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @(); Rfids = @('0001', '0011', '0012', '0013', '0014', '0015', '0016', '0017', '0018', '0019', '001A') }) }
        }
        'Uncoupling10' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '0002'; Expected = 11; Detected = 10; Result = 'UncouplingAlarm'; WarningContains = @('脱节报警'); Rfids = @('0002', '0011', '0012', '0013', '0014', '0015', '0016', '0017', '0018', '0019') }) }
        }
        'TwoConsecutiveTrains' {
            return [pscustomobject]@{ Records = @(
                [pscustomobject]@{ StationAddress = 1; HeadRfid = '0003'; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @(); Rfids = @('0003', '0021', '0022', '0023', '0024', '0025', '0026', '0027', '0028', '0029', '002A') },
                [pscustomobject]@{ StationAddress = 1; HeadRfid = '0004'; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @(); Rfids = @('0004', '0031', '0032', '0033', '0034', '0035', '0036', '0037', '0038', '0039', '003A') }
            ) }
        }
        'TwoStationsConcurrent' {
            return [pscustomobject]@{ Records = @(
                [pscustomobject]@{ StationAddress = 1; HeadRfid = '0001'; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @(); Rfids = @('0001', '0041', '0042', '0043', '0044', '0045', '0046', '0047', '0048', '0049', '004A') },
                [pscustomobject]@{ StationAddress = 4; HeadRfid = '0002'; Expected = 11; Detected = 10; Result = 'UncouplingAlarm'; WarningContains = @('脱节报警'); Rfids = @('0002', '0051', '0052', '0053', '0054', '0055', '0056', '0057', '0058', '0059') }
            ) }
        }
        'ClearReappearingTags' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '0001'; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @(); Rfids = @('0001', '0061', '0062', '0063', '0064', '0065', '0066', '0067', '0068', '0069', '006A') }) }
        }
        'SparseSlots' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = $null; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @('未检测到车头标签'); Rfids = @('001D', '0021', '0022', '0023', '0024', '0025', '0026', '0027', '0028', '0029', '002A') }) }
        }
        'MultipleHeads' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '0001'; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @('多个车头标签'); Rfids = @('0001', '0003', '0011', '0012', '0013', '0014', '0015', '0016', '0017', '0018', '0019') }) }
        }
        'NoHead' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = $null; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @('未检测到车头标签'); Rfids = @('000B', '000C', '000D', '000E', '000F', '0010', '0011', '0012', '0013', '0014', '0015') }) }
        }
        default {
            throw "未知验收场景：$Name"
        }
    }
}

function Get-FinalRuntimeStation {
    param(
        [Parameter(Mandatory = $true)]$Runtime,
        [Parameter(Mandatory = $true)][int]$StationAddress
    )

    return @($Runtime.Stations | Where-Object { [int]$_.StationAddress -eq $StationAddress })[0]
}

function Get-RuntimeStationHistory {
    param(
        [Parameter(Mandatory = $true)]$Runtime,
        [Parameter(Mandatory = $true)][int]$StationAddress
    )

    $states = New-Object System.Collections.ArrayList
    foreach ($entry in @($Runtime.History)) {
        foreach ($station in @($entry.Stations)) {
            if ([int]$station.StationAddress -eq $StationAddress) {
                [void]$states.Add($station)
            }
        }
    }
    return $states.ToArray()
}

function Get-CompactLifecycleSequence {
    param([Parameter(Mandatory = $true)][object[]]$States)

    $sequence = New-Object System.Collections.ArrayList
    foreach ($state in $States) {
        $value = [string]$state
        if ($sequence.Count -eq 0 -or $sequence[$sequence.Count - 1] -ne $value) {
            [void]$sequence.Add($value)
        }
    }
    return $sequence.ToArray()
}

function Test-ScenarioResult {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$DatabasePath,
        [Parameter(Mandatory = $true)]$Runtime,
        [Parameter(Mandatory = $true)]$SimulatorResult,
        [Parameter(Mandatory = $true)][string]$LogDirectory
    )

    if (-not $SimulatorResult.Pass) {
        throw "Simulator场景失败：$($SimulatorResult.FailureReason)"
    }

    $responses = @($SimulatorResult.Responses)
    if ($responses.Count -eq 0 -or @($responses | Where-Object { [int]$_.FrameLength -ne 40 }).Count -gt 0) {
        throw 'Simulator未提供完整40 Byte响应日志。'
    }
    foreach ($response in $responses) {
        $nonEmptyCount = @($response.Slots | Where-Object { [int]$_ -ne 0 }).Count
        if ([int]$response.ReportedCardCount -ne $nonEmptyCount) {
            throw "Simulator Byte7不等于14槽非空数量：Station=$($response.StationAddress)。"
        }
    }

    $logFiles = @(Get-ChildItem -LiteralPath $LogDirectory -Filter '*.log' -File -ErrorAction SilentlyContinue)
    foreach ($logFile in $logFiles) {
        if (Select-String -LiteralPath $logFile.FullName -Pattern '(?:192\.168\.|10\.\d{1,3}\.\d{1,3}\.|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.)' -Quiet) {
            throw "Acceptance日志出现非loopback通信地址：$($logFile.FullName)"
        }
    }

    $actualRecords = @(Get-DatabaseSnapshot -DatabasePath $DatabasePath)
    $expectation = Get-ScenarioExpectation -Name $Name
    if ($actualRecords.Count -ne @($expectation.Records).Count) {
        throw "SQLite记录数不符：expected=$(@($expectation.Records).Count), actual=$($actualRecords.Count)。"
    }
    if (($Name -eq 'TwoConsecutiveTrains') -and (@($actualRecords.PassageId | Select-Object -Unique).Count -ne 2)) {
        throw 'TwoConsecutiveTrains未生成两个不同的 PassageId。'
    }

    $resultRows = New-Object System.Collections.ArrayList
    for ($index = 0; $index -lt $actualRecords.Count; $index++) {
        $actual = $actualRecords[$index]
        $expected = @($expectation.Records)[$index]
        $expectedStation = @($Runtime.Stations | Where-Object { [int]$_.StationAddress -eq $expected.StationAddress })[0]
        if ($null -eq $expectedStation -or [string]::IsNullOrWhiteSpace([string]$expectedStation.StationId)) {
            throw "未找到验收测试配置中的StationId：$Name station=$($expected.StationAddress)。"
        }
        $expectedStationId = [string]$expectedStation.StationId
        if ($actual.StationId -ne $expectedStationId) {
            throw "SQLite StationId与当前验收测试配置不一致：$Name expected=$expectedStationId actual=$($actual.StationId)。"
        }
        if ($actual.StationAddress -ne $expected.StationAddress -or
            $actual.HeadRfid -ne $expected.HeadRfid -or
            $actual.Expected -ne $expected.Expected -or
            $actual.Detected -ne $expected.Detected -or
            $actual.Result -ne $expected.Result -or
            $actual.ClearState -ne 'Cleared' -or
            (@($actual.Rfids) -join ',') -ne (@($expected.Rfids) -join ',')) {
            throw "SQLite记录内容不符：$Name index=$index。"
        }
        foreach ($warningPart in @($expected.WarningContains)) {
            if ([string]::IsNullOrWhiteSpace($warningPart)) { continue }
            if ($null -eq $actual.Warning -or $actual.Warning.IndexOf($warningPart, [System.StringComparison]::Ordinal) -lt 0) {
                throw "SQLite告警内容不符：$Name index=$index missing=$warningPart。"
            }
        }

        $finalRuntime = Get-FinalRuntimeStation -Runtime $Runtime -StationAddress $actual.StationAddress
        if ($null -eq $finalRuntime -or $finalRuntime.LifecycleState -ne 'Idle') {
            throw "最终Runtime不是Idle：$Name station=$($actual.StationAddress)。"
        }
        $history = @(Get-RuntimeStationHistory -Runtime $Runtime -StationAddress $actual.StationAddress)
        $states = @($history | ForEach-Object { $_.LifecycleState })
        $lifecycleSequence = @(Get-CompactLifecycleSequence -States $states)
        # A fast UDP response can make the persisted snapshot observe the
        # intermediate Clearing state as WaitForEmpty. The Clear request and
        # the subsequent empty-read confirmation are validated below.
        if ($states -notcontains 'Recognizing' -or $states -notcontains 'WaitForEmpty') {
            throw "Runtime生命周期缺少 Recognizing/WaitForEmpty：$Name station=$($actual.StationAddress)。"
        }
        if ($actual.Result -eq 'Completed' -and $states -notcontains 'Completed') {
            throw "Runtime生命周期缺少Completed：$Name station=$($actual.StationAddress)。"
        }
        if ($actual.Result -eq 'UncouplingAlarm' -and $states -notcontains 'Alarm') {
            throw "Runtime生命周期缺少Alarm：$Name station=$($actual.StationAddress)。"
        }

        $clearCount = @($SimulatorResult.Requests | Where-Object {
            [int]$_.StationAddress -eq $actual.StationAddress -and [int]$_.Command -eq 1
        }).Count
        $readCount = @($SimulatorResult.Requests | Where-Object {
            [int]$_.StationAddress -eq $actual.StationAddress -and [int]$_.Command -eq 0
        }).Count
        $responseCount = @($SimulatorResult.Responses | Where-Object {
            [int]$_.StationAddress -eq $actual.StationAddress
        }).Count
        if ($clearCount -lt 1) {
            throw "Simulator未记录Clear：$Name station=$($actual.StationAddress)。"
        }
        if ($Name -eq 'ClearReappearingTags' -and $clearCount -lt 2) {
            throw 'ClearReappearingTags未证明旧标签出现后再次Clear。'
        }

        [void]$resultRows.Add([pscustomobject]@{
            Scenario = $Name
            Station = $actual.Station
            StationId = $actual.StationId
            StationAddress = $actual.StationAddress
            PassageId = $actual.PassageId
            HeadRfid = $actual.HeadRfid
            Expected = $actual.Expected
            Detected = $actual.Detected
            Result = $actual.Result
            Warning = $actual.Warning
            ClearState = $actual.ClearState
            ReadRequestCount = $readCount
            ClearRequestCount = $clearCount
            LifecycleSequence = $lifecycleSequence
            FinalRuntimeState = $finalRuntime.LifecycleState
            ResponseCount = $responseCount
            DurationMs = [long]$SimulatorResult.DurationMs
            FailureReason = $null
            Pass = $true
        })
    }

    return [pscustomobject]@{
        Records = $resultRows.ToArray()
        FinalRuntime = $Runtime
        Simulator = $SimulatorResult
    }
}

function New-FailureRows {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FailureReason,
        [Parameter(Mandatory = $true)][long]$DurationMs
    )

    return @([pscustomobject]@{
        Scenario = $Name
        Station = 'ALL'
        StationAddress = $null
        PassageId = $null
        HeadRfid = $null
        Expected = $null
        Detected = $null
        Result = $null
        Warning = $null
        ClearState = $null
        ReadRequestCount = $null
        ClearRequestCount = $null
        LifecycleSequence = $null
        FinalRuntimeState = $null
        ResponseCount = $null
        DurationMs = $DurationMs
        FailureReason = $FailureReason
        Pass = $false
    })
}

function Write-FailureDiagnostics {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$ScenarioRoot,
        [Parameter(Mandatory = $true)][string]$DatabasePath,
        [Parameter(Mandatory = $true)][string]$RuntimeStatePath,
        [Parameter(Mandatory = $true)][string]$SimulatorResultPath,
        [Parameter(Mandatory = $true)][string]$LogDirectory,
        [Parameter(Mandatory = $true)][string]$FailureReason
    )

    $runtime = $null
    $simulator = $null
    $database = @()
    try {
        if ([System.IO.File]::Exists($RuntimeStatePath)) {
            $runtime = Read-JsonFile -Path $RuntimeStatePath -TimeoutSeconds 2 -Description '失败Runtime状态'
        }
    }
    catch { }
    try {
        if ([System.IO.File]::Exists($SimulatorResultPath)) {
            $simulator = Read-JsonFile -Path $SimulatorResultPath -TimeoutSeconds 2 -Description '失败Simulator状态'
        }
    }
    catch { }
    try {
        if ([System.IO.File]::Exists($DatabasePath)) {
            $database = @(Get-DatabaseSnapshot -DatabasePath $DatabasePath)
        }
    }
    catch { }

    $recentLogs = @()
    foreach ($logFile in @(Get-ChildItem -LiteralPath $LogDirectory -Filter '*.log' -File -ErrorAction SilentlyContinue)) {
        $recentLogs += [pscustomobject]@{
            File = $logFile.FullName
            Tail = @(Get-Content -LiteralPath $logFile.FullName -Tail 20 -ErrorAction SilentlyContinue)
        }
    }

    $diagnosticsPath = Join-Path $ScenarioRoot 'failure-diagnostics.json'
    Write-AtomicJson -Path $diagnosticsPath -Value ([pscustomobject]@{
        Scenario = $Name
        FailureReason = $FailureReason
        Runtime = $runtime
        Simulator = $simulator
        Database = $database
        RecentLogs = $recentLogs
    })
    return $diagnosticsPath
}

function Invoke-Scenario {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$ScenarioRoot
    )

    [System.IO.Directory]::CreateDirectory($ScenarioRoot) | Out-Null
    $databasePath = Join-Path $ScenarioRoot 'MineRailMonitor.Acceptance.db'
    $runtimeStatePath = Join-Path $ScenarioRoot 'runtime-state.json'
    $simulatorResultPath = Join-Path $ScenarioRoot 'simulator-result.json'
    $simulatorReadyPath = Join-Path $ScenarioRoot 'simulator-ready.json'
    $upperReadyPath = Join-Path $ScenarioRoot 'upper-ready.json'
    $stopFile = Join-Path $ScenarioRoot 'stop'
    $logDirectory = Join-Path $ScenarioRoot 'logs'
    $simulatorStdout = Join-Path $ScenarioRoot 'simulator.stdout.log'
    $simulatorStderr = Join-Path $ScenarioRoot 'simulator.stderr.log'
    $upperStdout = Join-Path $ScenarioRoot 'upper.stdout.log'
    $upperStderr = Join-Path $ScenarioRoot 'upper.stderr.log'
    [System.IO.Directory]::CreateDirectory($logDirectory) | Out-Null

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $simulatorProcess = $null
    $upperProcess = $null
    $passed = $false
    $failureReason = $null
    $verified = $null
    try {
        $simulatorArguments = @(
            '--test-mode', '--scenario', $Name, '--port', $simulatorPort560.ToString(), '--port-620', $simulatorPort620.ToString(),
            '--result', $simulatorResultPath, '--ready-file', $simulatorReadyPath
        )
        $simulatorProcess = Start-AcceptanceProcess `
            -FilePath $simulatorExecutable `
            -Arguments $simulatorArguments `
            -WorkingDirectory $simulatorBinaryDirectory `
            -StandardOutput $simulatorStdout `
            -StandardError $simulatorStderr
        Wait-ForFile -Path $simulatorReadyPath -TimeoutSeconds 20 -Process $simulatorProcess -Description 'Simulator UDP就绪'

        $upperArguments = @(
            '--acceptance', '--database', $databasePath, '--runtime-state', $runtimeStatePath,
            '--log-dir', $logDirectory, '--ready-file', $upperReadyPath, '--stop-file', $stopFile,
            '--listen-port-560', $upperPort560.ToString(), '--listen-port-620', $upperPort620.ToString(),
            '--simulator-port-560', $simulatorPort560.ToString(), '--simulator-port-620', $simulatorPort620.ToString()
        )
        $upperProcess = Start-AcceptanceProcess `
            -FilePath $mainExecutable `
            -Arguments $upperArguments `
            -WorkingDirectory $mainBinaryDirectory `
            -StandardOutput $upperStdout `
            -StandardError $upperStderr
        Wait-ForFile -Path $upperReadyPath -TimeoutSeconds 30 -Process $upperProcess -Description '上位机UDP就绪'

        $simulatorResult = Read-JsonFile -Path $simulatorResultPath -TimeoutSeconds 75 -Process $simulatorProcess -Description 'Simulator场景结果'
        if (-not (Wait-ForProcessExit -Process $simulatorProcess -TimeoutSeconds 10 -Description 'Simulator')) {
            throw 'Simulator已写出结果但未正常退出。'
        }
        $runtime = Read-JsonFile -Path $runtimeStatePath -TimeoutSeconds 10 -Process $upperProcess -Description '上位机最终Runtime状态'
        $idleDeadline = [DateTime]::UtcNow.AddSeconds(10)
        while (@($runtime.Stations | Where-Object { $_.LifecycleState -ne 'Idle' }).Count -gt 0 -and [DateTime]::UtcNow -lt $idleDeadline) {
            Start-Sleep -Milliseconds 200
            $runtime = Read-JsonFile -Path $runtimeStatePath -TimeoutSeconds 2 -Process $upperProcess -Description '上位机最终Runtime状态'
        }
        $verified = Test-ScenarioResult -Name $Name -DatabasePath $databasePath -Runtime $runtime -SimulatorResult $simulatorResult -LogDirectory $logDirectory
        $passed = $true
    }
    catch {
        $failureReason = $_.Exception.Message
        if ($_.ScriptStackTrace) {
            $failureReason += "`n$($_.ScriptStackTrace)"
        }
    }
    finally {
        Stop-UpperProcess -Process $upperProcess -StopFile $stopFile
        Stop-SimulatorProcess -Process $simulatorProcess
        $watch.Stop()
    }

    $durationMs = [long]$watch.Elapsed.TotalMilliseconds
    $failureDiagnosticsPath = $null
    if (-not $passed) {
        try {
            $failureDiagnosticsPath = Write-FailureDiagnostics `
                -Name $Name `
                -ScenarioRoot $ScenarioRoot `
                -DatabasePath $databasePath `
                -RuntimeStatePath $runtimeStatePath `
                -SimulatorResultPath $simulatorResultPath `
                -LogDirectory $logDirectory `
                -FailureReason (if ([string]::IsNullOrWhiteSpace($failureReason)) { '场景验证失败。' } else { $failureReason })
        }
        catch {
            Write-Warning "失败诊断快照写入失败：$($_.Exception.Message)"
        }
    }
    if ($passed -and $null -ne $verified) {
        $rows = @($verified.Records | ForEach-Object {
            $_.DurationMs = $durationMs
            $_
        })
    }
    else {
        $failureText = if ([string]::IsNullOrWhiteSpace($failureReason)) { '场景验证失败。' } else { $failureReason }
        $rows = New-FailureRows -Name $Name -FailureReason $failureText -DurationMs $durationMs
    }

    return [pscustomobject]@{
        Scenario = $Name
        Pass = $passed
        DurationMs = $durationMs
        Records = $rows
        FailureReason = if ($passed) { $null } else { $failureReason }
        ArtifactDirectory = $ScenarioRoot
        FailureDiagnosticsFile = $failureDiagnosticsPath
    }
}

if ($null -eq $Scenario -or $Scenario.Count -eq 0) {
    $selectedScenarios = $scenarioNames
}
else {
    $selectedScenarios = @($Scenario | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    foreach ($name in $selectedScenarios) {
        if ($scenarioNames -notcontains $name) {
            throw "未知验收场景：$name"
        }
    }
}

[System.IO.Directory]::CreateDirectory($runRoot) | Out-Null
$verificationDirectory = Join-Path $runRoot 'verification'
[System.IO.Directory]::CreateDirectory($verificationDirectory) | Out-Null

$buildCheck = Invoke-DotnetCheck -Label 'Build' -Arguments @('build', 'MineRailMonitor.sln', '-c', $Configuration) -OutputPath (Join-Path $verificationDirectory 'build.log')
if (-not [System.IO.File]::Exists($sqliteAssembly)) {
    throw "未找到System.Data.SQLite程序集：$sqliteAssembly"
}
Add-Type -Path $sqliteAssembly

$coreCheck = Invoke-DotnetCheck -Label 'Core Tests' -Arguments @('test', 'tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj', '-c', $Configuration, '--no-build') -OutputPath (Join-Path $verificationDirectory 'core-tests.log')
$infrastructureCheck = Invoke-DotnetCheck -Label 'Infrastructure Tests' -Arguments @('test', 'tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj', '-c', $Configuration, '--no-build') -OutputPath (Join-Path $verificationDirectory 'infrastructure-tests.log')

$scenarioResults = New-Object System.Collections.ArrayList
$recordResults = New-Object System.Collections.ArrayList
foreach ($name in $selectedScenarios) {
    Write-Host "`n[$name] starting"
    if (-not $buildCheck.Pass -or -not $coreCheck.Pass -or -not $infrastructureCheck.Pass) {
        $scenarioResult = [pscustomobject]@{
            Scenario = $name
            Pass = $false
            DurationMs = 0
            Records = (New-FailureRows -Name $name -FailureReason 'Build/Core/Infrastructure verification failed.' -DurationMs 0)
            FailureReason = 'Build/Core/Infrastructure verification failed.'
            ArtifactDirectory = $null
        }
    }
    else {
        $scenarioRoot = Join-Path $runRoot $name
        $scenarioResult = Invoke-Scenario -Name $name -ScenarioRoot $scenarioRoot
    }
    [void]$scenarioResults.Add([pscustomobject]@{
        Scenario = $scenarioResult.Scenario
        Pass = $scenarioResult.Pass
        DurationMs = $scenarioResult.DurationMs
        FailureReason = $scenarioResult.FailureReason
        ArtifactDirectory = $scenarioResult.ArtifactDirectory
    })
    foreach ($row in @($scenarioResult.Records)) {
        [void]$recordResults.Add($row)
    }

    $partialReport = [pscustomobject]@{
        RunId = $runId
        GeneratedAt = [DateTimeOffset]::Now
        Verification = [pscustomobject]@{
            Build = $buildCheck
            CoreTests = $coreCheck
            InfrastructureTests = $infrastructureCheck
        }
        Scenarios = $scenarioResults.ToArray()
        Results = $recordResults.ToArray()
    }
    Write-AtomicJson -Path $reportPath -Value $partialReport

    if ($scenarioResult.Pass -and -not $KeepSuccessfulArtifacts -and $null -ne $scenarioResult.ArtifactDirectory) {
        Remove-SuccessfulArtifactDirectory -Path $scenarioResult.ArtifactDirectory
    }
}

$savedErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$diffCheckOutput = @(& git diff --check 2>&1)
$diffCheckExitCode = $LASTEXITCODE
$gitStatusOutput = @(& git status --short --branch 2>&1)
$gitStatusExitCode = $LASTEXITCODE
$ErrorActionPreference = $savedErrorActionPreference
$finalReport = [pscustomobject]@{
    Name = 'RFID Acceptance'
    RunId = $runId
    GeneratedAt = [DateTimeOffset]::Now
    RunRoot = $runRoot
    Verification = [pscustomobject]@{
        Build = $buildCheck
        CoreTests = $coreCheck
        InfrastructureTests = $infrastructureCheck
        GitDiffCheck = [pscustomobject]@{ Pass = ($diffCheckExitCode -eq 0); ExitCode = $diffCheckExitCode }
        GitStatus = $gitStatusOutput
    }
    Scenarios = $scenarioResults.ToArray()
    Results = $recordResults.ToArray()
    TotalDurationMs = [long]$acceptanceRunWatch.Elapsed.TotalMilliseconds
    PassedScenarioCount = @($scenarioResults | Where-Object { $_.Pass }).Count
    ScenarioCount = $scenarioResults.Count
    Pass = (@($scenarioResults | Where-Object { -not $_.Pass }).Count -eq 0 -and $buildCheck.Pass -and $coreCheck.Pass -and $infrastructureCheck.Pass -and $diffCheckExitCode -eq 0)
}
Write-AtomicJson -Path $reportPath -Value $finalReport

Write-Host "`nRFID Acceptance"
foreach ($scenarioResult in $scenarioResults) {
    $status = if ($scenarioResult.Pass) { 'PASS' } else { 'FAIL' }
    Write-Host ("{0,-25} {1}" -f $scenarioResult.Scenario, $status)
}
Write-Host ""
Write-Host ("{0}/{1} PASS" -f $finalReport.PassedScenarioCount, $finalReport.ScenarioCount)
Write-Host "Report: $reportPath"

if (-not $finalReport.Pass) {
    Write-Host "Failure artifacts are retained under: $runRoot"
    exit 1
}

exit 0
