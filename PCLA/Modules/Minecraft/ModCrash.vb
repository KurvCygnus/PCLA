'类型别名：本类名 CrashAnalyzer 会遮蔽同名 C# 类型，C# 分析核的静态访问与类型标注统一走别名（ModNet.vb 同模式）
Imports CrashAnalyzerCore = PCLCS.Crash.CrashAnalyzer

''' <summary>
''' 崩溃分析器：分析核已迁 C#（PCLCS.Crash.CrashAnalyzer，旧 :6-1113 除 Output 外逐行移植），导出核已迁 C#（PCLCS.Crash.CrashExport，旧 :861-920 文件操作面），
''' 本类保留旧公共签名做薄转发；Output（旧 :861-920）仅余弹窗编排（ShowWindowToTop/MyMsgBox/SaveFile/Hint/OpenExplorer 与诊断输出）。
''' </summary>
Public Class CrashAnalyzer


    Private ReadOnly Core As CrashAnalyzerCore

    '构造函数
    ''' <summary>
    ''' 构造函数（旧 :6-13）：暂存文件夹的申请经回调改调 C#（PCLCS.Common.TempFolders.RequestTaskTempFolder，
    ''' T5 迁核后的既定改调点；PathTemp/OsDrive 模块状态经参数注入，回调体内直读 ModBase 模块成员）。
    ''' TargetInstance 形参按旧公共签名保留（旧 :5 的同名字段在分析核内无任何读取，未随核迁移）。
    ''' </summary>
    Public Sub New(Optional TargetInstance As McInstance = Nothing)
        Core = New CrashAnalyzerCore(Function() PCLCS.Common.TempFolders.RequestTaskTempFolder(PathTemp, osDrive:=OsDrive))
    End Sub

    '暂存文件夹（旧 Private TempFolder :4）与可用于生成报告的文件（旧 Private OutputFiles :857）随导出核迁 C#，本壳不再消费
    '在弹窗中选择直接打开的文件（旧 Private DirectFile :134；C# 核 Prepare 分类时赋值，Output 弹窗「查看输出」消费）
    Private ReadOnly Property DirectFile As KeyValuePair(Of String, String())?
        Get
            Return Core.DirectFile
        End Get
    End Property

    '1：准备用于分析的 Log 文件
    ''' <summary>
    ''' 将可用于分析的日志存储到 AnalyzeRawFiles。（旧 :21-85 转发）
    ''' </summary>
    ''' <param name="LatestLog">从 PCL 捕获到的最后 200 行程序输出。</param>
    Public Sub Collect(VersionPathIndie As String, Optional LatestLog As IList(Of String) = Nothing)
        Core.Collect(VersionPathIndie, LatestLog)
    End Sub
    ''' <summary>
    ''' 从文件路径直接导入日志文件或崩溃报告压缩包。（旧 :89-124 转发）
    ''' </summary>
    Public Sub Import(FilePath As String)
        Core.Import(FilePath)
    End Sub

    '2：确认实际用于分析的 Log 文本
    ''' <summary>
    ''' 从 AnalyzeRawFiles 中提取实际有用的文本片段，并整理可用于生成报告的文件。
    ''' 返回是否有足够信息可用于分析。（旧 :139-302 转发）
    ''' </summary>
    Public Function Prepare() As Boolean
        Return Core.Prepare()
    End Function

    '3：根据文本分析崩溃原因
    ''' <summary>
    ''' 根据 AnalyzeLogs 与可能的版本信息分析崩溃原因。（旧 :391-451 转发）
    ''' </summary>
    Public Sub Analyze()
        Core.Analyze()
    End Sub

    '4：根据原因输出信息
    ''' <summary>
    ''' 弹出崩溃弹窗，并指导导出崩溃报告。（旧 :861-920：弹窗编排留宿主层，导出文件操作面已迁 C# CrashExport.BuildExportArchive）
    ''' </summary>
    Public Sub Output(IsManualAnalyze As Boolean, Optional ExtraFiles As List(Of String) = Nothing)
        '弹窗提示
        FrmMain.ShowWindowToTop()
        Dim Detail As String = GetAnalyzeResult(IsManualAnalyze)
        Select Case MyMsgBox(Detail, If(IsManualAnalyze, "错误报告分析结果", "Minecraft 出现错误"),
            "确定", If(IsManualAnalyze OrElse DirectFile Is Nothing, "", "查看输出"), If(IsManualAnalyze, "", "导出错误报告"),
            Button2Action:=If(IsManualAnalyze OrElse DirectFile Is Nothing, Nothing,
            Sub()
                '弹窗选择：查看输出
                If FileUtils.Exists(DirectFile.Value.Key) Then
                    StartProcess(DirectFile.Value.Key)
                Else
                    Dim FilePath As String = PathTemp & "Crash.txt"
                    FileUtils.Write(FilePath, DirectFile.Value.Value.Join(vbCrLf))
                    StartProcess(FilePath)
                End If
            End Sub))
            Case 3
                '弹窗选择：导出错误报告
                Dim FileAddress As String = Nothing
                Try
                    '获取文件路径
                    RunInUiWait(Sub() FileAddress = Dialogs.SaveFile("选择保存位置", "错误报告-" & Date.Now.ToString("G").Replace("/", "-").Replace(":", ".").Replace(" ", "_") & ".zip", filter:={("zip", "Minecraft 错误报告")}))
                    If FileAddress Is Nothing Then Return
                    '输出诊断信息
                    FeedbackInfo()
                    Logger.Instance.Flush()
                    '导出报告（C# 导出核：删旧文件、收集+脱敏、重命名表与 zip 打包；失败已记日志并返回 Nothing）
                    Dim Exported As String = PCLCS.Crash.CrashExport.BuildExportArchive(Core, ExtraFiles, FileAddress)
                    If Exported Is Nothing Then Return
                    Hint("错误报告已导出！", HintType.Green)
                    OpenExplorer(Exported)
                Catch ex As Exception
                    Logger.Error(ex, "导出错误报告失败")
                    Return
                End Try
        End Select
    End Sub
    ''' <summary>
    ''' 获取崩溃分析的结果描述。（旧 :924-1113 转发）
    ''' </summary>
    Friend Function GetAnalyzeResult(IsHandAnalyze As Boolean) As String
        Return Core.GetAnalyzeResult(IsHandAnalyze)
    End Function

End Class