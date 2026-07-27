# TCP设备上位机系统 — 框架文档

---

## 一、项目架构总览

### 1.1 解决方案结构

```
DataMonitor.sln
├── DataMonitor.Core                  (核心类库 - 协议/模型/接口)
│   ├── Models/Protocol/                 # FrameType, ParameterId, DeviceType
│   ├── Models/                          # TelemetryData, DeviceInfo, DataChannelDef
│   ├── Interfaces/                      # IDevicePlugin
│   ├── Protocols/                       # IProtocolCodec, Default/Temperature/Pressure协议
│   └── Services/                        # DeviceDiscoverer, ProtocolEncoder, ProtocolDecoder
├── DataMonitor.LowerComputer         (下位机模拟器 - 可多实例启动)
├── DataMonitor.Plugins.Default       (默认插件DLL - 兼容旧架构)
├── DataMonitor.Plugins.Temperature   (温度插件DLL - 兼容旧架构)
├── DataMonitor.Plugins.Pressure      (压力插件DLL - 兼容旧架构)
└── DataMonitor                       (WPF上位机)
    ├── Controls/                         # 自定义UserControl（通道卡片+设备卡片）
    ├── Protocols/                        # 三个插件实现
    ├── ViewModels/                       # 4个ViewModel（每个类独立文件）
    ├── Converters/                       # WPF转换器
    └── Configs/                         # 配置文件
```

### 1.2 分层架构图

```
┌─────────────────────────────────────────────────┐
│              WPF View (MainWindow.xaml)          │  ← 数据通道动态渲染
├─────────────────────────────────────────────────┤
│    ViewModel (MainViewModel + DeviceViewModel)   │  ← 多设备管理
├─────────────────────────────────────────────────┤
│         IDevicePlugin (插件接口)                  │  ← TCP连接抽象
├─────────────────────────────────────────────────┤
│   IProtocolCodec → ProtocolCodecBase             │  ← 可插拔协议框架
│   ├── DefaultProtocol    (AA55 + XOR)            │
│   ├── TemperatureProtocol(BB66 + SUM)            │
│   └── PressureProtocol   (CC77 + ~XOR)           │
├─────────────────────────────────────────────────┤
│   DeviceDiscoverer (端口扫描 + 协议匹配)          │  ← 设备发现
└─────────────────────────────────────────────────┘
```

### 1.3 技术栈

| 组件 | 技术选型 |
|------|----------|
| 框架 | .NET 8.0 |
| UI | WPF |
| MVVM | CommunityToolkit.Mvvm 8.4.0 |
| 图表 | LiveChartsCore.SkiaSharpView.WPF 2.0.0-rc3 |
| 通讯 | TCP/IP (System.Net.Sockets) |
| 协议 | 三种自定义二进制协议（不同帧头+校验） |

---

## 二、通讯协议定义

### 2.1 通用帧格式

所有三种协议共用相同的帧结构，区别在于**帧头魔数**和**校验算法**。

| 字段 | 偏移 | 长度 | 说明 |
|------|------|------|------|
| **Header** | 0 | 2 Bytes | 帧头魔数（协议相关） |
| **FrameType** | 2 | 1 Byte | 帧类型 |
| **PayloadLength** | 3 | 2 Bytes | Payload长度（Little-Endian） |
| **Payload** | 5 | N Bytes | 数据载荷 |
| **Checksum** | 5+N | 1 Byte | 校验和（协议相关） |
| **Tail** | 6+N | 2 Bytes | 帧尾：`0x0D 0x0A`（所有协议共用） |

### 2.2 三种协议对比

| 特性 | 通用传感器协议 | 温度监测协议 | 压力控制协议 |
|------|:---:|:---:|:---:|
| **帧头** | `0xAA 0x55` | `0xBB 0x66` | `0xCC 0x77` |
| **校验算法** | XOR（异或） | SUM（求和取低8位） | ~XOR（取反异或） |
| **端口** | 8888 | 8889 | 8890 |
| **数据通道** | 温度/湿度/压力/流量/状态 | 温度/状态 | 压力/流量/状态 |
| **参数数量** | 5个 | 3个 | 3个 |

