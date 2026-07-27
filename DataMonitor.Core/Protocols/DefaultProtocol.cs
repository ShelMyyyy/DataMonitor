namespace DataMonitor.Core.Protocols;

/// <summary>
/// 通用传感器协议
/// 帧头: 0xAA 0x55 | 校验: XOR | 端口: 8888
/// 
/// 校验算法: 所有校验字节依次异或（XOR）
/// 特点: 计算简单，硬件实现成本最低，适合资源受限的单片机
/// </summary>
public class DefaultProtocol : ProtocolCodecBase
{
    public override string Name => "通用传感器协议(AA55)";
    public override byte[] Header => [0xAA, 0x55];

    /// <summary>XOR校验：所有字节异或（最常用的简单校验方式）</summary>
    public override byte CalculateChecksum(byte[] data, int offset, int length)
    {
        byte cs = 0;
        for (int i = offset; i < offset + length; i++) cs ^= data[i];
        return cs;
    }
}
