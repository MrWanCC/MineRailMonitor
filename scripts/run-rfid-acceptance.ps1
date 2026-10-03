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
    'NoHead',
    'UdpU01Normal11',
    'UdpU02Uncoupling10Of11',
    'UdpU03LateEleventhCompletes',
    'UdpU04RepeatedTagsTimeout',
    'UdpU05HeadAppearsLate',
    'UdpU06UnknownHeadFallback',
    'UdpU07ConsecutiveAfterClear',
    'UdpU08ImmediateConsecutive'
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
       detected_vehicle_count, result, clear_state, warning_message, alarm_message,
       vehicle_no, vehicle_name, vehicle_head_rfid,
       alarm_acknowledged_at, alarm_recovered_at
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
            VehicleHeadRfid = Format-Rfid $row.vehicle_head_rfid
            VehicleNo = if ($row.IsNull('vehicle_no')) { $null } else { [string]$row.vehicle_no }
            VehicleName = if ($row.IsNull('vehicle_name')) { $null } else { [string]$row.vehicle_name }
            Expected = [int]$row.expected_vehicle_count
            Detected = [int]$row.detected_vehicle_count
            Result = if ([int]$row.result -eq 0) { 'Completed' } else { 'UncouplingAlarm' }
            ClearState = if ([int]$row.clear_state -eq 1) { 'Cleared' } else { 'PendingClear' }
            AlarmStatus = if ([int]$row.result -ne 1) { $null } elseif (-not $row.IsNull('alarm_recovered_at')) { 'Recovered' } elseif (-not $row.IsNull('alarm_acknowledged_at')) { 'Acknowledged' } else { 'Unconfirmed' }
            Warning = $warning
            Rfids = $rfids
        })
    }
    return $records.ToArray()
}

function Get-StandardTagSequence {
    param(
        [Parameter(Mandatory = $true)][int]$Head,
        [Parameter(Mandatory = $true)][int]$FirstWagon,
        [Parameter(Mandatory = $true)][int]$WagonCount
    )

    $values = New-Object System.Collections.ArrayList
    [void]$values.Add((Format-Rfid $Head))
    for ($index = 0; $index -lt $WagonCount; $index++) {
        [void]$values.Add((Format-Rfid ($FirstWagon + $index)))
    }
    return $values.ToArray()
}

function Get-PassageEvents {
    param([Parameter(Mandatory = $true)][string]$LogDirectory)

    $passageDirectory = Join-Path $LogDirectory 'Passage'
    $events = New-Object System.Collections.ArrayList
    foreach ($file in @(Get-ChildItem -LiteralPath $passageDirectory -Filter 'passage-event-*.jsonl' -File -ErrorAction SilentlyContinue)) {
        foreach ($line in @(Get-Content -LiteralPath $file.FullName -ErrorAction SilentlyContinue)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            [void]$events.Add(($line | ConvertFrom-Json))
        }
    }
    return $events.ToArray()
}

function Get-RfidLifecycleLogLines {
    param([Parameter(Mandatory = $true)][string]$LogDirectory)

    $lines = New-Object System.Collections.ArrayList
    foreach ($logFile in @(Get-ChildItem -LiteralPath $LogDirectory -Filter '*.log' -File -ErrorAction SilentlyContinue)) {
        foreach ($line in @(Get-Content -LiteralPath $logFile.FullName -ErrorAction SilentlyContinue)) {
            if ($line.Contains('[RFIDLifecycle]')) {
                [void]$lines.Add([string]$line)
            }
        }
    }
    return $lines.ToArray()
}

function Get-StatisticsSnapshot {
    param([Parameter(Mandatory = $true)][string]$DatabasePath)

    $store = [MineRailMonitor.Infrastructure.Persistence.SqlitePassageRecordStore]::new($DatabasePath)
    try {
        $overview = $store.GetOverview([DateTimeOffset]::MinValue, [DateTimeOffset]::MaxValue, $null)
        return [pscustomobject]@{
            TotalCount = $overview.TotalCount
            NormalCount = $overview.NormalCount
            AlarmCount = $overview.AlarmCount
            AlarmRatePercent = $overview.AlarmRatePercent
            AverageDurationSeconds = $overview.AverageDurationSeconds
        }
    }
    finally {
        $store.Dispose()
    }
}

