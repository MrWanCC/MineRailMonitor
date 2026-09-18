# RFID UDP Raw Packet Black Box Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不改变现有 RFID 协议和业务状态机的前提下，记录真实 UDP TX/RX 原始报文到按日期/站场分区的 JSONL 黑匣子文件。

**Architecture:** `RfidUdpTransport` 在真实发送成功后发布旁路 `DatagramSent`，RX 沿用现有 `DatagramReceived`；事件经 `YardCommunicationContext` 和 `YardCommunicationManager` 透传到 `MainWindow`。`MainWindow` 持有与通信 Manager 生命周期无关的 Infrastructure `RawPacketBlackBoxWriter`，按 Yard、Endpoint、ProtocolAddress 解析身份后把记录放入有界后台队列；`CommunicationPage` 只显示每秒快照并触发打开目录动作。

**Tech Stack:** .NET Framework 4.8、WPF、C#、`BlockingCollection<T>`、`StreamWriter`、Newtonsoft.Json、xUnit、现有 Acceptance PowerShell 流程。

**Spec:** `docs/superpowers/specs/2026-09-18-raw-packet-blackbox-design.md`

## Global Constraints

- 不修改 RFID 40-byte 协议、Byte4~7 语义、CRC、RfidFrameParser、Passage、脱节报警、Clear 状态机、数据库 schema、通信诊断统计和 simulator fault injection。
- 不新增 SQLite 表、PCAP、查询页面、导出、压缩、上传、MQ、Generic Host、DI、MVVM 或大型依赖。
- TX 只有 UDP `SendAsync` 成功完成后才产生 `DatagramSent`；订阅者异常不得改变发送结果。
- RX 非法帧、未匹配基站和未知地址也必须写入黑匣子。
- 使用 Yard + Endpoint + ProtocolAddress 隔离 560/620；不能只用 ProtocolAddress。
- Writer 使用容量 10000 的 bounded queue；队列满、关闭竞态和 IO 最终失败均只失败计数，不阻塞/拖垮 UDP。
- Writer IO 失败后移除并关闭对应 cache writer，后续记录重新尝试打开；成功写入后清除 `LastError`。
- 正式目录为 `AppContext.BaseDirectory/Logs/BlackBox`；Acceptance 目录为 `<AcceptanceOptions.LogDirectory>/BlackBox`。
- 现有 407 tests 不得减少，最终不得新增 skipped；Acceptance 必须保持 8/8。

---

### Task 1: Add real TX datagram event and propagation

**Files:**
- Create: `src/MineRailMonitor.Core/Communication/RfidUdpDatagramSentEventArgs.cs`
- Modify: `src/MineRailMonitor.Core/Communication/IRfidUdpTransport.cs`
- Modify: `src/MineRailMonitor.Core/Communication/RfidUdpTransport.cs`
- Modify: `src/MineRailMonitor.Core/Services/YardCommunicationContext.cs`
- Modify: `src/MineRailMonitor.Core/Services/YardCommunicationManager.cs`
- Test: `tests/MineRailMonitor.Core.Tests/RfidUdpTransportTests.cs`
- Test: `tests/MineRailMonitor.Core.Tests/YardCommunicationManagerTests.cs`

**Interfaces:**
- `RfidUdpDatagramSentEventArgs(byte[] data, IPEndPoint destinationEndPoint, DateTimeOffset sentAt, IPEndPoint? localEndPoint)` exposes defensive copies of `Data`, `DestinationEndPoint`, `SentAt`, and `LocalEndPoint`.
- `IRfidUdpTransport.DatagramSent` uses `EventHandler<RfidUdpDatagramSentEventArgs>`.
- `YardCommunicationContext.DatagramSent` and `YardCommunicationManager.DatagramSent` use `Action<YardCommunicationContext, RfidUdpDatagramSentEventArgs>`.

- [ ] **Step 1: Write failing transport tests.** Add `UdpTransport_sent_event_is_raised_after_successful_send` using a loopback receiver, assert the client receives the exact bytes and the event has the same bytes/destination. Add `UdpTransport_datagram_sent_observer_failure_does_not_fail_successful_send` with a throwing subscriber and assert `SendAsync` still completes.
- [ ] **Step 2: Run the focused tests and verify RED.** Run `dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~RfidUdpTransportTests"`; expected failures are missing event behavior, not test discovery errors.
- [ ] **Step 3: Implement the event.** Await `_client.SendAsync` in `RfidUdpTransport.SendAsync`, construct the event only after await, clone data/endpoints, and wrap observer dispatch so subscriber exceptions are traced and never rethrown. Subscribe/forward from Context and Manager with matching attach/detach lifecycle.
- [ ] **Step 4: Add propagation test and run focused tests.** Add `YardCommunicationManager_publishes_datagram_sent_with_context` using a disabled manual station and loopback endpoint; assert yard context, destination and data. Run the transport and manager focused filters; expected PASS.
- [ ] **Step 5: Commit.** `git add src/MineRailMonitor.Core tests/MineRailMonitor.Core.Tests && git commit -m "feat: publish real RFID UDP send events"`.

