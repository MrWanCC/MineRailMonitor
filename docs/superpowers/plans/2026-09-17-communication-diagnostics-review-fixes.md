# 通信诊断 reviewer 修复计划

## 目标

在 `feat/communication-diagnostics` 上以最小改动修复 pending 请求超时漏算、等待状态误计离线和 TX 日志重复/误报命令三个问题，保留当前通信测试页面视觉方向，不改 RFID 核心协议与业务逻辑。

## 步骤

1. 阅读 `RfidStationPollingStatus`、轮询/协调器、通信页和现有 Core/UI/Acceptance 测试，确认现有事件与状态流。
2. 先补 FIFO 响应匹配、等待/在线/离线统计和真实 TX 日志行为测试，运行相关测试确认失败。
3. 采用 FIFO 单请求消费，保留未匹配请求到超时；用 `LastSentAt` 区分等待与已尝试状态；移除基于 `SentCount` 猜测命令的 TX 日志，保留实际发送路径日志并避免手动重复。
4. 将“平均响应时间”改为准确的“最近平均响应”文案，保持现有 XAML 布局和样式。
5. 运行 `dotnet restore`、`dotnet build`、`dotnet test` 及可执行的 Acceptance 测试，修复本轮引入的问题。
6. 提交到当前分支并推送，不合并或修改 `main`；最后核验远程 HEAD、工作区和测试结果。

## 成功标准

- A/B 两个请求只收到一个响应时，最终 `ReceivedCount = 1` 且丢失请求 `TimeoutCount = 1`。
- 尚未发送请求的基站计为等待，不计入离线，在线率不把等待当离线。
- Read/Clear 日志来自真实命令，同一次手动发送不重复，最多保留 50 条。
- build、test、Acceptance 结果明确且无新增编译警告。
