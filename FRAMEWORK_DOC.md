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

### 7.3 添加新设备类型（完整流程）

以添加一个**"流量监测仪"**为例，假设帧头 `0xDD 0x88`，校验算法 CRC8，端口 8891，数据通道为"流量+状态"。

**第1步：定义设备类型枚举**

`DataMonitor.Core/Models/Protocol/DeviceType.cs` — 添加新枚举值：

```csharp
public enum DeviceType : byte
{
    GeneralSensor = 0x01,
    TemperatureMonitor = 0x02,
    PressureController = 0x03,
    FlowMonitor = 0x04    // ← 新增
}
```

**第2步：创建协议编解码类**

`DataMonitor.Core/Protocols/FlowProtocol.cs` — 继承 `IProtocolCodec`，定义帧头 `0xDD 0x88` 和 CRC8 校验：

```csharp
public class FlowProtocol : IProtocolCodec
{
    public string Name => "流量监测协议 (DD88 + CRC8)";
    public byte[] Header => [0xDD, 0x88];
    public byte[] Tail => ProtocolConstants.Tail;  // 0x0D 0x0A 通用

    public byte CalculateChecksum(byte[] data, int offset, int length)
    {
        byte crc = 0;
        for (int i = offset; i < offset + length; i++)
            crc = Crc8Table[data[i] ^ crc];
        return crc;
    }
    // ... 其余接口方法见 IProtocolCodec 定义
}
```

**第3步：注册端口扫描映射**

`DataMonitor.Core/Services/DeviceDiscoverer.cs` — 添加新端口：

```csharp
private static readonly Dictionary<int, IProtocolCodec> PortProtocols = new()
{
    { 8888, new DefaultProtocol() },
    { 8889, new TemperatureProtocol() },
    { 8890, new PressureProtocol() },
    { 8891, new FlowProtocol() }    // ← 新增
};

private static readonly int[] ScanPorts = [8888, 8889, 8890, 8891];
```

**第4步：定义设备信息（通道 + 参数）**

`DataMonitor.Core/Models/DeviceInfo.cs` — 在 `GetDataChannels()` 和 `GetDefaultParameters()` 的 switch 中添加：

```csharp
// GetDataChannels 中添加：
DeviceType.FlowMonitor => new()
{
    new("流量", "L/min", "FlowRate"),
    new("状态", "", "StatusText"),
},

// GetDefaultParameters 中添加：
DeviceType.FlowMonitor => FlowMonitorParameters(),

// 新增参数工厂方法：
private static List<DeviceParameter> FlowMonitorParameters()
{
    return new()
    {
        new() { Id = ParameterId.FlowRateLimit,  Name = "流量限制", Unit = "L/min", Description = "流量上限" },
        new() { Id = ParameterId.SampleInterval, Name = "采样间隔", Unit = "ms",    Description = "上报间隔" },
    };
}
```

**第5步：创建上位机插件**

`DataMonitor/Protocols/FlowMonitorPlugin.cs`（可仿照 `DefaultDevicePlugin.cs`，使用 `FlowProtocol`）：

```csharp
public class FlowMonitorPlugin : IDevicePlugin
{
    private readonly FlowProtocol _proto = new();
    // ... TCP 连接、收发逻辑与 DefaultDevicePlugin 完全相同
}
```

**第6步：下位机模拟器支持**

`DataMonitor.LowerComputer/LowerComputerSimulator.cs` — 构造函数中添加分支：

```csharp
// _proto switch 中添加：
DeviceType.FlowMonitor => new FlowProtocol(),

// _parameters switch 中添加：
DeviceType.FlowMonitor => new()
{
    { ParameterId.FlowRateLimit, 100 },
    { ParameterId.SampleInterval, 800 }
},
```

**第7步：命令行参数解析**

`Program.Main` 中添加类型映射：

```csharp
type = parts[1].ToLower() switch
{
    "temp" => DeviceType.TemperatureMonitor,
    "pressure" => DeviceType.PressureController,
    "flow" => DeviceType.FlowMonitor,    // ← 新增
    _ => DeviceType.GeneralSensor
};
```

**第8步：验证**

