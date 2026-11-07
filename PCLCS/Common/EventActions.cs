using System.Diagnostics;
using PCLCS.Startup;

namespace PCLCS.Common;

/// <summary>打开类事件类型的读取收敛（旧 CustomEvent.EventType：GetAbsoluteUrls 仅读取 打开帮助（:413）与
/// 打开文件/执行命令（:418）两个判定）。成员名即旧枚举名，ToString 用于日志插值（旧 :403-421 逐字）；
/// 宿主层按名显式映射（不做数值镜像，避免枚举重排的隐式耦合）。</summary>
public enum EventOpenKind
{
    None,
    打开文件,
    打开帮助,
    执行命令
}

/// <summary>
/// 自定义事件的 IO 业务核（自宿主层 ModEvent.vb 移植）：GetAbsoluteUrls 决策面（:362-428）、
/// 打开文件/执行命令 case 核（:197-220）、修改变量/写入变量的注册表面（:327-334）。
/// 弹窗/Hint/页面入口/下载客户端经回调注入（宿主层接线），路径与缓存根模块状态经参数注入（GetNativesFolder 同模式）。
/// </summary>
public static class EventActions
{

    private const string RegKey = $@"HKEY_CURRENT_USER\Software\{LauncherIdentity.RegistryFolder}"; //旧 :330 "HKEY_CURRENT_USER\Software\{RegFolder}"（RegFolder = LauncherIdentity.RegistryFolder，ModSecret.vb:8）

