using System.Diagnostics;
using System.Net.Http;
using MeloongCore;
using MeloongCore.Extensions;
using PCLCS.Common;
using PCLCS.Launch;
using PCLCS.Tests.Net;
using PCLCS.Startup;

namespace PCLCS.Tests.Common;

/// <summary>
/// 事件与杂项业务核测试：GetAbsoluteUrls 决策面（回环下载 ×2 URL、本地路径六分支、UI 线程守卫、下载失败包装）、
/// OpenFileOrExecute IO 核（StartProcess 实跑落盘、安全确认拦截、帮助页回调、异常吞咽）、WriteVariable 注册表写入面、
/// HelpEntry JSON 解析核（形状 Theory 与 TECHDEBT §9 宽松层逐点对照）、GpuPreference 注册表决策、WindowNative 容错。
/// </summary>
public class EventActionsTests : IDisposable
{
    private static readonly HttpClient SharedClient = new(); //短生命周期 HttpClient 会耗尽套接字，用例间共享

    private readonly string _root = Path.Combine(Path.GetTempPath(), "pclcs.event." + Guid.NewGuid().ToString("N"));

    public EventActionsTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
            //临时目录清理失败可容忍
        }
        GpuPreference.ReadOverride = null;
        GpuPreference.WriteOverride = null;
        EventActions.RegistryWriteProbe = null;
    }

    #region GetAbsoluteUrls

    [Fact]
    public void GetAbsoluteUrls_loopback_downloads_json_and_xaml()
    {
        using var server = new LoopbackServer();
        server.Responder = static _ => (200, "body", null);
        var basePath = _root + "base\\";
        var pathTemp = _root + "temp\\";
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(pathTemp);
        var requested = new List<string>();
        var extractCalled = false;
        var result = EventActions.GetAbsoluteUrls(
            server.UrlPrefix + "help/test.json", EventOpenKind.打开帮助,
            (url, localPath) =>
            {
                requested.Add(url);
                File.WriteAllBytes(localPath, SharedClient.GetByteArrayAsync(url).GetAwaiter().GetResult());
                return true;
            },
            () => extractCalled = true,
            basePath: basePath, pathTemp: pathTemp, osDrive: _root + "os\\");
        //下载 ×2（旧 :384-385）：json 与同路径 xaml
        Assert.Equal(2, requested.Count);
        Assert.EndsWith("/help/test.json", requested[0]);
        Assert.EndsWith("/help/test.xaml", requested[1]);
        Assert.True(extractCalled); //HelpTryExtract 在路径解析前无条件触发（旧 :399）
        //下载后落到本地临时文件，按绝对路径分支返回（路径含 ":\"）
        Assert.True(File.Exists(result[0]));
        Assert.EndsWith("test.json", result[0]);
        //旧 :395 返回前统一小写化（与原路径大小写无关）
        Assert.StartsWith((pathTemp + "TaskTemp\\").Lower(), result[0]);
        Assert.Equal(basePath, result[1]);
    }

    [Fact]
    public void GetAbsoluteUrls_download_failure_wraps_message()
    {
        using var server = new LoopbackServer();
        server.Responder = static _ => (404, "", null);
        var ex = Assert.Throws<Exception>(() => EventActions.GetAbsoluteUrls(
            server.UrlPrefix + "help/test.json", EventOpenKind.打开帮助,
            static (url, localPath) =>
            {
                File.WriteAllBytes(localPath, SharedClient.GetByteArrayAsync(url).GetAwaiter().GetResult());
                return true;
            },
            null, basePath: _root + "base\\", pathTemp: _root + "temp\\", osDrive: _root + "os\\"));
        Assert.StartsWith("下载指定的文件失败！", ex.Message);
        Assert.Contains("注意，联网帮助页面须指向一个帮助 JSON 文件，并在同路径下包含相应 XAML 文件！", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void GetAbsoluteUrls_non_json_url_throws()
    {
        var ex = Assert.Throws<Exception>(() => EventActions.GetAbsoluteUrls(
            "http://www.baidu.com/test.txt", EventOpenKind.打开帮助,
            null, null, basePath: _root + "base\\", pathTemp: _root + "temp\\"));
        Assert.StartsWith("联网帮助页面须指向一个帮助 JSON 文件，并在同路径下包含相应 XAML 文件！", ex.Message);
    }

    [Fact]
    public void GetAbsoluteUrls_http_on_ui_thread_throws_before_download()
    {
        var downloadCalled = false;
        var ex = Assert.Throws<Exception>(() => EventActions.GetAbsoluteUrls(
            "http://www.baidu.com/test.json", EventOpenKind.打开帮助,
            (_, _) =>
            {
                downloadCalled = true;
                return true;
            }, null, isUiThread: static () => true));
        Assert.Equal("能打开联网帮助页面的 MyListItem 必须手动设置 Title、Info 属性！", ex.Message);
        Assert.False(downloadCalled);
    }

    [Fact]
    public void GetAbsoluteUrls_absolute_path_branch()
    {
        var absolute = _root + "Somewhere\\Tool.EXE";
        var extractCalled = false;
        var result = EventActions.GetAbsoluteUrls(absolute, EventOpenKind.打开文件, null,
            () => extractCalled = true, basePath: _root + "base\\", pathTemp: _root + "temp\\");
        Assert.True(extractCalled);
        //旧 :395：先 Replace("/", "\") 再 Lower 再 TrimStart("\")，绝对分支原样返回
        Assert.Equal(absolute.Replace("/", "\\").Lower().TrimStart('\\'), result[0]);
        Assert.Equal(_root + "base\\", result[1]);
    }

    [Fact]
    public void GetAbsoluteUrls_relative_to_base_branch()
    {
        var basePath = _root + "base\\";
        Directory.CreateDirectory(basePath);
        File.WriteAllText(basePath + "help.json", "x");
        var result = EventActions.GetAbsoluteUrls("help.json", EventOpenKind.打开文件, null,
            static () => { }, basePath: basePath, pathTemp: _root + "temp\\");
        Assert.Equal(basePath + "help.json", result[0]);
        Assert.Equal(basePath, result[1]);
    }

    [Fact]
    public void GetAbsoluteUrls_relative_to_base_help_branch()
    {
        var basePath = _root + "base\\";
        Directory.CreateDirectory(basePath + "Help\\sub");
        File.WriteAllText(basePath + "Help\\sub\\a.json", "x");
        var result = EventActions.GetAbsoluteUrls("sub\\a.json", EventOpenKind.打开帮助, null,
            static () => { }, basePath: basePath, pathTemp: _root + "temp\\");
        Assert.Equal(basePath + "Help\\sub\\a.json", result[0]);
        Assert.Equal(basePath + "Help\\", result[1]); //WorkingDir 切到 Help 文件夹（旧 :411）
    }

    [Fact]
    public void GetAbsoluteUrls_pathtemp_help_branch_only_for_help_kind()
    {
        var pathTemp = _root + "temp\\";
        Directory.CreateDirectory(pathTemp + "Help");
        File.WriteAllText(pathTemp + "Help\\b.json", "x");
        //打开帮助：命中 PCL 自带帮助文件夹分支（旧 :413）
        var result = EventActions.GetAbsoluteUrls("b.json", EventOpenKind.打开帮助, null,
            static () => { }, basePath: _root + "base\\", pathTemp: pathTemp);
        Assert.Equal(pathTemp + "Help\\b.json", result[0]);
        Assert.Equal(pathTemp + "Help\\", result[1]);
        //打开文件：不命中该分支，落入直接使用原有路径（旧 :418）
        var direct = EventActions.GetAbsoluteUrls("b.json", EventOpenKind.打开文件, null,
            static () => { }, basePath: _root + "base\\", pathTemp: pathTemp);
        Assert.Equal("b.json", direct[0]);
        Assert.Equal(_root + "base\\", direct[1]);
    }

    [Fact]
    public void GetAbsoluteUrls_direct_use_for_file_and_command()
    {
        foreach (var kind in new[] { EventOpenKind.打开文件, EventOpenKind.执行命令 })
        {
            var result = EventActions.GetAbsoluteUrls("Whatever.EXE", kind, null,
                static () => { }, basePath: _root + "base\\", pathTemp: _root + "temp\\");
            Assert.Equal("whatever.exe", result[0]); //旧 :395 小写化后直接使用
            Assert.Equal(_root + "base\\", result[1]);
        }
    }

    [Fact]
    public void GetAbsoluteUrls_not_found_for_help_throws()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => EventActions.GetAbsoluteUrls(
            "nothere.json", EventOpenKind.打开帮助, null,
            static () => { }, basePath: _root + "base\\", pathTemp: _root + "temp\\"));
        Assert.Equal("未找到 EventData 指向的本地 xaml 文件：nothere.json", ex.Message);
        Assert.Equal("nothere.json", ex.FileName);
    }

    #endregion

    #region OpenFileOrExecute

    [Fact]
    public void OpenFileOrExecute_runs_command_in_resolved_working_dir()
    {
        var basePath = _root + "exec\\";
        Directory.CreateDirectory(basePath);
        var cmdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        string? confirmMessage = null;
        EventActions.OpenFileOrExecute(
            $"{cmdPath}|/c cd > out.txt", EventOpenKind.执行命令,
            null, msg =>
            {
                confirmMessage = msg;
                return true;
            }, null, static () => { }, basePath: basePath, pathTemp: _root + "temp\\", osDrive: _root + "os\\");
        Assert.NotNull(confirmMessage);
        Assert.StartsWith("即将执行：", confirmMessage);
        //进程以解析出的 WorkingDirectory 实跑（旧 :210-215 ProcessStartInfo + StartProcess）
        var sw = Stopwatch.StartNew();
        while (!File.Exists(basePath + "out.txt") && sw.ElapsedMilliseconds < 10000) Thread.Sleep(50);
        Assert.True(File.Exists(basePath + "out.txt"), "cmd 未在解析的 WorkingDirectory 下落盘");
    }

    [Fact]
    public void OpenFileOrExecute_safety_decline_prevents_start()
    {
        var basePath = _root + "decline\\";
        Directory.CreateDirectory(basePath);
        var cmdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        EventActions.OpenFileOrExecute(
            $"{cmdPath}|/c cd > out.txt", EventOpenKind.执行命令,
            null, static _ => false, null, static () => { }, basePath: basePath, pathTemp: _root + "temp\\");
        Thread.Sleep(300);
        Assert.False(File.Exists(basePath + "out.txt")); //安全确认拒绝即返回（旧 :209）
    }

    [Fact]
    public void OpenFileOrExecute_help_kind_routes_to_enter_help_page()
    {
        var basePath = _root + "helppage\\";
        Directory.CreateDirectory(basePath + "Help");
        File.WriteAllText(basePath + "Help\\x.json", "x");
        string? entered = null;
        EventActions.OpenFileOrExecute(
            "x.json", EventOpenKind.打开帮助,
            location => entered = location, static _ => true, null, static () => { },
            basePath: basePath, pathTemp: _root + "temp\\");
        Assert.Equal(basePath + "Help\\x.json", entered); //帮助分支回调（PageOtherHelp.EnterHelpPage 留宿主层）
    }

    [Fact]
    public void OpenFileOrExecute_swallows_resolution_failure()
    {
        //旧 :217-220：Catch 内 Logger.Error，异常不外抛
        EventActions.OpenFileOrExecute("nothere.json", EventOpenKind.打开帮助,
            null, static _ => true, null, static () => { },
            basePath: _root + "empty\\", pathTemp: _root + "temp\\");
    }

    #endregion

    #region WriteVariable

    [Fact]
    public void WriteVariable_writes_registry_and_notifies_hint()
    {
        var name = "PclcsT5" + Guid.NewGuid().ToString("N");
        var hintCalled = false;
        try
        {
            EventActions.WriteVariable(name, "SomeValue", () => hintCalled = true);
            var (result, exists) = RegistryUtils.Read(@"HKEY_CURRENT_USER\Software\" + LauncherIdentity.RegistryFolder, "CustomEvent" + name);
            Assert.True(exists);
            Assert.Equal("SomeValue", result);
            Assert.True(hintCalled);
        }
        finally
        {
            RegistryUtils.Delete(@"HKEY_CURRENT_USER\Software\" + LauncherIdentity.RegistryFolder, "CustomEvent" + name);
        }
    }

    [Fact]
    public void WriteVariable_swallows_write_failure()
    {
        var hintCalled = false;
        EventActions.RegistryWriteProbe = static (_, _, _) => throw new UnauthorizedAccessException("测试注入失败");
        //旧 :329-334：Try/Catch 内 Logger.Warn（Toast），异常不外抛且 Hint 不执行
        EventActions.WriteVariable("Var", "Value", () => hintCalled = true);
        Assert.False(hintCalled);
    }

    #endregion

    #region HelpEntry

    private string WriteEntryJson(string json, bool withXaml = false)
    {
        var path = Path.Combine(_root, "entry-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        if (withXaml) File.WriteAllText(Path.ChangeExtension(path, ".xaml"), "<Grid />");
        return path;
    }

    [Fact]
    public void HelpEntry_missing_title_throws()
    {
        var path = WriteEntryJson("""{ "Description": "d" }""", withXaml: true);
        var ex = Assert.Throws<ArgumentException>(() => new HelpEntry(path));
        Assert.Equal("未找到 Title 项", ex.Message);
    }

    [Fact]
    public void HelpEntry_missing_file_throws()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => new HelpEntry(_root + "nothere.json"));
        Assert.Equal("未找到帮助文件：" + _root + "nothere.json", ex.Message);
        Assert.Equal(_root + "nothere.json", ex.FileName);
    }

    [Fact]
    public void HelpEntry_basic_fields_and_defaults()
    {
        var path = WriteEntryJson("""{ "Title": "标题", "Description": "描述", "Keywords": "关键字", "Logo": "logo.png" }""", withXaml: true);
        var entry = new HelpEntry(path);
        Assert.Equal(path, entry.RawPath);
        Assert.Equal("标题", entry.Title);
        Assert.Equal("描述", entry.Desc);
        Assert.Equal("关键字", entry.Search);
        Assert.Equal("logo.png", entry.Logo);
        Assert.Empty(entry.Types);
        Assert.True(entry.ShowInSearch); //缺失保持默认（旧 :461）
        Assert.True(entry.ShowInPublic);
        Assert.True(entry.ShowInSnapshot);
        Assert.False(entry.IsEvent);
        Assert.Equal("<Grid />", entry.XamlContent); //同路径 .xaml 读取（旧 :515-517）
    }

    [Fact]
    public void HelpEntry_show_flags_leniency()
    {
        //§9 数字/字符串宽松：数字经 Convert 语义（1→True），字符串 "true" 同判；null 与缺失同判保持默认
        Assert.True(new HelpEntry(WriteEntryJson("""{ "Title": "t", "ShowInSearch": 1 }""", withXaml: true)).ShowInSearch);
        Assert.False(new HelpEntry(WriteEntryJson("""{ "Title": "t", "ShowInSearch": 0 }""", withXaml: true)).ShowInSearch);
        Assert.True(new HelpEntry(WriteEntryJson("""{ "Title": "t", "ShowInSearch": "true" }""", withXaml: true)).ShowInSearch);
        Assert.False(new HelpEntry(WriteEntryJson("""{ "Title": "t", "ShowInSearch": false }""", withXaml: true)).ShowInSearch);
        Assert.True(new HelpEntry(WriteEntryJson("""{ "Title": "t", "ShowInSearch": null }""", withXaml: true)).ShowInSearch); //统一层：JSON null ≡ 缺失（旧 Newtonsoft 转 False，病态输入差异已记录）
    }

    [Fact]
    public void HelpEntry_types_leniency()
    {
        var entry = new HelpEntry(WriteEntryJson("""{ "Title": "t", "Types": ["分类", 2] }""", withXaml: true));
        Assert.Equal(2, entry.Types.Count);
        Assert.Equal("分类", entry.Types[0]);
        Assert.Equal("2", entry.Types[1]); //数字元素宽松转字符串（旧 late-bind Conversions.ToString 语义）
        //缺失 → 空数组语义
        Assert.Empty(new HelpEntry(WriteEntryJson("""{ "Title": "t" }""", withXaml: true)).Types);
    }

    [Fact]
    public void HelpEntry_event_entry()
    {
        var path = WriteEntryJson("""{ "Title": "t", "IsEvent": true, "EventType": "打开文件", "EventData": "a|b" }""");
        var entry = new HelpEntry(path);
        Assert.True(entry.IsEvent);
        Assert.Equal("打开文件", entry.EventType); //原始串（VB 侧 EnumUtils.FromString 转 CustomEvent.EventType）
        Assert.Equal("a|b", entry.EventData);
        Assert.Null(entry.XamlContent);
    }

    [Fact]
    public void HelpEntry_event_type_numeric()
    {
        //§9 数字宽松：数字与数字串均可达枚举（旧 ToObject(Of Enum) 语义，VB 侧 FromString 收敛）
        Assert.Equal("2", new HelpEntry(WriteEntryJson("""{ "Title": "t", "IsEvent": true, "EventType": 2 }""")).EventType);
        Assert.Equal("2", new HelpEntry(WriteEntryJson("""{ "Title": "t", "IsEvent": true, "EventType": "2" }""")).EventType);
    }

    [Fact]
    public void HelpEntry_missing_xaml_throws()
    {
        var path = WriteEntryJson("""{ "Title": "t" }""");
        var ex = Assert.Throws<FileNotFoundException>(() => new HelpEntry(path));
        Assert.Equal("未找到帮助条目 .json 对应的 .xaml 文件（" + Path.ChangeExtension(path, ".xaml") + "）", ex.Message);
    }

    [Fact]
    public void HelpEntry_case_insensitive_keys()
    {
        //§9 大小写：统一层大小写不敏感（旧 Newtonsoft 索引器大小写敏感——病态输入差异已记录，统一层口径）
        var path = WriteEntryJson("""{ "title": "小写键", "showinsearch": false }""", withXaml: true);
        var entry = new HelpEntry(path);
        Assert.Equal("小写键", entry.Title);
        Assert.False(entry.ShowInSearch);
    }

    [Fact]
    public void HelpEntry_title_number_leniency()
    {
        //§9 数字/字符串宽松：Title 为数字时按旧 late-bind Conversions.ToString 语义转字符串
        var entry = new HelpEntry(WriteEntryJson("""{ "Title": 123 }""", withXaml: true));
        Assert.Equal("123", entry.Title);
    }

    [Fact]
    public void HelpEntry_comments_and_trailing_commas()
    {
        //§9：注释 Skip、尾逗号允许（JsonNodeUtils.DocumentOptions）
        var path = WriteEntryJson("""
            { "Title": "t", //注释
            "ShowInSearch": true, }
            """, withXaml: true);
        Assert.Equal("t", new HelpEntry(path).Title);
    }

    [Fact]
    public void HelpEntry_argument_replace_seam()
    {
        var path = WriteEntryJson("""{ "Title": "v-{pcl_version}" }""", withXaml: true);
        var entry = new HelpEntry(path, static s => s.Replace("{pcl_version}", "9.9"));
        Assert.Equal("v-9.9", entry.Title);
    }

    #endregion

    #region GpuPreference

    [Fact]
    public void GpuPreference_writes_when_missing()
    {
        string? wroteEntry = null;
        object? wroteValue = null;
        GpuPreference.ReadOverride = static (_, _) => (null, false);
        GpuPreference.WriteOverride = (_, entry, value) =>
        {
            wroteEntry = entry;
            wroteValue = value;
        };
        GpuPreference.Set("javaw.exe");
        Assert.Equal("javaw.exe", wroteEntry);
        Assert.Equal("GpuPreference=2;", wroteValue);
    }

    [Fact]
    public void GpuPreference_skips_when_equal()
    {
        var writeCalled = false;
        GpuPreference.ReadOverride = static (_, _) => ("GpuPreference=2;", true);
        GpuPreference.WriteOverride = (_, _, _) => writeCalled = true;
        GpuPreference.Set("javaw.exe");
        Assert.False(writeCalled); //现有设置已是高性能则不写（旧 :807-810）
    }

    [Fact]
    public void GpuPreference_rewrites_when_different()
    {
        var wroteValue = (object?)null;
        GpuPreference.ReadOverride = static (_, _) => ("GpuPreference=1;", true);
        GpuPreference.WriteOverride = (_, _, value) => wroteValue = value;
        GpuPreference.Set("javaw.exe");
        Assert.Equal("GpuPreference=2;", wroteValue);
    }

    #endregion

    #region WindowNative

    [Fact]
    public void WindowNative_dead_handle_does_not_throw()
    {
        //无效句柄：PostMessage/SetForegroundWindow 返回 false 而非抛出，方法整体无异常（旧 :788-793 Try 包裹）
        WindowNative.ShowWindowToTop(new IntPtr(321));
    }

    #endregion
}