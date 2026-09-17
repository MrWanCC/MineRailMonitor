# MineRailMonitor.Simulator 多虚拟基站设计

## 目标

在不修改正式上位机 Protocol、Runtime、Passage、地图或通信业务逻辑的前提下，将 Simulator 从“一个窗口承载一个监听端点”扩展为“一个窗口承载多个相互独立的虚拟 RFID 分站”。默认一次创建 6 个站点，但内部集合支持动态增加、删除和重新配置。

## 范围与约束

- 只修改 `src/MineRailMonitor.Simulator` 及 Simulator 相关测试。
- 继续使用现有 40 Byte 帧、CRC、Read、Clear、14 个槽位、逐张扫描和场景播放协议实现。
- 不修改 `src/MineRailMonitor.Core` 中正式 Protocol、Runtime、Passage、Poller 或数据绑定逻辑。
- 62002 保留给上位机本地 UDP 接收端口；Simulator 默认监听 62001、62003~62007。
- 不把多个虚拟站合并为一个 UDP Endpoint。
- 不提交、不推送。

## 方案选择

### 方案 A：继续在 MainWindow 中维护多套字段

改动最少，但会继续把槽位、计数、日志和监听器耦合在窗口中，并且很容易重新引入固定 6 站限制，不采用。

### 方案 B：`SimulatorStationContext` + 每站独立 Listener（采用）

将单站运行状态和生命周期抽到 `SimulatorStationContext`。每个 Context 拥有自己的配置、14 槽位、场景播放状态、计数器、日志、`RfidSimulatorResponder`、`SimulatorUdpResponder` 和取消令牌。`MainWindow` 只维护 `ObservableCollection<SimulatorStationContext>`、当前选择项和详情页显示桥接。这样可复用一套详情 UI，也可用不同端口运行同地址站点。

### 方案 C：完整 MVVM 重写

边界清晰，但会同时改造现有单站窗口和大量绑定，超出本次联调工具增强范围，不采用。

## 数据与生命周期设计

新增 `SimulatorStationContext`，职责是一个虚拟物理 RFID 分站的完整生命周期：

- `SimulatorStationConfig`：站名、监听 IP、监听端口、协议地址、启用状态。
- `SimulatorStation`：当前协议槽位、命令字节、空槽值和 CRC 参数。
- `ScenarioPlaybackState`：该站自己的序列、播放索引、当前/下一 RFID、播放状态。
- 运行时计数：Request、Response、Clear、Error。
- 最近请求时间、最近命令、请求来源及该站独立日志。
- `SimulatorUdpResponder` 与 `RfidSimulatorResponder`：一站一个实例、一端口一个监听循环。
- ListenIp、ListenPort、ProtocolAddress 均为 Context 的可编辑配置；默认值只是创建预设，不作为运行时硬编码限制。

Context 对外提供以下操作：

```csharp
Task StartAsync(CancellationToken cancellationToken = default)
void Stop()
void ClearSlots()
void ResetScenario()
void LoadScenario(string name, IEnumerable<ushort> sequence)
bool StepScenario()
void StartScenario(TimeSpan interval)
void PauseScenario()
```

真实协议响应仍由现有 `RfidSimulatorResponder` 和 `RfidResponseFrameBuilder` 完成。Context 只负责把本站当前槽位提供给 responder，并接收收发遥测；不增加或改变正式协议字段。

每个 Context 的 UDP listener 绑定自己的 IP/Port。相同 `ProtocolAddress` 只要端口不同，就由不同 Context 的 listener 独立处理，不共享地址路由表。一个 Context 停止、绑定失败或收到异常时，只更新该站状态，不停止其他 Context。

应用配置只针对当前选择站：如果当前站正在运行，先停止并释放旧 UDP listener，再校验新的 ListenIp、ListenPort 和 ProtocolAddress，校验通过后重新绑定新 Endpoint；其他 Context 的 listener、槽位、场景和计数完全不受影响。配置应用失败时当前站保持停止，旧端口不继续占用，错误显示在当前站详情和总览中。

Simulator 启动时从本地 Simulator 配置文件恢复各站最近一次使用的 ListenIp、ListenPort、ProtocolAddress、启用状态及基本站名配置。配置写入使用临时文件替换方式，避免进程异常时破坏已有配置；配置文件只属于 Simulator，不读写正式上位机基站配置。创建默认 6 基站只是首次无配置时的预设，后续启动优先恢复持久化值。

场景播放使用每站独立的播放状态和下一步时间。窗口保留一个 UI DispatcherTimer 负责刷新所有 Context 的播放与显示，Context 之间不共享播放索引、间隔或槽位。普通 Read 只读取当前槽位，不改变场景进度；Clear 只清空对应 Context 的槽位。

