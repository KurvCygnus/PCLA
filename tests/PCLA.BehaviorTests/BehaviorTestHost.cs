using System;
using System.IO;
using MeloongCore;
using PCL;

namespace PCLA.BehaviorTests;

internal static class BehaviorTestHost
{
    private static readonly object Gate = new();
    private static bool _initialized;

    public static string Root { get; private set; } = "";

    public static string DefaultMcFolder => Path.Combine(Root, "minecraft") + Path.DirectorySeparatorChar;

    public static void RestoreDefaultMcFolder()
    {
        ModMinecraft.McFolderSelected = DefaultMcFolder;
    }

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized) return;

            Root = Path.Combine(Path.GetTempPath(), "PCLA.BehaviorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Path.Combine(Root, "logs"));
            Directory.CreateDirectory(Path.Combine(Root, "temp"));
            Directory.CreateDirectory(DefaultMcFolder);

            Main.Init("PCLABehaviorTests");
            Directory.CreateDirectory(Paths.AppDataThenName);

            Logger.Instance = new SilentLogger
            {
                logFolder = Path.Combine(Root, "logs") + Path.DirectorySeparatorChar,
                MinLevel = LogLevel.Trace
            };
            ModBase.PathTemp = Path.Combine(Root, "temp") + Path.DirectorySeparatorChar;
            RestoreDefaultMcFolder();

            _initialized = true;
        }
    }

    public static string CreateCaseRoot(string name)
    {
        Initialize();
        var root = Path.Combine(Root, "cases", name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}

internal sealed class SilentLogger : PclLogger
{
    public override void HandleBehavior(string rawMessage, string formattedMessage, LogBehavior behavior, Exception? ex)
    {
        // 测试宿主不弹窗；错误仍写入日志文件。
    }
}
