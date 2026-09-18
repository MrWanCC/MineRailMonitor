# RFID UDP 原始报文黑匣子设计

## 目标

在不改变 RFID 协议、轮询、识别、报警、Clear 状态机、数据库和双站场通信架构的前提下，旁路记录真实 UDP TX/RX 报文，为现场偶发通信故障保留可独立复核的证据。

## 范围与边界

- 只记录本次应用运行期间产生的原始 UDP 报文，不提供历史查询、筛选、导出或 SQLite 持久化。
- TX 只有在 `RfidUdpTransport.SendAsync` 成功完成后才产生事件。
- RX 继续沿用现有 `DatagramReceived`，包括非法帧、未匹配基站和未知协议地址。
- 记录写入与 UDP 网络线程解耦；网络事件处理只做轻量记录构造和有界入队。
- 不修改 RFID 40 字节协议和现有业务状态机。

## 数据流

```text
RfidUdpTransport
  ├─ DatagramSent / DatagramReceived
  ↓
YardCommunicationContext
  ↓
YardCommunicationManager
  ↓
MainWindow
  ├─ 按 Yard + Endpoint + ProtocolAddress 解析基站身份
  └─ RawPacketBlackBoxWriter.TryEnqueue
```

`RawPacketBlackBoxWriter` 由 `MainWindow` 持有，独立于某个 `YardCommunicationContext`。重建通信 Manager 时只重挂事件，不重建 writer。

## TX 事件

新增 `RfidUdpDatagramSentEventArgs`，携带复制后的 `Data`、复制后的目标 `DestinationEndPoint`、`SentAt` 和可选 `LocalEndPoint`。`RfidUdpTransport.SendAsync` 等待底层 UDP 发送成功后再触发事件，调用者后续修改原数组不会影响事件数据。

TX 命令按真实发送帧识别：Byte4（数组索引 4）为 `01` 时为 `Clear`，否则为 `Read`。不从 UI 事件、SentCount 或请求状态推断 TX。

## 记录与身份

每条 JSONL 记录包含：

`schemaVersion`, `time`, `direction`, `yard`, `stationId`, `local`, `remote`, `protocolAddress`, `length`, `valid`, `validationError`, `command`, `hex`。

- `hex` 为完整大写空格分隔十六进制。
- RX 使用现有 `RfidUdpDatagramEventArgs.IsValid` 和 `ValidationError`。
- 基站匹配使用当前 Context、Endpoint 和 ProtocolAddress；找不到时 `stationId` 为空，但仍写入原始包。
- 相同 ProtocolAddress 的 560/620 记录按 Yard 和 Endpoint 隔离。

## Writer 与文件

- 使用 `BlockingCollection<RawPacketBlackBoxRecord>`，默认容量 10000。
- 单后台消费者，按 Yard/日期各持有一个 `StreamWriter`，`FileMode.Append`，`AutoFlush=true`。
- 队列满时立即返回，`DroppedCount` 加一，不阻塞 UDP。
- 文件位于 `<RootDirectory>/yyyy-MM-dd/<safe-yard>.jsonl`；正式模式 RootDirectory 为 `AppContext.BaseDirectory/Logs/BlackBox`，Acceptance 为 `<AcceptanceOptions.LogDirectory>/BlackBox`。
- Legacy shared listener 使用安全文件名 `LegacySharedListener`。
- Yard 和日期目录均经过安全化/严格格式化，禁止路径逃逸。
- `RetentionDays=30` 保留当前日期及之前 29 个日历日期，删除严格早于 `today - 29 days` 的 `yyyy-MM-dd` 目录；非日期目录不删除。
- 日期由注入的时间提供器或记录时间驱动，跨日时关闭旧 writer 并切换新目录。

## 异常与关闭

- writer 自身的目录、打开、写入和关闭异常被捕获，更新线程安全 `LastError`，当前记录计入丢弃，并继续处理后续记录。
- `Dispose` 幂等：先由 MainWindow 停止通信 Manager，再 `CompleteAdding`、排空队列、Flush 并关闭所有 writer。
- 黑匣子异常不得传播到 UDP 接收、发送、识别、报警或 Clear 业务路径。

## UI

CommunicationPage 现有深色工业风格中增加小型黑匣子状态区：运行状态、成功写入数、丢弃数、简短错误和“打开日志目录”按钮。MainWindow 每秒刷新快照并处理打开目录动作；页面不承担存储逻辑，不创建新导航页。

## 测试策略

先写红测再实现，至少覆盖：真实 TX 事件时序和数据复制、有效/非法 RX JSONL、双站场及相同协议地址隔离、Dispose 排空、30 天保留边界、跨日轮转、磁盘异常恢复、有界队列非阻塞、CommunicationPage markup，以及现有 Acceptance 的路径隔离。
