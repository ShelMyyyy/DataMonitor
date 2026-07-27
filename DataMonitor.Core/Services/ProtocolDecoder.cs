using System.Buffers.Binary;
using DataMonitor.Core.Models;
using DataMonitor.Core.Models.Protocol;

namespace DataMonitor.Core.Services;

/// <summary>
/// 协议解码器
/// 从TCP接收到的字节流中解析出完整的协议帧。
/// 
/// 核心方法 TryDecode 采用流式解析策略：
/// 1. 在缓冲区中搜索帧头 0xAA 0x55
/// 2. 读取 PayloadLength 确定帧总长
/// 3. 验证帧尾 0x0D 0x0A
/// 4. 验证XOR校验和
/// 5. 根据 FrameType 进一步解析 Payload
/// 
/// 所有方法都是无状态的静态方法，线程安全。
/// 不完整或损坏的数据会被自动跳过。
/// </summary>
public static class ProtocolDecoder
{
    /// <summary>
    /// 尝试从缓冲区中解析一帧数据
    /// 
    /// 此方法不会修改缓冲区内容，只是读取分析。
    /// 调用者通过 consumedBytes 知道消费了多少字节，
    /// 应自行将未消费数据前移。
    /// </summary>
    /// <param name="buffer">接收缓冲区（包含累积的原始字节数据）</param>
    /// <param name="offset">从缓冲区的哪个位置开始搜索</param>
    /// <param name="count">缓冲区中有效数据的字节数（从offset开始）</param>
    /// <param name="consumedBytes">
    /// 输出参数：已消费的字节数。
    /// - &gt;0 且 result != null：成功解析一帧，应跳过这些字节
    /// - &gt;0 且 result == null：跳过了无效/损坏数据，应继续搜索
    /// - ==0 且 result == null：数据不足以构成完整帧，等待更多数据
    /// </param>
    /// <returns>解析成功的 DecodeResult，null 表示需要更多数据或数据无效</returns>
    public static DecodeResult? TryDecode(byte[] buffer, int offset, int count, out int consumedBytes)
    {
        consumedBytes = 0;

        // ---- 阶段1：搜索帧头 ----
        // 在有效数据范围内查找连续的 0xAA 0x55
        int headerPos = -1;
        for (int i = offset; i < offset + count - 1; i++)
        {
            if (buffer[i] == ProtocolConstants.Header[0] &&
                buffer[i + 1] == ProtocolConstants.Header[1])
            {
                headerPos = i;
                break;
            }
        }

        // 未找到帧头：可以丢弃除最后1字节外的所有数据
        // （保留最后1字节因为帧头可能跨边界）
        if (headerPos < 0)
        {
            consumedBytes = count > 1 ? count - 1 : 0;
            return null;
        }

        // 帧头前面的"垃圾"字节数（由于粘包或数据损坏导致）
        int skipped = headerPos - offset;

        // ---- 阶段2：检查最小帧长 ----
        int remaining = count - skipped;
        if (remaining < ProtocolConstants.MinFrameLength)
        {
            // 数据不足最小帧长，保留已跳过后的数据，等待更多数据到达
            consumedBytes = skipped;
            return null;
        }

        // ---- 阶段3：读取Payload长度 ----
        // PayloadLength 位于帧头后第3个字节（FrameType之后）
        int payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(
            buffer.AsSpan(headerPos + 3));

        // 完整帧长 = MinFrameLength(8) + Payload长度
        int totalFrameLength = ProtocolConstants.MinFrameLength + payloadLength;

        if (remaining < totalFrameLength)
        {
            // 帧尚未完整到达，保留从帧头开始的数据，等待更多数据
            consumedBytes = skipped;
            return null;
        }

        // ---- 阶段4：验证帧尾 ----
        // 帧尾必须在 totalFrameLength 末尾的2字节位置
        int tailPos = headerPos + totalFrameLength - 2;
        if (buffer[tailPos] != ProtocolConstants.Tail[0] ||
            buffer[tailPos + 1] != ProtocolConstants.Tail[1])
        {
            // 帧尾不匹配：说明这不是有效帧，丢弃帧头字节后继续搜索
            consumedBytes = skipped + 1;
            return null;
        }

        // ---- 阶段5：验证XOR校验和 ----
        // 校验范围：FrameType(1) + PayloadLength(2) + Payload(n)
        byte expectedChecksum = 0;
        int checksumStart = headerPos + 2;  // FrameType 的位置
        int checksumLength = 3 + payloadLength; // FrameType + Length + Payload
        for (int i = checksumStart; i < checksumStart + checksumLength; i++)
            expectedChecksum ^= buffer[i];

        // 实际校验和位于帧尾前面1字节
        byte actualChecksum = buffer[headerPos + totalFrameLength - 3];

        if (expectedChecksum != actualChecksum)
        {
            // 校验失败：数据在传输中损坏，丢弃帧头继续搜索
            consumedBytes = skipped + 1;
            return null;
        }

        // ---- 阶段6：解析成功，提取Payload ----
        consumedBytes = skipped + totalFrameLength;

        // 读取帧类型
        FrameType frameType = (FrameType)buffer[headerPos + 2];

        // 提取Payload字节数组（位于索引5，即Header+FrameType+Length之后）
        int payloadStart = headerPos + 5;
        byte[] payload = new byte[payloadLength];
        Array.Copy(buffer, payloadStart, payload, 0, payloadLength);

        // 构建基础解码结果
        var decodeResult = new DecodeResult
        {
            FrameType = frameType,
            Payload = payload
        };

        // ---- 阶段7：根据帧类型二次解析Payload ----
        // 不同帧类型的Payload结构不同，需要分别解析
        switch (frameType)
        {
            case FrameType.RealTimeData:
                // Payload: Timestamp(8) + Temp(4) + Hum(4) + Press(4) + Flow(4) + Status(1)
                decodeResult.TelemetryData = ParseTelemetryData(payload);
                break;

            case FrameType.ReadParamRequest:
                // Payload: ParameterId(1)
                if (payload.Length >= ProtocolConstants.ReadParamRequestPayloadLength)
                    decodeResult.ParameterId = (ParameterId)payload[0];
                break;

            case FrameType.ReadParamResponse:
                // Payload: ParameterId(1) + Value(4 float)
                if (payload.Length >= ProtocolConstants.ReadParamResponsePayloadLength)
                {
                    decodeResult.ParameterId = (ParameterId)payload[0];
                    decodeResult.ParameterValue = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(1));
                }
                break;

            case FrameType.WriteParamRequest:
                // Payload: ParameterId(1) + Value(4 float)
                if (payload.Length >= ProtocolConstants.WriteParamPayloadLength)
                {
                    decodeResult.ParameterId = (ParameterId)payload[0];
                    decodeResult.ParameterValue = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(1));
                }
                break;

