# 脱节报警确认与恢复生命周期设计

## 目标

在不改变 RFID 协议、清除闭环、黑匣子、模拟器和双站场通信架构的前提下，为 `PassageOutcome.UncouplingAlarm` 增加可持久化的确认与恢复生命周期。

## 状态语义

- `PassageClearState` 继续只表示 RFID 设备 Clear 是否完成。
- `AlarmAcknowledgedAt` 表示人工确认时间。
- `AlarmRecoveredAt` 表示现有 Clear → WaitForEmpty → 连续空读 → MarkCleared 闭环完成时间。
- 普通 Warning 不进入确认机制。
- 未确认脱节报警使用每个基站的 `HashSet<Guid>` 锁存；恢复不会自动移除，人工确认才移除。

## 数据兼容

- SQLite schema version 从 2 升级到 3。
- 新增 `alarm_acknowledged_at` 与 `alarm_recovered_at` 可空列。
- v2 中已经 `Cleared` 的脱节报警使用 `COALESCE(cleared_at, completed_at)` 同时回填确认和恢复时间。
- v2 中仍 `PendingClear` 的脱节报警保持两个字段为空。

## 运行时与 UI

- 运行时确认顺序必须是 Store 写入成功 → 删除运行时未确认 PassageId → 刷新视觉状态。
- Store 写入失败时保留红色锁存并把异常交给页面错误区域。
- AlarmHistoryPage 增加报警状态、确认按钮；Warning 显示“无需确认”且不显示确认操作。
- PassageDetailsDialog 增加报警状态、确认时间、恢复时间。
- 恢复启动时按当前站场的稳定 StationId 过滤记录，不能用全局 ProtocolAddress 猜测 560/620 归属。
