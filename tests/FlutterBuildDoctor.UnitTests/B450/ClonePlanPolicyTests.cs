using FlutterBuildDoctor.Application.Repositories;

namespace FlutterBuildDoctor.UnitTests.B450;

public sealed class ClonePlanPolicyTests
{
    [Fact]
    public void Plan_NormalizesAndBuildsDeterministicShallowArguments()
    {
        var first = ClonePlanPolicy.Plan(new ClonePlanRequest(
            " https://github.com/acme/app.git/ ", " my app ", " feature/demo ", 5000));
        var second = ClonePlanPolicy.Plan(new ClonePlanRequest(
            "https://github.com/acme/app.git", "my app", "feature/demo", 5000));

        Assert.True(first.Allowed);
        Assert.False(first.ReuseExisting);
        Assert.Equal("my-app", first.DestinationName);
        Assert.Equal(ClonePlanPolicy.MaxDepth, first.Depth);
        Assert.Equal(new[] { "clone", "--depth", "1000", "--branch", "feature/demo", "--", "https://github.com/acme/app.git", "my-app" }, first.Arguments);
        Assert.Equal("fresh-clone", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Plan_FullCloneOmitsDepthAndReusesExistingRepository()
    {
        var decision = ClonePlanPolicy.Plan(new ClonePlanRequest(
            "https://github.com/acme/app", "app", Mode: CloneMode.Full,
            DestinationExists: true, DestinationIsRepository: true, DestinationIsEmpty: false));

        Assert.True(decision.Allowed);
        Assert.True(decision.ReuseExisting);
        Assert.Equal(0, decision.Depth);
        Assert.DoesNotContain("--depth", decision.Arguments);
        Assert.Equal("reuse-existing-repository", decision.ReasonCode);
    }

    [Fact]
    public void Plan_RejectsNonEmptyFreshDestination()
    {
        var decision = ClonePlanPolicy.Plan(new ClonePlanRequest(
            "https://github.com/acme/app", "app",
            DestinationExists: true, DestinationIsRepository: false, DestinationIsEmpty: false));

        Assert.False(decision.Allowed);
        Assert.Equal("destination-not-empty", decision.ReasonCode);
    }

    [Theory]
    [InlineData("http://github.com/acme/app")]
    [InlineData("file:///tmp/repo")]
    [InlineData("not-a-url")]
    public void NormalizeUrl_RejectsUnsupportedSources(string value)
        => Assert.Throws<ArgumentException>(() => ClonePlanPolicy.NormalizeUrl(value));

    [Theory]
    [InlineData("main..evil")]
    [InlineData("refs/heads/main@{1}")]
    public void NormalizeRef_RejectsUnsafeTokens(string value)
        => Assert.Throws<ArgumentException>(() => ClonePlanPolicy.NormalizeRef(value));
}