**校验算法详解：**

```
DefaultProtocol:    checksum = byte[2] ^ byte[3] ^ ... ^ byte[N-1]
TemperatureProtocol: checksum = (byte[2] + byte[3] + ... + byte[N-1]) & 0xFF
PressureProtocol:   checksum = ~(byte[2] ^ byte[3] ^ ... ^ byte[N-1])
```

### 2.3 帧类型定义

| 类型码 | 枚举名 | 方向 | 说明 |
|--------|--------|------|------|
| `0x01` | RealTimeData | 下位机→上位机 | 实时遥测数据自动上传 |
| `0x02` | ReadParamRequest | 上位机→下位机 | 读取设备参数请求 |
| `0x03` | ReadParamResponse | 下位机→上位机 | 读取设备参数响应 |
| `0x04` | WriteParamRequest | 上位机→下位机 | 修改设备参数请求 |
| `0x05` | WriteParamResponse | 下位机→上位机 | 修改设备参数响应 |
| `0x06` | Heartbeat | 下位机→上位机 | 心跳包 |
| `0x07` | InfoRequest | 上位机→下位机 | 设备信息请求（空Payload） |
| `0x08` | InfoResponse | 下位机→上位机 | 设备信息响应 |

### 2.4 各帧 Payload 结构

#### RealTimeData (0x01) — 25 Bytes
```
Timestamp(8B,Int64) | Temperature(4B,float) | Humidity(4B,float) | Pressure(4B,float) | FlowRate(4B,float) | Status(1B)
```

#### ReadParamRequest (0x02) — 1 Byte
```
ParameterId(1B)
```

#### ReadParamResponse (0x03) — 5 Bytes
```
ParameterId(1B) | Value(4B,float)
```

#### WriteParamRequest (0x04) — 5 Bytes
```
ParameterId(1B) | NewValue(4B,float)
```

#### WriteParamResponse (0x05) — 2 Bytes
```
ParameterId(1B) | Result(1B: 0=成功,1=失败)
```

#### Heartbeat (0x06) — 1 Byte
```
Status(1B: 0=正常,1=告警,2=故障)
```

#### InfoResponse (0x08) — 4 Bytes
```
DeviceType(1B) | FirmwareMajor(1B) | FirmwareMinor(1B) | ParameterCount(1B)
```

### 2.5 设备类型定义

| 类型码 | 枚举名 | 名称 | 默认端口 |
|--------|--------|------|----------|
| `0x01` | GeneralSensor | 通用传感器设备 | 8888 |
| `0x02` | TemperatureMonitor | 温度监测仪 | 8889 |
| `0x03` | PressureController | 压力控制器 | 8890 |

### 2.6 设备参数 ID

| ID | 参数名 | 单位 | 通用传感器 | 温度监测仪 | 压力控制器 |
|----|--------|------|:---:|:---:|:---:|
| `0x01` | TargetTemperature | °C | ✓ | ✓ | |
| `0x02` | MaxPressure | kPa | ✓ | | ✓ |
| `0x03` | FlowRateLimit | L/min | ✓ | | ✓ |
| `0x04` | SampleInterval | ms | ✓ | ✓ | ✓ |
| `0x05` | AlarmThreshold | °C | ✓ | ✓ | |

---

## 三、协议框架设计

### 3.1 IProtocolCodec 接口

```csharp
public interface IProtocolCodec
{
    string Name { get; }
    byte[] Header { get; }   // 帧头魔数
    byte[] Tail { get; }     // 帧尾（固定 CR LF）
    byte CalculateChecksum(byte[] data, int offset, int length);
    byte[] BuildFrame(byte frameType, byte[] payload);
    byte[] EncodeReadParamRequest(byte paramId);
    byte[] EncodeWriteParamRequest(byte paramId, float value);
    byte[] EncodeRealTimeData(TelemetryData data);
    byte[] EncodeInfoResponse(byte type, byte maj, byte min, byte cnt);
    DecodeResult? TryDecode(byte[] buffer, int offset, int count, out int consumed);
}
```

### 3.2 添加新协议的步骤

