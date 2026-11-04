Public Module ModJava

    Sub New()
        '选择编排核（JavaSelection）的宿主层接线：Mojang Java 列表的下载结果供给
        '（裁定 6：NetRequestByLoader + DlVersionListOrder 保持 1:1 下载机制；旧 SelectOrDownloadJava :212-215 的抓取点）
        PCLCS.JavaSelection.JavaListFetcher = Function() NetRequestByLoader(DlVersionListOrder(
            {"https://piston-meta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json"},
            {"https://bmclapi2.bangbang93.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json"}
        ), IsJson:=True)
    End Sub

    ''' <summary>
    ''' 初始化 Java 列表。
    ''' 若没有任何可用的 Java，则触发搜索。
    ''' </summary>
    Public Sub JavaInit()
        '初始化决策核已迁 C#（PCLCS.JavaRequirement.InitAsync，旧 :8-48 决策面逐行：
        'JavaConfigVersion=0 老版本迁移 → Configs.JavaList 读缓存 → 无 Java 判定）；
        '搜索触发留在宿主层（裁定 8）；旧 :44 Catch 的掩蔽语义（TECHDEBT §11）在核内保持：Failed 时不触发搜索
        If PCLCS.JavaRequirement.InitAsync().Run() = PCLCS.JavaInitState.NoJava Then JavaListRefreshWorker.Start()
    End Sub

    ''' <summary>
    ''' 搜索并获取所有可用的 Java，检查所有 Java 的可用性，写入设置，并在结束后更新设置页面显示。
    ''' </summary>
    Public WithEvents JavaListRefreshWorker As New RedoableWorker(Sub(c, p) JavaUtils.RefreshListAsync(c, p).Run())

#Region "UI 事件"

    ''' <summary>
    ''' 更新设置页面的 Java 列表显示。
    ''' </summary>
    Public Sub UpdateJavaLists() Handles JavaListRefreshWorker.Started, JavaListRefreshWorker.Stopped
        RunInUi(
        Sub()
            FrmSetupLaunch?.UpdateJavaList()
            FrmInstanceSetup?.UpdateJavaList()
        End Sub)
    End Sub

    ''' <summary>
    ''' 手动触发导入 Java。
    ''' </summary>
    Public Sub ManuallyImportJava()
        '选择
        Dim NewJavaPath As String = Dialogs.SelectFile("选择 java.exe（可能在 bin 文件夹中）", False, filter:="java.exe|java.exe").FirstOrDefault()
        If String.IsNullOrEmpty(NewJavaPath) Then Return
        '验证 Java 可用
        Dim NewJava As New Java(PathUtils.RemoveLastPart(NewJavaPath))
        If Not NewJava.CheckAsync().Run().IsSuccess Then
            Hint("该 Java 存在异常，无法使用", HintType.Red)
            Return
        End If
        '不再移除它
        Configs.JavaRemovedList.Edit(Sub(ByRef List) List.RemoveIf(Function(f) PathUtils.IsSame(f, NewJava.Folder)))
        '加入列表顶部
        Configs.JavaList.Edit(
        Sub(ByRef List)
            List.Remove(NewJava)
            List.Insert(0, NewJava)
        End Sub)
        Hint($"已将 Java 加入列表：{NewJavaPath}", HintType.Green)
        UpdateJavaLists()
    End Sub

    ''' <summary>
    ''' 手动从 Java 列表中移除指定的 Java，并记录到已移除列表中。
    ''' </summary>
    Public Sub ManuallyRemoveJava(JavaEntry As Java)
        Configs.JavaList.Edit(Sub(ByRef List) List.Remove(JavaEntry)) '从 Java 列表中移除
        Configs.JavaRemovedList.Edit(Function(List) List.Append(JavaEntry.Folder).DistinctBy(Function(s) s.Lower()).ToList()) '记录已移除
        Hint($"已将 Java 移除出列表：{JavaEntry}", HintType.Green)
        UpdateJavaLists()
        PageInstanceLeft.ReloadCurrentJava() '如果强制指定了该 Java，这会把它自动重置掉
    End Sub

    ''' <summary>
    ''' 手动触发搜索 Java，强制检查所有 Java。
    ''' </summary>
    Public Sub ManuallySearchJava(Optional CancellationToken As CancellationToken = Nothing)
        RunInThread(
        Sub()
            Configs.JavaList.Get().ForAll(Sub(java) java.InvalidateChecked()) '重置已检查的状态
            JavaListRefreshWorker.Start(cancellationToken:=CancellationToken)
            JavaListRefreshWorker.WaitIfRunningAsync(cancellationToken:=CancellationToken).Run() 'worker 开始/结束时会自动重载 UI
            If Configs.JavaList.Get().Any Then
                Hint("已找到 " & Configs.JavaList.Get().Count & " 个 Java！", HintType.Green)
            Else
                Hint("未找到可用的 Java！", HintType.Red)
            End If
        End Sub)
    End Sub

#End Region

#Region "选取"

    ''' <summary>
    ''' 分析指定 Minecraft 版本的基础 Java 版本要求。不受设置影响。
    ''' 如果由于 Java 版本要求冲突而导致没有可用的 Java，则返回 null。
    ''' </summary>
    Public Function GetJavaRequirement(Instance As McInstance) As (Range As ValueRange(Of Version), RecommendedComponent As String)
        '需求表核已迁 C#（PCLCS.JavaRequirement.Get，旧 :129-274 逐行：原版/OptiFine/Forge/NeoForge/Fabric/LiteLoader/统一通行证 → 区间交集；
        ':132/:134 与 :268 的设置读取经 C# SettingsService instanceKey 面）；
        'VB 侧仅提取 McInstance 独有基元后转发
        Dim RecommendedCode As Integer =
            If(Instance.JsonObject?("javaVersion")?("majorVersion")?.ToObject(Of Integer),
            If(Instance.JsonVersion?("java_version")?.ToObject(Of Integer), 0)) '旧 :176-178 逐字：两参 If 短路，JsonObject 链有值时不读 JsonVersion（jar）
        Dim Result = PCLCS.JavaRequirement.Get(New PCLCS.JavaRequirementQuery(
            Instance.Version, Instance.VersionDisplayName(), Instance.PathVersion, RecommendedCode,
            Function() If(Instance.JsonObject?("javaVersion")?("component")?.ToString,
                          Instance.JsonVersion?("java_component")?.ToString))) '旧 :182-184 逐字提取；以惰性提供者传入，与旧代码一致仅在 RecommendedCode>=22 时求值
        Return (Result.Range, Result.RecommendedComponent)
    End Function

    ''' <summary>
    ''' 根据版本或 Java 范围要求寻找适合的 Java。
    ''' 若指定了 <paramref name="TryFixOrHint"/>，则会在找不到时会尝试下载 Java 或提示用户下载，否则只会静默运行。
    ''' </summary>
    ''' <returns>最适合的 Java。若找不到或失败则返回 null，除非取消否则不会抛出异常。</returns>
    Public Function SelectOrDownloadJava(Target As OneOf(Of ValueRange(Of Version), McInstance), TryFixOrHint As Boolean, Optional c As CancellationToken = Nothing, Optional p As ProgressProvider = Nothing) As Java
        '选择编排核已迁 C#（PCLCS.JavaSelection.SelectOrDownload，旧 :113-305 决策面逐行：迁移判定 → 分支 2 版本文件夹搜索 /
        '分支 3 JavaForced（PreviewGet 自动重置 + CheckAsync 验活，裁定 7）/ 自动 = 需求区间 → 列表选择 → 搜索等待与重搜（#3797 JavaLock）→
        'Mojang 清单匹配（裁定 6 JavaListFetcher）→ 无匹配手装提示 → 下载重选）；
        'VB 侧仅提取实例基元（PathVersion/Name/Config/需求区间提供者）并供给回调面
        Dim Face As OneOf(Of ValueRange(Of Version), JavaSelectionInstance)
        If Target.Is(Of McInstance) Then
            Dim Instance As McInstance = Target.As(Of McInstance)
            Face = New JavaSelectionInstance(Instance.PathVersion, Instance.Name, Instance.Config, Function() GetJavaRequirement(Instance))
        Else
            Face = Target.As(Of ValueRange(Of Version))
        End If
        Return PCLCS.JavaSelection.SelectOrDownload(Face, TryFixOrHint, BuildJavaSelectionCallbacks(Target), c, p)
    End Function

    ''' <summary>
    ''' 构建选择编排核的回调面：以真实 McInstance 闭包承载旧代码的内联 UI 调用点（旧 :130-132/:142-153/:161-164/:306-312 等）。
    ''' </summary>
    Private Function BuildJavaSelectionCallbacks(Target As OneOf(Of ValueRange(Of Version), McInstance)) As JavaSelectionCallbacks
        Dim Instance = If(Target.Is(Of McInstance), Target.As(Of McInstance), Nothing)
        Return New JavaSelectionCallbacks With {
            .SwitchToInstanceSetup = Sub() SwitchToInstanceSetup(Instance),
            .MyMsgBox = Sub(Caption, Title, IsWarn, ForceWait) MyMsgBox(Caption, Title, IsWarn:=IsWarn, ForceWait:=ForceWait),
            .HintRed = Sub(Message) Hint(Message, HintType.Red),
            .UpdateJavaLists = AddressOf UpdateJavaLists,
            .MigrationUiDisable = Sub()
                                      If FrmInstanceSetup IsNot Nothing Then RunInUiWait(Sub() FrmInstanceSetup.ComboArgumentJava.IsEnabled = False) '旧 :320
                                  End Sub,
            .MigrationUiRestore = Sub()
                                      If FrmInstanceSetup IsNot Nothing Then RunInUiWait( '旧 :356-361
                                      Sub()
                                          SettingService.RefreshSettings(FrmInstanceSetup.ComboArgumentJava)
                                          SettingService.RefreshSettings(FrmInstanceSetup.ComboArgumentJavaSelect)
                                          FrmInstanceSetup.ComboArgumentJava.IsEnabled = True
                                      End Sub)
                                  End Sub,
            .ListRefreshWorker = JavaListRefreshWorker,
            .RunJavaDownload = AddressOf RunJavaDownloadLoader
        }
    End Function

    ''' <summary>
    ''' 执行 Java 下载加载器（旧 :289-294：CreateJavaDownloadLoader + WaitForExit(IsForceRestart:=True) + Finally Cancel）。
    ''' </summary>
    Private Sub RunJavaDownloadLoader(Target As JavaDownloadInfo, c As CancellationToken, p As ProgressProvider)
        Dim JavaLoader = CreateJavaDownloadLoader(Target)
        Try
            JavaLoader.WaitForExit(IsForceRestart:=True, c:=c, p:=p)
        Finally
            JavaLoader.Cancel() '确保取消时中止 Java 下载
        End Try
    End Sub

    Private Sub SwitchToInstanceSetup(Instance As McInstance)
        RunInUi(
        Sub()
            PageInstanceLeft.Instance = Instance
            FrmMain.PageChange(FormMain.PageType.InstanceSetup, FormMain.PageSubType.InstanceSetup)
        End Sub)
    End Sub

