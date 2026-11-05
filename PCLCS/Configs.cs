namespace PCLCS;

public static class Configs
{

    public static readonly DynamicConfigEntry<McInstallManifest> McInstallManifest = new("MinecraftInstallManifest", null);

    // Java

    /// <summary> 当前 Java 相关配置的版本。0 代表尚未从老的 Settings 系统中迁移。</summary>
    public static readonly ConfigEntry<int> JavaConfigVersion = new("JavaConfigVersion", 0);
    public static readonly ConfigEntry<ConcurrentList<Java>> JavaList = new("JavaList", []);
    public static readonly ConfigEntry<List<string>> JavaRemovedList = new("JavaRemovedList", []);
    public static readonly DynamicConfigEntry<bool> JavaMigrated = new("InstanceMigratedJava", false);
    public static readonly DynamicConfigEntry<Java> JavaForced = new("InstanceForcedJava", null);

    // 提示

    public static readonly ConfigEntry<bool> HintModifyInstanceWarning = new("HintModifyInstanceWarning", false);

    static Configs()
    {
        // 当 JavaForced 获取到一个不在 JavaList 中的值时，将其重置为 null。
        JavaForced.PreviewGet += JavaForcedPreviewGet;
    }

    //? JavaList 成员判定读取面：默认全局 Configs.JavaList（原处理器内联）；测试面覆写以隔离真实 %APPDATA%，
    //? 处理器语义1:1 不变
    internal static Func<ConcurrentList<Java>> JavaListReader { get; set; } = () => JavaList.Get()!;

    /// <summary>
    /// JavaForced 的 PreviewGet 自动重置语义（原 static ctor 内联 lambda 逐字提取；1:1 保留，
    /// 不与 SelectOrDownload 分支 3 的手动重置顺手去重）。提取为静态方法以便测试在隔离配置面挂接同一处理器。
    /// </summary>
    internal static void JavaForcedPreviewGet(PreviewGetEventArgs<Java> args, IConfigProvider provider)
    {
        if (args.Value is not null && !JavaListReader().Contains(args.Value))
        {
            args.Value = null;
            JavaForced.Reset(provider);
        }
    }
}