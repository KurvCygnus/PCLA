Imports System.Windows.Automation
Imports System.Windows.Automation.Peers
Imports System.Windows.Automation.Provider

''' <summary>
''' 为 MyListItem 暴露 Invoke 与 Toggle 模式。
''' </summary>
Public Class ListItemPeer
    Inherits AutomationPeerBase
    Implements IInvokeProvider
    Implements IToggleProvider

    Public Sub New(owner As MyListItem)
        MyBase.New(owner, AutomationControlType.ListItem, Function() owner.Title)
    End Sub

    Public Sub Invoke() Implements IInvokeProvider.Invoke
        Dim Item = DirectCast(Owner, MyListItem)
        If Not Item.IsEnabled Then Return
        Try
            Item.Dispatcher.Invoke(Sub() Item.PerformActivate())
        Catch ex As Exception
            Logger.Warn(ex, $"UIA 调用列表项失败：{Item.Title}")
        End Try
    End Sub

    Private ReadOnly Property CurrentToggleState As ToggleState Implements IToggleProvider.ToggleState
        Get
            Return If(DirectCast(Owner, MyListItem).Checked, ToggleState.On, ToggleState.Off)
        End Get
    End Property

    Public Sub Toggle() Implements IToggleProvider.Toggle
        Invoke()
    End Sub

    '//* UIA 只识别 GetPattern 返回的模式接口，仅 Implements 不会把模式暴露给客户端。
    Public Overrides Function GetPattern(patternInterface As PatternInterface) As Object
        If patternInterface = PatternInterface.Invoke OrElse patternInterface = PatternInterface.Toggle Then Return Me
        Return MyBase.GetPattern(patternInterface)
    End Function
End Class
