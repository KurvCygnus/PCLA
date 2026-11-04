using PCL;
using PCLCS.Watch;
using Xunit;

namespace PCLA.BehaviorTests;

/// <summary>
/// 看门狗宿主回调接线时序门（回归：W3 Task 7 评审 Critical）。
/// ModWatcher 模块的 Sub New 是四个宿主回调（StateChanged/LaunchLog/CrashHint/Feedback）的唯一接线落点，
/// 而嵌套类型实例化（New WatcherHost）与委托创建（ldftn）均不触发模块初始化——直连 C# 构造会丢失会话
/// 首个看门狗的全部宿主回调（构造/阶段/窗口/退出日志经 ?.Invoke 静默吞掉、汇总翻转守卫随即闩死，
/// MinecraftStart/MinecraftStop 永不运行）。已由独立 VB 复现钉住该时序（cctor 仅在首个真正的模块成员调用前运行）。
/// 修复：ModWatcher.CreateWatcher 工厂——模块成员调用先完成初始化（旧 :82 经模块内 WatcherStateChanged
/// 调用隐式完成同一保证）；生产单构造路径（工厂）由编译期保证，本测试触发任一模块成员后断言回调非空。
/// </summary>
public class WatcherWiringTests
{
    [Fact]
    public void ProviderCallbacksAreWiredOnceModuleInitialized()
    {
        _ = ModWatcher.HasRunningMinecraft;   //任一模块成员访问即触发模块初始化（与工厂同机制；转发属性只读 C# 静态，无副作用）
        Assert.NotNull(PCLCS.Watch.Watcher.StateChanged);
        Assert.NotNull(PCLCS.Watch.Watcher.LaunchLog);
        Assert.NotNull(PCLCS.Watch.Watcher.CrashHint);
        Assert.NotNull(PCLCS.Watch.Watcher.Feedback);
    }
}