### Task 2: Add the Infrastructure JSONL black box writer

**Files:**
- Create: `src/MineRailMonitor.Infrastructure/BlackBox/RawPacketBlackBoxRecord.cs`
- Create: `src/MineRailMonitor.Infrastructure/BlackBox/RawPacketBlackBoxStatus.cs`
- Create: `src/MineRailMonitor.Infrastructure/BlackBox/RawPacketBlackBoxWriter.cs`
- Test: `tests/MineRailMonitor.Infrastructure.Tests/RawPacketBlackBoxWriterTests.cs`

**Interfaces:**
- `RawPacketBlackBoxRecord` contains `SchemaVersion`, `Time`, `Direction`, `Yard`, `StationId`, `Local`, `Remote`, `ProtocolAddress`, `Length`, `Valid`, `ValidationError`, `Command`, and `Hex`, serialized with the exact lower-camel JSON names.
- `RawPacketBlackBoxStatus` contains `IsRunning`, `WrittenCount`, `DroppedCount`, `LastError`, and `RootDirectory`.
- `RawPacketBlackBoxWriter(string rootDirectory, int retentionDays = 30, int queueCapacity = 10000, Func<DateTimeOffset>? nowProvider = null)` exposes `bool TryEnqueue(RawPacketBlackBoxRecord record)`, `RawPacketBlackBoxStatus GetSnapshot()`, and idempotent `Dispose()`.

- [ ] **Step 1: Write failing writer tests.** Add tests named `BlackBox_writes_one_valid_json_line_per_packet`, `BlackBox_preserves_invalid_rx_frame`, `BlackBox_separates_yards_into_different_files`, `BlackBox_does_not_confuse_same_protocol_address_between_yards`, and `BlackBox_flushes_pending_records_on_dispose`. Each reads files after `Dispose` and independently calls `JsonConvert.DeserializeObject` per line.
- [ ] **Step 2: Add retention/rotation and resilience red tests.** Add `BlackBox_retention_removes_only_expired_date_directories` with injected clock and the exact 30-calendar-day boundary, `BlackBox_rotates_file_when_record_date_changes`, `BlackBox_write_failure_does_not_crash_writer_or_udp_path`, `BlackBox_queue_is_bounded_and_enqueue_does_not_block`, and `BlackBox_enqueue_after_complete_adding_returns_false_without_throwing`.
- [ ] **Step 3: Run the focused Infrastructure tests and verify RED.** Run `dotnet test tests/MineRailMonitor.Infrastructure.Tests/MineRailMonitor.Infrastructure.Tests.csproj -c Debug --filter "FullyQualifiedName~RawPacketBlackBoxWriterTests"`; expected failures are missing writer types/behavior.
- [ ] **Step 4: Implement the model and writer.** Use `BlockingCollection<RawPacketBlackBoxRecord>` with `TryAdd`; catch `InvalidOperationException`/`ObjectDisposedException` during close races and increment `DroppedCount`. Consume on one `TaskCreationOptions.LongRunning` worker. Keep one writer per yard/date, use `record.Time` for the file directory, and remove/dispose a cached writer after any open/write/flush failure before continuing. Purge only at startup or injected clock date change, only matching `yyyy-MM-dd` directories. Clear `LastError` after any successful write.
- [ ] **Step 5: Run focused writer tests and inspect JSONL.** Expected PASS with no skipped tests, exact fields, compact JSON, complete hex and safe filenames.
- [ ] **Step 6: Commit.** `git add src/MineRailMonitor.Infrastructure tests/MineRailMonitor.Infrastructure.Tests && git commit -m "feat: add bounded raw packet black box writer"`.

### Task 3: Build MainWindow record mapping and lifecycle integration

**Files:**
- Modify: `src/MineRailMonitor/MainWindow.xaml.cs`
- Modify: `src/MineRailMonitor/App.xaml.cs` only if startup ownership requires no other change
- Test: `tests/MineRailMonitor.Core.Tests/YardContextMarkupTests.cs` or a focused new `MainWindowBlackBoxMarkupTests.cs`

