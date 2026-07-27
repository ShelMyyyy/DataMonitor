using System.Buffers.Binary;
using DataMonitor.Core.Models;
using DataMonitor.Core.Services;

namespace DataMonitor.Core.Protocols;

/// <summary>
/// 协议编解码器接口
/// 定义设备通讯协议的完整契约，每种设备类型可实现不同的协议变体。
/// 
/// 设计目的：
/// - 不同硬件厂商可能使用不同的帧格式（不同帧头、校验算法）
/// - 通过此接口，上位机只需依赖抽象协议，不关心具体帧格式
/// - 新增硬件只需实现此接口 + ProtocolCodecBase，无需修改其他代码
/// 
/// 使用方式：
/// - 插件构造函数中创建对应的协议实例（如 new TemperatureProtocol()）
/// - 调用方通过接口方法编解码，无需知晓帧头是 AA55 还是 BB66
/// </summary>
public interface IProtocolCodec
{
    /// <summary>协议名称，用于日志标识（如"温度监测协议(BB66)"）</summary>
    string Name { get; }

    /// <summary>
    /// 帧头标识字节（2字节魔数）
    /// 解码器在缓冲区中扫描此魔数定位帧起始位置
    /// 不同协议使用不同魔数，使得同一网络中可区分不同设备的数据
    /// </summary>
    byte[] Header { get; }

    /// <summary>
    /// 帧尾标识字节（固定 0x0D 0x0A = CR LF）
    /// 解码器验证帧尾以确认帧完整性
    /// </summary>
    byte[] Tail { get; }

    /// <summary>
    /// 计算校验和
    /// 校验范围：从 FrameType 到 Payload 末尾的所有字节（不含Header/Checksum/Tail）
    /// </summary>
    /// <param name="data">完整帧数据</param>
    /// <param name="offset">校验起始偏移（FrameType位置，即2）</param>
    /// <param name="length">校验字节数（3 + PayloadLength）</param>
    /// <returns>1字节校验值</returns>
    byte CalculateChecksum(byte[] data, int offset, int length);

    /// <summary>编码"读参数请求"帧 (FrameType=0x02)</summary>
    byte[] EncodeReadParamRequest(byte paramId);

    /// <summary>编码"写参数请求"帧 (FrameType=0x04)</summary>
    byte[] EncodeWriteParamRequest(byte paramId, float value);

    /// <summary>编码"实时数据"帧 (FrameType=0x01)，Payload=25字节遥测数据</summary>
    byte[] EncodeRealTimeData(TelemetryData data);

    /// <summary>编码"设备信息响应"帧 (FrameType=0x08)</summary>
    byte[] EncodeInfoResponse(byte deviceType, byte fwMajor, byte fwMinor, byte paramCount);

    /// <summary>
    /// 通用帧构造入口
    /// 给定帧类型和Payload，自动添加Header/长度/校验/Tail，输出完整帧字节
    /// </summary>
    /// <param name="frameType">帧类型（0x01~0x08）</param>
    /// <param name="payload">载荷字节数组</param>
    /// <returns>完整的帧字节序列</returns>
    byte[] BuildFrame(byte frameType, byte[] payload);

    /// <summary>
    /// 从字节流缓冲区中尝试解析一帧
    /// 内部执行：搜索帧头 → 读长度 → 验证帧尾 → 验证校验 → 解析Payload
    /// </summary>
    /// <param name="buffer">接收缓冲区</param>
    /// <param name="offset">搜索起始位置</param>
    /// <param name="count">有效数据字节数</param>
    /// <param name="consumed">输出：已消费字节数（含被跳过的无效字节）</param>
    /// <returns>解析成功返回DecodeResult，需要更多数据或数据无效返回null</returns>
    DecodeResult? TryDecode(byte[] buffer, int offset, int count, out int consumed);
}

