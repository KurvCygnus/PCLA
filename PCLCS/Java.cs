using PCLCS.Results;
using System.Text.Json.Serialization;

namespace PCLCS;

/// <summary>
/// 单个 Java 的实例。
/// </summary>
[Serializable]
public class Java(string folder, Version? version = null) : IEquatable<Java>
{

    /// <summary>
    /// java.exe 文件所在的文件夹路径，这通常是 bin 文件夹。以 \ 结尾。
    /// </summary>
    public readonly string Folder = PathUtils.AddSlashSuffix(PathUtils.ForCompare(folder));
    /// <summary>
    /// java.exe 文件的完整路径。
    /// </summary>
    [JsonIgnore] public string JavaExePath => Path.Combine(Folder, "java.exe");

    /// <summary>
    /// Java 的版本号（例如 16.0.1），保证 <see cref="Version.Major"/> 为其主版本。
    /// <para/> 若为 null，则还需要调用 <see cref="CheckAsync"/> 以获取版本号。
    /// </summary>
    public Version? Version = version;
    /// <summary>Java 的默认字符编码。</summary>
    [JsonIgnore] public string? FileEncodingName;
    /// <summary>
    /// Java 根据系统环境确定的本地字符编码，仅 Java 17+ 可用。
    /// <para/>若系统开启了 UTF-8，部分 Java 会错误地返回 JDK，因此不能使用 <see cref="Encoding.Default"/>。
    /// </summary>
    [JsonIgnore] public string? NativeEncodingName;

    /// <summary>最近一次检查失败的原因；成功或未检查时为 null。</summary>
    [JsonIgnore, NonSerialized] public JavaCheckError? LastError;

    /// <summary>检查 Java 是否可用；失败写入 LastError，取消不记为失败。</summary>
    public async Task<Result<Java, JavaCheckError>> CheckAsync(
        CancellationToken cancellationToken = default, IJavaProcessRunner? runner = null)
    {
        if (Interlocked.Exchange(ref isChecked, 1) == 1 && available is not null)
        {
            return available.Value
                ? Result<Java, JavaCheckError>.Ok(this)
                : Result<Java, JavaCheckError>.Err(
                    LastError ?? new JavaCheckError(JavaFailureReason.Unexpected, "Java 检查失败", Folder));
        }

        JavaProcessOutput? output = null;
        try
        {
            if (runner is null && !FileUtils.Exists(JavaExePath))
            {
                return Fail(JavaFailureReason.MissingExe, "未找到 java.exe 文件", null, null);
            }

            output = await (runner ?? new ProcessJavaRunner())
                .RunAsync(JavaExePath, "-XshowSettings:properties -version", 15000, cancellationToken)
                .NoCapture();
            if (output.TimedOut)
            {
                return Fail(JavaFailureReason.Timeout, "Java 检查超时", output.ExitCode, output.Output);
            }

            var rawOutput = output.Output;
            var text = rawOutput.Lower();
            if (output.ExitCode != 0)
            {
                return Fail(JavaFailureReason.ProcessFailed, "Java 进程返回了非零退出码",
                    output.ExitCode, text);
            }
            if (text == "")
            {
                return Fail(JavaFailureReason.OutputUnreadable, "运行该 Java 失败", output.ExitCode, text);
            }

            Logger.Trace($"Java 检查输出：{JavaExePath}{Environment.NewLine}{text}");
            if (text.Contains("/lib/ext exists"))
            {
                return Fail(JavaFailureReason.ProcessFailed, "无法运行该 Java，请删除 Java 文件夹中的 /lib/ext 文件夹后重试",
                    output.ExitCode, text);
            }
            if (text.Contains("a fatal error") || text.Contains("error: "))
            {
                return Fail(JavaFailureReason.ProcessFailed, "无法运行该 Java，请检查 Java 或系统环境",
                    output.ExitCode, text);
            }

            var versionString = (text.RegexSeek(@"(?<=version "")[^""]+")
                ?? text.RegexSeek("(?<=openjdk )[0-9]+"))
                ?.Replace("_", ".").Replace("+", ".").BeforeFirst("-");
            if (string.IsNullOrEmpty(versionString))
            {
                return Fail(JavaFailureReason.OutputUnreadable, "未找到该 Java 的版本号",
                    output.ExitCode, text);
            }
            if (versionString.StartsWithF("1.")) versionString = versionString!.AfterFirst(".");
            var segments = versionString!.Split(".", true).ToList();
            while (segments.Count < 4) segments.Add("0");
            Version = new Version(segments.Take(4).Join("."));
            if (Version.Major is <= 4 or >= 100)
            {
                return Fail(JavaFailureReason.UnsupportedVersion, $"Java 版本不受支持：{Version}",
                    output.ExitCode, text);
            }
            if (!text.Contains("64-bit"))
            {
                return Fail(JavaFailureReason.Not64Bit, "该 Java 为 32 位版本，请安装 64 位 Java",
                    output.ExitCode, text);
            }

            string? GetProperty(string name)
            {
                var prefix = name + " = ";
                var line = rawOutput.Split('\n').Select(l => l.Trim())
                    .FirstOrDefault(l => l.StartsWithF(prefix));
                return line?[prefix.Length..].Trim();
            }

            FileEncodingName = GetProperty("file.encoding");
            NativeEncodingName = GetProperty("native.encoding") ?? FileEncodingName;

            available = true;
            LastError = null;
            Logger.Info($"检查 Java 成功：{this}");
            return Result<Java, JavaCheckError>.Ok(this);
        } catch (OperationCanceledException)
        {
            isChecked = 0;
            throw;
        } catch (Exception ex)
        {
            return Fail(JavaFailureReason.Unexpected, "检查 Java 时发生未预期错误",
                output?.ExitCode, output?.Output, ex);
        }
    }