#End Region

#Region "下载（均为内部工具方法，无外部调用）"

    ''' <summary>
    ''' 构建下载指定 Java 的加载器。
    ''' 文件清单产出已迁 C#（PCLCS.JavaSelection.CreateJavaDownloadFiles，旧 :546-558 决策面：piston-meta manifest → NetFile 清单，
    ''' 3 哈希跳过 #3827）；此处仅保留 LoaderDownload+LoaderWorker 组装（裁定 6）。
    ''' </summary>
    Private Function CreateJavaDownloadLoader(Target As JavaDownloadInfo) As LoaderCombo(Of Integer)

        '分析需要下载的 Java 文件列表（旧 :546-558 → C# CreateJavaDownloadFiles）
        Dim Downloaded = PCLCS.JavaSelection.CreateJavaDownloadFiles(Target, DlSourcePreferMojang, AddressOf FetchJavaManifest)
        Dim FilesToDownload = Downloaded.Files
        Dim BaseDir = Downloaded.BaseDir

        '构建加载器（旧 :559-581 原样）
        Dim Loader = New LoaderCombo(Of Integer)("下载 Java", {
            New LoaderDownload("下载 Java 文件", FilesToDownload) With {.ProgressWeight = 10},
            New LoaderWorker("刷新 Java 列表", JavaListRefreshWorker)
        }) With {.OnStateChanged =
        Sub(RawLoader As LoaderCombo(Of Integer))
            If RawLoader.State = LoadState.Failed OrElse RawLoader.State = LoadState.Canceled Then
                Logger.Warn($"由于 Java 下载未完成，清理未下载完成的 Java 文件夹：{BaseDir}")
                DirectoryUtils.Delete(BaseDir)
            End If
        End Sub}
        Return Loader

    End Function

    ''' <summary>
    ''' manifest 获取供给（旧 :548 的 NetRequestByLoader 下载；裁定 6：保持 1:1 下载机制）。
    ''' </summary>
    Private Function FetchJavaManifest(Urls As IEnumerable(Of String)) As String
        Return NetRequestByLoader(Urls, IsJson:=True)
    End Function

#End Region

End Module