/// <summary>
/// 协议编解码器基类
/// 提供完整的帧组装和解析逻辑。子类只需定义 Header、校验算法和协议名称。
/// 
/// 帧结构（所有协议统一）：
/// ┌──────────┬───────────┬──────────────┬─────────┬──────────┬──────────┐
/// │ Header   │ FrameType │ PayloadLength│ Payload │ Checksum │ Tail     │
/// │ (2 Byte) │ (1 Byte)  │ (2 Byte LE)  │ (N Byte)│ (1 Byte) │ (2 Byte) │
/// └──────────┴───────────┴──────────────┴─────────┴──────────┴──────────┘
/// 
/// 三种协议差异对比：
/// | 协议          | 帧头      | 校验算法      | 端口 |
/// | DefaultProtocol    | 0xAA 0x55 | XOR           | 8888 |
/// | TemperatureProtocol| 0xBB 0x66 | SUM(低8位)     | 8889 |
/// | PressureProtocol   | 0xCC 0x77 | ~XOR(取反)    | 8890 |
/// </summary>
public abstract class ProtocolCodecBase : IProtocolCodec
{
    public abstract string Name { get; }
    public abstract byte[] Header { get; }
    public byte[] Tail => [0x0D, 0x0A]; // 所有协议共用CR LF作为帧尾
    public abstract byte CalculateChecksum(byte[] data, int offset, int length);

    /// <summary>最小帧长度 = Header(2)+Type(1)+Len(2)+CS(1)+Tail(2) = 8</summary>
    protected const int MinFrameLen = 8;

    // ================================================================
    // 编码方法 —— 将业务数据转为二进制帧
    // ================================================================

    /// <summary>读参数请求帧: Payload = ParamId(1字节)</summary>
    public byte[] EncodeReadParamRequest(byte paramId)
        => BuildFrame(0x02, [(byte)paramId]);

