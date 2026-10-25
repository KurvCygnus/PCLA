namespace PCLCS.Results;

/// <summary>只在 VB/UI/线程边界包装 Result 失败，不替代领域错误。</summary>
public sealed class PclaException : Exception {
    public PclaException(PclaError error) : base(error.Message, error.Cause) {
        Error = error;
    }

    public PclaError Error { get; }
}