1. 创建新协议类，继承 `ProtocolCodecBase`
2. 定义帧头魔数（2字节，需与已有协议不冲突）
3. 实现 `CalculateChecksum` 校验算法
4. 实现对应的插件类（实现 `IDevicePlugin`，使用该协议）
5. 在 `DeviceDiscoverer.PortProtocols` 中注册端口→协议映射
6. 在下位机中支持该协议类型（修改 `LowerComputerSimulator` 构造函数）

---

## 四、设备发现机制

### 4.1 扫描流程

```
上位机点击"扫描设备"
    │
    ├─ 并行连接 127.0.0.1:8888 (DefaultProtocol)
    ├─ 并行连接 127.0.0.1:8889 (TemperatureProtocol)
    └─ 并行连接 127.0.0.1:8890 (PressureProtocol)
         │
         ├─ TCP连接成功 → 发送 InfoRequest(0x07, 空Payload)
         ├─ 接收 InfoResponse(0x08) → 解析设备类型/版本/参数数
         └─ 添加到设备列表（已下线设备自动移除）
```

### 4.2 DeviceDiscoverer

`DeviceDiscoverer` 维护端口到协议的映射字典，扫描时每个端口使用对应的协议帧头发送请求。

```csharp
PortProtocols = {
    { 8888, new DefaultProtocol() },
    { 8889, new TemperatureProtocol() },
    { 8890, new PressureProtocol() }
};
```

---

## 五、硬件插件

### 5.1 插件列表

| 插件类 | 命名空间 | 协议 | 端口 |
|--------|----------|------|------|
| `DefaultDevicePlugin` | `DataMonitor.Protocols` | DefaultProtocol | 8888 |
| `TemperatureMonitorPlugin` | `DataMonitor.Protocols` | TemperatureProtocol | 8889 |
| `PressureControllerPlugin` | `DataMonitor.Protocols` | PressureProtocol | 8890 |

插件代码位于 `DataMonitor/Protocols/` 文件夹下。

### 5.2 IDevicePlugin 接口

```csharp
public interface IDevicePlugin : IDisposable
{
    string PluginName { get; }
    bool IsConnected { get; }
    Task ConnectAsync(string ip, int port, CancellationToken ct);
    Task DisconnectAsync();
    Task<float> ReadParameterAsync(ParameterId pid, CancellationToken ct);
    Task<bool> WriteParameterAsync(ParameterId pid, float value, CancellationToken ct);
    event EventHandler<TelemetryData>? TelemetryDataReceived;
    event EventHandler<bool>? ConnectionStateChanged;
    event EventHandler<string>? LogMessage;
}
```

---

## 六、WPF 界面设计

### 6.1 主窗口布局

界面采用**三列主布局 + 可折叠底栏**，默认不显示通讯日志，保持界面简洁。

```
┌──────────┬───────────────────────┬──────────────┐
│ 硬件设备  │      实时数据          │   设备参数    │
│          │                       │              │
│ [扫描设备]│ ☑ 温度 85.3 °C        │ 目标温度=80°C│
│          │   ┌──────────────┐   │ [新值:___]   │
│ ●通用传感器│   │  ▁▂▃▄▅▆ 折线图 │   │  [读取][写入] │
│  127.0.0.1│   └──────────────┘   │              │
│  :8888    │ ☑ 湿度 62.1 %        │ 最大压力=200│
│  [连][断] │   ┌──────────────┐   │  [读取][写入] │
│          │   │  ▁▂▃▄▅▆ 折线图 │   │              │
│ ●温度监测仪│   └──────────────┘   │  [读取全部]  │
│  ...     │ ☑ 压力 125.5 kPa     │              │
│          │   ┌──────────────┐   │              │
│          │   │  ▁▂▃▄▅▆ 折线图 │   │              │
│          │   └──────────────┘   │              │
│          │ 最后更新: 12:30:02   │              │
└──────────┴───────────────────────┴──────────────┘
┌─────────────────────────────────────────────────┐
│ 通讯日志 (仅在点击"📋 日志"按钮后显示)            │
│ [12:30:01] [温度监测仪] 已连接          [清空][✕] │
└─────────────────────────────────────────────────┘
```