```bash
# 启动新设备模拟器
dotnet run --no-build --project DataMonitor.LowerComputer -- 8891:flow

# 另开终端启动上位机
dotnet run --no-build --project DataMonitor
```

点击"扫描设备" → 应出现"流量监测仪"@ 127.0.0.1:8891。

**修改文件汇总：**

| 文件 | 修改内容 |
|------|----------|
| `DeviceType.cs` | 新增枚举值 |
| `FlowProtocol.cs`（新建） | 新协议类 |
| `DeviceDiscoverer.cs` | 端口映射 + 扫描列表 |
| `DeviceInfo.cs` | 通道定义 + 参数工厂 |
| `FlowMonitorPlugin.cs`（新建） | 上位机插件 |
| `LowerComputerSimulator.cs` | 协议/参数分支 |
| `Program.cs` | CLI 类型映射 |

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





 ## 一、整体架构概览

   ┌──────────────────────────────────────────────────────────┐
   │  上位机 (WPF应用 DataMonitor)                             │
   │  ┌────────────────────────────────────────────────┐     │
   │  │  插件层 (IDevicePlugin)                          │     │
   │  │  DefaultDevicePlugin / TemperatureMonitorPlugin │     │
   │  │  → 通过 TcpClient 连接下位机                      │     │
   │  └──────────────────┬─────────────────────────────┘     │
   └──────────────────── │ ──────────────────────────────────┘
                         │  TCP/IP (二进制协议帧)
   ┌──────────────────── │ ──────────────────────────────────┐
   │  下位机模拟器 (DataMonitor.LowerComputer)                │
   │  ┌──────────────────┴─────────────────────────────┐     │
   │  │  LowerComputerSimulator                         │     │
   │  │  → 绑定端口，监听TCP连接                          │     │
   │  │  → 生成模拟传感器数据并周期性发送                   │     │
   │  │  → 响应参数读写指令                               │     │
   │  └────────────────────────────────────────────────┘     │
   └──────────────────────────────────────────────────────────┘

   核心思路：下位机模拟器通过 TCP 监听端口，与上位机的插件建立 TCP 连接后，使用自定义二进制协议帧进行双向通信。

---

   ## 二、模拟器的启动：绑定 TCP 端口

   LowerComputerSimulator 的入口在 Program.Main，支持多实例并行启动：

   [csharp]
   // 支持命令行参数指定端口和设备类型
   // 例如: dotnet run -- 8888 8889:temp 8890:pressure
   new LowerComputerSimulator(s.type).StartAsync(s.port, cts.Token);

   StartAsync 内部创建 TcpListener 监听指定端口：

   [csharp]
   _listener = new TcpListener(IPAddress.Any, port);
   _listener.Start();

   然后进入一个循环，不断 AcceptTcpClientAsync
   接受客户端连接。连接是透明的——对于模拟器来说，它不区分来的是真正的硬件终端还是上位机插件，都是标准的 TCP 客户端。

---

   ## 三、模拟器的核心数据结构

   创建模拟器实例时，根据设备类型初始化了两套数据：

   [csharp]
   // 1. 选择对应的协议编解码器（不同的帧头 + 校验算法）
   _proto = type switch {
       DeviceType.TemperatureMonitor => new TemperatureProtocol(),  // 帧头 BB66, 求和校验
       DeviceType.PressureController  => new PressureProtocol(),    // 帧头 CC77, 取反XOR校验
       _ => new DefaultProtocol()                                   // 帧头 AA55, XOR校验
   };

   // 2. 初始化可调节参数（模拟设备的配置寄存器）
   _parameters = type switch {
       TemperatureMonitor => { TargetTemperature: 60, AlarmThreshold: 75, SampleInterval: 1500 },
       PressureController  => { MaxPressure: 300,    FlowRateLimit: 80, SampleInterval: 500 },
       _                   => { /* 通用传感器全部5个参数 */ }
   };

