using PCLCS.Install;
using PCLCS.Startup;
namespace PCLCS.Common;
/// <summary>
/// 任务缓存文件夹核（自宿主层 ModMain.vb:906-958 移植：清理闩 :915-935 逐字、申请链 :942-958 逐字；
/// 带空格判定核已在本体 Install.InstallPlans.ShouldUseTempFallback，权限检查在 <see cref="CommonUtils"/>）。
/// 宿主层模块状态（PathTemp/OsDrive，ModBase.vb:54/:82）经参数注入——GetNativesFolder 同模式，无静态接线时序坑；
/// VB 侧 ModMain 壳同名转发，调用点零改动；CrashAnalyzer 构造回调经本类型直取。
/// </summary>
public static class TempFolders
{
    //清理闩（旧 Private IsTaskTempCleared/IsTaskTempClearing :908-909；进程级单例语义）
    private static bool isTaskTempCleared;
    private static bool isTaskTempClearing;
    /// <summary>
    /// 尝试清理任务缓存文件夹。
    /// 在整次运行中只会实际清理一次。（旧 :915-935 逐字：闩先落再清理、失败仅告警、并发方忙等清理完成）
    /// </summary>
    public static void TryClearTaskTemp(string pathTemp, string? osDrive = null)
    {
        if (!isTaskTempCleared)
        {
            isTaskTempCleared = true;
            isTaskTempClearing = true;
            try
            {
                Logger.Info("开始清理任务缓存文件夹");
                DirectoryUtils.Delete($"{osDrive ?? DefaultOsDrive()}ProgramData\\{LauncherIdentity.FolderName}\\TaskTemp\\");
                DirectoryUtils.Delete($"{pathTemp}TaskTemp\\");
                Logger.Info("已清理任务缓存文件夹");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "清理任务缓存文件夹失败");
            }
            finally
            {
                isTaskTempClearing = false;
            }
        }
        else if (isTaskTempClearing)
        {
            //等待另一个清理步骤完成
            //! 逐字移植豁免：旧 :929-934 的忙等轮询（无锁、无 volatile、Thread.Sleep(1)）为既有语义，逐字保留
            while (isTaskTempClearing)
            {
                Thread.Sleep(1);
            }
        }
    }
    /// <summary>
    /// 申请一个可用于任务缓存的临时文件夹，以 \ 结尾。这些文件夹无需进行后续清理。
    /// 若所有缓存位置均没有权限，会抛出异常。（旧 :942-958 逐字：主路径异常经空 Catch 汇入备选路径）
    /// </summary>
    /// <param name="pathTemp">缓存文件夹（宿主层 PathTemp 注入，以 \ 结尾）。</param>
    /// <param name="requireNonSpace">是否要求路径不包含空格。</param>
    /// <param name="osDrive">系统盘（宿主层 OsDrive 注入；null 按 #3799 派生）。</param>
    public static string RequestTaskTempFolder(string pathTemp, bool requireNonSpace = false, string? osDrive = null)
    {
        TryClearTaskTemp(pathTemp, osDrive);
        string resultFolder;
        try
        {
            resultFolder = $"{pathTemp}TaskTemp\\{CommonUtils.GetUuid()}-{CommonUtils.RandomInteger(0, 1000000)}\\";
            if (!InstallPlans.ShouldUseTempFallback(resultFolder, requireNonSpace))
            {
                DirectoryUtils.Create(resultFolder);
                CommonUtils.CheckPermissionWithException(resultFolder);
                return resultFolder;
            }
            //带空格（旧 :947 Exit Try——跳过 Catch 与主路径返回，直落下方备选路径）
        }
        catch
        {
            //! 逐字移植豁免：旧 :951-952 的空 Catch（吞掉主路径异常，落入备选路径）
        }
        //使用备用路径
        resultFolder = $"{osDrive ?? DefaultOsDrive()}ProgramData\\{LauncherIdentity.FolderName}\\TaskTemp\\{CommonUtils.GetUuid()}-{CommonUtils.RandomInteger(0, 1000000)}\\";
        DirectoryUtils.Create(resultFolder);
        CommonUtils.CheckPermission(resultFolder);
        return resultFolder;
    }
    /// <summary>系统盘（旧宿主层 OsDrive 模块状态的等价派生，ModBase.vb:82，#3799）。</summary>
    private static string DefaultOsDrive() => OsDriveValue.Value;
    private static readonly Lazy<string> OsDriveValue = new(static () => Environment.GetLogicalDrives().First(static p => Directory.Exists(p)));
    //? 测试隔离面：复位清理闩（生产路径不调用）
    internal static void TestResetGlobals()
    {
        isTaskTempCleared = false;
        isTaskTempClearing = false;
    }
}