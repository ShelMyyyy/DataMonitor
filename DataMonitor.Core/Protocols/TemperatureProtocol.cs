namespace DataMonitor.Core.Protocols;

/// <summary>
/// 温度监测仪协议
/// 帧头: 0xBB 0x66 | 校验: 求和取低8位 | 端口: 8889
/// 
/// 校验算法: 所有校验字节相加，取结果的低8位
/// 特点: 相比XOR有更好的错误检测能力（能检测到相邻字节交换等XOR无法发现的错误）
/// </summary>
public class TemperatureProtocol : ProtocolCodecBase
{
    public override string Name => "温度监测协议(BB66)";
    public override byte[] Header => [0xBB, 0x66];

    /// <summary>求和校验：所有字节相加，取低8位</summary>
    public override byte CalculateChecksum(byte[] data, int offset, int length)
    {
        int sum = 0;
        for (int i = offset; i < offset + length; i++) sum += data[i];
        return (byte)(sum & 0xFF);
    }
}