---

   ## 四、数据发送的核心逻辑

   ### 4.1 每个客户端连接有两个并发循环

   当 TCP 客户端连接上后，HandleClient 方法同时启动两个独立的任务：

   HandleClient
     ├── SendLoop(ns, ct)       ← 定时发送遥测数据（推送模式）
     └── 主循环读数据            ← 接收并处理上位机的指令（请求-响应模式）

   这是一种全双工通信设计。

   ### 4.2 定时发送遥测数据（SendLoop）

   [csharp]
   private async Task SendLoop(NetworkStream ns, CancellationToken ct)
   {
       while (!ct.IsCancellationRequested)
       {
           int iv = (int)_parameters.GetValueOrDefault(ParameterId.SampleInterval, 1000);
           await Task.Delay(Math.Max(iv, 100), ct);           // 按采样间隔等待
           var data = GenData();                              // 生成随机传感器数据
           await ns.WriteAsync(_proto.EncodeRealTimeData(data), ct);  // 编码并发送
       }
   }

   数据是怎么生成的？ GenData() 使用随机数模拟真实传感器：

   [csharp]
   private TelemetryData GenData()
   {
       float bt = _parameters[TargetTemperature];   // 以目标温度为基准
       float t = bt + (Random.NextDouble() * 20 - 10);  // 在 ±10°C 范围内波动
       float h = 40 + (Random.NextDouble() * 40);       // 湿度 40~80%
       float p = 80 + (Random.NextDouble() * mp * 0.5); // 压力在 80 到一半最大压力之间波动
       float f = Random.NextDouble() * fl;              // 流量随机
       // 状态判断：温度超过阈值 → 告警(1)，超过1.2倍 → 故障(2)
       byte s = 0; if (t > at) s = 1; if (t > at * 1.2f) s = 2;
       return new TelemetryData { ... };
   }

   数据是怎么编码发送的？ 通过 _proto.EncodeRealTimeData(data) 将 TelemetryData 编码为 33
   字节的二进制帧（8字节帧头帧尾开销 + 25字节Payload）。帧结构如下：

   ┌──────────┬───────────┬──────────────┬─────────┬──────────┬──────────┐
   │ Header   │ FrameType │ PayloadLength│ Payload │ Checksum │ Tail     │
   │ (2 Byte) │ (1 Byte)  │ (2 Byte LE)  │ (N Byte)│ (1 Byte) │ (2 Byte) │
   └──────────┴───────────┴──────────────┴─────────┴──────────┴──────────┘
     AA55       0x01        25字节LE      25字节     XOR校验   0D0A(CRLF)

   Payload 的具体布局（25字节）：

   ┌──────────┬───────────────────────────────┬─────────┬────────────────────────────────────────────────────────┐
   │ 字节偏移 │ 字段                          │ 类型    │ 说明                                                   │
   ├──────────┼───────────────────────────────┼─────────┼────────────────────────────────────────────────────────┤
   │ 0~7      │ Timestamp                     │ Int64   │ DateTime.Ticks                                         │
   ├──────────┼───────────────────────────────┼─────────┼────────────────────────────────────────────────────────┤
   │ 8~11     │ Temperature                   │ Float32 │ 温度 (°C)                                              │
   ├──────────┼───────────────────────────────┼─────────┼────────────────────────────────────────────────────────┤
   │ 12~15    │ Humidity                      │ Float32 │ 湿度 (%RH)                                             │
   ├──────────┼───────────────────────────────┼─────────┼────────────────────────────────────────────────────────┤
   │ 16~19    │ Pressure                      │ Float32 │ 压力 (kPa)                                             │
   ├──────────┼───────────────────────────────┼─────────┼────────────────────────────────────────────────────────┤
   │ 20~23    │ FlowRate                      │ Float32 │ 流量 (L/min)                                           │
   ├──────────┼───────────────────────────────┼─────────┼────────────────────────────────────────────────────────┤
   │ 24       │ Status                        │ Byte    │ 0=正常 1=告警 2=故障                                   │
   └──────────┴───────────────────────────────┴─────────┴────────────────────────────────────────────────────────┘

   ### 4.3 响应上位机指令（ProcessCmd）

   上位机插件可以发送三种类型的指令帧，模拟器分别处理：

   ┌────────┬────────────────┬───────────────────────────────────────────────────────────────────────────────────┐
   │ 帧类型 │ 指令含义       │ 模拟器的响应                                                                      │
   ├────────┼────────────────┼───────────────────────────────────────────────────────────────────────────────────┤
   │ 0x02   │ 读取参数请求   │ 从 `_parameters` 字典读取值，返回 `ReadParamResponse`(0x03)                       │
   ├────────┼────────────────┼───────────────────────────────────────────────────────────────────────────────────┤
   │ 0x04   │ 写入参数请求   │ 更新 `_parameters` 字典，返回成功/失败 `WriteParamResponse`(0x05)                 │
   ├────────┼────────────────┼───────────────────────────────────────────────────────────────────────────────────┤
   │ 0x08   │ 查询设备信息   │ 返回设备类型 + 固件版本 + 参数数量 `InfoResponse`(0x08)                           │
   └────────┴────────────────┴───────────────────────────────────────────────────────────────────────────────────┘

