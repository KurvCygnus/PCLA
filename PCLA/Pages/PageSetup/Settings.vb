Imports PCLCS.Settings

''' <summary>
''' 设置系统的 VB 薄转发层。业务实现在 PCLCS.Settings（C#）；本类只做回调注册与 façade。
''' </summary>
Public Class Settings

    ''' <summary>OnChanged 回调表（原 entry 初始化器里的 UI 副作用，全部集中于此）。</summary>
    Private Shared ReadOnly ChangedHandlers As New Dictionary(Of String, Action(Of Object))

    Shared Sub New()
        '新存储落 <Base>\PCLA\Settings.json（与旧 Setup.ini 同目录）；必须先于任何读写指定
        SettingsService.Configure(Paths.BaseThenName)
        SettingsService.IdentifyProvider = AddressOf GetIdentify
        '↓ 逐条搬自原 :5-188 的 OnChanged:=... 参数（键 → 原委托，一字不改）
        ChangedHandlers("SystemDebugMode") = Sub() Logger.Instance.MinLevel = If(ModeDebug, LogLevel.Trace, LogLevel.Info)
        ChangedHandlers("LaunchSkinID") = Sub() PageLaunchLeft.SkinLegacy.Start()
        ChangedHandlers("LaunchSkinType") = AddressOf PageSetupLaunch.UpdateSkinType
        ChangedHandlers("LaunchRamType") = AddressOf PageSetupLaunch.UpdateRamType
        ChangedHandlers("ToolDownloadSpeed") = AddressOf ModNet.UpdateNetTaskSpeedLimitHigh
        ChangedHandlers("ToolDownloadCert") = AddressOf ModNet.ShouldValidateSslCertificateOnLogin
        ChangedHandlers("UiLauncherTransparent") = Sub(Value As Integer) If FrmMain IsNot Nothing Then FrmMain.Opacity = Value / 1000 + 0.4 '避免与 PCL1 设置冲突（UiLauncherOpacity）
        ChangedHandlers("UiBackgroundColorful") = Sub() ThemeRefresh() '刷新主题颜色
        ChangedHandlers("UiBackgroundOpacity") = AddressOf FormMain.UpdateBackgroundAndTitleBar
        ChangedHandlers("UiBackgroundBlur") = AddressOf FormMain.UpdateBackgroundAndTitleBar
        ChangedHandlers("UiBackgroundSuit") = AddressOf FormMain.UpdateBackgroundAndTitleBar
        ChangedHandlers("UiCustomType") = AddressOf PageSetupUI.OnMainPageTypeChanged
        ChangedHandlers("UiLogoType") = AddressOf FormMain.UpdateBackgroundAndTitleBar
        ChangedHandlers("UiLogoText") = AddressOf FormMain.UpdateBackgroundAndTitleBar
        ChangedHandlers("UiLogoLeft") = AddressOf FormMain.UpdateBackgroundAndTitleBar
        For Each HiddenKey In {"UiHiddenPageDownload", "UiHiddenPageLink", "UiHiddenPageSetup", "UiHiddenPageOther",
                               "UiHiddenFunctionSelect", "UiHiddenFunctionModUpdate", "UiHiddenFunctionInstanceModify",
                               "UiHiddenFunctionHidden", "UiHiddenSetupLaunch", "UiHiddenSetupUi", "UiHiddenSetupLink",
                               "UiHiddenSetupSystem", "UiHiddenOtherHelp", "UiHiddenOtherFeedback", "UiHiddenOtherAbout",
                               "UiHiddenOtherTest"}
            ChangedHandlers(HiddenKey) = AddressOf PageSetupUI.HiddenRefresh
        Next
        ChangedHandlers("VersionRamType") = AddressOf PageInstanceSetup.OnVersionRamTypeChanged
        ChangedHandlers("VersionServerLogin") = AddressOf PageInstanceSetup.OnVersionServerLoginChanged
        AddHandler SettingsService.SettingChanged,
            Sub(Key As String, Value As Object, InstanceKey As String)
                Dim Handler As Action(Of Object) = Nothing
                If ChangedHandlers.TryGetValue(Key, Handler) Then Handler(Value)
            End Sub
        Try
            SettingsImport.EnsureImported() '首次触碰 Settings 类必然在任何 Get 之前；失败不阻断启动，下次启动会重试
        Catch ex As Exception
            Logger.Error(ex, "导入旧版设置失败")
        End Try
    End Sub

    ''' <summary>façade：以 C# 目录为源重建 Entries（保留 Source/ValueCache 成员供 McInstance.ResetSettingsCache 使用）。</summary>
    Public Shared ReadOnly Entries As Dictionary(Of String, Setting) =
        SettingsCatalog.Entries.Values.Select(Function(e) New Setting(e)).ToDictionary(Function(e) e.Key)

    Public Enum Sources
        Normal
        Registry
        Instance
    End Enum

    Public Class Setting

        Public Key As String
        Public Encrypted As Boolean
        Public DefaultValue As Object
        Public Source As Sources
        ''' <summary>
        ''' 当设置项的值实际被改变时触发，参数为新值。
        ''' （回调现已集中注册于 Settings.ChangedHandlers；本字段保留以维持公共面）
        ''' </summary>
        Public OnChanged As Action(Of Object)
        ''' <summary>
        ''' 该设置的值的实际类型。
        ''' </summary>
        Public Type As Type
        ''' <summary>
        ''' 旧 ValueCache 字典的兼容壳：仅保留外部仍在使用的 Remove，读写一律经 C# 服务缓存。
        ''' </summary>
        Public ReadOnly ValueCache As ValueCacheProxy

        ''' <summary>以 C# 目录项构造 façade；回调经 Settings.ChangedHandlers 集中注册。</summary>
        Public Sub New(Entry As SettingsEntry)
            Key = Entry.Key
            DefaultValue = Entry.DefaultValue
            Encrypted = Entry.Encrypted
            Source = CType(Entry.Source, Sources)
            Type = Entry.ValueType
            ValueCache = New ValueCacheProxy(Me)
        End Sub

        ''' <summary>
        ''' 旧 ValueCache 字典的兼容壳：仅保留外部仍在使用的 Remove（McInstance.ResetSettingsCache），
        ''' 其余一律转发 C# 服务缓存。
        ''' </summary>
        Public Class ValueCacheProxy

            Private ReadOnly Owner As Setting

            Public Sub New(Owner As Setting)
                Me.Owner = Owner
            End Sub

            ''' <summary>移除当前设置项在指定实例（或全局 ""）缓存桶中的内存值；持久化值保留。</summary>
            Public Sub Remove(InstanceKey As String)
                SettingsService.RemoveInstanceCacheEntry(Owner.Key, InstanceKey)
            End Sub

        End Class

        ''' <summary>
        ''' 读取当前缓存值（按需从存储加载并填充缓存；与旧实现的“未读取则为 Nothing”不同，总是返回值）。
        ''' 若为版本独立设置，Instance 不能为 Nothing。
        ''' </summary>
        Public Function GetCache(Instance As McInstance)
            Return SettingsService.Get(Key, InstanceKey(Me, Instance))
        End Function

        ''' <summary>
        ''' 写入当前值并落盘（旧实现仅写内存缓存，由 Save 落盘；服务侧缓存与存储始终同步，故合并为一步）。
        ''' </summary>
        Public Sub [Set](Value As Object, Instance As McInstance)
            SettingsService.Set(Key, Value, InstanceKey(Me, Instance))
        End Sub

        ''' <summary>
        ''' 立即将当前的 Value 写入对应的存储（服务的 Set 幂等：值未变化时不会重复落盘或触发回调）。
        ''' </summary>
        Public Sub Save(Optional Instance As McInstance = Nothing)
            SettingsService.Set(Key, GetCache(Instance), InstanceKey(Me, Instance))
        End Sub

    End Class

    ''' <summary>
    ''' 把 McInstance 归一为 C# 服务的 instanceKey。
    ''' 版本独立设置会在此强制解引用 Instance（Instance 为 Nothing 时抛出 NRE，与旧实现的时序一致），
    ''' 并懒式导入该实例的旧 PCL\Setup.ini。
    ''' </summary>
    Private Shared Function InstanceKey(Entry As Setting, Instance As McInstance) As String
        If Entry.Source <> Sources.Instance Then Return Nothing
        Dim InstancePath As String = Instance.PathVersion
        Try
            SettingsImport.EnsureInstanceImported(InstancePath) '导入失败不阻断读取
        Catch ex As Exception
            Logger.Warn(ex, $"导入实例旧设置失败（{InstancePath}）")
        End Try
        Return InstancePath
    End Function

    ''' <summary>
    ''' 改变某个设置项的值。
    ''' </summary>
    Public Shared Sub [Set](Key As String, Value As Object, Optional Instance As McInstance = Nothing)
        Dim Entry As Setting = Nothing
        If Not Entries.TryGetValue(Key, Entry) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
        Try
            SettingsService.Set(Key, Value, InstanceKey(Entry, Instance))
        Catch ex As Exception
            Logger.Error(ex, $"设置设置项时出错（{Key}, {Value}）")
        End Try
    End Sub

    ''' <summary>
    ''' 写入某个未经加密的设置项。
    ''' 若该设置项经过了加密，则会抛出异常。
    ''' </summary>
    Public Shared Sub SetSafe(Key As String, Value As Object, Optional Instance As McInstance = Nothing)
        Dim Entry As Setting = Nothing
        If Not Entries.TryGetValue(Key, Entry) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
        If Entry.Encrypted Then Throw New InvalidOperationException("禁止写入加密设置项：" & Key)
        [Set](Key, Value, Instance)
    End Sub

    ''' <summary>
    ''' 获取某个设置项的值。
    ''' </summary>
    Public Shared Function [Get](Key As String, Optional Instance As McInstance = Nothing)
        Dim Entry As Setting = Nothing
        If Not Entries.TryGetValue(Key, Entry) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
        Return SettingsService.Get(Key, InstanceKey(Entry, Instance))
    End Function

    Public Shared Function [Get](Of T)(Key As String, Optional Instance As McInstance = Nothing) As T
        Dim Entry As Setting = Nothing
        If Not Entries.TryGetValue(Key, Entry) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
        Dim InstancePath As String = InstanceKey(Entry, Instance)
        'LoginType 的默认值在 C# 目录中以 Integer 储存（枚举 McLoginType 暂留 VB，W3 才迁移）；
        '调用方全部以 Get(Of McLoginType)("LoginType") 读取，这里还原枚举
        If Key = "LoginType" Then Return CType(SettingsService.Get(Key, InstancePath), T)
        Return SettingsService.Get(Of T)(Key, InstancePath)
    End Function

    ''' <summary>
    ''' 获取某个未经加密的设置项的值。
    ''' 若该设置项经过了加密，则会抛出异常。
    ''' </summary>
    Public Shared Function GetSafe(Key As String, Optional Instance As McInstance = Nothing)
        Dim Entry As Setting = Nothing
        If Not Entries.TryGetValue(Key, Entry) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
        If Entry.Encrypted Then Throw New InvalidOperationException("禁止读取加密设置项：" & Key)
        Return [Get](Key, Instance)
    End Function

    ''' <summary>
    ''' 初始化某个设置项的值。
    ''' </summary>
    Public Shared Sub Reset(Key As String, Optional Instance As McInstance = Nothing)
        Dim Entry As Setting = Nothing
        If Not Entries.TryGetValue(Key, Entry) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
        Try
            SettingsService.Reset(Key, InstanceKey(Entry, Instance))
        Catch ex As Exception
            Logger.Error(ex, $"重置设置项时出错（{Key}）")
        End Try
    End Sub

    ''' <summary>
    ''' 获取某个设置项的默认值。
    ''' </summary>
    Public Shared Function GetDefault(Key As String) As String
        Return Entries(Key).DefaultValue
    End Function

    ''' <summary>
    ''' 对应的注册表或文件是否已经储存了某个设置项。
    ''' 若改成了和默认值一样的，在 2.12.3- 会储存更改，在 2.12.4+ 不会储存更改。
    ''' </summary>
    Public Shared Function HasSaved(Key As String, Optional Instance As McInstance = Nothing) As Boolean
        Dim Entry As Setting = Nothing
        If Not Entries.TryGetValue(Key, Entry) Then Throw New KeyNotFoundException("未找到设置项：" & Key)
        If Entry.Source = Sources.Instance Then
            '保持旧实现（原 :421-423）的异常契约：精确 Exception 类型与消息
            If Instance Is Nothing Then Throw New Exception($"判断版本设置 {Key} 时未提供目标版本")
            Try
                SettingsImport.EnsureInstanceImported(Instance.PathVersion) '导入失败不阻断读取
            Catch ex As Exception
                Logger.Warn(ex, $"导入实例旧设置失败（{Instance.PathVersion}）")
            End Try
            Return SettingsService.HasSaved(Key, Instance.PathVersion)
        End If
        Return SettingsService.HasSaved(Key, Nothing)
    End Function

End Class