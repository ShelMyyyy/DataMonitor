using System.Windows;

namespace DataMonitor;

/// <summary>
/// WPF应用程序入口
/// 负责启动前的初始化工作。
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 应用程序启动时调用
    /// 确保插件目录存在，以便 PluginLoader 能够加载插件DLL
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 创建 Plugins 目录（如果不存在）
        // PluginLoader 从此目录加载插件程序集
        // 编译时 CopyPluginDlls Target 会自动将插件DLL复制到这里
        string pluginsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "Plugins");
        if (!System.IO.Directory.Exists(pluginsDir))
            System.IO.Directory.CreateDirectory(pluginsDir);
    }
}
