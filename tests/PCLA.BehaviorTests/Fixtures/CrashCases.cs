namespace PCLA.BehaviorTests.Fixtures;

public sealed class CrashCase
{
    public string Name { get; }
    public string Log { get; }
    public string ExpectedText { get; }

    public CrashCase(string name, string log, string expectedText)
    {
        Name = name;
        Log = log;
        ExpectedText = expectedText;
    }
}

internal static class CrashCases
{
    public static TheoryData<CrashCase> All { get; } = new()
    {
        new("out-of-memory", "java.lang.OutOfMemoryError: Java heap space", "内存不足"),
        new("night-config", "com.electronwill.nightconfig.core.io.ParsingException: Not enough data available", "Night Config"),
        new("duplicate-mods", "DuplicateModsFoundException: Found a duplicate mod example.jar", "重复"),
        new("missing-dependency",
            "java.lang.RuntimeException: Mod resolution encountered an incompatible mod set!\n" +
            "Missing or unsupported mandatory dependencies:\n" +
            "\tMod ID: 'examplemod', Requested by: 'othermod', Expected range: '[1.0,)', Actual version: '[MISSING]'",
            "前置"),
        new("java-too-new", "Unsupported class file major version 65", "Java"),
        new("java-32bit", "Invalid maximum heap size: -Xmx4G\nThe specified size exceeds the maximum representable size.", "32 位"),
        new("opengl", "The driver does not appear to support OpenGL", "显卡驱动"),
        new("unknown",
            "[12:00:00] [main/INFO]: Loading Minecraft 1.20.1\n" +
            "[12:00:01] [main/INFO]: Preparing spawn area\n" +
            "[12:00:02] [Render thread/WARN]: Something unexpected happened\n" +
            "java.lang.RuntimeException: something unexpected happened",
            "很抱歉")
    };
}