## 窗口与 UI 设计

保留现有右侧单站详情区域：连接配置、RFID 槽位、测试场景、场景播放、应答控制、通信日志均改为显示 `SelectedStation` 的数据。

左侧新增“虚拟基站列表”，每行显示：

```text
RFID-01  ●  62001 / 01
```

点击或双击行只切换 `SelectedStation`，不停止或重置其他站。列表提供动态添加和删除入口；删除运行中的站点先停止其 listener，再从集合移除。

增加全局操作：

- 一键创建 6 基站：清理当前虚拟站集合后创建默认 6 站，并保持 62002 不被占用。
- 全部启动：启动所有 Enabled 且配置有效的站点；单站失败只显示该站错误。
- 全部停止：停止并释放全部站点 listener。
- 全部清空：清空所有站点槽位，不改变场景序列。
- 全部重置场景：停止各站播放、清空槽位、索引归零，不关闭 listener。

增加紧凑“多站总览”，显示 Station、Endpoint、Addr、运行状态、场景、播放进度、有效 RFID 数、请求、响应、Clear、异常。双击总览行与左侧列表使用同一个选择逻辑。

右侧现有按钮语义保持不变：场景按钮作用于当前选择站；启动/停止应答作用于当前选择站；日志、槽位编辑和 Clear 只作用于当前选择站。

## 默认站点与预设

默认“一键创建6基站”配置：

```text
RFID-01  127.0.0.1:62001  Addr 01
RFID-02  127.0.0.1:62003  Addr 02
RFID-03  127.0.0.1:62004  Addr 03
RFID-04  127.0.0.1:62005  Addr 04
RFID-05  127.0.0.1:62006  Addr 05
RFID-06  127.0.0.1:62007  Addr 06
```

提供以下联调预设：

1. **6基站并发**：创建上述 6 个本机端点并启动全部站点。
2. **全部正常**：为 6 个站加载 Normal11，每站可保留各自扫卡间隔。
3. **1正常5离线**：只启动 RFID-01，其他站保持停止。
4. **同Address隔离**：RFID-01 使用 `62001 / 01`，RFID-02 使用 `62003 / 01`，允许两站加载不同场景并独立响应。

## 错误处理

- IP、端口、地址和帧参数校验只阻止当前站启动。
- 点击“应用配置”时，当前站先停止旧 listener 并释放旧端口，再校验并绑定新配置；其他站不参与此过程。
- 端口已占用时不崩溃，当前站保持停止，当前站显示“端口已被占用”，总览显示停止/错误状态，其他站继续运行。
- 配置应用失败不得回退为继续使用旧 listener；当前站保持停止，直到用户修正配置并再次应用。
- `Stop` 必须取消接收任务、释放 UDP socket，并允许同一个 Context 重新启动。
- 窗口关闭时停止并释放所有 Context。
- 任何日志/遥测回调都不能直接访问已销毁的窗口控件；窗口只在 Dispatcher 上更新当前站或总览。

## 测试设计

在现有 `MineRailMonitor.Core.Tests` 中增加 Context 级真实 loopback UDP 测试，端口使用动态可用端口，避免依赖现场端口。测试覆盖：

1. 同一进程同时启动 6 个不同端点。
2. 六个端点均能独立响应合法 Read。
3. RFID-01 改槽位不影响 RFID-02。
4. RFID-02 Clear 不清空 RFID-01。
5. 停止一个站不影响其余 5 个。
6. 一个站绑定/运行异常不导致其他 listener 退出。
7. 两个不同端口使用相同 ProtocolAddress 仍能独立响应。
8. 每站 ScenarioPlayer 的 Step、Pause、Reset 和间隔互不影响。
9. Reset 一个站不改变其他站的槽位和播放索引。
10. 全部停止后 6 个端口都能再次绑定并启动。
11. 编辑并应用单站 ListenIp、ListenPort、ProtocolAddress 时只重绑定当前站。
12. 应用到已占用端口时当前站停止并显示“端口已被占用”，其他站仍可收发。
13. 关闭并重新创建 Context/窗口后恢复已持久化的自定义端口和协议地址。

测试还验证普通 Read 不改变场景进度、Clear 只影响目标站，以及重复 RFID 原样保留给正式 Runtime 判断。测试结束使用 `finally` 停止并释放所有 Context。

## 验收方式

实现后执行：

```text
Core Tests
Infrastructure Tests
Release Build
Simulator Build
RFID Acceptance 8/8
git diff --check
```

最后通过 `git status` 确认只存在本次 Simulator/测试及用户已有修改，不 commit、不 push，并提供人工操作方法和测试结果。
