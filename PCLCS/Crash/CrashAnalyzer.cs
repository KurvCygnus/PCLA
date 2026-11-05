namespace PCLCS.Crash;

/// <summary>
/// 崩溃分析核（自宿主层 ModCrash.vb:6-1113 逐行移植，Output 弹窗编排除外——暂留宿主层，导出拆解时再迁）。
/// 五步流水：Collect/Import 收集 → Prepare 分类（五类分流 + 截断魔数逐字）→ Analyze（Crit1→Crit2→条件堆栈→Crit3）→
/// GetAnalyzeResult（CrashReason → 中文段落，\n/\h/\e 标记）。宿主层依赖经构造回调（TaskTemp 申请）与静态接线（导出脱敏）注入。
/// </summary>
public sealed class CrashAnalyzer
{

    //宿主层接线（ModNet.Sub New 同模式；null 时为测试直用语义）
    /// <summary>导出脱敏回调：旧 Output :906 的 FilterUserName(FilterAccessToken(Raw, FilterChar), "*") 组合——
    /// FilterChar 作用于 AccessToken 过滤，用户名过滤固定 *；宿主层在模块 Sub New 接线（模块静态构造时序缺陷，见 TECHDEBT §14），导出核消费。</summary>
    public static Func<string, char, string>? DesensitizeFilters;

    /// <summary>暂存的日志文件：文件完整路径 -> 文件内容（旧 :16）。</summary>
    public List<KeyValuePair<string, string[]>> AnalyzeRawFiles { get; } = [];

    /// <summary>
    /// 构造（旧 :6-13）：经回调申请任务缓存文件夹并建 Temp/Report 子目录。
    /// </summary>
    public CrashAnalyzer(Func<string>? taskTempFolderProvider)
    {
        TempFolder = (taskTempFolderProvider ?? throw new ArgumentNullException(nameof(taskTempFolderProvider)))();
        DirectoryUtils.Create(TempFolder + "Temp\\");
        DirectoryUtils.Create(TempFolder + "Report\\");
        Logger.Info($"崩溃分析暂存文件夹：{TempFolder}");
    }

    /// <summary>任务缓存文件夹（旧 :4；以 \ 结尾，Output 段在其下建 Report）。</summary>
    public string TempFolder { get; }

