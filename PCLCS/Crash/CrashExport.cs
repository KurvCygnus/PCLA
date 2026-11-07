namespace PCLCS.Crash;

/// <summary>
/// 崩溃报告导出核（自宿主层 ModCrash.vb 旧 :861-920 的文件操作面逐行移植）：
/// 删旧文件、收集 OutputFiles+ExtraFiles、重命名表（:894-903 逐字）、经静态脱敏 provider 逐行过滤（:906 组合语义）、
/// zip 打包（MeloongCore FileUtils.CreateZipFromDirectory 语义）与 Report 工作目录清理。
/// 弹窗编排（ShowWindowToTop/MyMsgBox/SaveFile/Hint/OpenExplorer）与诊断输出（FeedbackInfo/日志 Flush）留宿主层。
/// </summary>
public static class CrashExport
{

    /// <summary>
    /// 导出错误报告压缩包。成功返回压缩包路径；savePath 为空（宿主层 SaveFile 取消，生产路径已在宿主层挡下）或导出失败（已记日志）时返回 null。
    /// </summary>
    public static string? BuildExportArchive(CrashAnalyzer analyzer, IEnumerable<string>? extraFiles, string? savePath)
    {
        if (savePath is null) return null; //兜底同宿主层取消语义：静默返回，不视作失败
        try
        {
            //删除同名旧文件（旧 :885）
            FileUtils.Delete(savePath);
            //复制文件（旧 :890-909 逐字：ExtraFiles 并入共享列表 → 重命名表 → 存在性过滤 → 读取脱敏 → 写 Report）
            if (extraFiles is not null) analyzer.OutputFiles.AddRange(extraFiles);
            foreach (var outputFile in analyzer.OutputFiles)
            {
                var fileName = PathUtils.GetLastPart(outputFile);
                Encoding? fileEncoding = null;
                //! 逐字移植豁免：重命名表（旧 :894-903）逐条对照，不合并、不补默认分支
                switch (fileName)
                {
                    case "LatestLaunch.bat":
                        fileName = "启动脚本.bat";
                        break;
                    case "Log1.txt":
                        fileName = "PCL 启动器日志.txt";
                        fileEncoding = Encoding.UTF8;
                        break;
                    case "RawOutput.log":
                        fileName = "游戏崩溃前的输出.txt";
                        fileEncoding = Encoding.UTF8;
                        break;
                }
                if (!FileUtils.Exists(outputFile)) continue;
                var fileContent = FileUtils.ReadAsString(outputFile, fileEncoding);
                //! 脱敏组合（旧 :906）：FilterChar 作用于 AccessToken 过滤、用户名过滤固定 '*'；组合体留宿主层
                //! （ModMinecraft.vb:1445/:1463，宿主层 Sub New 接线至 CrashAnalyzer.DesensitizeFilters），FilterChar
                //! 按重命名后的文件名判定（启动脚本→'F'，其余→'*'）；provider 未接线（测试直用语义）时按原文写出
                fileContent = CrashAnalyzer.DesensitizeFilters?.Invoke(fileContent, fileName == "启动脚本.bat" ? 'F' : '*') ?? fileContent;
                FileUtils.Write(analyzer.TempFolder + "Report\\" + fileName, fileContent, fileEncoding);
                Logger.Info($"导出错误报告文件：{fileName}");
            }
            //导出报告（旧 :911-912）
            FileUtils.CreateZipFromDirectory(savePath, analyzer.TempFolder + "Report\\");
            DirectoryUtils.Delete(analyzer.TempFolder + "Report\\");
            return savePath;
        }
        catch (Exception ex)
        {
            //! 逐字移植豁免：吞异常仅记日志的 Catch 语义原样保留（旧 :915-917），失败经返回 null 传达给宿主层
            Logger.Error(ex, "导出错误报告失败");
            return null;
        }
    }

}