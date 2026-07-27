namespace DataMonitor.Core.Protocols;

/// <summary>
/// 压力控制器协议
/// 帧头: 0xCC 0x77 | 校验: 取反XOR | 端口: 8890
/// 
/// 校验算法: 先对所有校验字节做XOR，再按位取反
/// 特点: 取反操作使校验值不可能为0（除非数据全0），避免与未初始化的内存混淆
/// </summary>
public class PressureProtocol : ProtocolCodecBase
{
    public override string Name => "压力控制协议(CC77)";
    public override byte[] Header => [0xCC, 0x77];

    /// <summary>取反XOR校验：先异或再按位取反（~XOR）</summary>
    public override byte CalculateChecksum(byte[] data, int offset, int length)
    {
        byte cs = 0;
        for (int i = offset; i < offset + length; i++) cs ^= data[i];
        return (byte)~cs; // 按位取反，确保校验值非零
    }
}
