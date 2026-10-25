using System.Threading;

namespace PCLCS.Results;

/// <summary>非泛型工厂，避免调用方重复写泛型参数。</summary>
public static class Result {
    public static Result<T, TError> Ok<T, TError>(T value) where TError : PclaError =>
        Result<T, TError>.Ok(value);

    public static Result<T, TError> Err<T, TError>(TError error) where TError : PclaError =>
        Result<T, TError>.Err(error);

    public static Result<T, PclaError> Try<T>(Func<T> action) =>
        Try<T, PclaError>(action, ex => PclaError.Unexpected(ex));

    public static Result<T, TError> Try<T, TError>(Func<T> action, Func<Exception, TError> map)
        where TError : PclaError {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (map is null) throw new ArgumentNullException(nameof(map));
        try {
            return Ok<T, TError>(action());
        } catch (Exception ex) when (IsCatchable(ex)) {
            return Err<T, TError>(map(ex));
        }
    }

    public static async Task<Result<T, PclaError>> TryAsync<T>(Func<Task<T>> action) {
        if (action is null) throw new ArgumentNullException(nameof(action));
        try {
            return Ok<T, PclaError>(await action().ConfigureAwait(false));
        } catch (Exception ex) when (IsCatchable(ex)) {
            return Err<T, PclaError>(PclaError.Unexpected(ex));
        }
    }

    private static bool IsCatchable(Exception ex) =>
        ex is not OperationCanceledException
        && ex is not OutOfMemoryException
        && ex is not StackOverflowException
        && ex is not AccessViolationException
        && ex is not ThreadAbortException;
}

/// <summary>成功值或领域错误；不提供 default 可用状态。</summary>
public sealed class Result<T, TError> where TError : PclaError {
    private readonly bool _isSuccess;
    private readonly T _value;
    private readonly TError? _error;

    private Result(bool isSuccess, T value, TError? error) {
        _isSuccess = isSuccess;
        _value = value;
        _error = error;
    }

    public bool IsSuccess => _isSuccess;
    public bool IsFailure => !_isSuccess;

    public T Value => _isSuccess
        ? _value
        : throw new InvalidOperationException("失败的 Result 不能访问 Value。");

    public TError Error => !_isSuccess
        ? _error!
        : throw new InvalidOperationException("成功的 Result 不能访问 Error。");

    public static Result<T, TError> Ok(T value) {
        if (value is null) throw new ArgumentNullException(nameof(value), "成功值不能为 null；无返回值请使用 Unit。");
        return new Result<T, TError>(true, value, null);
    }

    public static Result<T, TError> Err(TError error) {
        if (error is null) throw new ArgumentNullException(nameof(error));
        return new Result<T, TError>(false, default!, error);
    }

    public Result<U, TError> Map<U>(Func<T, U> map) {
        if (map is null) throw new ArgumentNullException(nameof(map));
        return _isSuccess
            ? Result<U, TError>.Ok(map(_value))
            : Result<U, TError>.Err(_error!);
    }

    public Result<U, TError> Bind<U>(Func<T, Result<U, TError>> bind) {
        if (bind is null) throw new ArgumentNullException(nameof(bind));
        if (!_isSuccess) return Result<U, TError>.Err(_error!);
        var next = bind(_value);
        if (next is null) throw new InvalidOperationException("Bind 返回了 null。");
        return next;
    }

    public Result<T, UError> MapError<UError>(Func<TError, UError> map) where UError : PclaError {
        if (map is null) throw new ArgumentNullException(nameof(map));
        return _isSuccess
            ? Result<T, UError>.Ok(_value)
            : Result<T, UError>.Err(map(_error!));
    }

    public TResult Match<TResult>(Func<T, TResult> ok, Func<TError, TResult> err) {
        if (ok is null) throw new ArgumentNullException(nameof(ok));
        if (err is null) throw new ArgumentNullException(nameof(err));
        return _isSuccess ? ok(_value) : err(_error!);
    }

    public Result<T, TError> Tap(Action<T> action) {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (_isSuccess) action(_value);
        return this;
    }

    public Result<T, TError> TapError(Action<TError> action) {
        if (action is null) throw new ArgumentNullException(nameof(action));
        if (!_isSuccess) action(_error!);
        return this;
    }

    public T OrElse(T fallback) => _isSuccess ? _value : fallback;

    public T OrElseGet(Func<TError, T> fallback) {
        if (fallback is null) throw new ArgumentNullException(nameof(fallback));
        return _isSuccess ? _value : fallback(_error!);
    }

    public T OrThrow() => _isSuccess ? _value : throw new PclaException(_error!);
}
