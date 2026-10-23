Imports System.Windows.Automation.Peers

''' <summary>
''' 自定义控件的 Automation Peer 基类，统一提供类名、控件类型与名称。
''' </summary>
Public Class AutomationPeerBase
    Inherits FrameworkElementAutomationPeer

    Private ReadOnly _ControlType As AutomationControlType
    Private ReadOnly _NameProvider As Func(Of String)

    Public Sub New(owner As FrameworkElement, controlType As AutomationControlType, nameProvider As Func(Of String))
        MyBase.New(owner)
        _ControlType = controlType
        _NameProvider = nameProvider
    End Sub

    Protected Overrides Function GetClassNameCore() As String
        Return Owner.GetType().Name
    End Function

    Protected Overrides Function GetAutomationControlTypeCore() As AutomationControlType
        Return _ControlType
    End Function

    Protected Overrides Function GetNameCore() As String
        Dim ProvidedName = _NameProvider?.Invoke()
        If Not String.IsNullOrWhiteSpace(ProvidedName) Then Return ProvidedName
        Return MyBase.GetNameCore()
    End Function
End Class
