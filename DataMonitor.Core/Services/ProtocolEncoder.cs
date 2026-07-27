using System.Buffers.Binary;
using DataMonitor.Core.Models.Protocol;

namespace DataMonitor.Core.Services;

/// <summary>
/// 协议帧定义常量
/// 集中管理所有协议相关的魔数、长度常量和映射表，
/// 便于维护和避免硬编码分散。
/// </summary>
public static class ProtocolConstants
{
    /// <summary>帧头标识：0xAA 0x55，用于帧同步</summary>
    public static readonly byte[] Header = [0xAA, 0x55];

    /// <summary>帧尾标识：0x0D 0x0A（CR LF），标记帧结束</summary>
    public static readonly byte[] Tail = [0x0D, 0x0A];

    /// <summary>
    /// 最小帧长度：Header(2) + FrameType(1) + PayloadLength(2) + Checksum(1) + Tail(2) = 8
    /// 任何有效帧都不会小于此值
    /// </summary>
    public const int MinFrameLength = 8;

    /// <summary>
    /// 实时数据Payload长度：Timestamp(8) + Temperature(4) + Humidity(4) + Pressure(4) + FlowRate(4) + Status(1) = 25
    /// </summary>
    public const int RealTimeDataPayloadLength = 25;

    /// <summary>
    /// 写参数请求Payload长度：ParamId(1) + Value(4) = 5
    /// </summary>
    public const int WriteParamPayloadLength = 5;

    /// <summary>
    /// 读参数请求Payload长度：ParamId(1) = 1
    /// </summary>
    public const int ReadParamRequestPayloadLength = 1;

    /// <summary>
    /// 读参数响应Payload长度：ParamId(1) + Value(4) = 5
    /// </summary>
    public const int ReadParamResponsePayloadLength = 5;

    /// <summary>
    /// 写参数响应Payload长度：ParamId(1) + Result(1) = 2
    /// </summary>
    public const int WriteParamResponsePayloadLength = 2;

    /// <summary>
    /// 心跳包Payload长度：Status(1) = 1
    /// </summary>
    public const int HeartbeatPayloadLength = 1;

    /// <summary>
    /// 设备信息响应Payload长度：DeviceType(1)+FirmwareMajor(1)+FirmwareMinor(1)+ParamCount(1) = 4
    /// </summary>
    public const int InfoResponsePayloadLength = 4;

    /// <summary>
    /// 参数ID到中文名称的映射表
    /// 用于日志输出和控制台显示，避免在主逻辑中重复switch
    /// </summary>
    public static readonly Dictionary<ParameterId, string> ParameterNames = new()
    {
        { ParameterId.TargetTemperature, "目标温度" },
        { ParameterId.MaxPressure, "最大压力" },
        { ParameterId.FlowRateLimit, "流量限制" },
        { ParameterId.SampleInterval, "采样间隔" },
        { ParameterId.AlarmThreshold, "报警阈值" }
    };
}

/// <summary>
/// 协议编码器
/// 将高层数据（参数ID、参数值）编码为符合协议的二进制帧。
/// 所有方法都是无状态的静态方法，线程安全。
/// 
/// 帧结构（详见 FRAMEWORK_DOC.md）：
/// | Header(2) | FrameType(1) | PayloadLength(2 LE) | Payload(N) | Checksum(1) | Tail(2) |
/// </summary>
public static class ProtocolEncoder
{
    /// <summary>
    /// 计算XOR校验和
    /// 校验范围：从 FrameType 开始到 Payload 末尾的所有字节
    /// </summary>
    /// <param name="data">完整帧数据（或包含校验范围的数组）</param>
    /// <param name="offset">校验起始偏移（通常是 FrameType 的位置，即2）</param>
    /// <param name="length">校验字节数（3 + PayloadLength）</param>
    /// <returns>1字节异或校验和</returns>
    private static byte CalculateChecksum(byte[] data, int offset, int length)
    {
        byte checksum = 0;
        for (int i = offset; i < offset + length; i++)
            checksum ^= data[i];
        return checksum;
    }

    /// <summary>
    /// 构建完整帧
    /// 按协议顺序组装：Header → FrameType → PayloadLength → Payload → Checksum → Tail
    /// </summary>
    /// <param name="frameType">帧类型</param>
    /// <param name="payload">数据载荷字节数组</param>
    /// <returns>完整的帧字节数组</returns>
    private static byte[] BuildFrame(FrameType frameType, byte[] payload)
    {
        // 计算总帧长：Header(2) + FrameType(1) + Length(2) + Payload(n) + Checksum(1) + Tail(2)
        int totalLength = ProtocolConstants.MinFrameLength + payload.Length;
        byte[] frame = new byte[totalLength];
        int pos = 0;

        // 1. 写入帧头 0xAA 0x55
        Array.Copy(ProtocolConstants.Header, 0, frame, pos, 2);
        pos += 2;

        // 2. 写入帧类型（1字节）
        frame[pos++] = (byte)frameType;

        // 3. 写入Payload长度（2字节，Little-Endian）
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(pos), (ushort)payload.Length);
        pos += 2;

        // 4. 写入Payload数据
        Array.Copy(payload, 0, frame, pos, payload.Length);
        pos += payload.Length;

        // 5. 计算并写入校验和
        //    校验范围：FrameType + PayloadLength + Payload（索引2到pos-1）
        byte checksum = CalculateChecksum(frame, 2, 3 + payload.Length);
        frame[pos++] = checksum;

        // 6. 写入帧尾 0x0D 0x0A
        Array.Copy(ProtocolConstants.Tail, 0, frame, pos, 2);

        return frame;
    }

    /// <summary>
    /// 编码"读参数请求"帧（0x02）
    /// Payload = ParameterId(1字节)
    /// </summary>
    /// <param name="paramId">要读取的参数ID</param>
    /// <returns>完整的 ReadParamRequest 帧</returns>
    public static byte[] EncodeReadParamRequest(ParameterId paramId)
    {
        // Payload 只有1字节：参数ID
        return BuildFrame(FrameType.ReadParamRequest, [(byte)paramId]);
    }

    /// <summary>
    /// 编码"写参数请求"帧（0x04）
    /// Payload = ParameterId(1字节) + NewValue(4字节 float LE)
    /// </summary>
    /// <param name="paramId">要修改的参数ID</param>
    /// <param name="value">新的参数值</param>
    /// <returns>完整的 WriteParamRequest 帧</returns>
    public static byte[] EncodeWriteParamRequest(ParameterId paramId, float value)
    {
        // Payload：1字节参数ID + 4字节float值（Little-Endian）
        byte[] payload = new byte[5];
        payload[0] = (byte)paramId;
        BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(1), value);
        return BuildFrame(FrameType.WriteParamRequest, payload);
    }
}