    /// <summary>
    /// 将可用于分析的日志存储到 AnalyzeRawFiles（旧 :21-85）。
    /// </summary>
    /// <param name="versionPathIndie">版本隔离下的版本文件夹路径。</param>
    /// <param name="latestLog">从启动器捕获到的最后 200 行程序输出。</param>
    public void Collect(string versionPathIndie, IList<string>? latestLog = null)
    {
        //! 逐字移植豁免：枚举循环与正向 if 嵌套逐字对照旧 :21-85（LINQ 化与卫语句反转破坏行级对照）
        Logger.Info("步骤 1：收集日志文件");

        //简单收集可能的日志文件路径
        var possibleLogs = new List<string>();
        try
        {
            possibleLogs.AddRange(DirectoryUtils.EnumerateFiles(versionPathIndie + "crash-reports\\", true));
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "收集 Minecraft 崩溃日志文件夹下的日志失败");
        }
        try
        {
            //! Parent 为 null 时 NRE → 由外层 Catch 捕获（VB 语义 1:1）
            foreach (var file in DirectoryUtils.GetInfo(versionPathIndie).Parent!.Parent!.EnumerateFiles())
            {
                if (file.Extension != ".log") continue;
                possibleLogs.Add(PathUtils.RemoveExtendedPrefix(file.FullName));
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "收集 Minecraft 主文件夹下的日志失败");
        }
        try
        {
            foreach (var file in DirectoryUtils.GetInfo(versionPathIndie).EnumerateFiles())
            {
                if (file.Extension != ".log") continue;
                possibleLogs.Add(PathUtils.RemoveExtendedPrefix(file.FullName));
            }
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "收集 Minecraft 隔离文件夹下的日志失败");
        }
        possibleLogs.Add(versionPathIndie + "logs\\latest.log"); //Minecraft 日志
        possibleLogs.Add(versionPathIndie + "logs\\debug.log"); //Minecraft Debug 日志
        possibleLogs = possibleLogs.Distinct().ToList();

        //确定最新的日志文件
        var rightLogs = new List<string>();
        foreach (var logFile in possibleLogs)
        {
            try
            {
                var info = FileUtils.GetInfo(logFile);
                if (!info.Exists) continue;
                var time = Math.Abs((info.LastWriteTime - DateTime.Now).TotalMinutes);
                if (time < 3 && info.Length > 0)
                {
                    rightLogs.Add(logFile);
                    Logger.Info($"可能可用的日志文件：{logFile}（{Math.Round(time, 1)} 分钟）");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"确认崩溃日志时间失败（{logFile}）");
            }
        }
        if (!rightLogs.Any()) Logger.Info("未发现可能可用的日志文件");

        //将可能可用的日志文件导出
        foreach (var filePath in rightLogs)
        {
            try
            {
                AnalyzeRawFiles.Add(new KeyValuePair<string, string[]>(filePath, FileUtils.ReadAsLines(filePath)));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"读取可能的崩溃日志文件失败（{filePath}）");
            }
        }
        if (latestLog != null && latestLog.Any())
        {
            var rawOutput = latestLog.Join("\r\n");
            Logger.Info($"以下为游戏输出的最后一段内容：\r\n{rawOutput}");
            FileUtils.Write(TempFolder + "RawOutput.log", rawOutput);
            AnalyzeRawFiles.Add(new KeyValuePair<string, string[]>(TempFolder + "RawOutput.log", latestLog.ToArray()));
            latestLog.Clear();
        }

        Logger.Info($"步骤 1：收集日志文件完成，收集到 {AnalyzeRawFiles.Count} 个文件");
    }

    /// <summary>
    /// 从文件路径直接导入日志文件或崩溃报告压缩包（旧 :89-124）。
    /// </summary>
    public void Import(string filePath)
    {
        Logger.Info("步骤 1：自主导入日志文件");

        //尝试视作压缩包解压
        try
        {
            var info = FileUtils.GetInfo(filePath);
            if (info.Exists && info.Length > 0 && PathUtils.GetExtension(filePath) != "jar")
            {
                FileUtils.ExtractToDirectory(filePath, TempFolder + "Temp\\");
                Logger.Info($"已解压导入的日志文件：{filePath}");
                goto Extracted;
            }
        }
        catch
        {
            //! 逐字移植豁免：空 Catch 原样保留（TECHDEBT §3 P0-5）——非压缩包时静默回退到下方复制分支
        }
        //并非压缩包
        FileUtils.Copy(filePath, TempFolder + "Temp\\" + PathUtils.GetLastPart(filePath));
        Logger.Info($"已复制导入的日志文件：{filePath}");
    Extracted:

        //导入其中的日志文件
        foreach (var targetFile in DirectoryUtils.GetInfo(TempFolder + "Temp\\").EnumerateFiles())
        {
            try
            {
                if (!targetFile.Exists || targetFile.Length == 0) continue;
                var ext = targetFile.Extension.Lower();
                if (ext == ".log" || ext == ".txt")
                {
                    AnalyzeRawFiles.Add(new KeyValuePair<string, string[]>(
                        PathUtils.RemoveExtendedPrefix(targetFile.FullName), FileUtils.ReadAsLines(targetFile.FullName)));
                }
                else
                {
                    FileUtils.Delete(targetFile.FullName);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "导入单个日志文件失败");
            }
        }

        Logger.Info($"步骤 1：自主导入日志文件，收集到 {AnalyzeRawFiles.Count} 个文件");
    }

    //2：确认实际用于分析的 Log 文本
    /// <summary>待分类文件种类（旧 :127-133）。</summary>
    private enum AnalyzeFileType
    {
        HsErr,
        MinecraftLog,
        ExtraLogFile,
        ExtraReportFile,
        CrashReport
    }

    /// <summary>在弹窗中选择直接打开的文件（旧 :134；Prepare 分类时赋值，Output 段消费）。</summary>
    public KeyValuePair<string, string[]>? DirectFile { get; private set; }

    /// <summary>
    /// 从 AnalyzeRawFiles 中提取实际有用的文本片段存储到 Log* 分桶，并整理可用于生成报告的文件。
    /// 返回是否有足够信息可用于分析。（旧 :139-302）
    /// </summary>
    public bool Prepare()
    {
        Logger.Info("步骤 2：准备日志文本");

        //对日志文件进行分类
        DirectFile = null;
        var allFiles = new List<KeyValuePair<AnalyzeFileType, KeyValuePair<string, string[]>>>();
        foreach (var logFile in AnalyzeRawFiles)
        {
            var matchName = PathUtils.GetLastPart(logFile.Key).Lower();
            AnalyzeFileType targetType;
            if (matchName.StartsWithF("hs_err"))
            {
                targetType = AnalyzeFileType.HsErr;
                if (DirectFile is null) DirectFile = logFile;
            }
            else if (matchName.StartsWithF("crash-"))
            {
                targetType = AnalyzeFileType.CrashReport;
                if (DirectFile is null) DirectFile = logFile;
            }
            else if (matchName == "latest.log" || matchName == "latest log.txt" ||
                     matchName == "debug.log" || matchName == "debug log.txt" ||
                     matchName == "游戏崩溃前的输出.txt" || matchName == "rawoutput.log")
            {
                targetType = AnalyzeFileType.MinecraftLog;
                DirectFile = logFile;
            }
            else if (matchName == "启动器日志.txt" || matchName == "PCL2 启动器日志.txt" || matchName == "PCL 启动器日志.txt" || matchName == "log1.txt")
            {
                if (logFile.Value.Any(static s => s.Contains("以下为游戏输出的最后一段内容")))
                {
                    targetType = AnalyzeFileType.MinecraftLog;
                    if (DirectFile is null) DirectFile = logFile;
                }
                else
                {
                    targetType = AnalyzeFileType.ExtraLogFile;
                }
            }
            else if (PathUtils.GetExtension(matchName) == "log")
            {
                targetType = AnalyzeFileType.ExtraLogFile;
            }
            else if (PathUtils.GetExtension(matchName) == "txt")
            {
                targetType = AnalyzeFileType.ExtraReportFile;
            }
            else
            {
                Logger.Info($"{matchName} 分类为 Ignore");
                continue;
            }
            if (logFile.Value.Any())
            {
                allFiles.Add(new KeyValuePair<AnalyzeFileType, KeyValuePair<string, string[]>>(targetType, logFile));
                Logger.Info($"{matchName} 分类为 {targetType}");
            }
            else
            {
                Logger.Info($"{matchName} 由于内容为空跳过");
            }
        }

        //若只有额外日志，则将它们视作 Minecraft 日志
        if (allFiles.Any() && allFiles.All(static p => p.Key == AnalyzeFileType.ExtraLogFile))
        {
            Logger.Info("由于仅发现了额外日志，将它们视作 Minecraft 日志进行分析");
            allFiles = allFiles.Select(static p => new KeyValuePair<AnalyzeFileType, KeyValuePair<string, string[]>>(AnalyzeFileType.MinecraftLog, p.Value)).ToList();
        }

        //将分类后的文件分别写入
        foreach (var selectType in new[] { AnalyzeFileType.MinecraftLog, AnalyzeFileType.HsErr, AnalyzeFileType.ExtraLogFile, AnalyzeFileType.CrashReport })
        {
            //获取该种类的所有文件 {文件路径 -> 文件内容行}
            var selectedFiles = new List<KeyValuePair<string, string[]>>();
            foreach (var file in allFiles)
            {
                if (selectType == file.Key) selectedFiles.Add(file.Value);
            }
            if (!selectedFiles.Any()) continue;
            try
            {
                //根据文件类别判断
                switch (selectType)
                {
                    case AnalyzeFileType.HsErr:
                    case AnalyzeFileType.CrashReport:
                        {
                            //获取文件的修改日期
                            var datedFiles = new SortedList<DateTime, KeyValuePair<string, string[]>>();
                            foreach (var file in selectedFiles)
                            {
                                try
                                {
                                    datedFiles.Add(FileUtils.GetInfo(file.Key).LastWriteTime, file);
                                }
                                catch (Exception ex)
                                {
                                    Logger.Warn(ex, "获取日志文件修改时间失败");
                                    datedFiles.Add(new DateTime(1900, 1, 1), file);
                                }
                            }
                            //输出最新的文件
                            var newestFile = datedFiles.Last().Value;
                            OutputFiles.Add(newestFile.Key);
                            if (selectType == AnalyzeFileType.HsErr)
                            {
                                LogHs = GetHeadTailLines(newestFile.Value, 200, 100);
                                Logger.Info($"输出报告：{newestFile.Key}，作为虚拟机错误信息");
                                Logger.Info($"导入分析：{newestFile.Key}，作为虚拟机错误信息");
                            }
                            else
                            {
                                LogCrash = GetHeadTailLines(newestFile.Value, 300, 700);
                                Logger.Info($"输出报告：{newestFile.Key}，作为 Minecraft 崩溃报告");
                                Logger.Info($"导入分析：{newestFile.Key}，作为 Minecraft 崩溃报告");
                            }
                            break;
                        }
                    case AnalyzeFileType.MinecraftLog:
                        {
                            LogMc = "";
                            LogMcDebug = "";
                            //创建文件名词典
                            var fileNameDict = new Dictionary<string, KeyValuePair<string, string[]>>();
                            foreach (var selectedFile in selectedFiles)
                            {
                                fileNameDict[PathUtils.GetLastPart(selectedFile.Key).Lower()] = selectedFile;
                                OutputFiles.Add(selectedFile.Key);
                                Logger.Info($"输出报告：{selectedFile.Key}，作为 Minecraft 或启动器日志");
                            }
                            //选择一份最佳的来自启动器的游戏日志
                            foreach (var fileName in new[] { "rawoutput.log", "启动器日志.txt", "log1.txt", "游戏崩溃前的输出.txt", "PCL2 启动器日志.txt", "PCL 启动器日志.txt" })
                            {
                                if (!fileNameDict.TryGetValue(fileName, out var currentLog)) continue;
                                //截取 “以下为游戏输出的最后一段内容” 后的内容
                                var hasLauncherMark = false;
                                foreach (var line in currentLog.Value)
                                {
                                    if (hasLauncherMark)
                                    {
                                        LogMc += line + "\n";
                                    }
                                    else if (line.Contains("以下为游戏输出的最后一段内容"))
                                    {
                                        hasLauncherMark = true;
                                        Logger.Info("找到 PCL 输出的游戏实时日志头");
                                    }
                                }
                                //导入后 500 行
                                if (!hasLauncherMark) LogMc += GetHeadTailLines(currentLog.Value, 0, 500);
                                LogMc = LogMc.TrimEnd('\r', '\n');
                                Logger.Info($"导入分析：{currentLog.Key}，作为启动器日志");
                                break;
                            }
                            //选择一份最佳的 Minecraft Log
                            foreach (var fileName in new[] { "latest.log", "latest log.txt", "debug.log", "debug log.txt" })
                            {
                                if (!fileNameDict.TryGetValue(fileName, out var currentLog)) continue;
                                LogMc += GetHeadTailLines(currentLog.Value, 1500, 500);
                                Logger.Info($"导入分析：{currentLog.Key}，作为 Minecraft 日志");
                                break;
                            }
                            //查找 Debug Log
                            foreach (var fileName in new[] { "debug.log", "debug log.txt" })
                            {
                                if (!fileNameDict.TryGetValue(fileName, out var currentLog)) continue;
                                LogMcDebug += GetHeadTailLines(currentLog.Value, 1000, 0);
                                Logger.Info($"导入分析：{currentLog.Key}，作为 Minecraft Debug 日志");
                                break;
                            }
                            //兜底
                            if (LogMc == "")
                            {
                                if (LogMcDebug != "")
                                {
                                    //如果没有找到 Minecraft 日志，则使用 Debug 日志作为兜底
                                    LogMc = LogMcDebug;
                                }
                                else if (fileNameDict.Any())
                                {
                                    //如果都没有找到，则使用第一个文件
                                    var currentLog = fileNameDict.First().Value;
                                    LogMc += GetHeadTailLines(currentLog.Value, 1500, 500);
                                    Logger.Info($"导入分析：{currentLog.Key}，作为兜底日志");
                                }
                                else
                                {
                                    LogMc = null;
                                    throw new Exception("无法找到匹配的 Minecraft Log");
                                }
                            }
                            if (LogMcDebug == "") LogMcDebug = null;
                            break;
                        }
                    case AnalyzeFileType.ExtraLogFile:
                    case AnalyzeFileType.ExtraReportFile:
                        {
                            //全部丢过去
                            foreach (var selectedFile in selectedFiles)
                            {
                                OutputFiles.Add(selectedFile.Key);
                                Logger.Info($"输出报告：{selectedFile.Key}，不用作分析");
                            }
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "分类处理日志文件时出错");
            }
        }

        //结束
        var result = LogMc is not null || LogHs is not null || LogCrash is not null;
        if (result)
        {
            Logger.Info($"{("[Crash] 步骤 2：准备日志文本完成，找到" + (LogMc is null ? "" : "游戏日志、") + (LogMcDebug is null ? "" : "游戏 Debug 日志、") + (LogHs is null ? "" : "虚拟机日志、") + (LogCrash is null ? "" : "崩溃日志、")).TrimEnd('、')}用作分析");
        }
        else
        {
            Logger.Info("步骤 2：准备日志文本完成，没有任何可供分析的日志");
        }
        return result;
    }

    /// <summary>
    /// 输出字符串的前后某些行，并统一行尾为 \n (正则 \n)、删除空行和重复行（旧 :306-330）。
    /// </summary>
    private static string GetHeadTailLines(string[] raw, int headLines, int tailLines)
    {
        if (raw.Length <= headLines + tailLines) return raw.Distinct().Join("\n");
        var lines = new List<string>();
        int realHeadLines = 0, viewedLines;
        for (viewedLines = 0; viewedLines <= raw.Length - 1; viewedLines++)
        {
            if (lines.Contains(raw[viewedLines])) continue;
            realHeadLines++;
            lines.Add(raw[viewedLines]);
            if (realHeadLines >= headLines) break;
        }
        var realTailLines = 0;
        for (var i = raw.Length - 1; i >= viewedLines; i--)
        {
            if (lines.Contains(raw[i])) continue;
            realTailLines++;
            lines.Insert(realHeadLines, raw[i]);
            if (realTailLines >= tailLines) break;
        }
        var result = new StringBuilder();
        foreach (var line in lines)
        {
            if (line == "") continue;
            result.Append(line);
            result.Append('\n');
        }
        return result.ToString();
    }

    //3：根据文本分析崩溃原因
    /// <summary>Minecraft 日志（旧 :333）。</summary>
    public string? LogMc;
    /// <summary>Minecraft Debug 日志（旧 :333）。</summary>
    public string? LogMcDebug;
    /// <summary>虚拟机日志（旧 :333）。</summary>
    public string? LogHs;
    /// <summary>Minecraft 崩溃报告（旧 :333）。</summary>
    public string? LogCrash;
    /// <summary>全部分析文本（旧 :334；Analyze 时拼接）。</summary>
    public string? LogAll;

    /// <summary>可能导致崩溃的原因与附加信息（旧 :336）。</summary>
    public Dictionary<CrashReason, List<string>> CrashReasons { get; } = [];

    /// <summary>
    /// 根据 AnalyzeLogs 与可能的版本信息分析崩溃原因（旧 :391-451）。
    /// </summary>
    public void Analyze()
    {
        Logger.Info("步骤 3：分析崩溃原因");
        LogAll = (LogMc ?? LogMcDebug ?? "") + (LogHs ?? "") + (LogCrash ?? "");

        //1. 精准日志匹配，中/高优先级
        AnalyzeCrit1();
        if (CrashReasons.Any()) goto Done;
        AnalyzeCrit2();
        if (CrashReasons.Any()) goto Done;

        //2. 堆栈分析
        if (LogAll.Contains("orge") || LogAll.Contains("abric") || LogAll.Contains("uilt") || LogAll.Contains("iteloader"))
        {
            var keywords = new List<string>();
            //崩溃日志
            if (LogCrash is not null)
            {
                Logger.Info("开始进行崩溃日志堆栈分析");
                keywords.AddRange(AnalyzeStackKeyword(LogCrash.BeforeFirst("System Details")));
            }
            //Minecraft 日志
            if (LogMc is not null)
            {
                var fatals = LogMc.RegexSearch("/FATAL] .+?(?=[\\n]+\\[)").ToList();
                if (LogMc.Contains("Unreported exception thrown!")) fatals.Add(LogMc.Between("Unreported exception thrown!", "at oolloo.jlw.Wrapper"));
                Logger.Info($"开始进行 Minecraft 日志堆栈分析，发现 {fatals.Count} 个报错项");
                foreach (var fatal in fatals)
                {
                    keywords.AddRange(AnalyzeStackKeyword(fatal));
                }
            }
            //虚拟机日志
            if (LogHs is not null)
            {
                Logger.Info("开始进行虚拟机堆栈分析");
                var stackLogs = LogHs.Between("T H R E A D", "Registers:");
                keywords.AddRange(AnalyzeStackKeyword(stackLogs));
            }
            //Mod 名称分析
            if (keywords.Any())
            {
                var names = AnalyzeModName(keywords);
                if (names is null)
                {
                    AppendReason(CrashReason.堆栈分析发现关键字, keywords);
                }
                else
                {
                    AppendReason(CrashReason.堆栈分析发现Mod名称, names);
                }
                goto Done;
            }
        }
        else
        {
            Logger.Info("可能并未安装 Mod，不进行堆栈分析");
        }

        //3. 精准日志匹配，低优先级
        AnalyzeCrit3();

        //输出到日志
    Done:
        if (!CrashReasons.Any())
        {
            Logger.Info("步骤 3：分析崩溃原因完成，未找到可能的原因");
        }
        else
        {
            Logger.Info($"步骤 3：分析崩溃原因完成，找到 {CrashReasons.Count} 条可能的原因");
            foreach (var reason in CrashReasons)
            {
                Logger.Info($"- {reason.Key}{(reason.Value.Any() ? "（" + reason.Value.Join('；') + "）" : "")}");
            }
        }
    }

    /// <summary>
    /// 增加一个可能的崩溃原因（旧 :455-465）。
    /// </summary>
    private void AppendReason(CrashReason reason, IEnumerable<string>? additional = null)
    {
        var additionalList = additional?.ToList(); //物化一次再逐处消费（旧 IEnumerable 逐次枚举，结果相同）
        if (CrashReasons.ContainsKey(reason))
        {
            if (additionalList is not null)
            {
                CrashReasons[reason].AddRange(additionalList);
                CrashReasons[reason] = CrashReasons[reason].Distinct().ToList();
            }
        }
        else
        {
            CrashReasons.Add(reason, additionalList ?? []);
        }
        Logger.Info($"可能的崩溃原因：{reason}{(additionalList is { Count: > 0 } ? "（" + additionalList.Join('；') + "）" : "")}");
    }

    /// <summary>增加一个可能的崩溃原因（旧 :466-468 的字符串重载；空串视为无附加信息）。</summary>
    private void AppendReason(CrashReason reason, string? additional)
    {
        if (additional is not { Length: > 0 }) //net48 参照程序集无 NotNullWhen 注解，不以 IsNullOrEmpty 做空流判定
        {
            AppendReason(reason);
        }
        else
        {
            AppendReason(reason, new List<string> { additional });
        }
    }

    /// <summary>
    /// 进行精准日志匹配。匹配优先级高于堆栈分析的崩溃。（旧 :474-584）
    /// </summary>
    private void AnalyzeCrit1()
    {
        //! 逐字移植豁免：规则块的正向 if 嵌套逐字对照旧 :474-584（卫语句反转破坏行级对照）

        //空白分析
        if (LogMc is null && LogHs is null && LogCrash is null)
        {
            AppendReason(CrashReason.没有可用的分析文件);
            return;
        }

        //崩溃报告分析，高优先级
        if (LogCrash is not null)
        {
            if (LogCrash.Contains("Unable to make protected final java.lang.Class java.lang.ClassLoader.defineClass")) AppendReason(CrashReason.Java版本过高);
            if (LogCrash.Contains("Failed loading config file ")) AppendReason(CrashReason.Mod配置文件导致游戏崩溃, [TryAnalyzeModName((LogCrash.RegexSeek(@"(?<=Failed loading config file .+ for modid )[^\n]+") ?? "").TrimEnd('\r', '\n')).First(), (LogCrash.RegexSeek(@"(?<=Failed loading config file ).+(?= of type)") ?? "").TrimEnd('\r', '\n')]); //#7071
        }

        //游戏日志分析
        if (LogMc is not null)
        {
            if (LogMc.Contains("Unrecognized option:")) AppendReason(CrashReason.Java虚拟机参数有误);
            if (LogMc.Contains("Found multiple arguments for option fml.forgeVersion, but you asked for only one")) AppendReason(CrashReason.版本Json中存在多个Forge);
            if (LogMc.Contains("The driver does not appear to support OpenGL")) AppendReason(CrashReason.显卡不支持OpenGL);
            if (LogMc.Contains("java.lang.ClassCastException: java.base/jdk")) AppendReason(CrashReason.使用JDK);
            if (LogMc.Contains("java.lang.ClassCastException: class jdk.")) AppendReason(CrashReason.使用JDK);
            if (LogMc.Contains("java.lang.NoSuchMethodError: 'void net.minecraft.client.renderer.texture.SpriteContents.<init>")) AppendReason(CrashReason.OptiFine与Forge不兼容);
            if (LogMc.Contains("java.lang.NoSuchMethodError: 'java.lang.String com.mojang.blaze3d.systems.RenderSystem.getBackendDescription")) AppendReason(CrashReason.OptiFine与Forge不兼容);
            if (LogMc.Contains("java.lang.NoSuchMethodError: 'void net.minecraft.client.renderer.block.model.BakedQuad.<init>")) AppendReason(CrashReason.OptiFine与Forge不兼容);
            if (LogMc.Contains("java.lang.NoSuchMethodError: 'void net.minecraftforge.client.gui.overlay.ForgeGui.renderSelectedItemName")) AppendReason(CrashReason.OptiFine与Forge不兼容);
            if (LogMc.Contains("java.lang.NoSuchMethodError: 'void net.minecraft.server.level.DistanceManager")) AppendReason(CrashReason.OptiFine与Forge不兼容);
            if (LogMc.Contains("java.lang.NoSuchMethodError: 'net.minecraft.network.chat.FormattedText net.minecraft.client.gui.Font.ellipsize")) AppendReason(CrashReason.OptiFine与Forge不兼容);
            if (LogMc.Contains("Open J9 is not supported") || LogMc.Contains("OpenJ9 is incompatible") || LogMc.Contains(".J9VMInternals.")) AppendReason(CrashReason.使用OpenJ9);
            if (LogMc.Contains("java.lang.NoSuchFieldException: ucp")) AppendReason(CrashReason.Java版本过高);
            if (LogMc.Contains("because module java.base does not export")) AppendReason(CrashReason.Java版本过高);
            if (LogMc.Contains("java.lang.ClassNotFoundException: jdk.nashorn.api.scripting.NashornScriptEngineFactory")) AppendReason(CrashReason.Java版本过高);
            if (LogMc.Contains("java.lang.ClassNotFoundException: java.lang.invoke.LambdaMetafactory")) AppendReason(CrashReason.Java版本过高);
            if (LogMc.Contains("The directories below appear to be extracted jar files. Fix this before you continue.")) AppendReason(CrashReason.Mod文件被解压);
            if (LogMc.Contains("Extracted mod jars found, loading will NOT continue")) AppendReason(CrashReason.Mod文件被解压);
            if (LogMc.Contains("java.lang.ClassNotFoundException: org.spongepowered.asm.launch.MixinTweaker")) AppendReason(CrashReason.MixinBootstrap缺失);
            if (LogMc.Contains("Couldn't set pixel format")) AppendReason(CrashReason.显卡驱动不支持导致无法设置像素格式);
            if (LogMc.Contains("java.lang.OutOfMemoryError") || LogMc.Contains("an out of memory error")) AppendReason(CrashReason.内存不足);
            if (LogMc.Contains("java.lang.RuntimeException: Shaders Mod detected. Please remove it, OptiFine has built-in support for shaders.")) AppendReason(CrashReason.ShadersMod与OptiFine同时安装);
            if (LogMc.Contains("java.lang.NoSuchMethodError: sun.security.util.ManifestEntryVerifier") || LogMc.Contains("java.lang.NoSuchMethodError: 'void sun.security.util.ManifestEntryVerifier")) AppendReason(CrashReason.低版本Forge与高版本Java不兼容);
            if (LogMc.Contains("1282: Invalid operation")) AppendReason(CrashReason.光影或资源包导致OpenGL1282错误);
            if (LogMc.Contains("signer information does not match signer information of other classes in the same package")) AppendReason(CrashReason.文件或内容校验失败, (LogMc.RegexSeek(@"(?<=class "")[^']+(?=""'s signer information)") ?? "").TrimEnd('\r', '\n'));
            if (LogMc.Contains("Maybe try a lower resolution resourcepack?")) AppendReason(CrashReason.材质过大或显卡配置不足);
            if (LogMc.Contains("java.lang.NoSuchMethodError: net.minecraft.world.server.ChunkManager$ProxyTicketManager.shouldForceTicks(J)Z") && LogMc.Contains("OptiFine")) AppendReason(CrashReason.OptiFine导致无法加载世界);
            if (LogMc.Contains("com.electronwill.nightconfig.core.io.ParsingException: Not enough data available") && !CrashReasons.ContainsKey(CrashReason.Mod配置文件导致游戏崩溃)) AppendReason(CrashReason.NightConfig的Bug);
            if (LogMc.Contains("Cannot find launch target fmlclient, unable to launch")) AppendReason(CrashReason.Forge安装不完整);
            if (LogMc.Contains("Invalid paths argument, contained no existing paths") && LogMc.Contains(@"libraries\net\minecraftforge\fmlcore")) AppendReason(CrashReason.Forge安装不完整);
            if (LogMc.Contains("Invalid module name: '' is not a Java identifier")) AppendReason(CrashReason.Mod名称包含特殊字符);
            if (LogMc.Contains("has been compiled by a more recent version of the Java Runtime (class file version 55.0), this version of the Java Runtime only recognizes class file versions up to")) AppendReason(CrashReason.Mod需要Java11);
            if (LogMc.Contains("java.lang.RuntimeException: java.lang.NoSuchMethodException: no such method: sun.misc.Unsafe.defineAnonymousClass(Class,byte[],Object[])Class/invokeVirtual")) AppendReason(CrashReason.Mod需要Java11);
            if (LogMc.Contains("java.lang.IllegalArgumentException: The requested compatibility level JAVA_11 could not be set. Level is not supported by the active JRE or ASM version")) AppendReason(CrashReason.Mod需要Java11);
            if (LogMc.Contains("Unsupported class file major version")) AppendReason(CrashReason.Java版本不兼容);
            if (LogMc.Contains("Unsupported major.minor version")) AppendReason(CrashReason.Java版本不兼容);
            if (LogMc.Contains("Level is not supported by the active JRE or ASM version")) AppendReason(CrashReason.Java版本不兼容);
            if (LogMc.Contains("Invalid maximum heap size")) AppendReason(CrashReason.使用32位Java导致JVM无法分配足够多的内存);
            if (LogMc.Contains("Could not reserve enough space"))
            {
                if (LogMc.Contains("for 1048576KB object heap"))
                {
                    AppendReason(CrashReason.使用32位Java导致JVM无法分配足够多的内存);
                }
                else
                {
                    AppendReason(CrashReason.内存不足);
                }
            }
            //确定的 Mod 导致崩溃
            if (LogMc.Contains("Caught exception from ")) AppendReason(CrashReason.确定Mod导致游戏崩溃, TryAnalyzeModName(LogMc.RegexSeek(@"(?<=Caught exception from )[^\n]+?")?.TrimEnd('\r', '\n', ' ')));
            //Mod 重复 / 前置问题
            if (LogMc.Contains("DuplicateModsFoundException")) AppendReason(CrashReason.Mod重复安装, LogMc.RegexSearch(@"(?<=\n\t[\w]+ : [A-Z]:[^\n]+(/|\\))[^/\\\n]+?.jar", RegexOptions.IgnoreCase));
            if (LogMc.Contains("Found a duplicate mod")) AppendReason(CrashReason.Mod重复安装, (LogMc.RegexSeek("Found a duplicate mod[^\n]+") ?? "").RegexSearch(@"[^\\/]+.jar", RegexOptions.IgnoreCase));
            if (LogMc.Contains("Found duplicate mods")) AppendReason(CrashReason.Mod重复安装, LogMc.RegexSearch(@"(?<=Mod ID: ')\w+?(?=' from mod files:)").Distinct().ToList());
            if (LogMc.Contains("ModResolutionException: Duplicate")) AppendReason(CrashReason.Mod重复安装, (LogMc.RegexSeek("ModResolutionException: Duplicate[^\n]+") ?? "").RegexSearch(@"[^\\/]+.jar", RegexOptions.IgnoreCase));
            if (LogMc.Contains("Incompatible mods found!")) //#5006, #5115
            {
                AppendReason(CrashReason.Mod互不兼容, (LogMc.RegexSeek(@"(?<=Incompatible mods found![\s\S]+: )[\s\S]+?(?=\tat )") ?? "").BeforeFirst("更多信息：").Replace("Some of your mods are incompatible with the game or each other!", "").Trim('\r', '\n', ' '));
            }
            if (LogMc.Contains("Missing or unsupported mandatory dependencies:"))
            {
                AppendReason(CrashReason.Mod缺少前置或MC版本错误,
                    LogMc.RegexSearch(@"(?<=Missing or unsupported mandatory dependencies:)([\n\r]+\t(.*))+", RegexOptions.IgnoreCase).
                    Select(static s => s.Trim('\r', '\n', '\t', ' ')).Distinct().ToList());
            }
        }

        //虚拟机日志分析
        if (LogHs is not null)
        {
            if (LogHs.Contains("The system is out of physical RAM or swap space")) AppendReason(CrashReason.内存不足);
            if (LogHs.Contains("Out of Memory Error")) AppendReason(CrashReason.内存不足);
            if (LogHs.Contains("EXCEPTION_ACCESS_VIOLATION"))
            {
                if (LogHs.Contains("# C  [ig")) AppendReason(CrashReason.Intel驱动不兼容导致EXCEPTION_ACCESS_VIOLATION);
                if (LogHs.Contains("# C  [atio")) AppendReason(CrashReason.AMD驱动不兼容导致EXCEPTION_ACCESS_VIOLATION);
                if (LogHs.Contains("# C  [nvoglv")) AppendReason(CrashReason.Nvidia驱动不兼容导致EXCEPTION_ACCESS_VIOLATION);
            }
        }

        //崩溃报告分析
        if (LogCrash is not null)
        {
            if (LogCrash.Contains("maximum id range exceeded")) AppendReason(CrashReason.Mod过多导致超出ID限制);
            if (LogCrash.Contains("java.lang.OutOfMemoryError")) AppendReason(CrashReason.内存不足);
            if (LogCrash.Contains("Pixel format not accelerated")) AppendReason(CrashReason.显卡驱动不支持导致无法设置像素格式);
            if (LogCrash.Contains("Manually triggered debug crash")) AppendReason(CrashReason.玩家手动触发调试崩溃);
            if (LogCrash.Contains("has mods that were not found") && LogCrash.RegexCheck(@"The Mod File [^\n]+optifine\\OptiFine[^\n]+ has mods that were not found")) AppendReason(CrashReason.OptiFine与Forge不兼容);
            //Mod 导致的崩溃
            if (LogCrash.Contains("-- MOD "))
            {
                var logCrashMod = LogCrash.Between("-- MOD ", "Failure message:");
                if (logCrashMod.ContainsIgnoreCase(".jar"))
                {
                    AppendReason(CrashReason.确定Mod导致游戏崩溃, (LogCrash.RegexSeek("(?<=Mod File: ).+") ?? "").TrimEnd('\r', '\n', ' '));
                }
                else
                {
                    AppendReason(CrashReason.Mod加载器报错, (LogCrash.RegexSeek(@"(?<=Failure message: )[\w\W]+?(?=\tMod)") ?? "").Replace("\t", " ").TrimEnd('\r', '\n', ' '));
                }
            }
            if (LogCrash.Contains("Multiple entries with same key: ")) AppendReason(CrashReason.确定Mod导致游戏崩溃, TryAnalyzeModName((LogCrash.RegexSeek("(?<=Multiple entries with same key: )[^=]+") ?? "").TrimEnd('\r', '\n', ' ')));
            if (LogCrash.Contains("LoaderExceptionModCrash: Caught exception from ")) AppendReason(CrashReason.确定Mod导致游戏崩溃, TryAnalyzeModName((LogCrash.RegexSeek(@"(?<=LoaderExceptionModCrash: Caught exception from )[^\n]+") ?? "").TrimEnd('\r', '\n', ' ')));
            if (LogCrash.Contains("Failed loading config file ")) AppendReason(CrashReason.Mod配置文件导致游戏崩溃, [TryAnalyzeModName((LogCrash.RegexSeek(@"(?<=Failed loading config file .+ for modid )[^\n]+") ?? "").TrimEnd('\r', '\n')).First(), (LogCrash.RegexSeek(@"(?<=Failed loading config file ).+(?= of type)") ?? "").TrimEnd('\r', '\n')]);
        }

    }

    /// <summary>
    /// 进行精准日志匹配。匹配优先级高于堆栈分析的崩溃，但低于上面的。
    /// 如果第一步已经找到了原因则不执行该检测。（旧 :589-645）
    /// </summary>
    private void AnalyzeCrit2()
    {
        //! 逐字移植豁免：正向 if 嵌套逐字对照旧 :589-645（卫语句反转破坏行级对照）

        //游戏日志分析
        if (LogMc is not null)
        {
            //Mixin 崩溃
            var isMixin = MixinAnalyze(LogMc);
            //常规信息
            if (LogMc.Contains("An exception was thrown, the game will display an error screen and halt.")) AppendReason(CrashReason.Forge报错, LogMc.RegexSeek(@"(?<=the game will display an error screen and halt.[\n\r]+[^\n]+?Exception: )[\s\S]+?(?=\n\tat)")?.Trim('\r', '\n'));
            if (LogMc.Contains("A potential solution has been determined:")) AppendReason(CrashReason.Fabric报错并给出解决方案, (LogMc.RegexSeek(@"(?<=A potential solution has been determined:\n)(\s+ - [^\n]+\n)+") ?? "").RegexSearch(@"(?<=\s+)[^\n]+").Join("\n"));
            if (LogMc.Contains("A potential solution has been determined, this may resolve your problem:")) AppendReason(CrashReason.Fabric报错并给出解决方案, (LogMc.RegexSeek(@"(?<=A potential solution has been determined, this may resolve your problem:\n)(\s+ - [^\n]+\n)+") ?? "").RegexSearch(@"(?<=\s+)[^\n]+").Join("\n"));
            if (LogMc.Contains("确定了一种可能的解决方法，这样做可能会解决你的问题：")) AppendReason(CrashReason.Fabric报错并给出解决方案, (LogMc.RegexSeek(@"(?<=确定了一种可能的解决方法，这样做可能会解决你的问题：\n)(\s+ - [^\n]+\n)+") ?? "").RegexSearch(@"(?<=\s+)[^\n]+").Join("\n"));
            if (!isMixin && LogMc.Contains("due to errors, provided by ")) //在 #3104 的情况下，这一句导致 OptiFabric 的 Mixin 失败错判为 Fabric Loader 加载失败
            {
                AppendReason(CrashReason.确定Mod导致游戏崩溃, TryAnalyzeModName((LogMc.RegexSeek(@"(?<=due to errors, provided by ')[^']+") ?? "").TrimEnd('\r', '\n', ' ')));
            }
        }

        //崩溃报告分析
        if (LogCrash is not null)
        {
            //Mixin 崩溃
            MixinAnalyze(LogCrash);
            //常规信息
            if (LogCrash.Contains("Suspected Mod"))
            {
                var suspectsRaw = LogCrash.Between("Suspected Mod", "Stacktrace");
                if (!suspectsRaw.StartsWithF("s: None")) //Suspected Mods: None
                {
                    var suspects = suspectsRaw.RegexSearch(@"(?<=\n\t[^(\t]+\()[^)\n]+").ToList(); //物化一次再逐处消费（旧逐次枚举，结果相同）
                    if (suspects.Any()) AppendReason(CrashReason.怀疑Mod导致游戏崩溃, TryAnalyzeModName(suspects));
                }
            }
        }

        //Mixin 分析（旧 :592-615 的 lambda；C# 局部函数可后置声明）
        bool MixinAnalyze(string logText)
        {
            var isMixin =
                logText.Contains("Mixin prepare failed ") || logText.Contains("Mixin apply failed ") ||
                logText.Contains("MixinApplyError") || logText.Contains("MixinTransformerError") ||
                logText.Contains("mixin.injection.throwables.") || logText.Contains(".json] FAILED during )");
            if (!isMixin) return false;
            //Mod 名称匹配
            var modName = logText.RegexSeek(@"(?<=from mod )[^.\/ ]+(?=\] from)");
            if (modName is null) modName = logText.RegexSeek(@"(?<=for mod )[^.\/ ]+(?= failed)");
            if (modName is not null)
            {
                AppendReason(CrashReason.ModMixin失败, TryAnalyzeModName(modName.TrimEnd('\r', '\n', ' ')));
                return true;
            }
            //JSON 名称匹配
            foreach (var jsonName in logText.RegexSearch(@"(?<=^[^\t]+[ \[{(])[^ \[{(]+\.[^ ]+(?=\.json)", RegexOptions.Multiline))
            {
                AppendReason(CrashReason.ModMixin失败,
                             TryAnalyzeModName(jsonName.Replace("mixins", "mixin").Replace(".mixin", "").Replace("mixin.", "")));
                return true;
            }
            //没有明确匹配
            AppendReason(CrashReason.ModMixin失败);
            return true;
        }

    }

    /// <summary>
    /// 进行精准日志匹配。匹配优先级低于堆栈分析的崩溃。（旧 :649-670）
    /// </summary>
    private void AnalyzeCrit3()
    {
        //! 逐字移植豁免：正向 if 嵌套逐字对照旧 :649-670（卫语句反转破坏行级对照）

        //游戏日志分析
        if (LogMc is not null)
        {
            //极短的程序输出
            if (!(LogMc.Contains("at net.") || LogMc.Contains("INFO]")) && LogHs is null && LogCrash is null && LogMc.Length < 100)
            {
                AppendReason(CrashReason.极短的程序输出, LogMc);
            }
            //Mod 解析错误（常见于 Fabric 前置校验失败）
            if (LogMc.Contains("Mod resolution failed")) AppendReason(CrashReason.Mod加载器报错);
            //Mixin 失败可以导致大量 Mod 实例创建失败，因此需要放到低优先级中
            if (LogMc.Contains("Failed to create mod instance.")) AppendReason(CrashReason.Mod初始化失败, TryAnalyzeModName((LogMc.RegexSeek(@"(?<=Failed to create mod instance. ModID: )[^,]+") ?? LogMc.RegexSeek(@"(?<=Failed to create mod instance. ModId )[^\n]+(?= for )") ?? "").TrimEnd('\r', '\n')));
            //注意：Fabric 的 Warnings were found! 不一定是崩溃原因，它可能是单纯的警报
        }

        //崩溃报告分析
        if (LogCrash is not null)
        {
            if (LogCrash.Contains("\tBlock location: World: ")) AppendReason(CrashReason.特定方块导致崩溃, (LogCrash.RegexSeek(@"(?<=\tBlock: Block\{)[^\}]+") ?? "") + " " + (LogCrash.RegexSeek(@"(?<=\tBlock location: World: )\([^\)]+\)") ?? ""));
            if (LogCrash.Contains("\tEntity's Exact location: ")) AppendReason(CrashReason.特定实体导致崩溃, (LogCrash.RegexSeek(@"(?<=\tEntity Type: )[^\n]+(?= \()") ?? "") + " (" + (LogCrash.RegexSeek(@"(?<=\tEntity's Exact location: )[^\n]+") ?? "").TrimEnd('\r', '\n') + ")");
        }

    }

    /// <summary>
    /// 从堆栈中提取 Mod ID 关键字。若失败则返回空列表。（旧 :674-733）
    /// </summary>
    private static List<string> AnalyzeStackKeyword(string? errorStack)
    {
        errorStack = "\n" + (errorStack ?? "") + "\n";

        //进行正则匹配
        var stackSearchResults = new List<string>();
        stackSearchResults.AddRange(errorStack.RegexSearch(@"(?<=\n[^{]+)[a-zA-Z_]+\w+\.[a-zA-Z_]+[\w\.]+(?=\.[\w\.$]+\.)"));
        stackSearchResults.AddRange(errorStack.RegexSearch(@"(?<=at [^(]+?\.\w+\$\w+\$)[\w\$]+?(?=\$\w+\()").Select(static s => s.Replace("$", "."))); //Mixin 堆栈：xxx.xxx.xxxx$xxxx$xxx
        stackSearchResults = stackSearchResults.Distinct().ToList();

        //检查堆栈开头（旧 GoTo NextStack 跳过黑名单前缀）
        var possibleStacks = new List<string>();
        foreach (var stack in stackSearchResults)
        {
            if (new[]
            {
                "java", "sun", "javax", "jdk", "oolloo",
                "org.lwjgl", "com.sun", "net.minecraftforge", "paulscode.sound", "com.mojang", "net.minecraft", "cpw.mods", "com.google", "org.apache", "org.spongepowered", "net.fabricmc", "com.mumfrey",
                "com.electronwill.nightconfig", "it.unimi.dsi",
                "MojangTricksIntelDriversForPerformance_javaw"
            }.Any(ignoreStack => stack.StartsWithF(ignoreStack))) continue;
            possibleStacks.Add(stack.Trim());
        }
        possibleStacks = possibleStacks.Distinct().ToList();
        Logger.Info($"找到 {possibleStacks.Count} 条可能的堆栈信息");
        if (!possibleStacks.Any()) return [];
        foreach (var stack in possibleStacks)
        {
            Logger.Info($"- {stack}");
        }

        //检查堆栈关键词
        var possibleWords = new List<string>();
        foreach (var stack in possibleStacks)
        {
            var splited = stack.Split('.');
            for (var i = 0; i <= Math.Min(3, splited.Length - 1); i++) //最多取前 4 节
            {
                var word = splited[i];
                if (word.Length <= 2 || word.StartsWithF("func_")) continue;
                if (new[]
                {
                    "com", "org", "net", "asm", "fml", "mod", "jar", "sun", "lib", "map", "gui", "dev", "nio", "api", "dsi", "top", "mcp",
                    "core", "init", "mods", "main", "file", "game", "load", "read", "done", "util", "tile", "item", "base", "fake", "oshi", "impl", "data", "pool", "task",
                    "forge", "setup", "block", "model", "mixin", "event", "unimi", "netty", "world", "lwjgl", "fakes",
                    "fabric", "gitlab", "common", "server", "config", "mixins", "compat", "loader", "launch", "script", "entity", "assist", "client", "plugin", "modapi", "mojang", "shader", "events", "github", "recipe", "render", "packet", "events",
                    "preinit", "preload", "machine", "reflect", "channel", "general", "handler", "content", "systems", "modules", "service", "scripts", "network",
                    "fastutil", "optifine", "internal", "platform", "override", "fabricmc", "neoforge", "external",
                    "injection", "listeners", "scheduler", "minecraft", "universal", "multipart", "neoforged", "microsoft",
                    "transformer", "transformers", "minecraftforge", "blockentity", "spongepowered", "electronwill", "concurrent"
                }.Contains(word.Lower())) continue;
                possibleWords.Add(word.Trim());
            }
        }
        possibleWords = possibleWords.Distinct().ToList();
        Logger.Info($"从堆栈信息中找到 {possibleWords.Count} 个可能的 Mod ID 关键词");
        if (possibleWords.Any()) Logger.Info($"- {possibleWords.Join(", ")}");
        if (possibleWords.Count > 10)
        {
            Logger.Info("关键词过多，考虑匹配出错，不纳入考虑");
            return [];
        }
        else
        {
            return possibleWords;
        }

    }

    /// <summary>
    /// 根据 Mod 关键词尝试获取实际的 Mod 名称。
    /// 若失败则返回 null。（旧 :738-839）
    /// </summary>
    private List<string>? AnalyzeModName(IEnumerable<string> keywords)
    {
        //! 逐字移植豁免：关键词预处理与 Mod 行筛选循环逐字对照旧 :738-839（LINQ 化破坏行级对照）
        var modFileNames = new List<string>();

        //预处理关键词（分割括号）
        var realKeywords = new List<string>();
        foreach (var keyword in keywords)
        {
            foreach (var subKeyword in keyword.Split('('))
            {
                realKeywords.Add(subKeyword.Trim(' ', ')'));
            }
        }
        keywords = realKeywords;

        //从崩溃报告获取 Mod 信息
        if (LogCrash is not null && LogCrash.Contains("A detailed walkthrough of the error"))
        {
            var details = LogCrash.Replace("A detailed walkthrough of the error", "¨");
            var isFabricDetail = details.Contains("Fabric Mods"); //是否为 Fabric 信息格式
            if (isFabricDetail)
            {
                details = details.Replace("Fabric Mods", "¨");
                Logger.Info("崩溃报告中检测到 Fabric Mod 信息格式");
            }
            details = details.AfterLast("¨");

            //[Forge] 获取所有包含 .jar 的行
            //[Fabric] 获取所有包含 Mod 信息的行
            var modNameLines = new List<string>();
            foreach (var line in details.Split('\n'))
            {
                if ((line.ContainsIgnoreCase(".jar") && line.Length - line.Replace(".jar", "").Length == 4) || //只有一个 .jar
                    (isFabricDetail && line.StartsWithF("\t\t") && !line.RegexCheck(@"\t\tfabric[\w-]*: Fabric")))
                {
                    modNameLines.Add(line);
                }
            }
            Logger.Info($"崩溃报告中找到 {modNameLines.Count} 个可能的 Mod 项目行");

            //获取 Mod ID 与关键词的匹配行
            var hintLines = new List<string>();
            foreach (var keyWord in keywords)
            {
                foreach (var modString in modNameLines)
                {
                    var realModString = modString.Lower().Replace("_", "");
                    if (!realModString.Contains(keyWord.Lower().Replace("_", ""))) continue;
                    if (realModString.Contains("minecraft.jar") || realModString.Contains(" forge-") || realModString.Contains(" mixin-")) continue;
                    hintLines.Add(modString.Trim('\r', '\n'));
                    break;
                }
            }
            hintLines = hintLines.Distinct().ToList();
            Logger.Info($"崩溃报告中找到 {hintLines.Count} 个可能的崩溃 Mod 匹配行");
            foreach (var modLine in hintLines)
            {
                Logger.Info($"- {modLine}");
            }

            //从 Mod 匹配行中提取 .jar 文件的名称
            foreach (var line in hintLines)
            {
                string? name;
                if (isFabricDetail)
                {
                    name = line.RegexSeek(@"(?<=: )[^\n]+(?= [^\n]+)");
                }
                else
                {
                    name = line.RegexSeek(@"(?<=\()[^\t]+.jar(?=\))|(?<=(\t\t)|(\| ))[^\t\|]+.jar", RegexOptions.IgnoreCase);
                }
                if (name is not null) modFileNames.Add(name);
            }

        }

        //从 debug.log 获取 Mod 信息
        if (LogMcDebug is not null)
        {

            //Forge: Found valid mod file YungsBetterStrongholds-1.20-Forge-4.0.1.jar with {betterstrongholds} mods - versions {1.20-Forge-4.0.1}
            var modNameLinesDebug = LogMcDebug.RegexSearch(@"(?<=valid mod file ).*", RegexOptions.Multiline).ToList();
            Logger.Info($"Debug 信息中找到 {modNameLinesDebug.Count} 个可能的 Mod 项目行");

            //获取 Mod ID 与关键词的匹配行
            var hintLinesDebug = new List<string>();
            foreach (var keyWord in keywords)
            {
                foreach (var modString in modNameLinesDebug)
                {
                    if (modString.Contains("{" + keyWord + "}")) hintLinesDebug.Add(modString);
                }
            }
            hintLinesDebug = hintLinesDebug.Distinct().ToList();
            Logger.Info($"Debug 信息中找到 {hintLinesDebug.Count} 个可能的崩溃 Mod 匹配行");
            foreach (var modLine in hintLinesDebug)
            {
                Logger.Info($"- {modLine}");
            }

            //从 Mod 匹配行中提取 .jar 文件的名称
            foreach (var line in hintLinesDebug)
            {
                var name = line.RegexSeek(".*(?= with)");
                if (name is not null) modFileNames.Add(name);
            }

        }

        //输出
        modFileNames = modFileNames.Distinct().ToList();
        if (!modFileNames.Any())
        {
            return null;
        }
        else
        {
            Logger.Info($"找到 {modFileNames.Count} 个可能的崩溃 Mod 文件名");
            foreach (var modFileName in modFileNames)
            {
                Logger.Info($"- {modFileName}");
            }
            return modFileNames;
        }
    }

    /// <summary>
    /// 尝试从关键字获取 Mod 名称，若失败则返回原关键字。（旧 :843-847）
    /// </summary>
    private List<string> TryAnalyzeModName(string? keyword)
    {
        var rawList = new List<string> { keyword ?? "" };
        if (string.IsNullOrEmpty(keyword)) return rawList;
        return AnalyzeModName(rawList) ?? rawList;
    }

    /// <summary>
    /// 尝试从关键字获取 Mod 名称，若失败则返回原关键字。（旧 :851-854）
    /// </summary>
    private List<string> TryAnalyzeModName(IEnumerable<string> keywords)
    {
        var keywordList = keywords.ToList(); //物化一次再逐处消费（旧 IEnumerable 逐次枚举，结果相同）
        if (keywordList.Count == 0) return keywordList;
        return AnalyzeModName(keywordList) ?? keywordList;
    }

    //4：根据原因输出信息
    /// <summary>可用于生成报告的文件（旧 :857；Prepare 收集，Output 段消费）。</summary>
    public List<string> OutputFiles { get; } = [];

    /// <summary>
    /// 获取崩溃分析的结果描述（旧 :924-1113；\n/\h/\e 为约定标记，出口处统一替换）。
    /// </summary>
    public string GetAnalyzeResult(bool isHandAnalyze)
    {

        //没有结果的处理
        if (!CrashReasons.Any())
        {
            return isHandAnalyze
                ? "很抱歉，PCL 无法确定错误原因。"
                : "很抱歉，你的游戏出现了一些问题……\r\n如果要寻求帮助，请把错误报告文件发给对方，而不是发送这个窗口的照片或者截图。";
        }

        //根据不同原因判断
        var results = new List<string>();
        foreach (var reason in CrashReasons)
        {
            var additional = reason.Value;
            switch (reason.Key)
            {
                case CrashReason.Java虚拟机参数有误:
                    results.Add(@"由于 Java 参数有误，导致游戏无法继续运行。\n请检查高级选项中的 Java 虚拟机参数和游戏参数设置是否有误。");
                    break;
                case CrashReason.Mod文件被解压:
                    results.Add(@"由于 Mod 文件被解压了，导致游戏无法继续运行。\n直接把整个 Mod 文件放进 Mod 文件夹中即可，若解压就会导致游戏出错。\n\n请删除 Mod 文件夹中已被解压的 Mod，然后再启动游戏。");
                    break;
                case CrashReason.内存不足:
                    results.Add(@"Minecraft 内存不足，导致其无法继续运行。\n这很可能是因为电脑内存不足、游戏分配的内存不足，或是配置要求过高。\n\n你可以尝试在 更多 → 百宝箱 中选择 内存优化，然后再启动游戏。\n如果还是不行，请在启动设置中增加为游戏分配的内存，并删除配置要求较高的材质、Mod、光影。\n如果依然不奏效，请在开始游戏前尽量关闭其他软件，或者……换台电脑？\h");
                    break;
                case CrashReason.使用OpenJ9:
                    results.Add(@"游戏因为使用 OpenJ9 而崩溃了。\n请在版本设置的 Java 选项中改用非 OpenJ9 的 Java，然后再启动游戏。");
                    break;
                case CrashReason.使用JDK:
                    results.Add(@"游戏似乎因为使用 JDK，或 Java 版本过高而崩溃了。\n请在版本设置的 Java 选项中改用 Java 8，然后再启动游戏。\n如果你没有安装 Java 8，可以从网络中下载、安装一个。");
                    break;
                case CrashReason.Java版本过高:
                    results.Add(@"游戏似乎因为你所使用的 Java 版本过高而崩溃了。\n请在版本设置的 Java 选项中改用较低版本的 Java，然后再启动游戏。\n如果没有，可以从网络中下载、安装一个。");
                    break;
                case CrashReason.Java版本不兼容:
                    results.Add(@"游戏不兼容你当前使用的 Java。\n请在版本设置的 Java 选项中改用合适版本的 Java，然后再启动游戏。\n如果你没有安装任何 Mod 并且因此崩溃，请向 PCL 提交反馈。");
                    break;
                case CrashReason.Mod名称包含特殊字符:
                    results.Add(@"由于有 Mod 的名称包含特殊字符，导致游戏崩溃。\n请尝试修改 Mod 文件名，让它只包含英文字母、数字、减号（-）、下划线（_）和小数点，然后再启动游戏。");
                    break;
                case CrashReason.MixinBootstrap缺失:
                    results.Add(@"由于缺失 MixinBootstrap，导致游戏崩溃。\n请尝试安装 MixinBootstrap。若安装后依然崩溃，可以尝试在文件名前添加英文感叹号。");
                    break;
                case CrashReason.使用32位Java导致JVM无法分配足够多的内存:
                    if (Environment.Is64BitOperatingSystem)
                    {
                        results.Add(@"你似乎正在使用 32 位 Java，这会导致 Minecraft 无法使用所需的内存，进而造成崩溃。\n\n请在启动设置的 Java 选择一项中改用 64 位的 Java 再启动游戏，然后再启动游戏。\n如果你没有安装 64 位的 Java，你可以从网络中下载、安装一个。");
                    }
                    else
                    {
                        results.Add(@"你正在使用 32 位的操作系统，这会导致 Minecraft 无法使用所需的内存，进而造成崩溃。\n\n你或许只能重装 64 位的操作系统来解决此问题。\n如果你的电脑内存在 2GB 以内，那或许只能换台电脑了……\h");
                    }
                    break;
                case CrashReason.Mod缺少前置或MC版本错误:
                    if (additional.Any())
                    {
                        results.Add("由于未安装正确的前置 Mod，导致游戏退出。\n缺失的依赖项：\n - " + additional.Join("\\n - ") + "\n\n请根据上述信息进行对应处理，如果看不懂英文可以使用翻译软件。");
                    }
                    else
                    {
                        results.Add(@"由于未安装正确的前置 Mod，导致游戏退出。\n请根据错误报告中的日志信息进行对应处理，如果看不懂英文可以使用翻译软件。\h");
                    }
                    break;
                case CrashReason.堆栈分析发现关键字:
                    if (additional.IsSingle())
                    {
                        results.Add("你的游戏遇到了一些问题，PCL 为此找到了一个可疑的关键词：" + additional.First() + @"。\n\n如果你知道某个关键词对应的 Mod，那么有可能就是它引起的错误，你也可以查看错误报告获取详情。\h");
                    }
                    else
                    {
                        results.Add(@"你的游戏遇到了一些问题，PCL 为此找到了以下可疑的关键词：\n - " + additional.Join(", ") + @"\n\n如果你知道某个关键词对应的 Mod，那么有可能就是它引起的错误，你也可以查看错误报告获取详情。\h");
                    }
                    break;
                case CrashReason.堆栈分析发现Mod名称:
                case CrashReason.怀疑Mod导致游戏崩溃:
                    if (additional.IsSingle())
                    {
                        results.Add("PCL 怀疑名为 " + additional.First() + @" 的 Mod 导致了游戏出错，但不能完全确定。\n你可以尝试禁用此 Mod，然后观察游戏是否还会崩溃。\n\e\h");
                    }
                    else
                    {
                        results.Add(@"PCL 怀疑以下 Mod 导致了游戏出错，但不能完全确定：\n - " + additional.Join("\\n - ") + @"\n\n你可以尝试依次禁用上述 Mod，然后观察游戏是否还会崩溃。\n\e\h");
                    }
                    break;
                case CrashReason.确定Mod导致游戏崩溃:
                    if (additional.IsSingle())
                    {
                        results.Add("名为 " + additional.First() + @" 的 Mod 导致了游戏出错。\n你可以尝试禁用此 Mod，然后观察游戏是否还会崩溃。\n\e\h");
                    }
                    else
                    {
                        results.Add(@"以下 Mod 导致了游戏出错：\n - " + additional.Join("\\n - ") + @"\n\n你可以尝试依次禁用上述 Mod，然后观察游戏是否还会崩溃。\n\e\h");
                    }
                    break;
                case CrashReason.ModMixin失败:
                    if (additional.Count == 0)
                    {
                        results.Add(@"部分 Mod 注入失败，导致游戏出错。\n这一般代表着部分 Mod 与其他 Mod 或当前环境不兼容，或是它存在 Bug。\n你可以尝试逐步禁用 Mod，然后观察游戏是否还会崩溃，以此定位导致崩溃的 Mod。\n\e\h");
                    }
                    else if (additional.IsSingle())
                    {
                        results.Add("名为 " + additional.First() + @" 的 Mod 注入失败，导致游戏出错。\n这一般代表着它与其他 Mod 或当前环境不兼容，或是它存在 Bug。\n你可以尝试禁用此 Mod，然后观察游戏是否还会崩溃。\n\e\h");
                    }
                    else
                    {
                        results.Add(@"以下 Mod 导致了游戏出错：\n - " + additional.Join("\\n - ") + @"\n这一般代表着它们与其他 Mod 或当前环境不兼容，或是它存在 Bug。\n你可以尝试依次禁用上述 Mod，然后观察游戏是否还会崩溃。\n\e\h");
                    }
                    break;
                case CrashReason.Mod配置文件导致游戏崩溃:
                    if ((object?)additional[1] is null) //! 逐字保留的运行空判（旧 Additional(1) Is Nothing；元素注解非空，运行时仍可为 null）
                    {
                        results.Add("名为 " + additional.First() + @" 的 Mod 导致了游戏出错。\n\e\h");
                    }
                    else
                    {
                        results.Add("名为 " + additional.First() + " 的 Mod 导致了游戏出错：\n其配置文件 " + additional[1] + " 存在异常，无法读取。");
                    }
                    break;
                case CrashReason.Mod初始化失败:
                    if (additional.IsSingle())
                    {
                        results.Add("名为 " + additional.First() + @" 的 Mod 初始化失败，导致游戏无法继续加载。\n你可以尝试禁用此 Mod，然后观察游戏是否还会崩溃。\n\e\h");
                    }
                    else
                    {
                        results.Add(@"以下 Mod 初始化失败，导致游戏出错：\n - " + additional.Join("\\n - ") + @"\n\n你可以尝试依次禁用上述 Mod，然后观察游戏是否还会崩溃。\n\e\h");
                    }
                    break;
                case CrashReason.特定方块导致崩溃:
                    if (additional.IsSingle())
                    {
                        results.Add("游戏似乎因为方块 " + additional.First() + @" 出现了问题。\n\n你可以创建一个新世界，并观察游戏的运行情况：\n - 若正常运行，则是该方块导致出错，你或许需要使用一些方式删除此方块。\n - 若仍然出错，问题就可能来自其他原因……\h");
                    }
                    else
                    {
                        results.Add(@"游戏似乎因为世界中的某些方块出现了问题。\n\n你可以创建一个新世界，并观察游戏的运行情况：\n - 若正常运行，则是某些方块导致出错，你或许需要删除该世界。\n - 若仍然出错，问题就可能来自其他原因……\h");
                    }
                    break;
                case CrashReason.Mod重复安装:
                    if (additional.Count >= 2)
                    {
                        results.Add(@"你重复安装了多个相同的 Mod：\n - " + additional.Join("\\n - ") + "\n\n每个 Mod 只能出现一次，请删除重复的 Mod，然后再启动游戏。");
                    }
                    else
                    {
                        results.Add(@"你可能重复安装了多个相同的 Mod，导致游戏出错。\n\n每个 Mod 只能出现一次，请删除重复的 Mod，然后再启动游戏。\e\h");
                    }
                    break;
                case CrashReason.特定实体导致崩溃:
                    if (additional.IsSingle())
                    {
                        if (additional.First().Contains("minecraft:player"))
                        {
                            if (additional.First().Contains(" "))
                            {
                                results.Add($"游戏因为位于 {additional.First().AfterFirst(" ")} 的玩家实体导致了崩溃。\\h");
                            }
                            else
                            {
                                results.Add("游戏因为玩家实体导致了崩溃。\\h");
                            }
                        }
                        else
                        {
                            results.Add($"游戏因为实体 {additional.First()} 导致了崩溃。\\h");
                        }
                    }
                    else
                    {
                        results.Add($"游戏因为下列实体导致了崩溃：{additional.Join('、')}\\h");
                    }
                    break;
                case CrashReason.OptiFine与Forge不兼容:
                    results.Add(@"由于 OptiFine 与当前版本的 Forge 不兼容，导致了游戏崩溃。\n\n请前往 OptiFine 官网（https://optifine.net/downloads）查看 OptiFine 所兼容的 Forge 版本，并严格按照对应版本重新安装游戏。");
                    break;
                case CrashReason.ShadersMod与OptiFine同时安装:
                    results.Add(@"无需同时安装 OptiFine 和 Shaders Mod，OptiFine 已经集成了 Shaders Mod 的功能。\n在删除 Shaders Mod 后，游戏即可正常运行。");
                    break;
                case CrashReason.低版本Forge与高版本Java不兼容:
                    results.Add(@"由于低版本 Forge 与当前 Java 不兼容，导致了游戏崩溃。\n\n请尝试以下解决方案：\n - 更新 Forge 到 36.2.26 或更高版本\n - 换用版本低于 1.8.0.320 的 Java");
                    break;
                case CrashReason.版本Json中存在多个Forge:
                    results.Add(@"可能由于其他启动器修改了 Forge 版本，当前版本的文件存在异常，导致了游戏崩溃。\n请尝试重新全新安装 Forge，而非使用其他启动器修改 Forge 版本。");
                    break;
                case CrashReason.玩家手动触发调试崩溃:
                    results.Add(@"* 事实上，你的游戏没有任何问题，这是你自己触发的崩溃。\n* 你难道没有更重要的事要做吗？");
                    break;
                case CrashReason.Mod需要Java11:
                    results.Add(@"你所安装的部分 Mod 似乎需要使用 Java 11 启动。\n请在启动设置的 Java 选择一项中改用 Java 11，然后再启动游戏。\n如果你没有安装 Java 11，你可以从网络中下载、安装一个。");
                    break;
                case CrashReason.极短的程序输出:
                    results.Add($"程序返回了以下信息：\\n{additional.First()}\\n\\h");
                    break;
                case CrashReason.OptiFine导致无法加载世界:
                    results.Add(@"你所使用的 OptiFine 可能导致了你的游戏出现问题。\n\n该问题只在特定 OptiFine 版本中出现，你可以尝试更换 OptiFine 的版本。\h");
                    break;
                case CrashReason.显卡驱动不支持导致无法设置像素格式:
                case CrashReason.Intel驱动不兼容导致EXCEPTION_ACCESS_VIOLATION:
                case CrashReason.AMD驱动不兼容导致EXCEPTION_ACCESS_VIOLATION:
                case CrashReason.Nvidia驱动不兼容导致EXCEPTION_ACCESS_VIOLATION:
                case CrashReason.显卡不支持OpenGL:
                    if (LogAll!.Contains("hd graphics ")) //! GetAnalyzeResult 依赖 Analyze 先行（VB 同序）
                    {
                        results.Add(@"你的显卡驱动存在问题，或未使用独立显卡，导致游戏无法正常运行。\n\n如果你的电脑存在独立显卡，请使用独立显卡而非 Intel 核显启动 PCL 与 Minecraft。\n如果问题依然存在，请尝试升级你的显卡驱动到最新版本，或回退到出厂版本。\n如果还是不行，还可以尝试使用 8.0.51 或更低版本的 Java。\h");
                    }
                    else
                    {
                        results.Add(@"你的显卡驱动存在问题，导致游戏无法正常运行。\n\n请尝试升级你的显卡驱动到最新版本，或回退到出厂版本，然后再启动游戏。\n如果还是不行，可以尝试使用 8.0.51 或更低版本的 Java。\n如果问题依然存在，那么你可能需要换个更好的显卡……\h");
                    }
                    break;
                case CrashReason.材质过大或显卡配置不足:
                    results.Add(@"你所使用的材质分辨率过高，或显卡配置不足，导致游戏无法继续运行。\n\n如果你正在使用高清材质，请将它移除。\n如果你没有使用材质，那么你可能需要更新显卡驱动，或者换个更好的显卡……\h");
                    break;
                case CrashReason.NightConfig的Bug:
                    results.Add(@"由于 Night Config 存在问题，导致了游戏崩溃。\n你可以尝试安装 Night Config Fixes 模组，这或许能解决此问题。\h");
                    break;
                case CrashReason.光影或资源包导致OpenGL1282错误:
                    results.Add(@"你所使用的光影或材质导致游戏出现了一些问题……\n\n请尝试删除你所添加的这些额外资源。\h");
                    break;
                case CrashReason.Mod过多导致超出ID限制:
                    results.Add(@"你所安装的 Mod 过多，超出了游戏的 ID 限制，导致了游戏崩溃。\n请尝试安装 JEID 等修复 Mod，或删除部分大型 Mod。");
                    break;
                case CrashReason.文件或内容校验失败:
                    results.Add(@"部分文件或内容校验失败，导致游戏出现了问题。\n\n请尝试删除游戏（包括 Mod）并重新下载，或尝试在重新下载时使用 VPN。\h");
                    break;
                case CrashReason.Forge安装不完整:
                    results.Add(@"由于安装的 Forge 文件丢失，导致游戏无法正常运行。\n请重新安装一次相同版本的 Forge，然后再启动游戏。\n在打包游戏时删除 libraries 文件夹可能导致此错误。\h");
                    break;
                case CrashReason.Fabric报错:
                    if (additional.IsSingle())
                    {
                        results.Add("Fabric 提供了以下错误信息：\n" + additional.First() + "\n\n请根据上述信息进行对应处理，如果看不懂英文可以使用翻译软件。");
                    }
                    else
                    {
                        results.Add(@"Fabric 可能已经提供了错误信息，请根据错误报告中的日志信息进行对应处理，如果看不懂英文可以使用翻译软件。\h");
                    }
                    break;
                case CrashReason.Mod互不兼容:
                    if (additional.IsSingle())
                    {
                        results.Add("你所安装的 Mod 不兼容：\n" + additional.First() + "\n\n请根据上述信息进行对应处理，如果看不懂英文可以使用翻译软件。");
                    }
                    else
                    {
                        results.Add(@"你所安装的 Mod 不兼容，Mod 加载器可能已经提供了错误信息，请根据错误报告中的日志信息进行对应处理，如果看不懂英文可以使用翻译软件。\h");
                    }
                    break;
                case CrashReason.Mod加载器报错:
                    if (additional.IsSingle())
                    {
                        results.Add("Mod 加载器提供了以下错误信息：\n" + additional.First() + "\n\n请根据上述信息进行对应处理，如果看不懂英文可以使用翻译软件。");
                    }
                    else
                    {
                        results.Add(@"Mod 加载器可能已经提供了错误信息，请根据错误报告中的日志信息进行对应处理，如果看不懂英文可以使用翻译软件。\h");
                    }
                    break;
                case CrashReason.Fabric报错并给出解决方案:
                    if (additional.IsSingle())
                    {
                        results.Add("Fabric 提供了以下解决方案：\n" + additional.First() + "\n\n请根据上述信息进行对应处理，如果看不懂英文可以使用翻译软件。");
                    }
                    else
                    {
                        results.Add(@"Fabric 可能已经提供了解决方案，请根据错误报告中的日志信息进行对应处理，如果看不懂英文可以使用翻译软件。\h");
                    }
                    break;
                case CrashReason.Forge报错:
                    if (additional.IsSingle())
                    {
                        results.Add("Forge 提供了以下错误信息：\n" + additional.First() + "\n\n请根据上述信息进行对应处理，如果看不懂英文可以使用翻译软件。");
                    }
                    else
                    {
                        results.Add(@"Forge 可能已经提供了错误信息，请根据错误报告中的日志信息进行对应处理，如果看不懂英文可以使用翻译软件。\h");
                    }
                    break;
                case CrashReason.没有可用的分析文件:
                    results.Add(@"你的游戏出现了一些问题，但 PCL 未能找到相关记录文件，因此无法进行分析。\h");
                    break;
                default:
                    results.Add("PCL 获取到了没有详细信息的错误原因（" + CrashReasons.First().Key + "），请向 PCL 作者提交反馈以获取详情。\\h");
                    break;
            }
        }

        return results.Join("\n\n此外，").
            Replace("\\n", "\r\n").
            Replace("\\h", "").
            Replace("\\e", isHandAnalyze ? "" : "\r\n你可以查看错误报告了解错误具体是如何发生的。").
            ReplaceLineEndings("\r\n").
            Trim('\r', '\n') +
            (!results.Any(static r => r.EndsWithF("\\h")) || isHandAnalyze ? "" : "\r\n如果要寻求帮助，请把错误报告文件发给对方，而不是发送这个窗口的照片或者截图。");
    }

}

