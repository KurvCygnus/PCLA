namespace PCLCS.Launch;
/// <summary>
/// 高性能显卡偏好核（自宿主层 ModMain.vb:803-814 逐字移植：现有设置一致则跳过，否则写 UserGpuPreferences）。
/// 纯 RegistryUtils 面（复用 MeloongCore 注册表助手，不另建）；调用点 McLaunchPrerun 与 Application --gpu 在宿主层。
/// </summary>
public static class GpuPreference
{
    private const string RegKey = @"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences"; //旧 REG_KEY :804
    private const string RegValue = "GpuPreference=2;"; //旧 REG_VALUE :805
    //? 测试注入面（null 时直连 RegistryUtils）：该键为系统 DirectX 偏好，测试不得写真实注册表
    internal static Func<string, string, (object? result, bool exists)>? ReadOverride;
    internal static Action<string, string, object?>? WriteOverride;
    /// <summary>
    /// 将特定程序设置为使用高性能显卡启动。
    /// 如果失败，则抛出异常。（旧 :803-814 逐字；形参即旧 Executeable 的实际读取面——旧签名仅一个路径，无 AMD/NVIDIA 分支）
    /// </summary>
    public static void Set(string exePath)
    {
        //查看现有设置
        var (existing, _) = ReadOverride is null ? RegistryUtils.Read(RegKey, exePath) : ReadOverride(RegKey, exePath);
        if (existing?.ToString() == RegValue)
        {
            Logger.Info($"无需调整显卡设置：{exePath}");
            return;
        }
        //写入新设置
        if (WriteOverride is null)
        {
            RegistryUtils.Write(RegKey, exePath, RegValue);
        }
        else
        {
            WriteOverride(RegKey, exePath, RegValue);
        }
        Logger.Info($"已调整显卡设置：{exePath}");
    }
}