    /// <summary>
    /// 返回自定义事件的绝对 Url。实际返回 {绝对 Url, WorkingDir}。
    /// 失败会抛出异常。（旧 ModEvent.vb:362-428 逐行移植）
    /// </summary>
    /// <param name="relativeUrl">旧 RelativeUrl。</param>
    /// <param name="kind">打开类事件类型（旧 Type 的决策与日志面）。</param>
    /// <param name="downloadClient">下载客户端回调（旧 NetDownloadByClient :384-385，宿主层经 W2 NetRequest 接线）。
    /// 返回是否成功；失败也可直接抛异常，两者同判进入 :386-392 的包装。</param>
    /// <param name="helpTryExtract">内置帮助解压回调（旧 HelpTryExtract :399；资源在宿主程序集，留宿主层）。</param>
    /// <param name="isUiThread">UI 线程判定回调（旧 RunInUi() :366）。null 时视为非 UI 线程（测试直用语义）。</param>
    /// <param name="basePath">基础文件夹（旧 Paths.BaseThenName；null 取真实值，测试注入）。</param>
    /// <param name="pathTemp">缓存文件夹（旧 PathTemp；null 按 %TEMP% 派生，测试注入）。</param>
    /// <param name="osDrive">系统盘（旧 OsDrive；null 按 #3799 派生）。</param>
    public static List<string> GetAbsoluteUrls(string relativeUrl, EventOpenKind kind,
        Func<string, string, bool>? downloadClient, Action? helpTryExtract,
        Func<bool>? isUiThread = null, string? basePath = null, string? pathTemp = null, string? osDrive = null)
    {
        basePath ??= Paths.BaseThenName;
        pathTemp ??= DefaultPathTemp();
        osDrive ??= DefaultOsDrive();

        //网页确认
        if (relativeUrl.StartsWithF("http", true))
        {
            if (isUiThread?.Invoke() ?? false)
            {
                throw new Exception("能打开联网帮助页面的 MyListItem 必须手动设置 Title、Info 属性！");
            }
            //获取文件名
            string rawFileName;
            try
            {
                rawFileName = PathUtils.GetLastPart(relativeUrl);
                if (PathUtils.GetExtension(rawFileName) != "json") throw new Exception("未指向 .json 后缀的文件");
            }
            catch (Exception ex)
            {
                throw new Exception("联网帮助页面须指向一个帮助 JSON 文件，并在同路径下包含相应 XAML 文件！\r\n" +
                                    "例如：\r\n" +
                                    " - https://www.baidu.com/test.json（填写这个路径）\r\n" +
                                    " - https://www.baidu.com/test.xaml（同时也需要包含这个文件）", ex);
            }
            //下载文件
            var localTemp = TempFolders.RequestTaskTempFolder(pathTemp, osDrive: osDrive) + rawFileName;
            Logger.Info($"转换网络资源：{relativeUrl} -> {localTemp}");
            try
            {
                if (downloadClient is null) throw new InvalidOperationException("下载客户端未接线");
                if (!downloadClient(relativeUrl, localTemp) ||
                    !downloadClient(relativeUrl.Replace(".json", ".xaml"), localTemp.Replace(".json", ".xaml")))
                {
                    throw new Exception("下载失败"); //回调返回 false 与抛异常同判（旧 NetDownloadByClient 失败即抛）
                }
            }
            catch (Exception ex)
            {
                throw new Exception("下载指定的文件失败！\r\n" +
                                    "注意，联网帮助页面须指向一个帮助 JSON 文件，并在同路径下包含相应 XAML 文件！\r\n" +
                                    "例如：\r\n" +
                                    " - https://www.baidu.com/test.json（填写这个路径）\r\n" +
                                    " - https://www.baidu.com/test.xaml（同时也需要包含这个文件）", ex);
            }
            relativeUrl = localTemp;
        }
        relativeUrl = relativeUrl.Replace("/", "\\").Lower().TrimStart('\\');

        //确认实际路径
        string location;
        var workingDir = basePath;
        helpTryExtract?.Invoke();
        if (relativeUrl.Contains(":\\"))
        {
            //绝对路径
            location = relativeUrl;
            Logger.Info($"自定义事件中由绝对路径{kind}：{location}");
        }
        else if (FileUtils.Exists(basePath + relativeUrl))
        {
            //相对 PCL 文件夹的路径
            location = basePath + relativeUrl;
            Logger.Info($"自定义事件中由相对 PCL 文件夹的路径{kind}：{location}");
        }
        else if (FileUtils.Exists(basePath + "Help\\" + relativeUrl))
        {
            //相对 PCL 本地帮助文件夹的路径
            location = basePath + "Help\\" + relativeUrl;
            workingDir = basePath + "Help\\";
            Logger.Info($"自定义事件中由相对 PCL 本地帮助文件夹的路径{kind}：{location}");
        }
        else if (kind == EventOpenKind.打开帮助 && FileUtils.Exists(pathTemp + "Help\\" + relativeUrl))
        {
            //相对 PCL 自带帮助文件夹的路径
            location = pathTemp + "Help\\" + relativeUrl;
            workingDir = pathTemp + "Help\\";
            Logger.Info($"自定义事件中由相对 PCL 自带帮助文件夹的路径{kind}：{location}");
        }
        else if (kind is EventOpenKind.打开文件 or EventOpenKind.执行命令)
        {
            //直接使用原有路径启动程序
            location = relativeUrl;
            Logger.Info($"自定义事件中直接{kind}：{location}");
        }
        else
        {
            //打开帮助，但是格式不对劲
            throw new FileNotFoundException($"未找到 EventData 指向的本地 xaml 文件：{relativeUrl}", relativeUrl);
        }

        return [location, workingDir];
    }

