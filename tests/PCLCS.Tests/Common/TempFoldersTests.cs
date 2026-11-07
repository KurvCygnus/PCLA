using System.Text.RegularExpressions;
using PCLCS.Common;
using PCLCS.Startup;

namespace PCLCS.Tests.Common;

/// <summary>
/// 任务临时文件夹核测试：清理闩两态（首次实际清理 / 已清理空转）与备选路径链
/// （主路径成功、带空格回退、主路径建目录失败回退 ProgramData），以及随迁的
/// CheckPermissionWithException 异常面（ModBase.vb:524 逐字）。
/// </summary>
public class TempFoldersTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pclcs.temp." + Guid.NewGuid().ToString("N"));

    public TempFoldersTests()
    {
        Directory.CreateDirectory(_root);
        TempFolders.TestResetGlobals(); //清理闩为进程级状态，逐用例复位
    }

    public void Dispose()
    {
        TempFolders.TestResetGlobals();
        try
        {
            Directory.Delete(_root, true);
        }
        catch
        {
            //临时目录清理失败可容忍
        }
    }

    private string PathTemp => _root + "cache\\";

    private string OsDrive => _root + "os\\";

    private string FallbackRoot => OsDrive + "ProgramData\\" + LauncherIdentity.FolderName + "\\TaskTemp\\";

    #region TryClearTaskTemp 清理闩

    [Fact]
    public void TryClearTaskTemp_first_call_clears_both_roots()
    {
        Directory.CreateDirectory(PathTemp + "TaskTemp");
        File.WriteAllText(PathTemp + "TaskTemp\\a.txt", "x");
        Directory.CreateDirectory(FallbackRoot);
        File.WriteAllText(FallbackRoot + "b.txt", "x");
        TempFolders.TryClearTaskTemp(PathTemp, OsDrive);
        Assert.False(Directory.Exists(PathTemp + "TaskTemp")); //旧 :922
        Assert.False(Directory.Exists(FallbackRoot)); //旧 :921
    }

    [Fact]
    public void TryClearTaskTemp_second_call_is_noop()
    {
        Directory.CreateDirectory(PathTemp + "TaskTemp");
        File.WriteAllText(PathTemp + "TaskTemp\\a.txt", "x");
        TempFolders.TryClearTaskTemp(PathTemp, OsDrive);
        //闩已落：重建后再调用不再清理（旧 :916 只进一次）
        Directory.CreateDirectory(PathTemp + "TaskTemp");
        File.WriteAllText(PathTemp + "TaskTemp\\b.txt", "x");
        TempFolders.TryClearTaskTemp(PathTemp, OsDrive);
        Assert.True(File.Exists(PathTemp + "TaskTemp\\b.txt"));
    }

    [Fact]
    public void TryClearTaskTemp_tolerates_missing_roots()
    {
        //两根均不存在：Delete 无操作不抛（旧 :919-928 Try 包裹）
        TempFolders.TryClearTaskTemp(PathTemp, OsDrive);
    }

    [Fact]
    public void RequestTaskTempFolder_triggers_and_consumes_latch()
    {
        Directory.CreateDirectory(PathTemp + "TaskTemp");
        _ = TempFolders.RequestTaskTempFolder(PathTemp, osDrive: OsDrive);
        File.WriteAllText(PathTemp + "TaskTemp\\keep.txt", "x");
        TempFolders.TryClearTaskTemp(PathTemp, OsDrive); //RequestTaskTempFolder 先行清理（旧 :943），此处闩已落
        Assert.True(File.Exists(PathTemp + "TaskTemp\\keep.txt"));
    }

    #endregion

    #region RequestTaskTempFolder 备选路径链

    [Fact]
    public void RequestTaskTempFolder_primary_success()
    {
        var result = TempFolders.RequestTaskTempFolder(PathTemp, osDrive: OsDrive);
        Assert.StartsWith(PathTemp + "TaskTemp\\", result);
        Assert.EndsWith("\\", result);
        Assert.True(Directory.Exists(result));
    }

    [Fact]
    public void RequestTaskTempFolder_primary_kept_when_no_space_and_no_space_in_path()
    {
        var result = TempFolders.RequestTaskTempFolder(PathTemp, requireNonSpace: true, osDrive: OsDrive);
        Assert.StartsWith(PathTemp + "TaskTemp\\", result); //路径无空格：主路径照常
    }

    [Fact]
    public void RequestTaskTempFolder_falls_back_when_path_has_space()
    {
        var spaceTemp = _root + "with space\\";
        var result = TempFolders.RequestTaskTempFolder(spaceTemp, requireNonSpace: true, osDrive: OsDrive);
        Assert.StartsWith(FallbackRoot, result); //ShouldUseTempFallback（半角空格）→ 退出主路径落备选（旧 :947）
        Assert.True(Directory.Exists(result));
    }

    [Fact]
    public void RequestTaskTempFolder_falls_back_when_primary_create_fails()
    {
        //主路径下 TaskTemp 为同名文件：DirectoryUtils.Create 抛出 → 空 Catch → 备选路径（旧 :945-952）
        Directory.CreateDirectory(PathTemp);
        File.WriteAllText(PathTemp + "TaskTemp", "x");
        var result = TempFolders.RequestTaskTempFolder(PathTemp, osDrive: OsDrive);
        Assert.StartsWith(FallbackRoot, result);
        Assert.True(Directory.Exists(result));
    }

    [Fact]
    public void RequestTaskTempFolder_consecutive_calls_differ()
    {
        //候选以 {Uuid}-{Random} 命名（旧 :946），两次申请不共用
        var first = TempFolders.RequestTaskTempFolder(PathTemp, osDrive: OsDrive);
        var second = TempFolders.RequestTaskTempFolder(PathTemp, osDrive: OsDrive);
        Assert.NotEqual(first, second);
        Assert.Matches("^" + Regex.Escape(PathTemp + "TaskTemp\\") + @"\d+-\d+\\$", first);
    }

    #endregion

    #region CheckPermissionWithException（ModBase.vb:524 逐字随迁面）

    [Fact]
    public void CheckPermissionWithException_empty_throws_argument_null()
    {
        Assert.Throws<ArgumentNullException>(static () => CommonUtils.CheckPermissionWithException(""));
        Assert.Throws<ArgumentNullException>(static () => CommonUtils.CheckPermissionWithException("  "));
    }

    [Fact]
    public void CheckPermissionWithException_system_folder_throws_unauthorized()
    {
        Assert.Throws<UnauthorizedAccessException>(static () =>
            CommonUtils.CheckPermissionWithException("C:\\System Volume Information\\"));
        Assert.Throws<UnauthorizedAccessException>(static () =>
            CommonUtils.CheckPermissionWithException("C:\\$RECYCLE.BIN\\"));
    }

    [Fact]
    public void CheckPermissionWithException_missing_folder_throws_not_found()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            CommonUtils.CheckPermissionWithException(_root + "nothere\\"));
    }

    [Fact]
    public void CheckPermissionWithException_passes_on_writable_folder()
    {
        Directory.CreateDirectory(_root + "ok");
        CommonUtils.CheckPermissionWithException(_root + "ok"); //不抛即通过
    }

    #endregion
}