**Interfaces:**
- MainWindow owns one `RawPacketBlackBoxWriter` rooted at `Path.Combine(_acceptanceOptions.Enabled ? _acceptanceOptions.LogDirectory! : Path.Combine(AppContext.BaseDirectory, "Logs"), "BlackBox")`.
- RX mapping receives `(YardCommunicationContext, RfidUdpDatagramEventArgs)` and always enqueues a record. TX mapping receives `(YardCommunicationContext, RfidUdpDatagramSentEventArgs)` and identifies command from `data[4] == 0x01`.
- Station resolution compares context Yard ownership, configured remote endpoint, and protocol address; unmatched packets use null `stationId` and still enqueue.

- [ ] **Step 1: Write failing integration/static tests.** Assert MainWindow wires `DatagramSent`, retains the writer across manager recreation, disposes communication before black box, and has separate Acceptance/normal BlackBox roots. Add a test for TX command classification from Byte4 and unmatched RX retention if a pure helper is extracted.
- [ ] **Step 2: Run focused tests and verify RED.** Run the selected Core test filter; expected failures are missing wiring and root expressions.
- [ ] **Step 3: Implement mapping and subscriptions.** Create the writer once after acceptance options are loaded. Subscribe/unsubscribe both manager events in `RecreateYardCommunicationManagerAsync`; enqueue RX at the beginning of the existing RX handler before business matching and enqueue TX from the new manager event. Do not use UI `CommandSent`, `SentCount`, or poller state for black box TX.
- [ ] **Step 4: Implement shutdown and directory action.** On window close, dispose the communication manager first, then dispose the writer. Add a safe Explorer launch that creates the root directory and logs open failures without throwing.
- [ ] **Step 5: Run focused integration tests and commit.** `git add src/MineRailMonitor/MainWindow.xaml.cs tests/MineRailMonitor.Core.Tests && git commit -m "feat: integrate raw packet black box with communication"`.

### Task 4: Add the CommunicationPage black box status area

**Files:**
- Modify: `src/MineRailMonitor/Pages/CommunicationPage.xaml`
- Modify: `src/MineRailMonitor/Pages/CommunicationPage.xaml.cs`
- Test: Create `tests/MineRailMonitor.Core.Tests/CommunicationPageMarkupTests.cs`

**Interfaces:**
- Add `BlackBoxStatusText`, `BlackBoxWrittenCountText`, `BlackBoxDroppedCountText`, and `OpenBlackBoxDirectoryButton` without adding a navigation page.
- Add `SetBlackBoxStatus(RawPacketBlackBoxStatus status)` and an `OpenBlackBoxDirectoryRequested` event/callback; the page does not own storage.

- [ ] **Step 1: Write failing markup tests.** Assert the four named controls, status strings, open-directory handler, and no new navigation entry.
- [ ] **Step 2: Run the markup filter and verify RED.** Run `dotnet test tests/MineRailMonitor.Core.Tests/MineRailMonitor.Core.Tests.csproj -c Debug --filter "FullyQualifiedName~CommunicationPageMarkupTests"`.
- [ ] **Step 3: Add the compact dark-industrial status block.** Place it in the existing communication statistics bar, retain existing colors/layout, show running/error state, written count and optional dropped count, and route the button to MainWindow.
- [ ] **Step 4: Add the one-second refresh.** MainWindow calls `SetBlackBoxStatus(_blackBoxWriter.GetSnapshot())` from the existing clock tick and once after startup. Clear errors visually when the writer snapshot has no `LastError`.
- [ ] **Step 5: Run the markup test and commit.** Expected PASS; no new page or navigation.

### Task 5: Full regression and Acceptance black-box isolation

**Files:**
- Modify only if a test needs a small non-production helper; do not modify Acceptance architecture.
- Test artifacts: Acceptance temporary `LogDirectory/BlackBox`.

- [ ] **Step 1: Run `dotnet restore` and `dotnet build -c Debug`.** Require 0 warnings and 0 errors.
- [ ] **Step 2: Run `dotnet test -c Debug --no-build`.** Require at least 407 total tests, 0 failures, 0 new skips; record Core/Infrastructure counts.
- [ ] **Step 3: Run existing Acceptance.** Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-rfid-acceptance.ps1 -Configuration Debug`; require 8/8 and verify black-box output, if produced, is below the Acceptance temporary `LogDirectory/BlackBox`, not formal `Logs/BlackBox`.
- [ ] **Step 4: Perform the allowed minimal manual check when the UI environment is available.** Verify Normal creates TX/RX, Drop creates TX without the corresponding RX, Normal recovery creates RX, InvalidFrame records RX with `valid=false`, and 560/620 write separate files.
- [ ] **Step 5: Review the final diff.** Run `git status`, `git diff --stat main...HEAD`, `git diff main...HEAD`, confirm no protocol/database/diagnostic/simulator changes, then commit any remaining required integration changes and push with `git push -u origin feat/raw-packet-blackbox`.