---

   ## 五、三种协议的差异化设计

   为了让模拟更真实，不同设备类型使用不同的协议变体（帧头 + 校验算法不同），体现了"同一网络中可以共存多种设备"的场景：

   ┌────────────────────────────────┬──────────────────┬──────────────────────┬──────────┬──────────────────────┐
   │ 协议类                         │ 帧头             │ 校验算法             │ 默认端口 │ 对应的设备类型       │
   ├────────────────────────────────┼──────────────────┼──────────────────────┼──────────┼──────────────────────┤
   │ `DefaultProtocol`              │ `0xAA 0x55`      │ XOR                  │ 8888     │ 通用传感器           │
   ├────────────────────────────────┼──────────────────┼──────────────────────┼──────────┼──────────────────────┤
   │ `TemperatureProtocol`          │ `0xBB 0x66`      │ SUM(取低8位)         │ 8889     │ 温度监测仪           │
   ├────────────────────────────────┼──────────────────┼──────────────────────┼──────────┼──────────────────────┤
   │ `PressureProtocol`             │ `0xCC 0x77`      │ ~XOR(按位取反)       │ 8890     │ 压力控制器           │
   └────────────────────────────────┴──────────────────┴──────────────────────┴──────────┴──────────────────────┘

   这使得上位机在解码时，先通过帧头魔数识别是哪一种协议的帧，然后用对应的校验算法验证完整性。真实场景中不同厂商的设备往
   往就是通过这种方式来区分的。

---

   ## 六、上位机插件是如何接收这些数据的

   以 DefaultDevicePlugin 为例，它启动后会运行 ReceiveLoop：

   NetworkStream.ReadAsync → 累积字节到 _receiveBuffer(4096B)
       → 循环调用 ProtocolDecoder.TryDecode() 解析帧
       → ProcessDecodedFrame(result) 分发处理:
           ├── RealTimeData(0x01) → 触发 TelemetryDataReceived 事件 → UI更新
           ├── ReadParamResponse(0x03) → 完成 _pendingResponse(TaskCompletionSource)
           ├── WriteParamResponse(0x05) → 完成 _pendingResponse
           └── Heartbeat(0x06) → 可用于更新设备在线状态

   对于读/写参数，插件使用 TaskCompletionSource 实现请求-响应模式：发送请求帧后创建一个 TaskCompletionSource
   并等待，当接收循环收到对应响应帧时通过 TrySetResult 唤醒等待方，超时时间 5 秒。

---

   ## 总结：一条数据从哪里来、到哪里去

   1. GenData() 用随机数模拟传感器采集，生成 TelemetryData 对象
          ↓
   2. _proto.EncodeRealTimeData(data) 将 TelemetryData 编码为二进制帧
          ↓
   3. NetworkStream.WriteAsync() 通过 TCP 发送字节流
          ↓
   4. 上位机插件的 ReceiveLoop 从 TCP 读取字节流
          ↓
   5. ProtocolDecoder.TryDecode() 解析帧 → 恢复为 TelemetryData 对象
          ↓
   6. TelemetryDataReceived 事件触发 → ViewModel 更新 → WPF UI 刷新显示

   本质就是：用 TCP 作为传输层，用自定义二进制帧作为应用层协议，模拟器负责生成随机数据和响应指令，插件负责连接和解析——
   整个过程完全模拟了真实硬件的工作方式。
