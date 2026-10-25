using System.Text;

namespace PCLCS.Results;

/// <summary>日志系统本身失败时的最后兜底；绝不抛出。</summary>
public static class PclaCrashFallback {
    public static string? TryWrite(Exception exception, PclaError error, string? tempPath) {
        try {
            var folder = string.IsNullOrWhiteSpace(tempPath)
                ? Path.Combine(Path.GetTempPath(), "PCLA")
                : tempPath;
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
            var text = new StringBuilder()
                .AppendLine($"时间：{DateTime.Now:O}")
                .AppendLine($"错误码：{error.Code}")
                .AppendLine($"消息：{error.Message}")
                .AppendLine($"异常：{exception}")
                .AppendLine()
                .AppendLine("内部异常：")
                .AppendLine(error.Cause?.ToString() ?? "无")
                .ToString();
            File.WriteAllText(path, text, Encoding.UTF8);
            return path;
        } catch {
            return null;
        }
    }
}
