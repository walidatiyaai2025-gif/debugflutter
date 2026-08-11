using FlutterBuildDoctor.Application.Builds;

namespace FlutterBuildDoctor.UnitTests.B350;

public sealed class BuildExecutionPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesModeBoundsTimeoutRetriesAndProducesStableKey()
    {
        var request = new BuildExecutionRequest(
            " Build-Apk ",
            " RELEASE ",
            IsWorkingTreeClean: true,
            Timeout: TimeSpan.FromHours(3),
            RetryCount: 99);

        var first = BuildExecutionPolicy.Evaluate(request);
        var second = BuildExecutionPolicy.Evaluate(request);

        Assert.Equal("build-apk", first.CommandId);
        Assert.Equal(BuildExecutionMode.Release, first.Mode);
        Assert.Equal(BuildExecutionPolicy.MaxTimeout, first.Timeout);
        Assert.Equal(BuildExecutionPolicy.MaxRetryCount, first.RetryCount);
        Assert.True(first.Allowed);
        Assert.Equal("ready", first.ReasonCode);
        Assert.Equal(first.ExecutionKey, second.ExecutionKey);
        Assert.Equal(64, first.ExecutionKey.Length);
    }

    [Fact]
    public void Evaluate_AllowsRetryOnlyForTransientFailure()
    {
        var transient = BuildExecutionPolicy.Evaluate(new BuildExecutionRequest(
            "apk", "debug", PreviousFailure: BuildFailureKind.Transient, RetryCount: 2));
        var deterministic = BuildExecutionPolicy.Evaluate(new BuildExecutionRequest(
            "apk", "debug", PreviousFailure: BuildFailureKind.Deterministic, RetryCount: 2));

        Assert.True(transient.CanRetry);
        Assert.Equal("retry-transient", transient.ReasonCode);
        Assert.False(deterministic.CanRetry);
        Assert.Equal("deterministic-failure", deterministic.ReasonCode);
    }

    [Fact]
    public void Evaluate_DeniesRetryForCancelledBuildAndPreservesReason()
    {
        var decision = BuildExecutionPolicy.Evaluate(new BuildExecutionRequest(
            "apk",
            "debug",
            RetryCount: 3,
            PreviousFailure: BuildFailureKind.Cancelled,
            CancellationReason: " user stopped run "));

        Assert.False(decision.Allowed);
        Assert.False(decision.CanRetry);
        Assert.Equal(0, decision.RetryCount);
        Assert.Equal("user stopped run", decision.CancellationReason);
        Assert.Equal("cancelled", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_RequiresCleanWorkingTreeForRelease()
    {
        var decision = BuildExecutionPolicy.Evaluate(new BuildExecutionRequest(
            "appbundle", "release", IsWorkingTreeClean: false));

        Assert.False(decision.Allowed);
        Assert.Equal("dirty-release-tree", decision.ReasonCode);
    }

    [Theory]
    [InlineData("debug", BuildExecutionMode.Debug)]
    [InlineData("PROFILE", BuildExecutionMode.Profile)]
    [InlineData(" release ", BuildExecutionMode.Release)]
    public void NormalizeMode_RecognizesSupportedModes(string input, BuildExecutionMode expected)
    {
        Assert.Equal(expected, BuildExecutionPolicy.NormalizeMode(input));
    }

    [Fact]
    public void NormalizeModeAndCommand_RejectInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => BuildExecutionPolicy.NormalizeCommandId("bad command"));
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildExecutionPolicy.NormalizeMode("unknown"));
    }
}