    private Result<Java, JavaCheckError> Fail(JavaFailureReason reason, string message, int? exitCode,
        string? output, Exception? cause = null)
    {
        available = false;
        LastError = new JavaCheckError(reason, message, Folder, exitCode, output, cause);
        Logger.Warn($"检查 Java 失败：{JavaExePath}（{LastError.Code}：{message}）");
        return Result<Java, JavaCheckError>.Err(LastError);
    }

    public void InvalidateChecked()
    {
        isChecked = 0;
        available = null;
        LastError = null;
    }

    [JsonIgnore] internal int isChecked = 0;
    /// <summary>若已进行过检查，表示该检查是否成功。若未检查，则为 null。</summary>
    [JsonIgnore] internal bool? available = null;

    // 相同检查
    public bool Equals(Java? other)
        => other is not null && PathUtils.IsSame(Folder, other.Folder);
    public override bool Equals(object? obj) => obj is Java other && Equals(other);
    public override int GetHashCode() => StringComparer.CurrentCultureIgnoreCase.GetHashCode(PathUtils.ForCompare(Folder));

    /// <summary>用户友好的描述文本。</summary>
    public override string ToString()
        => $"Java {Version?.Major.ToString() ?? "尚未检查"} ({Version})：{Folder}";
}

public static class JavaUtils
{

    /// <summary>
    /// 检查所有指定的 Java 是否存在问题，并获取其版本号。
    /// </summary>
    /// <returns>所有通过检查的 Java。</returns>
    public static async Task<List<Java>> CheckAllAsync(IEnumerable<Java> javas, CancellationToken cancellationToken = default, ProgressProvider? progress = null)
    {
        await TaskUtils.WhenAll(javas.Where(j => j.available == null).Select(
            async java => await java.CheckAsync(cancellationToken).NoCapture()
        ), progress).NoCapture();
        return javas.Where(j => j.available == true).ToList();
    }

