Imports System.Windows.Automation
Imports System.Windows.Automation.Peers
Imports System.Windows.Automation.Provider

''' <summary>
''' 为 MyRadioButton 暴露 Invoke 与 Toggle 模式。
''' </summary>
Public Class RadioButtonPeer
    Inherits AutomationPeerBase
    Implements IInvokeProvider
    Implements IToggleProvider

    Public Sub New(owner As MyRadioButton)
        MyBase.New(owner, AutomationControlType.RadioButton, Function() owner.Text)
    End Sub

    Public Sub Invoke() Implements IInvokeProvider.Invoke
        Dim Button = DirectCast(Owner, MyRadioButton)
        If Not Button.IsEnabled Then Return
        Try
            Button.Dispatcher.Invoke(Sub() Button.PerformCheck(True))
        Catch ex As Exception
            Logger.Warn(ex, $"UIA 调用单选框失败：{Button.Text}")
        End Try
    End Sub

    Private ReadOnly Property CurrentToggleState As ToggleState Implements IToggleProvider.ToggleState
        Get
            Return If(DirectCast(Owner, MyRadioButton).Checked, ToggleState.On, ToggleState.Off)
        End Get
    End Property

    Public Sub Toggle() Implements IToggleProvider.Toggle
        Invoke()
    End Sub
End Class