    /// <summary>写参数请求帧: Payload = ParamId(1) + Value(4 float LE)</summary>
    public byte[] EncodeWriteParamRequest(byte paramId, float value)
    {
        byte[] p = new byte[5]; p[0] = paramId;
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(1), value);
        return BuildFrame(0x04, p);
    }

    /// <summary>
    /// 实时数据帧: Payload = Timestamp(8) + Temp(4) + Hum(4) + Press(4) + Flow(4) + Status(1) = 25字节
    /// </summary>
    public byte[] EncodeRealTimeData(TelemetryData data)
    {
        byte[] p = new byte[25];
        BinaryPrimitives.WriteInt64LittleEndian(p.AsSpan(0), data.Timestamp.Ticks);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(8), data.Temperature);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(12), data.Humidity);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(16), data.Pressure);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(20), data.FlowRate);
        p[24] = data.Status;
        return BuildFrame(0x01, p);
    }

    /// <summary>设备信息响应帧: Payload = DeviceType(1)+FwMajor(1)+FwMinor(1)+ParamCount(1)</summary>
    public byte[] EncodeInfoResponse(byte deviceType, byte fwMajor, byte fwMinor, byte paramCount)
        => BuildFrame(0x08, [deviceType, fwMajor, fwMinor, paramCount]);

    /// <summary>
    /// 通用帧构造（公开方法，供下位机和插件共同使用）
    /// 组装顺序：Header → FrameType → PayloadLength(LE) → Payload → Checksum → Tail
    /// </summary>
    public byte[] BuildFrame(byte frameType, byte[] payload)
    {
        int total = MinFrameLen + payload.Length;
        byte[] f = new byte[total]; int pos = 0;
        // 1. 写入帧头（子类定义的魔数）
        Array.Copy(Header, 0, f, pos, 2); pos += 2;
        // 2. 写入帧类型
        f[pos++] = frameType;
        // 3. 写入Payload长度（2字节Little-Endian）
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(pos), (ushort)payload.Length); pos += 2;
        // 4. 写入Payload数据
        Array.Copy(payload, 0, f, pos, payload.Length); pos += payload.Length;
        // 5. 计算并写入校验和（从FrameType到Payload末尾）
        f[pos++] = CalculateChecksum(f, 2, 3 + payload.Length);
        // 6. 写入帧尾
        Array.Copy(Tail, 0, f, pos, 2);
        return f;
    }

    // ================================================================
    // 解码方法 —— 从字节流中恢复业务数据
    // ================================================================

    /// <summary>
    /// 从缓冲区尝试解码一帧
    /// 7阶段解析流程：
    /// ① 搜索帧头魔数 → ② 检查最小帧长 → ③ 读取Payload长度 → 
    /// ④ 验证帧尾 → ⑤ 验证校验和 → ⑥ 提取Payload → ⑦ 按帧类型二次解析
    /// 
    /// consumedBytes 的语义：
    ///   &gt;0 且 result != null : 成功解析，前进 consumedBytes 字节
    ///   &gt;0 且 result == null : 跳过了无效数据，前进 consumedBytes 字节继续搜索
    ///   ==0 且 result == null : 数据不足，等待更多数据后再调用
    /// </summary>
    public DecodeResult? TryDecode(byte[] buffer, int offset, int count, out int consumed)
    {
        consumed = 0;
        // ① 搜索帧头（子类的Header魔数，如0xAA 0x55）
        int hp = -1;
        for (int i = offset; i < offset + count - 1; i++)
            if (buffer[i] == Header[0] && buffer[i + 1] == Header[1]) { hp = i; break; }
        if (hp < 0) { consumed = count > 1 ? count - 1 : 0; return null; }

        // 帧头前面的无效字节数
        int skipped = hp - offset;
        int rem = count - skipped;

        // ② 数据是否够最小帧长？
        if (rem < MinFrameLen) { consumed = skipped; return null; }

        // ③ 读取Payload长度（2字节LE）
        int plen = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(hp + 3));
        int total = MinFrameLen + plen; // 完整帧总长

        // 帧是否完整到达？
        if (rem < total) { consumed = skipped; return null; }

        // ④ 验证帧尾是否匹配 CR LF
        if (buffer[hp + total - 2] != Tail[0] || buffer[hp + total - 1] != Tail[1])
        { consumed = skipped + 1; return null; }

        // ⑤ 验证校验和（核心完整性检查）
        byte expected = CalculateChecksum(buffer, hp + 2, 3 + plen);
        if (expected != buffer[hp + total - 3]) { consumed = skipped + 1; return null; }

        // ⑥ 提取Payload字节
        consumed = skipped + total;
        byte ft = buffer[hp + 2]; // 帧类型
        byte[] payload = new byte[plen];
        Array.Copy(buffer, hp + 5, payload, 0, plen);

        // ⑦ 按帧类型二次解析
        var r = new DecodeResult { FrameType = (Models.Protocol.FrameType)ft, Payload = payload };
        ParsePayload(r, ft, payload);
        return r;
    }

    /// <summary>
    /// 根据帧类型解析Payload中的业务字段
    /// 子类可重写此方法以支持自定义帧类型
    /// </summary>
    protected virtual void ParsePayload(DecodeResult r, byte frameType, byte[] payload)
    {
        switch (frameType)
        {
            case 0x01: // RealTimeData: 25字节遥测数据
                r.TelemetryData = ParseTelemetry(payload);
                break;
            case 0x02: // ReadParamRequest: ParamId(1)
                if (payload.Length >= 1) r.ParameterId = (Models.Protocol.ParameterId)payload[0];
                break;
            case 0x03: // ReadParamResponse: ParamId(1)+Value(4)
                if (payload.Length >= 5)
                { r.ParameterId = (Models.Protocol.ParameterId)payload[0]; r.ParameterValue = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(1)); }
                break;
            case 0x04: // WriteParamRequest: ParamId(1)+Value(4)
                if (payload.Length >= 5)
                { r.ParameterId = (Models.Protocol.ParameterId)payload[0]; r.ParameterValue = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(1)); }
                break;
            case 0x05: // WriteParamResponse: ParamId(1)+Result(1)
                if (payload.Length >= 2)
                { r.ParameterId = (Models.Protocol.ParameterId)payload[0]; r.Success = payload[1] == 0x00; }
                break;
            case 0x06: // Heartbeat: Status(1)
                if (payload.Length >= 1) r.DeviceStatus = payload[0];
                break;
            case 0x08: // InfoResponse: DeviceType(1)+FwMajor(1)+FwMinor(1)+ParamCount(1)
                if (payload.Length >= 4)
                { r.DeviceInfo = new DeviceInfo { Type = (Models.Protocol.DeviceType)payload[0], FirmwareMajor = payload[1], FirmwareMinor = payload[2], ParameterCount = payload[3] }; }
                break;
        }
    }

    /// <summary>从25字节Payload解析遥测数据对象</summary>
    protected TelemetryData ParseTelemetry(byte[] p)
    {
        if (p.Length < 25) throw new InvalidDataException("实时数据长度不足");
        return new TelemetryData
        {
            Timestamp = new DateTime(BinaryPrimitives.ReadInt64LittleEndian(p.AsSpan(0)), DateTimeKind.Local),
            Temperature = BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(8)),
            Humidity = BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(12)),
            Pressure = BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(16)),
            FlowRate = BinaryPrimitives.ReadSingleLittleEndian(p.AsSpan(20)),
            Status = p[24]
        };
    }
}
