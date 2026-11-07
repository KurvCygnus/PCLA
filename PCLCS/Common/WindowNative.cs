namespace PCLCS.Common;

/// <summary>
/// 窗口置顶 Win32 核（自宿主层 ModMain.vb:787-794 逐字移植；比照 Watcher.cs 的 P/Invoke 先例随核进入 PCLCS，仅 Windows 语义）。
/// 宿主层 ModMain.ShowWindowToTop 壳转发保持调用点零改动；FindWindow/SetForegroundWindow 的 Declare 仍留宿主层
/// （FormMain.xaml.vb:788 与 Application.xaml.vb:176 直用）。
/// </summary>
public static class WindowNative
{

    //! Win32 声明随类迁移（旧 :797 Declare 逐字；VB 的 Long 参数收紧为指针宽度——PostMessageA 的 WPARAM/LPARAM 即指针宽度，
    //! 传 0 行为一致；VB Declare 的 Boolean 返回经 VARIANT_BOOL（2 字节）marshaling，此处以 4 字节 BOOL 对齐 Win32 约定，
    //! 返回值未被使用，与 Watcher.cs 同批注记。DllImport 经 csproj 全局 using System.Runtime.InteropServices）
    [DllImport("user32", EntryPoint = "PostMessageA")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32")]
    private static extern int SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// 把某个 PCL 窗口拖到最前面。（旧 :787-794 逐字）
    /// </summary>
    public static void ShowWindowToTop(IntPtr handle)
    {
        try
        {
            PostMessage(handle, 400 * 16 + 2, 0, 0);
            SetForegroundWindow(handle); //不在这里放不行，神秘 WinAPI，建议别动
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "设置窗口置顶失败", LogBehavior.Toast);
        }
    }

}