            case FrameType.WriteParamResponse:
                // Payload: ParameterId(1) + Result(1)
                if (payload.Length >= ProtocolConstants.WriteParamResponsePayloadLength)
                {
                    decodeResult.ParameterId = (ParameterId)payload[0];
                    decodeResult.Success = payload[1] == 0x00; // 0x00=成功, 0x01=失败
                }
                break;

            case FrameType.Heartbeat:
                // Payload: Status(1)
                if (payload.Length >= 1)
                    decodeResult.DeviceStatus = payload[0];
                break;

            case FrameType.InfoResponse:
                // Payload: DeviceType(1)+FirmwareMajor(1)+FirmwareMinor(1)+ParamCount(1) = 4
                if (payload.Length >= ProtocolConstants.InfoResponsePayloadLength)
                {
                    decodeResult.DeviceInfo = new DeviceInfo
                    {
                        Type = (DeviceType)payload[0],
                        FirmwareMajor = payload[1],
                        FirmwareMinor = payload[2],
                        ParameterCount = payload[3]
                    };
                }
                break;
        }

        return decodeResult;
    }

    /// <summary>
    /// 将25字节Payload解析为 TelemetryData 对象
    /// </summary>
    /// <param name="payload">25字节的RealTimeData Payload</param>
    /// <returns>解析完成的遥测数据</returns>
    /// <exception cref="InvalidDataException">Payload长度不足</exception>
    private static TelemetryData ParseTelemetryData(byte[] payload)
    {
        if (payload.Length < ProtocolConstants.RealTimeDataPayloadLength)
            throw new InvalidDataException(
                $"实时数据payload长度不足: 期望{ProtocolConstants.RealTimeDataPayloadLength}, 实际{payload.Length}");

        return new TelemetryData
        {
            // 字节0~7: DateTime.Ticks (Int64 LE)
            Timestamp = new DateTime(
                BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(0)),
                DateTimeKind.Local),
            // 字节8~11: Temperature (float LE)
            Temperature = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(8)),
            // 字节12~15: Humidity (float LE)
            Humidity = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(12)),
            // 字节16~19: Pressure (float LE)
            Pressure = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(16)),
            // 字节20~23: FlowRate (float LE)
            FlowRate = BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(20)),
            // 字节24: Status (byte)
            Status = payload[24]
        };
    }
}

/// <summary>
/// 解码结果
/// TryDecode 方法的返回值，包含帧的完整解析信息。
/// 根据 FrameType 的不同，各字段的填充情况也不同：
/// 
/// | FrameType           | 有效字段                          |
/// |---------------------|----------------------------------|
/// | RealTimeData        | TelemetryData                    |
/// | ReadParamRequest    | ParameterId                      |
/// | ReadParamResponse   | ParameterId, ParameterValue      |
/// | WriteParamRequest   | ParameterId, ParameterValue      |
/// | WriteParamResponse  | ParameterId, Success             |
/// | Heartbeat           | DeviceStatus                     |
/// </summary>
public class DecodeResult
{
    /// <summary>帧类型</summary>
    public FrameType FrameType { get; set; }

    /// <summary>原始Payload字节数组</summary>
    public byte[] Payload { get; set; } = [];

    /// <summary>遥测数据（仅 RealTimeData 帧有效）</summary>
    public TelemetryData? TelemetryData { get; set; }

    /// <summary>参数ID（读写参数帧有效）</summary>
    public ParameterId? ParameterId { get; set; }

    /// <summary>参数值（读响应和写请求帧有效）</summary>
    public float? ParameterValue { get; set; }

    /// <summary>操作结果（仅 WriteParamResponse 帧有效，true=成功）</summary>
    public bool? Success { get; set; }

    /// <summary>设备状态码（仅 Heartbeat 帧有效）</summary>
    public byte? DeviceStatus { get; set; }

    /// <summary>设备发现信息（仅 InfoResponse 帧有效）</summary>
    public DeviceInfo? DeviceInfo { get; set; }
}