/// <summary>
/// 导致崩溃的原因枚举（旧 :340-387 逐字；自宿主层私有嵌套枚举提升为命名空间级公共面，供导出核与测试断言）。
/// </summary>
//! 逐字移植豁免：成员名对照旧 :340-387 原样保留（含 JDK/OpenGL/JVM/ID/MC 等大小写），命名规则建议不适用
public enum CrashReason
{
    Java虚拟机参数有误,
    Mod文件被解压,
    MixinBootstrap缺失,
    内存不足,
    使用JDK,
    显卡不支持OpenGL,
    使用OpenJ9,
    Java版本过高,
    Java版本不兼容,
    Mod名称包含特殊字符,
    显卡驱动不支持导致无法设置像素格式,
    极短的程序输出,
    Intel驱动不兼容导致EXCEPTION_ACCESS_VIOLATION, //https://bugs.mojang.com/browse/MC-32606
    AMD驱动不兼容导致EXCEPTION_ACCESS_VIOLATION, //https://bugs.mojang.com/browse/MC-31618
    Nvidia驱动不兼容导致EXCEPTION_ACCESS_VIOLATION,
    玩家手动触发调试崩溃,
    光影或资源包导致OpenGL1282错误,
    文件或内容校验失败,
    确定Mod导致游戏崩溃,
    怀疑Mod导致游戏崩溃,
    Mod配置文件导致游戏崩溃,
    ModMixin失败,
    Mod加载器报错,
    Mod初始化失败,
    堆栈分析发现关键字,
    堆栈分析发现Mod名称,
    OptiFine导致无法加载世界, //https://www.minecraftforum.net/forums/support/java-edition-support/3051132-exception-ticking-world
    特定方块导致崩溃,
    特定实体导致崩溃,
    材质过大或显卡配置不足,
    没有可用的分析文件,
    使用32位Java导致JVM无法分配足够多的内存,
    Mod重复安装,
    Mod互不兼容,
    OptiFine与Forge不兼容,
    Fabric报错,
    Fabric报错并给出解决方案,
    Forge报错,
    低版本Forge与高版本Java不兼容,
    版本Json中存在多个Forge,
    Mod过多导致超出ID限制,
    NightConfig的Bug,
    ShadersMod与OptiFine同时安装,
    Forge安装不完整,
    Mod需要Java11,
    Mod缺少前置或MC版本错误
}