using PCLCS.Results;

namespace PCLCS.Tests;

public class ResultTests
{
    private sealed class TestError : PclaError
    {
        public TestError(string message = "失败", Exception? cause = null)
            : base("test.failure", message, PclaErrorKind.Expected, cause) { }
    }

    private sealed class OtherError : PclaError
    {
        public OtherError(string message) : base("test.other", message, PclaErrorKind.Unexpected) { }
    }

    [Fact]
    public void OkExposesValue()
    {
        var result = Result<int, TestError>.Ok(42);
        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ErrExposesError()
    {
        var error = new TestError();
        var result = Result<int, TestError>.Err(error);
        Assert.True(result.IsFailure);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void ValueOnFailureThrows()
    {
        var result = Result<int, TestError>.Err(new TestError());
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void ErrorOnSuccessThrows()
    {
        var result = Result<int, TestError>.Ok(1);
        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    [Fact]
    public void OkRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string, TestError>.Ok(null!));
    }

    [Fact]
    public void MapTransformsSuccessAndShortCircuitsFailure()
    {
        Assert.Equal(84, Result<int, TestError>.Ok(42).Map(value => value * 2).Value);

        var failure = Result<int, TestError>.Err(new TestError());
        var mapped = failure.Map(value => value * 2);
        Assert.True(mapped.IsFailure);
        Assert.Same(failure.Error, mapped.Error);
    }

    [Fact]
    public void BindChainsAndShortCircuits()
    {
        var success = Result<int, TestError>.Ok(2).Bind(value => Result<int, TestError>.Ok(value + 3));
        Assert.Equal(5, success.Value);

        var called = false;
        var failure = Result<int, TestError>.Err(new TestError())
            .Bind(value => { called = true; return Result<int, TestError>.Ok(value); });
        Assert.False(called);
        Assert.True(failure.IsFailure);
    }

    [Fact]
    public void BindRejectsNullResult()
    {
        var result = Result<int, TestError>.Ok(1);
        Assert.Throws<InvalidOperationException>(() => result.Bind<int>(_ => null!));
    }

    [Fact]
    public void MapErrorTransformsFailureOnly()
    {
        var mapped = Result<int, TestError>.Err(new TestError("旧错误"))
            .MapError(error => new OtherError(error.Message));
        Assert.Equal("test.other", mapped.Error.Code);
        Assert.Equal("旧错误", mapped.Error.Message);

        var successCalled = false;
        var success = Result<int, TestError>.Ok(1)
            .MapError(error => { successCalled = true; return new OtherError(error.Message); });
        Assert.False(successCalled);
        Assert.Equal(1, success.Value);
    }

    [Fact]
    public void MatchCallsExactlyOneBranch()
    {
        var successBranch = 0;
        var failureBranch = 0;

        var success = Result<int, TestError>.Ok(7).Match(
            value => { successBranch++; return value; },
            _ => { failureBranch++; return -1; });
        Assert.Equal(7, success);
        Assert.Equal(1, successBranch);
        Assert.Equal(0, failureBranch);

        var failure = Result<int, TestError>.Err(new TestError()).Match(
            _ => { successBranch++; return 0; },
            error => { failureBranch++; return error.Message.Length; });
        Assert.Equal(2, failure);
        Assert.Equal(1, successBranch);
        Assert.Equal(1, failureBranch);
    }

    [Fact]
    public void TapAndTapErrorRunOnlyOnMatchingState()
    {
        var tapped = 0;
        var tappedError = 0;

        var success = Result<int, TestError>.Ok(1)
            .Tap(_ => tapped++)
            .TapError(_ => tappedError++);
        Assert.Equal(1, success.Value);
        Assert.Equal(1, tapped);
        Assert.Equal(0, tappedError);

        var failure = Result<int, TestError>.Err(new TestError())
            .Tap(_ => tapped++)
            .TapError(_ => tappedError++);
        Assert.True(failure.IsFailure);
        Assert.Equal(1, tapped);
        Assert.Equal(1, tappedError);
    }

    [Fact]
    public void OrElseAndOrElseGetUseFallbackOnlyOnFailure()
    {
        Assert.Equal(1, Result<int, TestError>.Ok(1).OrElse(9));
        Assert.Equal(9, Result<int, TestError>.Err(new TestError()).OrElse(9));
        Assert.Equal(1, Result<int, TestError>.Ok(1).OrElseGet(_ => 9));
        Assert.Equal(9, Result<int, TestError>.Err(new TestError()).OrElseGet(_ => 9));
    }

    [Fact]
    public void OrThrowThrowsPclaException()
    {
        var error = new TestError("失败了");
        var result = Result<int, TestError>.Err(error);
        var ex = Assert.Throws<PclaException>(() => result.OrThrow());
        Assert.Same(error, ex.Error);
        Assert.Equal("失败了", ex.Message);
    }

    [Fact]
    public void UnitHasSingleValue()
    {
        Assert.Equal(Unit.Value, default);
        Assert.True(default(Unit).Equals(Unit.Value));
        Assert.Equal("()", Unit.Value.ToString());
    }

    [Fact]
    public void TryCatchesOrdinaryException()
    {
        var success = Result.Try(() => 42);
        Assert.Equal(42, success.Value);

        var failure = Result.Try<int>(() => throw new InvalidOperationException("boom"));
        Assert.True(failure.IsFailure);
        Assert.Equal(PclaErrorKind.Unexpected, failure.Error.Kind);
        Assert.Equal("boom", failure.Error.Message);
    }

    [Fact]
    public void TryWithMapperUsesMapper()
    {
        var failure = Result.Try<int, TestError>(
            () => throw new InvalidOperationException("boom"),
            ex => new TestError(ex.Message));
        Assert.Equal("test.failure", failure.Error.Code);
        Assert.Equal("boom", failure.Error.Message);
    }

    [Fact]
    public void TryMapperExceptionPropagates()
    {
        Assert.Throws<ArgumentException>(() => Result.Try<int, TestError>(
            () => throw new InvalidOperationException(),
            _ => throw new ArgumentException("mapper")));
    }

    [Fact]
    public void TryRethrowsCancellation()
    {
        Assert.Throws<OperationCanceledException>(() =>
            Result.Try<int>(() => throw new OperationCanceledException()));
    }

    [Fact]
    public void TryDoesNotCatchFatalExceptions()
    {
        Assert.Throws<OutOfMemoryException>(() =>
            Result.Try<int>(() => throw new OutOfMemoryException()));
    }

    [Fact]
    public async Task TryAsyncCatchesOrdinaryException()
    {
        var success = await Result.TryAsync(() => Task.FromResult(42));
        Assert.Equal(42, success.Value);

        var failure = await Result.TryAsync<int>(
            () => Task.FromException<int>(new InvalidOperationException("boom")));
        Assert.True(failure.IsFailure);
        Assert.Equal(PclaErrorKind.Unexpected, failure.Error.Kind);
    }

    [Fact]
    public async Task TryAsyncRethrowsCancellation()
    {
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await Result.TryAsync<int>(() => Task.FromException<int>(new OperationCanceledException())));
    }
}