    /// <summary>
    /// 打开文件/打开帮助/执行命令 case 的 IO 核（旧 ModEvent.vb:197-220 RunInThread 体内逐行；
    /// 异常吞咽与告警日志在核内，弹窗与页面入口经回调留宿主层）。
    /// </summary>
    /// <param name="arg">完整事件参数（旧 Arg；Arg0/Arg1/Args 在核内按旧 :183-185 重推）。</param>
    /// <param name="kind">打开类事件类型（旧 Type）。</param>
    /// <param name="enterHelpPage">帮助页入口回调（旧 PageOtherHelp.EnterHelpPage(Location) :207，页面域留宿主层）。</param>
    /// <param name="safetyConfirm">安全确认弹窗回调（旧 EventSafetyConfirm :209/:433-444，弹窗与设置留宿主层）。
    /// 返回是否继续执行；null 时为测试直用语义（不拦截）。</param>
    /// <param name="downloadClient">同 <see cref="GetAbsoluteUrls"/>。</param>
    /// <param name="helpTryExtract">同 <see cref="GetAbsoluteUrls"/>。</param>
    /// <param name="isUiThread">同 <see cref="GetAbsoluteUrls"/>。</param>
    /// <param name="basePath">同 <see cref="GetAbsoluteUrls"/>。</param>
    /// <param name="pathTemp">同 <see cref="GetAbsoluteUrls"/>。</param>
    /// <param name="osDrive">同 <see cref="GetAbsoluteUrls"/>。</param>
    public static void OpenFileOrExecute(string arg, EventOpenKind kind, Action<string>? enterHelpPage,
        Func<string, bool>? safetyConfirm, Func<string, string, bool>? downloadClient, Action? helpTryExtract,
        Func<bool>? isUiThread = null, string? basePath = null, string? pathTemp = null, string? osDrive = null)
    {
        try
        {
            var args = arg.Split("|");
            var arg0 = arg.BeforeFirst("|");
            var arg1 = arg.AfterFirst("|");
            //确认实际路径
            var actualPaths = GetAbsoluteUrls(arg0, kind, downloadClient, helpTryExtract, isUiThread, basePath, pathTemp, osDrive);
            var location = actualPaths[0];
            var workingDir = actualPaths[1];
            Logger.Info($"打开类自定义事件实际路径：{location}，工作目录：{workingDir}");
            //执行
            if (kind == EventOpenKind.打开帮助)
            {
                enterHelpPage?.Invoke(location);
            }
            else
            {
                if (safetyConfirm?.Invoke($"即将执行：{location}{(args.Length >= 2 ? $" {arg1}" : "")}") == false) return;
                var info = new ProcessStartInfo
                {
                    Arguments = args.Length >= 2 ? arg1 : "",
                    FileName = location,
                    WorkingDirectory = workingDir
                };
                ProcessStarter.Start(info);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "执行打开类自定义事件失败", LogBehavior.Alert);
        }
    }

    /// <summary>
    /// 修改变量/写入变量 case 的注册表写入核（旧 ModEvent.vb:327-334 的 RegistryUtils 面逐字；
    /// 判长与异常消息在宿主层，成功提示经回调，失败告警在核内）。
    /// </summary>
    /// <param name="arg">变量名（旧 Arg0）。</param>
    /// <param name="data">写入值（旧 Args(1)）。</param>
    /// <param name="hint">写入成功提示回调（旧 If Args.Length = 2 Then Hint(...)；判长由宿主层条件传入）。</param>
    public static void WriteVariable(string arg, string data, Action? hint = null)
    {
        try
        {
            if (RegistryWriteProbe is null)
            {
                RegistryUtils.Write(RegKey, $"CustomEvent{arg}", data);
            }
            else
            {
                RegistryWriteProbe(RegKey, $"CustomEvent{arg}", data);
            }
            hint?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, $"写入变量失败：{arg} → {data}", LogBehavior.Toast);
        }
    }

    //? 测试注入面（null 时直连 RegistryUtils）：写入失败路径需要可控异常
    internal static Action<string, string, object?>? RegistryWriteProbe;

    /// <summary>缓存文件夹（旧 PathTemp 的等价派生：%TEMP%\&lt;应用文件夹&gt;\，仅测试/未注入直用面）。</summary>
    private static string DefaultPathTemp() => PathUtils.AddSlashSuffix(Path.GetTempPath() + LauncherIdentity.FolderName + "\\");

    /// <summary>系统盘（旧宿主层 OsDrive 模块状态的等价派生，ModBase.vb:82，#3799）。</summary>
    private static string DefaultOsDrive() => OsDriveValue.Value;
    private static readonly Lazy<string> OsDriveValue = new(static () => Environment.GetLogicalDrives().First(static p => Directory.Exists(p)));

}