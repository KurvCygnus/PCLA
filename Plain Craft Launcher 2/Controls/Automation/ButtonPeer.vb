Imports System.Windows.Automation.Peers
Imports System.Windows.Automation.Provider

''' <summary>
''' 为 MyButton 暴露 Invoke 模式。
''' </summary>
Public Class ButtonPeer
    Inherits AutomationPeerBase
    Implements IInvokeProvider

    Public Sub New(owner As MyButton)
        MyBase.New(owner, AutomationControlType.Button, Function() owner.Text)
    End Sub

    '//* UIA 只识别 GetPattern 返回的模式接口，仅 Implements 不会把模式暴露给客户端。
    Public Overrides Function GetPattern(patternInterface As PatternInterface) As Object
        If patternInterface = PatternInterface.Invoke Then Return Me
        Return MyBase.GetPattern(patternInterface)
    End Function

    Public Sub Invoke() Implements IInvokeProvider.Invoke
        Dim Button = DirectCast(Owner, MyButton)
        If Not Button.IsEnabled Then Return
        Try
            Button.Dispatcher.Invoke(Sub() Button.PerformClick())
        Catch ex As Exception
            Logger.Warn(ex, $"UIA 调用按钮失败：{Button.Text}")
        End Try
    End Sub
End Class
