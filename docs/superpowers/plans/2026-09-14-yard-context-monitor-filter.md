# Phase 3.7：站场上下文隔离底座与实时监控页过滤

## 目标

建立仅依赖 `StationConfig -> DeviceConfig.RfidStationId -> RfidStationConfig.StationId` 的站场解析与当前站场上下文；仅让实时监控页按当前站场或全局视图过滤，保持 Poller、Runtime、UDP、Passage、SQLite 和其他页面全局运行。

## 约束

- 不使用 `ProtocolAddress` 推导新站场关系，不修改 Default 配置来制造绑定。
- 不修改协议、Runtime 状态机、Clear/报警语义、Passage/SQLite schema、History/Alarm/Statistics/Communication/Settings 页面。
- 保留未绑定、缺失、重复绑定诊断；默认站场只按实际配置报告。
- 不提交、不推送。

## 执行任务

1. 增加 resolver、scope、诊断模型及 `CurrentYardContext`，先补充单元测试覆盖显式绑定、禁用站点、缺失/重复/未绑定和全局解析。
2. 将 `MainWindow` 的当前站场选择接入上下文；后台 Poller/Runtime 继续使用全量配置；只向 `MonitorPage` 提供当前 scope 的展示配置和状态快照。
3. 让 MonitorPage 的卡片、概览、报警展示和选中 RFID 跟随当前 scope；切换到全局时恢复全部站点；不改变其他页面。
4. 补充集成/源码约束测试，确认不按 ProtocolAddress 推导、不改 Default、后台全局运行及选中项越界清理。
5. 运行 Core、Infrastructure、Release、Simulator、RFID Acceptance、diff check/status，并记录 Default 实际映射和所有失败链路。