    /// <summary>
    /// 将 Java 列表进行排序。
    /// 这同时会检查所有未检查的 Java。
    /// </summary>
    public static async Task<List<Java>> SortAsync(List<Java> javas, CancellationToken cancellationToken = default, ProgressProvider? progress = null)
    {
        javas = await JavaUtils.CheckAllAsync(javas, cancellationToken, progress).NoCapture();
        javas.Sort((left, right) =>
        {
            // 优先使用带完整 Java 的文件夹列表中的 Java
            var isLeftInCandidateFolder = JavaUtils.CandidateFolders.Value.Any(folder => PathUtils.IsParentOf(folder, left.Folder));
            var isRightInCandidateFolder = JavaUtils.CandidateFolders.Value.Any(folder => PathUtils.IsParentOf(folder, right.Folder));
            if (isLeftInCandidateFolder != isRightInCandidateFolder) return isLeftInCandidateFolder ? -1 : 1;
            // 其次优先使用主版本号接近 21 的 Java
            var leftDistance = left.Version is null ? int.MaxValue : Math.Abs(left.Version.Major - 21);
            var rightDistance = right.Version is null ? int.MaxValue : Math.Abs(right.Version.Major - 21);
            return leftDistance.CompareTo(rightDistance);
        });
        return javas;
    }

    #region 搜索

