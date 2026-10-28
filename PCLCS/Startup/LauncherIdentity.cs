namespace PCLCS.Startup;

/// <summary>
/// 启动器身份的唯一来源。窗口标题、数据文件夹、注册表与单例名都从这里取值，
/// 以便与上游 Plain Craft Launcher 隔离并允许两者同时运行。
/// </summary>
public static class LauncherIdentity
{
    /// <summary>显示名，不含窗口标题追加的全角空格。</summary>
    public const string DisplayName = "Plain Craft Launcher (PCL) Alternative";

    /// <summary>启动器数据文件夹名，同时作为 <c>MeloongCore.Main.Init</c> 的应用名。</summary>
    public const string FolderName = "PCLA";

    /// <summary>注册表设置所在的文件夹名。</summary>
    public const string RegistryFolder = "PCLADebug";

    /// <summary>单例互斥量名称。</summary>
    public const string MutexName = "PCLA_SingletonMutex";

    /// <summary>程序集名与启动器在 Minecraft 参数中的名字。</summary>
    public const string AssemblyName = "PCLA";
}