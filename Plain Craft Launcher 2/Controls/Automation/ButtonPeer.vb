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
