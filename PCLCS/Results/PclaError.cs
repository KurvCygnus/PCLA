namespace PCLCS.Results;

/// <summary>失败是否在预期之内。</summary>
public enum PclaErrorKind
{
    Expected,
    Unexpected,
}

/// <summary>所有领域错误的基类；Message 只放面向用户的短句。</summary>
public abstract class PclaError
{
    protected PclaError(string code, string message, PclaErrorKind kind, Exception? cause = null)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("错误码不能为空。", nameof(code));
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("错误消息不能为空。", nameof(message));
        Code = code;
        Message = message;
        Kind = kind;
        Cause = cause;
    }

    public string Code { get; }
    public string Message { get; }
    public PclaErrorKind Kind { get; }
    public Exception? Cause { get; }

    public static UnexpectedError Unexpected(Exception cause, string? code = null) => new(cause, code);

    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>未预期失败的默认错误。</summary>
public sealed class UnexpectedError : PclaError
{
    public UnexpectedError(Exception cause, string? code = null)
        : base(code ?? "unexpected",
            string.IsNullOrWhiteSpace(cause.Message) ? "未预期的错误" : cause.Message,
            PclaErrorKind.Unexpected, cause)
    { }
}