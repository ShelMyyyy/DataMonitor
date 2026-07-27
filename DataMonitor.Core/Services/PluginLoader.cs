using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataMonitor.Core.Interfaces;

namespace DataMonitor.Core.Services;

/// <summary>
/// 插件配置模型
/// 对应 Configs/plugin_config.json 文件的JSON结构。
/// 使用 System.Text.Json 反序列化。
/// </summary>
public class PluginConfig
{
    /// <summary>插件名称（用于日志标识）</summary>
    [JsonPropertyName("pluginName")]
    public string PluginName { get; set; } = "DefaultDevice";

    /// <summary>插件DLL文件名（相对于 Plugins 目录）</summary>
    [JsonPropertyName("pluginAssembly")]
    public string PluginAssembly { get; set; } = "DataMonitor.Plugins.Default.dll";

    /// <summary>插件实现类的完全限定名（Namespace.ClassName）</summary>
    [JsonPropertyName("pluginType")]
    public string PluginType { get; set; } = "DataMonitor.Plugins.Default.DefaultDevicePlugin";

    /// <summary>连接参数</summary>
    [JsonPropertyName("connectionSettings")]
    public ConnectionSettings ConnectionSettings { get; set; } = new();
}

/// <summary>
/// 连接设置模型
/// 定义TCP连接的IP、端口和缓冲区配置。
/// </summary>
public class ConnectionSettings
{
    /// <summary>目标设备IP地址</summary>
    [JsonPropertyName("ipAddress")]
    public string IpAddress { get; set; } = "127.0.0.1";

    /// <summary>目标设备TCP端口</summary>
    [JsonPropertyName("port")]
    public int Port { get; set; } = 8888;

    /// <summary>接收缓冲区大小（字节），默认4096</summary>
    [JsonPropertyName("receiveBufferSize")]
    public int ReceiveBufferSize { get; set; } = 4096;
}

/// <summary>
/// 插件加载器
/// 
/// 负责从配置文件或配置对象动态加载设备插件DLL。
/// 
/// 工作流程：
/// 1. 读取JSON配置文件，反序列化为 PluginConfig
/// 2. 从 Plugins/ 目录加载指定的DLL程序集
/// 3. 通过反射查找实现了 IDevicePlugin 的类型
/// 4. 创建实例并自动调用 ConnectAsync 建立连接
/// 5. 返回已连接的插件实例给调用方
/// 
/// 要添加新硬件支持，只需：
/// - 实现 IDevicePlugin 接口的新类库
/// - 将DLL复制到 Plugins/ 目录
/// - 修改 JSON 配置文件指向新插件
/// </summary>
public static class PluginLoader
{
    /// <summary>
    /// 从JSON配置文件加载并连接设备插件
    /// </summary>
    /// <param name="configFilePath">JSON配置文件的绝对或相对路径</param>
    /// <returns>已连接的就绪插件实例</returns>
    /// <exception cref="InvalidOperationException">配置文件解析失败</exception>
    /// <exception cref="FileNotFoundException">插件DLL不存在</exception>
    public static async Task<IDevicePlugin> LoadFromConfigAsync(string configFilePath)
    {
        // 读取并反序列化JSON配置
        string json = await File.ReadAllTextAsync(configFilePath);
        var config = JsonSerializer.Deserialize<PluginConfig>(json)
            ?? throw new InvalidOperationException("无法解析插件配置文件");

        return await LoadPluginAsync(config);
    }

    /// <summary>
    /// 从配置对象加载并连接设备插件
    /// 这是主要的插件加载入口，包含DLL加载、类型查找、实例化和自动连接。
    /// </summary>
    /// <param name="config">插件配置对象</param>
    /// <returns>已连接的就绪插件实例</returns>
    /// <exception cref="FileNotFoundException">插件DLL文件未找到</exception>
    /// <exception cref="InvalidOperationException">插件类型未找到或未实现接口</exception>
    public static async Task<IDevicePlugin> LoadPluginAsync(PluginConfig config)
    {
        // 构建插件DLL的完整路径：{应用目录}/Plugins/{插件文件名}
        string assemblyPath = Path.Combine(
            AppContext.BaseDirectory,
            "Plugins",
            config.PluginAssembly);

        // 检查DLL文件是否存在
        if (!File.Exists(assemblyPath))
            throw new FileNotFoundException($"插件程序集未找到: {assemblyPath}");

        // 动态加载程序集（不锁定文件，允许热更新替换）
        var assembly = Assembly.LoadFrom(assemblyPath);

        // 在加载的程序集中查找实现了IDevicePlugin的类型
        var type = assembly.GetType(config.PluginType)
            ?? throw new InvalidOperationException(
                $"插件类型未找到: {config.PluginType}，请检查 pluginType 配置是否正确");

        // 反射创建实例，验证是否实现了IDevicePlugin接口
        if (Activator.CreateInstance(type) is not IDevicePlugin plugin)
            throw new InvalidOperationException(
                $"插件类型未实现IDevicePlugin接口: {config.PluginType}");

        // 使用配置文件中的连接参数自动连接设备
        await plugin.ConnectAsync(
            config.ConnectionSettings.IpAddress,
            config.ConnectionSettings.Port);

        return plugin;
    }
}
