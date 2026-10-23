Public Class PageOtherAbout

    Private Shadows IsLoaded As Boolean = False
    Private Sub PageOtherAbout_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '重复加载部分
        PanBack.ScrollToHome()

        '非重复加载部分
        If IsLoaded Then Return
        IsLoaded = True

        ItemAboutPcl.Info = ItemAboutPcl.Info.Replace("%VERSION%", VersionDisplay).Replace("%BRANCH%", BuildChannelDisplay)

    End Sub

    '? 设置页的“复制识别码”入口仍调用此方法，待该入口移除后一并删除。
    Public Shared Sub CopyIdentify()
        ClipboardSet(Identify)
    End Sub
    Private Sub BtnAboutUpdate_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnAboutUpdate.Click
        UpdateCheckByButton()
    End Sub
    Private Sub BtnAboutLicence_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnAboutLicence.Click
        Dim licencePath As String = Paths.Base & "LICENCE"
        If FileUtils.Exists(licencePath) Then
            OpenExplorer(licencePath)
        Else
            OpenWebsite("https://github.com/KurvCygnus/PCLA/blob/main/LICENCE")
        End If
    End Sub

End Class
