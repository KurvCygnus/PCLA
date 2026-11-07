'输入验证类族已迁 C#（PCLCS\Validate\Validate.cs，旧 :8-424 逐字移植）：本文件仅余模块壳转发与 XAML 所需薄壳。
'XAML 以 local:（clr-namespace:PCL，宿主程序集）实例化 ValidateExcept/FolderPath/Http/Integer/Length/Nullable 六类，
'故这六类以继承薄壳保留在宿主层；其余类与基类 Validate 经项目级 Imports PCLCS.Validate（PCLA.vbproj）直达 C# 类型，消费点零改动。
'//! 逐字移植豁免：壳内公共签名形参名逐字（PascalCase）与根命名空间省略为工程级惯例（ModCrash.vb 同模式），非新代码问题
Imports ValidateCore = PCLCS.Validate.Validate

'提供不同的输入验证方法，名称以 Validate 开头
Public Module ModValidate

    ''' <summary>
    ''' 进行输入验证，并返回错误原因。
    ''' 如果没有错误则返回空字符串。
    ''' </summary>
    Public Function Validate(Text As String, ValidateRules As IEnumerable(Of Validate)) As String
        '旧 :8-16 循环体随族迁 C#（Validate.ValidateAll）；C# 成员名不得与封闭类型同名（CS0542），故聚合入口记作 ValidateAll
        Return ValidateCore.ValidateAll(Text, ValidateRules)
    End Function

End Module

''' <summary>
''' 若为空则直接通过检查。（旧 :33-41 已迁 C#，本壳继承转发——XAML 以 local:ValidateNullable 实例化）
''' </summary>
Public Class ValidateNullable
    Inherits PCLCS.Validate.ValidateNullable
    Public Sub New()
    End Sub
End Class

''' <summary>
''' 必须是一个完整网址。（旧 :105-114 已迁 C#，本壳继承转发——XAML 以 local:ValidateHttp 实例化）
''' </summary>
Public Class ValidateHttp
    Inherits PCLCS.Validate.ValidateHttp
    Public Sub New()
    End Sub
End Class

''' <summary>
''' 必须为整数。（旧 :119-137 已迁 C#，本壳继承转发——XAML 以 local:ValidateInteger 实例化）
''' </summary>
Public Class ValidateInteger
    Inherits PCLCS.Validate.ValidateInteger
    Public Sub New()
    End Sub
    Public Sub New(Min As Integer, Max As Integer)
        MyBase.New(Min, Max)
    End Sub
End Class

''' <summary>
''' 长度限制。（旧 :142-158 已迁 C#，本壳继承转发——XAML 以 local:ValidateLength 实例化）
''' </summary>
Public Class ValidateLength
    Inherits PCLCS.Validate.ValidateLength
    Public Sub New()
    End Sub
    Public Sub New(Min As Integer, Optional Max As Integer = Integer.MaxValue)
        MyBase.New(Min, Max)
    End Sub
End Class

''' <summary>
''' 不能包含某些特定字符串。忽略大小写。（旧 :163-193 已迁 C#，本壳继承转发——XAML 以 local:ValidateExcept 实例化并赋 Excepts）
''' </summary>
Public Class ValidateExcept
    Inherits PCLCS.Validate.ValidateExcept
    Public Sub New()
    End Sub
    Public Sub New(Excepts As ObjectModel.Collection(Of String), Optional ErrorMessage As String = "输入内容不能包含 %")
        MyBase.New(Excepts, ErrorMessage)
    End Sub
    Public Sub New(Excepts As IEnumerable(Of String), Optional ErrorMessage As String = "输入内容不能包含 %")
        MyBase.New(Excepts, ErrorMessage)
    End Sub
    Public Sub New(Excepts As IEnumerable(Of Char), Optional ErrorMessage As String = "输入内容不能包含 %")
        MyBase.New(Excepts, ErrorMessage)
    End Sub
End Class

''' <summary>
''' 要求输入一个可用的文件夹路径。（旧 :374-424 已迁 C#，本壳继承转发——XAML 以 local:ValidateFolderPath 实例化）
''' </summary>
Public Class ValidateFolderPath
    Inherits PCLCS.Validate.ValidateFolderPath
    Public Sub New()
    End Sub
    Public Sub New(UseMinecraftCharCheck As Boolean)
        MyBase.New(UseMinecraftCharCheck)
    End Sub
End Class