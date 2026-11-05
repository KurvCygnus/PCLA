using PCL;
using PCLA.BehaviorTests.Fixtures;
using System;
using System.IO;

namespace PCLA.BehaviorTests;

public sealed class CrashAnalyzerTests : IDisposable
{
    private string _root = "";

    public CrashAnalyzerTests()
    {
        BehaviorTestHost.Initialize();
    }

    [Theory]
    [MemberData(nameof(CrashCases.All), MemberType = typeof(CrashCases))]
    public void ClassifiesKnownCrash(CrashCase c)
    {
        _root = BehaviorTestHost.CreateCaseRoot("crash-" + c.Name);
        var logDir = Path.Combine(_root, "logs");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine(logDir, "latest.log");
        File.WriteAllText(logPath, c.Log);

        //分析核已迁 C#（W4 T1）：构造限定名更新，TaskTemp 经回调走宿主层 RequestTaskTempFolder（保持 VB 原路径）
        var analyzer = new PCLCS.Crash.CrashAnalyzer(static () => ModMain.RequestTaskTempFolder());
        analyzer.Import(logPath);
        Assert.True(analyzer.Prepare());
        analyzer.Analyze();

        var text = analyzer.GetAnalyzeResult(false);
        Assert.Contains(c.ExpectedText, text, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}