### 6.2 动态数据通道 + 实时图表

每个通道同时展示**实时数值**和**LiveCharts2 折线图**（最近 60 个数据点）：

- **数值始终可见**：标签、当前值（大字橙色）、单位始终显示，随遥测数据实时刷新
- **图表可选隐藏**：勾选框只控制下方折线图的显隐，不影响数值显示
- **坐标轴可见**：X/Y 轴带浅灰分隔线和数值标签
- **状态通道**：数值显示中文（正常/告警/故障）而非数字

通道类型：

- **通用传感器**：温度、湿度、压力、流量、状态（5个通道）
- **温度监测仪**：温度、状态（2个通道）
- **压力控制器**：压力、流量、状态（3个通道）

### 6.3 通讯日志面板

- **默认隐藏**：启动时不显示通讯日志，界面更简洁
- **切换按钮**：右下角"📋 日志"按钮，点击后底部展开日志面板
- **关闭按钮**：日志面板右上角 ✕ 按钮可关闭

### 6.4 MVVM 架构

| 类 | 职责 |
|----|------|
| `MainViewModel` | 管理设备列表、扫描、数据通道、参数、日志显隐 |
| `DeviceViewModel` | 单个设备的连接状态、插件实例、遥测回调、通道数据 |
| `ChannelViewModel` | 数据通道的标签、当前值、图表历史数据、可见性控制 |
| `ParameterItem` | 单个参数的显示值、编辑值、读/写命令 |
| `DataChannelDef` | 数据通道定义（标签、单位、属性名），位于 Core 层 |

### 6.6 转换器

| 转换器 | 用途 |
|--------|------|
| `BoolToBrushConverter` | 连接状态→颜色（绿/红） |
| `StatusToBrushConverter` | 设备状态文本→颜色 |
| `BoolInvertConverter` | 布尔值取反 |
| `BoolToVisibilityConverter` | 布尔→可见性 |
| `BoolToVisibilityInvertConverter` | 布尔取反→可见性 |
| `HasItemsConverter` | 集合计数→可见性，支持 invert 参数 |

### 6.7 自定义 UserControl

项目使用 WPF **UserControl** 模式封装可复用的 UI 组件：

| 控件 | 文件 | 职责 |
|------|------|------|
| `ChannelCard` | `Controls/ChannelCard.xaml/.cs` | 通道卡片：标签+数值+单位+LiveCharts2图表+可见性勾选 |
| `DeviceCard` | `Controls/DeviceCard.xaml/.cs` | 设备卡片：状态灯+名称+地址+连/断按钮+点击选中 |

**UserControl 的优点：**
- 每个控件的 XAML 和逻辑独立封装，MainWindow 只需一行 `<controls:XXXCard/>` 引用
- 通过 `DataContext` 绑定对应的 ViewModel，保持 MVVM 模式
- 控件内定义自己的 Resources（转换器等），不污染全局资源
- 便于单独测试和替换

**与 CustomControl 的区别：**
- UserControl：XAML+CS 组合，适合界面片段复用 ★当前采用
- CustomControl：继承 Control 基类，用 ControlTemplate/Style 定义外观，适合需要主题化的控件

---

## 七、下位机模拟器

### 7.1 启动方式

```bash
# 单实例启动
dotnet run --project DataMonitor.LowerComputer -- 8888           # 通用传感器
dotnet run --project DataMonitor.LowerComputer -- 8889:temp      # 温度监测仪
dotnet run --project DataMonitor.LowerComputer -- 8890:pressure  # 压力控制器

# 多实例一次启动
dotnet run --project DataMonitor.LowerComputer -- 8888 8889:temp 8890:pressure

# 首次编译后使用 --no-build 跳过编译（避免DLL锁定）
dotnet run --no-build --project DataMonitor.LowerComputer -- 8888 8889:temp 8890:pressure
```

### 7.2 各设备默认参数

