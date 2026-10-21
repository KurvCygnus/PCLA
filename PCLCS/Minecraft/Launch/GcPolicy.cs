namespace PCLCS.Minecraft.Launch;

/// <summary>
/// 启动时的 GC 参数策略。
/// </summary>
public static class GcPolicy {
    // 与 ModLaunch.vb 中的清理正则保持一致。
    private const string GcArgumentPattern =
        "( )*-XX:[+-]?(Use\\w+GC|ZGenerational|UseCompactObjectHeaders|G1\\w+Percent|G1\\w+Size|(Max|Min)(GCPauseMillis|HeapFreeRatio))";

    /// <summary>
    /// 判断参数是否属于需要先移除的 GC 参数。
    /// </summary>
    public static bool IsGcArgument(string argument) => Regex.IsMatch(argument, GcArgumentPattern);

    /// <summary>
    /// 生成 GC 参数。
    /// </summary>
    /// <param name="setupType">0 分代/非分代自动，1 仅 Java 21+ 分代，2 仅 G1，3 自定义，4 优化 G1；其他值按 ZGC 处理。</param>
    /// <param name="javaMajor">Java 主版本号。</param>
    /// <param name="windowsVersion">当前 Windows 版本。</param>
    /// <param name="debug">是否为调试模式。</param>
    public static IReadOnlyList<string> Build(int setupType, int javaMajor, Version windowsVersion, bool debug) {
        var result = new List<string>();
        if (setupType == 3) return result;

        // 只有 2 和 4 强制 G1；0 与 1 按 Java 版本回退，其余值（含越界的设置）落到 ZGC。
        var useG1 = setupType is 2 or 4
                    || (setupType == 0 && javaMajor < 15)
                    || (setupType == 1 && javaMajor < 21);
        // ZGC 需要 Windows 10 1809+。
        if (windowsVersion.Major < 10 || windowsVersion.Build < 17763) useG1 = true;

        result.Add("-XX:+UnlockExperimentalVMOptions");
        if (javaMajor >= 24) result.Add("-XX:+UseCompactObjectHeaders");
        if (useG1 && setupType == 4) {
            result.Add("-XX:+UseG1GC");
            result.Add("-XX:G1NewSizePercent=20");
            result.Add("-XX:G1ReservePercent=20");
            result.Add("-XX:G1HeapRegionSize=32M");
            result.Add("-XX:MaxGCPauseMillis=50");
            // 调试时不加该参数，保留 hsperfdata，便于外部工具读取性能数据。
            if (!debug) result.Add("-XX:+PerfDisableSharedMem");
            if (javaMajor <= 7) result.Add("-XX:MaxPermSize=512m");
            if (javaMajor == 8) result.Add("-XX:+ParallelRefProcEnabled");
            // 需要比 G1NewSizePercent 略高。
            if (javaMajor >= 12) result.Add("-XX:MinHeapFreeRatio=25");
            // 需要比 MinHeapFreeRatio 略高。
            if (javaMajor >= 12) result.Add("-XX:MaxHeapFreeRatio=40");
        } else if (useG1) {
            result.Add("-XX:+UseG1GC");
            result.Add("-XX:G1NewSizePercent=20");
            result.Add("-XX:G1ReservePercent=20");
            result.Add("-XX:G1HeapRegionSize=32M");
            result.Add("-XX:MaxGCPauseMillis=50");
            if (javaMajor <= 7) result.Add("-XX:MaxPermSize=512m");
        } else {
            // Java 23 起默认启用分代 ZGC，不需要再添加参数。
            result.Add("-XX:+UseZGC");
            if (javaMajor is 21 or 22) result.Add("-XX:+ZGenerational");
        }
        return result;
    }
}