    /// <summary>
    /// 存在完整 Java 的文件夹列表。
    /// </summary>
    internal static readonly Lazy<List<string>> CandidateFolders = new(() =>
    {
        List<string> folders = [
            Paths.AppData + @".minecraft\runtime\", // 这也是 PCL 下载 Java 的路径
            Paths.AppData + @".hmcl\java\",
            Paths.AppData + @"ATLauncher\runtimes\minecraft\",
            Paths.AppData + @"ModrinthApp\meta\java_versions\",
            Paths.AppData + @"PrismLauncher\java\",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\curseforge\minecraft\Install\runtime\",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\.jdks\",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\.sdkman\candidates\java\",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\.ftba\bin\runtime\",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Packages\Microsoft.4297127D64EC6_8wekyb3d8bbwe\LocalCache\Local\runtime\",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\Minecraft Launcher\runtime\",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) + @"\Minecraft\runtime\",
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + @"\Curse\Minecraft\Install\runtime\",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Java\",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Eclipse Adoptium\",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Amazon Corretto\",
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Zulu\",
            ..DirectoryUtils.EnumerateDirectories(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\Microsoft\", searchPattern: "jdk-*"),
            // 环境变量
            ..(Environment.GetEnvironmentVariable("JDK_HOME") + ";" + Environment.GetEnvironmentVariable("JAVA_HOME")).
                Split(";", true).Select(path => path.Trim(' ', '"'))
        ];
        return folders.Select(f =>
        {
            try
            {
                return PathUtils.ForCompare(f);
            } catch (Exception ex)
            {
                Logger.Warn(ex, $"Java 候选文件夹路径无效（{f}）");
                return null;
            }
        }).Where(f => f is not null).Cast<string>().ToList();
    });


    /// <summary>
    /// 从常见文件夹和环境变量中搜索 Java，并将搜索结果写入设置。
    /// </summary>
    public static async Task RefreshListAsync(CancellationToken c = default, ProgressProvider? p = null)
    {
        // 搜索
        List<Java> javaList = await SearchFoldersAsync(true, CandidateFolders.Value, c, p?.SplitTo(0.5)).NoCapture();
        // 将新发现的 Java 排在最前，原有列表保留既有顺序排在后面
        var oldJavaList = Configs.JavaList.Get()!;
        oldJavaList = await JavaUtils.CheckAllAsync(oldJavaList, c, p?.SplitTo(0.9)).NoCapture(); // 检查原有列表中的 Java
        javaList.RemoveAll(oldJavaList); // 此时 javaList 中只剩下新发现的 Java
        javaList = await JavaUtils.SortAsync(javaList, c).NoCapture(); // 将所有新发现的 Java 按优先级排序
        javaList.AddRange(oldJavaList);
        javaList = javaList.Distinct().ToList();
        // 删掉已被移除的 Java
        javaList.RemoveIf(j => Configs.JavaRemovedList.Get()!.Contains(j.Folder, StringComparer.OrdinalIgnoreCase));
        // 写入设置
        Configs.JavaList.Set([.. javaList]);
        Logger.Info($"Java 搜索完成，发现 {javaList.Count} 个 Java");
    }

    /// <summary>
    /// 并行搜索所有指定的文件夹及其子文件夹中的 Java，并检查其有效性。
    /// </summary>
    public static async Task<List<Java>> SearchFoldersAsync(bool includeSubDirectories, IEnumerable<string> folders, CancellationToken cancellationToken = default, ProgressProvider? progress = null)
    {
        // 并行搜索
        ConcurrentBag<(string Folder, bool HasReparsePoint, bool IsSpecialPath)> results = [];
        await TaskUtils.ForEachAsync(folders.Distinct(StringComparer.OrdinalIgnoreCase), 4, async (folder, cp) =>
        {
            await Task.Run(() =>
            {
                try
                {
                    foreach (var file in DirectoryUtils.EnumerateFiles(folder, includeSubDirectories, "java.exe"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string targetFolder = PathUtils.AddSlashSuffix(PathUtils.ForCompare(PathUtils.RemoveLastPart(file)));
                        // 判断文件夹是否包含重解析点（例如符号链接）
                        static bool HasReparsePoint(FileSystemInfo? info)
                        {
                            if (info is null) return false;
                            if (PathUtils.IsParentOf(Paths.AppData + @".minecraft\runtime\", info.FullName)) return false; // 不筛查 PCL 的下载路径（#8928）
                            do
                            {
                                if (info.Attributes.HasFlagF(FileAttributes.ReparsePoint)) return true;
                                // FileInfo.Directory 与 DirectoryInfo.Parent 在根处返回 null，由 while 条件终止循环。
                                info = info is FileInfo fileInfo ? fileInfo.Directory : ((DirectoryInfo) info).Parent;
                            } while (info is not null);
                            return false;
                        }
                        // 结果
                        var hasReparsePoint = HasReparsePoint(FileUtils.GetInfo(Path.Combine(targetFolder, "java.exe")));
                        var isSpecialPath = targetFolder.ContainsIgnoreCase("java8path_target_") || targetFolder.ContainsIgnoreCase("javapath_target_") || targetFolder.ContainsIgnoreCase("javatmp") || targetFolder.ContainsIgnoreCase("system32");
                        results.Add((targetFolder, hasReparsePoint, isSpecialPath));
                    }
                } catch (UnauthorizedAccessException)
                {
                    Logger.Info($"快速查找 Java 时没有权限（{folder}）");
                } catch (Exception ex)
                {
                    ex.ThrowIfCanceled();
                    Logger.Warn(ex, $"快速查找 Java 时出错（{folder}）");
                }
            }, cancellationToken).NoCapture();
        }, cancellationToken, progress?.SplitTo(0.2)).NoCapture();
        if (!results.Any()) return [];

        // 筛除重解析点
        if (results.All(r => r.HasReparsePoint))
        {
            Logger.Warn("找到的所有 Java 都包含重解析点，保留全部结果");
        } else if (results.Any(r => r.HasReparsePoint))
        {
            Logger.Warn($"找到的以下 Java 包含重解析点，将它们从列表中排除：{results.Where(r => r.HasReparsePoint).Select(r => r.Folder).Join('、')}");
            results = [.. results.Where(r => !r.HasReparsePoint)];
        }
        // 筛除特殊路径
        if (results.All(r => r.IsSpecialPath))
        {
            Logger.Warn("找到的所有 Java 都包含特殊路径，保留全部结果");
        } else if (results.Any(r => r.IsSpecialPath))
        {
            Logger.Warn($"找到的以下 Java 包含特殊路径，将它们从列表中排除：{results.Where(r => r.IsSpecialPath).Select(r => r.Folder).Join('、')}");
            results = [.. results.Where(r => !r.IsSpecialPath)];
        }

        // 检查并返回
        List<Java> javaList = results.Select(r => r.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Select(folder => new Java(folder)).ToList();
        javaList = (await JavaUtils.CheckAllAsync(javaList, cancellationToken, progress?.SplitTo(1)).NoCapture()).ToList();
        javaList = await JavaUtils.SortAsync(javaList, cancellationToken).NoCapture();
        Logger.Info($"快速查找 Java 完成，共有 {javaList.Count} 个候选：{javaList.Join('、')}");
        return javaList;
    }

    #endregion

}