| 参数 | 通用传感器 | 温度监测仪 | 压力控制器 |
|------|:---:|:---:|:---:|
| 目标温度 | 80°C | 60°C | — |
| 最大压力 | 200 kPa | — | 300 kPa |
| 流量限制 | 50 L/min | — | 80 L/min |
| 采样间隔 | 1000ms | 1500ms | 500ms |
| 报警阈值 | 95°C | 75°C | — |

---

## 八、调试方法

### 8.1 完整调试流程

```bash
# 1. 编译
dotnet build

# 2. 启动下位机（三个设备）
dotnet run --no-build --project DataMonitor.LowerComputer -- 8888 8889:temp 8890:pressure

# 3. 另开终端，启动上位机
dotnet run --no-build --project DataMonitor

# 4. 在WPF中点击"扫描设备" → 三个设备出现
# 5. 点击设备行选中 → 右侧显示该设备的数据和参数
# 6. 点击"连"按钮连接 → 实时数据开始刷新
# 7. 测试参数读/写
```

### 8.2 常见问题

| 问题 | 原因 | 解决 |
|------|------|------|
| `MSB3026` DLL锁定 | 下位机上次未关闭 | `taskkill /F /IM DataMonitor.LowerComputer.exe` |
| 扫描不到设备 | 下位机未启动 | 先执行步骤2 |
| 连接后无数据 | 协议不匹配 | 确认下位机端口与类型对应 |
| 读参超时 | 网络延迟 | 检查下位机控制台是否有响应日志 |

---

## 九、项目文件清单

```
DataMonitor.sln
│
├── DataMonitor.Core/
│   ├── Models/
│   │   ├── Protocol/FrameType.cs         # 帧类型枚举 (0x01~0x08)
│   │   ├── Protocol/ParameterId.cs       # 参数ID枚举 (0x01~0x05)
│   │   ├── Protocol/DeviceType.cs        # 设备类型枚举 (0x01~0x03)
│   │   ├── TelemetryData.cs             # 遥测数据模型
│   │   ├── DeviceParameter.cs           # 设备参数模型
│   │   └── DeviceInfo.cs                # 设备发现信息 + 通道定义
│   ├── Interfaces/IDevicePlugin.cs      # 插件接口
│   ├── Protocols/
│   │   ├── IProtocolCodec.cs            # 协议接口 + 基类实现
│   │   ├── DefaultProtocol.cs           # AA55 + XOR
│   │   ├── TemperatureProtocol.cs       # BB66 + SUM
│   │   └── PressureProtocol.cs          # CC77 + ~XOR
│   └── Services/
│       ├── ProtocolEncoder.cs           # 协议常量 + 编码器
│       ├── ProtocolDecoder.cs           # 协议解码器 + DecodeResult
│       ├── PluginLoader.cs              # 插件加载器
│       └── DeviceDiscoverer.cs          # 设备扫描发现
│
├── DataMonitor.LowerComputer/
│   └── LowerComputerSimulator.cs        # 多协议下位机模拟器
│
└── DataMonitor/
    ├── Protocols/                        # 插件实现文件夹
    │   ├── DefaultDevicePlugin.cs       # 通用传感器插件
    │   ├── TemperatureMonitorPlugin.cs  # 温度监测仪插件
    │   └── PressureControllerPlugin.cs  # 压力控制器插件
    ├── Controls/                         # 自定义 UserControl
    │   ├── ChannelCard.xaml/.cs         # 通道卡片（标签+数值+LiveCharts2图表）
    │   └── DeviceCard.xaml/.cs          # 设备卡片（指示灯+名称+连/断按钮）
    ├── ViewModels/                       # MVVM ViewModel
    │   ├── MainViewModel.cs             # 主VM（设备管理、扫描、日志）
    │   ├── DeviceViewModel.cs           # 设备VM（连接、遥测、参数）
    │   ├── ChannelViewModel.cs          # 通道VM（图表数据、LiveCharts2 系列）
    │   └── ParameterItem.cs             # 参数VM（读写命令）
    ├── Converters/Converters.cs          # 5个值转换器
    ├── MainWindow.xaml                   # 主窗口（3列布局+可折叠日志底栏）
    ├── MainWindow.xaml.cs                # 纯UI宿主
    └── Configs/plugin_config.json       # 插件配置文件
```