function Get-ScenarioExpectation {
    param([Parameter(Mandatory = $true)][string]$Name)

    switch ($Name) {
        'Normal11' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '03E9'; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @(); Rfids = (Get-StandardTagSequence 0x03E9 0x07D1 10) }) }
        }
        'Uncoupling10' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '03E9'; Expected = 11; Detected = 10; Result = 'UncouplingAlarm'; WarningContains = @('脱节报警'); Rfids = (Get-StandardTagSequence 0x03E9 0x07D1 9) }) }
        }
        'TwoConsecutiveTrains' {
            return [pscustomobject]@{ Records = @(
                [pscustomobject]@{ StationAddress = 1; HeadRfid = '03E9'; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @(); Rfids = (Get-StandardTagSequence 0x03E9 0x07D1 10) },
                [pscustomobject]@{ StationAddress = 1; HeadRfid = '03EA'; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @(); Rfids = (Get-StandardTagSequence 0x03EA 0x07DB 10) }
            ) }
        }
        'TwoStationsConcurrent' {
            return [pscustomobject]@{ Records = @(
                [pscustomobject]@{ StationAddress = 1; HeadRfid = '03E9'; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @(); Rfids = (Get-StandardTagSequence 0x03E9 0x07D1 10) },
                [pscustomobject]@{ StationAddress = 4; HeadRfid = '03EB'; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @(); Rfids = (Get-StandardTagSequence 0x03EB 0x07E5 10) }
            ) }
        }
        'ClearReappearingTags' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '03E9'; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @(); Rfids = (Get-StandardTagSequence 0x03E9 0x07D1 10) }) }
        }
        'SparseSlots' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = $null; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @('未检测到车头槽位(Position 0)'); Rfids = (Get-StandardTagSequence 0x03E9 0x07D1 10) }) }
        }
        'MultipleHeads' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '03E9'; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @(); Rfids = @('03E9', '03EA', '07D1', '07D2', '07D3', '07D4', '07D5', '07D6', '07D7', '07D8', '07D9') }) }
        }
        'NoHead' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = $null; Expected = 11; Detected = 11; Result = 'Completed'; WarningContains = @('未检测到车头槽位(Position 0)'); Rfids = (Get-StandardTagSequence 0x07D1 0x07D2 10) }) }
        }
        'UdpU01Normal11' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '1001'; VehicleHeadRfid = '1001'; VehicleNo = 'K001'; VehicleName = '11节验收车辆001'; Expected = 11; Detected = 11; Result = 'Completed'; AlarmStatus = $null; WarningContains = @(); Rfids = (Get-StandardTagSequence 0x1001 0x1101 10) }) }
        }
        'UdpU02Uncoupling10Of11' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '1001'; VehicleHeadRfid = '1001'; VehicleNo = 'K001'; VehicleName = '11节验收车辆001'; Expected = 11; Detected = 10; Result = 'UncouplingAlarm'; AlarmStatus = 'Unconfirmed'; WarningContains = @('脱节报警'); Rfids = (Get-StandardTagSequence 0x1001 0x1101 9) }) }
        }
        'UdpU03LateEleventhCompletes' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '1001'; VehicleHeadRfid = '1001'; VehicleNo = 'K001'; VehicleName = '11节验收车辆001'; Expected = 11; Detected = 11; Result = 'Completed'; AlarmStatus = $null; WarningContains = @(); Rfids = (Get-StandardTagSequence 0x1001 0x1101 10) }) }
        }
        'UdpU04RepeatedTagsTimeout' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '1001'; VehicleHeadRfid = '1001'; VehicleNo = 'K001'; VehicleName = '11节验收车辆001'; Expected = 11; Detected = 10; Result = 'UncouplingAlarm'; AlarmStatus = 'Unconfirmed'; WarningContains = @('脱节报警'); Rfids = (Get-StandardTagSequence 0x1001 0x1101 9) }) }
        }
        'UdpU05HeadAppearsLate' {
            $rfids = @((Get-StandardTagSequence 0 0x1101 10) | Select-Object -Skip 1) + @('1001')
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '1001'; VehicleHeadRfid = '1001'; VehicleNo = 'K001'; VehicleName = '11节验收车辆001'; Expected = 11; Detected = 11; Result = 'Completed'; AlarmStatus = $null; WarningContains = @(); Rfids = $rfids }) }
        }
        'UdpU06UnknownHeadFallback' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '2001'; VehicleHeadRfid = $null; VehicleNo = $null; VehicleName = $null; Expected = 11; Detected = 11; Result = 'Completed'; AlarmStatus = $null; WarningContains = @(); Rfids = (Get-StandardTagSequence 0x2001 0x2101 10) }) }
        }
        'UdpU07ConsecutiveAfterClear' {
            return [pscustomobject]@{ Records = @(
                [pscustomobject]@{ StationAddress = 1; HeadRfid = '1001'; VehicleHeadRfid = '1001'; VehicleNo = 'K001'; VehicleName = '11节验收车辆001'; Expected = 11; Detected = 11; Result = 'Completed'; AlarmStatus = $null; WarningContains = @(); Rfids = (Get-StandardTagSequence 0x1001 0x1101 10) },
                [pscustomobject]@{ StationAddress = 1; HeadRfid = '1002'; VehicleHeadRfid = '1002'; VehicleNo = 'K002'; VehicleName = '11节验收车辆002'; Expected = 11; Detected = 11; Result = 'Completed'; AlarmStatus = $null; WarningContains = @(); Rfids = (Get-StandardTagSequence 0x1002 0x1201 10) }
            ) }
        }
        'UdpU08ImmediateConsecutive' {
            return [pscustomobject]@{ Records = @([pscustomobject]@{ StationAddress = 1; HeadRfid = '1001'; VehicleHeadRfid = '1001'; VehicleNo = 'K001'; VehicleName = '11节验收车辆001'; Expected = 11; Detected = 11; Result = 'Completed'; AlarmStatus = $null; WarningContains = @(); Rfids = (Get-StandardTagSequence 0x1001 0x1101 10) }) }
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
    $requests = @($SimulatorResult.Requests)
    if ($responses.Count -eq 0 -or @($responses | Where-Object { [int]$_.FrameLength -ne 40 }).Count -gt 0) {
        throw 'Simulator未提供完整40 Byte响应日志。'
    }
    if ($requests.Count -eq 0 -or @($requests | Where-Object { [int]$_.FrameLength -ne 40 }).Count -gt 0) {
        throw 'Simulator未提供完整40 Byte请求日志。'
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
    $passageEvents = @(Get-PassageEvents -LogDirectory $LogDirectory)
    $lifecycleLogLines = @(Get-RfidLifecycleLogLines -LogDirectory $LogDirectory)
    $statistics = Get-StatisticsSnapshot -DatabasePath $DatabasePath
    if ($actualRecords.Count -ne @($expectation.Records).Count) {
        throw "SQLite记录数不符：expected=$(@($expectation.Records).Count), actual=$($actualRecords.Count)。"
    }
    if (($Name -in @('TwoConsecutiveTrains', 'UdpU07ConsecutiveAfterClear')) -and (@($actualRecords.PassageId | Select-Object -Unique).Count -ne 2)) {
        throw "$Name 未生成两个不同的 PassageId。"
    }
    if ($statistics.TotalCount -ne $actualRecords.Count -or
        $statistics.NormalCount -ne @($actualRecords | Where-Object Result -eq 'Completed').Count -or
        $statistics.AlarmCount -ne @($actualRecords | Where-Object Result -eq 'UncouplingAlarm').Count) {
        throw "统计服务结果与SQLite Passage记录不一致：$Name。"
    }
    if ($Name -eq 'UdpU08ImmediateConsecutive' -and $actualRecords.Count -ne 1) {
        throw "U08应保留A09当前边界结果（一条A车Passage）；实际记录数=$($actualRecords.Count)。"
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
        foreach ($identityField in @('VehicleHeadRfid', 'VehicleNo', 'VehicleName', 'AlarmStatus')) {
            if ($expected.PSObject.Properties.Match($identityField).Count -gt 0 -and $actual.$identityField -ne $expected.$identityField) {
                throw "SQLite车辆/报警快照不符：$Name index=$index field=$identityField expected=$($expected.$identityField) actual=$($actual.$identityField)。"
            }
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
        $passageLifecycleLines = @($lifecycleLogLines | Where-Object {
            $_.Contains("StationId=$expectedStationId") -and $_.Contains("PassageId=$($actual.PassageId)")
        })
        $diagnosticStates = @($passageLifecycleLines | ForEach-Object {
            if ($_ -match 'Event=STATE_TRANSITION\s+State=([A-Za-z]+)') { $Matches[1] }
        } | Select-Object -Unique)
        # A fast UDP response can make the persisted snapshot observe the
        # intermediate Clearing state as WaitForEmpty. The Clear request and
        # the subsequent empty-read confirmation are validated below.
        if ($states -notcontains 'Recognizing' -or $states -notcontains 'WaitForEmpty') {
            throw "Runtime生命周期缺少 Recognizing/WaitForEmpty：$Name station=$($actual.StationAddress)。"
        }
        if ($actual.Result -eq 'Completed' -and $states -notcontains 'Completed' -and $diagnosticStates -notcontains 'Completed') {
            throw "Runtime生命周期缺少Completed：$Name station=$($actual.StationAddress)。"
        }
        if ($actual.Result -eq 'UncouplingAlarm' -and $states -notcontains 'Alarm' -and $diagnosticStates -notcontains 'Alarm') {
            throw "Runtime生命周期缺少Alarm：$Name station=$($actual.StationAddress)。"
        }

        $passageEventRows = @($passageEvents | Where-Object { [string]$_.PassageId -eq $actual.PassageId })
        $passageEventTypes = @($passageEventRows | ForEach-Object { [string]$_.EventType })
        $terminalEvent = if ($actual.Result -eq 'Completed') { 'PASSAGE_COMPLETE' } else { 'PASSAGE_ALARM' }
        if (@($passageEventTypes | Where-Object { $_ -eq 'PASSAGE_START' }).Count -ne 1 -or
            @($passageEventTypes | Where-Object { $_ -eq 'PASSAGE_PROGRESS' }).Count -ne $actual.Detected -or
            @($passageEventTypes | Where-Object { $_ -eq $terminalEvent }).Count -ne 1 -or
            @($passageEventTypes | Where-Object { $_ -eq 'PASSAGE_CLEAR' }).Count -ne 1) {
            throw "PassageEvent生命周期事件与SQLite记录不一致：$Name passage=$($actual.PassageId) events=$($passageEventTypes -join ',')。"
        }

        $terminalEventRow = @($passageEventRows | Where-Object { $_.EventType -eq $terminalEvent })[0]
        $clearEventRow = @($passageEventRows | Where-Object { $_.EventType -eq 'PASSAGE_CLEAR' })[0]
        $completedToIdleMs = ([DateTimeOffset]::Parse([string]$clearEventRow.OccurredAt) - [DateTimeOffset]::Parse([string]$terminalEventRow.OccurredAt)).TotalMilliseconds
        $lastProgressEvent = @($passageEventRows | Where-Object { $_.EventType -eq 'PASSAGE_PROGRESS' } | Sort-Object OccurredAt | Select-Object -Last 1)
        $noNewRfidTimeoutSeconds = $null
        if ($actual.Result -eq 'UncouplingAlarm' -and $lastProgressEvent.Count -gt 0) {
            $noNewRfidTimeoutSeconds = ([DateTimeOffset]::Parse([string]$terminalEventRow.OccurredAt) - [DateTimeOffset]::Parse([string]$lastProgressEvent[0].OccurredAt)).TotalSeconds
        }
        $passageClearRequests = @($requests | Where-Object {
            [int]$_.StationAddress -eq $actual.StationAddress -and
            [int]$_.Command -eq 1 -and
            [DateTimeOffset]::Parse([string]$_.ReceivedAt) -ge [DateTimeOffset]::Parse([string]$terminalEventRow.OccurredAt)
        })
        $firstPassageClearRequest = if ($passageClearRequests.Count -gt 0) { $passageClearRequests[0] } else { $null }
        $emptyResponsesAfterClear = @()
        $clearToFirstEmptyMs = $null
        $emptyConfirmationGapMs = $null
        $nextTrainFirstNonEmptyAt = $null
        if ($null -ne $firstPassageClearRequest) {
            $clearRequestAt = [DateTimeOffset]::Parse([string]$firstPassageClearRequest.ReceivedAt)
            $responsesAfterClear = @($responses | Where-Object {
                [int]$_.StationAddress -eq $actual.StationAddress -and
                [DateTimeOffset]::Parse([string]$_.SentAt) -ge $clearRequestAt
            } | Sort-Object { [DateTimeOffset]::Parse([string]$_.SentAt) })
            $emptyResponsesAfterClear = @($responsesAfterClear | Where-Object { $_.IsEmpty })
            $nextTrainFirstNonEmptyAt = @($responsesAfterClear | Where-Object { -not $_.IsEmpty } | Select-Object -First 1 | ForEach-Object { [string]$_.SentAt }) | Select-Object -First 1
        }

        $oneOfTwoConfirmationLines = @($passageLifecycleLines | Where-Object {
            $_.Contains('Event=EMPTY_CONFIRMATION ') -and $_.Contains('Detail=Count=1/2')
        })
        $twoOfTwoConfirmationLines = @($passageLifecycleLines | Where-Object {
            $_.Contains('Event=EMPTY_CONFIRMATION ') -and $_.Contains('Detail=Count=2/2')
        })
        if ($oneOfTwoConfirmationLines.Count -eq 0 -or $twoOfTwoConfirmationLines.Count -eq 0) {
            throw "生命周期日志缺少连续空帧确认1/2、2/2：$Name passage=$($actual.PassageId)。"
        }
        $secondConfirmationLine = $twoOfTwoConfirmationLines[-1]
        $secondConfirmationAt = [DateTimeOffset]::Parse([regex]::Match($secondConfirmationLine, 'Time=(\S+)').Groups[1].Value)
        $firstConfirmationLine = @($oneOfTwoConfirmationLines | Where-Object {
            [DateTimeOffset]::Parse([regex]::Match($_, 'Time=(\S+)').Groups[1].Value) -lt $secondConfirmationAt
        })[-1]
        $firstConfirmationAt = [DateTimeOffset]::Parse([regex]::Match($firstConfirmationLine, 'Time=(\S+)').Groups[1].Value)
        $emptyConfirmationGapMs = ($secondConfirmationAt - $firstConfirmationAt).TotalMilliseconds
        $successfulClearRequest = @($passageClearRequests | Where-Object {
            [DateTimeOffset]::Parse([string]$_.ReceivedAt) -le $firstConfirmationAt
        } | Select-Object -Last 1)
        if ($successfulClearRequest.Count -gt 0) {
            $clearToFirstEmptyMs = ($firstConfirmationAt - [DateTimeOffset]::Parse([string]$successfulClearRequest[0].ReceivedAt)).TotalMilliseconds
        }

        if ($Name -in @('UdpU02Uncoupling10Of11', 'UdpU04RepeatedTagsTimeout') -and
            ($null -eq $noNewRfidTimeoutSeconds -or $noNewRfidTimeoutSeconds -lt 29.5 -or $noNewRfidTimeoutSeconds -gt 32.5)) {
            throw "无新唯一RFID后的报警时间不符合30秒超时：$Name elapsed=$noNewRfidTimeoutSeconds 秒。"
        }
        if ($Name -eq 'UdpU03LateEleventhCompletes') {
            $progressEvents = @($passageEventRows | Where-Object { $_.EventType -eq 'PASSAGE_PROGRESS' } | Sort-Object OccurredAt)
            if ($progressEvents.Count -ne 11) {
                throw "S03补扫场景没有11次唯一标签进度：count=$($progressEvents.Count)。"
            }
            $lastTagDelay = ([DateTimeOffset]::Parse([string]$progressEvents[10].OccurredAt) - [DateTimeOffset]::Parse([string]$progressEvents[9].OccurredAt)).TotalSeconds
            if ($lastTagDelay -lt 25 -or $lastTagDelay -ge 30) {
                throw "S03第11个标签没有在30秒超时前补到：delay=$lastTagDelay 秒。"
            }
        }

        $clearCount = @($requests | Where-Object {
            [int]$_.StationAddress -eq $actual.StationAddress -and [int]$_.Command -eq 1
        }).Count
        $readCount = @($requests | Where-Object {
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
        if ($Name -eq 'UdpU08ImmediateConsecutive') {
            $secondVehicleSeen = @($responses | Where-Object {
                [int]$_.StationAddress -eq $actual.StationAddress -and
                @($_.Slots | Where-Object { [int]$_ -eq 0x1002 }).Count -gt 0
            }).Count -gt 0
            $candidateFrameLogged = @($passageLifecycleLines | Where-Object { $_.Contains('Event=NON_EMPTY_FRAME_AFTER_TERMINAL_STATE') }).Count -gt 0
            if (-not $secondVehicleSeen -or -not $candidateFrameLogged -or $clearCount -lt 2 -or $passageEventRows.Count -ne 14) {
                throw "U08未复现并记录清场期间B车候选帧被读取后清除的边界：BSeen=$secondVehicleSeen CandidateFrameLogged=$candidateFrameLogged Clear=$clearCount Events=$($passageEventRows.Count)。"
            }
        }

        if ($Name -eq 'UdpU04RepeatedTagsTimeout') {
            $steadySnapshots = @($history | Where-Object { $_.LifecycleState -eq 'Recognizing' -and [int]$_.Detected -eq 10 })
            $lastNewTimes = @($steadySnapshots | ForEach-Object { [string]$_.LastNewVehicleAt } | Select-Object -Unique)
            if ($steadySnapshots.Count -eq 0 -or $lastNewTimes.Count -ne 1) {
                throw "U04重复读取期间LastNewVehicleAt发生变化或快照缺失：$Name。"
            }
            $lastNewAt = [DateTimeOffset]::Parse($lastNewTimes[0])
            $alarmEvent = @($passageEventRows | Where-Object { $_.EventType -eq 'PASSAGE_ALARM' })[0]
            $timeoutElapsed = ([DateTimeOffset]::Parse([string]$alarmEvent.OccurredAt) - $lastNewAt).TotalSeconds
            if ($timeoutElapsed -lt 29.5 -or $timeoutElapsed -gt 32.5) {
                throw "U04报警耗时不在30秒超时边界：elapsed=$timeoutElapsed 秒。"
            }
        }
        if ($Name -eq 'UdpU05HeadAppearsLate' -and @($passageEventTypes | Where-Object { $_ -eq 'VEHICLE_MATCH_LATE_RESOLVED' }).Count -ne 1) {
            throw 'U05未记录后续Position 0车头匹配事件。'
        }

        [void]$resultRows.Add([pscustomobject]@{
            Scenario = $Name
            Station = $actual.Station
            StationId = $actual.StationId
            StationAddress = $actual.StationAddress
            PassageId = $actual.PassageId
            HeadRfid = $actual.HeadRfid
            VehicleNo = $actual.VehicleNo
            VehicleName = $actual.VehicleName
            Expected = $actual.Expected
            Detected = $actual.Detected
            Result = $actual.Result
            AlarmStatus = $actual.AlarmStatus
            PassageEvents = $passageEventTypes
            LifecycleDiagnostics = $passageLifecycleLines
            DiagnosticTransitions = $diagnosticStates
            Statistics = $statistics
            Warning = $actual.Warning
            ClearState = $actual.ClearState
            ReadRequestCount = $readCount
            ClearRequestCount = $clearCount
            LifecycleSequence = $lifecycleSequence
            FinalRuntimeState = $finalRuntime.LifecycleState
            ResponseCount = $responseCount
            TerminalEventAt = $terminalEventRow.OccurredAt
            ClearCompletedAt = $clearEventRow.OccurredAt
            CompletedToIdleMs = [long][Math]::Max(0, $completedToIdleMs)
            ClearToFirstEmptyMs = if ($null -eq $clearToFirstEmptyMs) { $null } else { [long][Math]::Max(0, $clearToFirstEmptyMs) }
            EmptyConfirmationGapMs = if ($null -eq $emptyConfirmationGapMs) { $null } else { [long][Math]::Max(0, $emptyConfirmationGapMs) }
            NoNewRfidTimeoutSeconds = if ($null -eq $noNewRfidTimeoutSeconds) { $null } else { [Math]::Round($noNewRfidTimeoutSeconds, 3) }
            NextTrainFirstNonEmptyAt = $nextTrainFirstNonEmptyAt
            EmptyResponsesAfterFirstClear = $emptyResponsesAfterClear.Count
            DurationMs = [long]$SimulatorResult.DurationMs
            FailureReason = $null
            Pass = $true
        })
    }

    return [pscustomobject]@{
        Records = $resultRows.ToArray()
        FinalRuntime = $Runtime
        Simulator = $SimulatorResult
        PassageEvents = $passageEvents
        Statistics = $statistics
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
            $diagnosticFailureReason = if ([string]::IsNullOrWhiteSpace($failureReason)) { '场景验证失败。' } else { $failureReason }
            $failureDiagnosticsPath = Write-FailureDiagnostics `
                -Name $Name `
                -ScenarioRoot $ScenarioRoot `
                -DatabasePath $databasePath `
                -RuntimeStatePath $runtimeStatePath `
                -SimulatorResultPath $simulatorResultPath `
                -LogDirectory $logDirectory `
                -FailureReason $diagnosticFailureReason
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
$coreAssembly = Join-Path $mainBinaryDirectory 'MineRailMonitor.Core.dll'
$infrastructureAssembly = Join-Path $mainBinaryDirectory 'MineRailMonitor.Infrastructure.dll'
Add-Type -Path $coreAssembly
Add-Type -Path $infrastructureAssembly

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
