using FlutterBuildDoctor.Application.Builds;

namespace FlutterBuildDoctor.UnitTests.B450;

public sealed class BuildCachePolicyTests
{
    private static readonly string Toolchain = new('a', 64);
    private static readonly string Dependencies = new('b', 64);

    [Fact]
    public void Evaluate_NormalizesBuildScopeKeyAndReusesMatchingCache()
    {
        var created = new DateTimeOffset(2026, 8, 11, 15, 0, 0, TimeSpan.FromHours(3));
        var existing = new BuildCacheSnapshot(Toolchain, Dependencies, created.AddHours(-1));

        var first = BuildCachePolicy.Evaluate(" Flutter.Build ", BuildCacheScope.Release,
            new[] { " App ", "Arm64" }, Toolchain, Dependencies, created, existing);
        var second = BuildCachePolicy.Evaluate("flutter.build", BuildCacheScope.Release,
            new[] { "App", "ARM64" }, Toolchain, Dependencies, created, existing);

        Assert.True(first.ReuseExisting);
        Assert.Equal("cache-valid", first.ReasonCode);
        Assert.Equal("flutter.build", first.Namespace);
        Assert.Contains(":release:", first.CacheKey, StringComparison.Ordinal);
        Assert.True(first.CacheKey.Length <= BuildCachePolicy.MaxKeyLength);
        Assert.Equal(TimeSpan.Zero, first.CreatedAtUtc.Offset);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_InvalidatesOnToolchainOrDependencyChange()
    {
        var created = DateTimeOffset.UtcNow;
        var toolChanged = BuildCachePolicy.Evaluate("build", BuildCacheScope.Debug, new[] { "app" }, Toolchain, Dependencies, created,
            new BuildCacheSnapshot(new string('c', 64), Dependencies, created));
        Assert.False(toolChanged.ReuseExisting);
        Assert.Equal("toolchain-changed", toolChanged.ReasonCode);

        var dependencyChanged = BuildCachePolicy.Evaluate("build", BuildCacheScope.Profile, new[] { "app" }, Toolchain, Dependencies, created,
            new BuildCacheSnapshot(Toolchain, new string('c', 64), created));
        Assert.False(dependencyChanged.ReuseExisting);
        Assert.Equal("dependency-changed", dependencyChanged.ReasonCode);
    }

    [Fact]
    public void NormalizeSegments_RejectsSecretsAndBoundsCount()
    {
        Assert.Throws<ArgumentException>(() => BuildCachePolicy.NormalizeSegments(new[] { "token=secret" }));
        var tooMany = Enumerable.Range(0, BuildCachePolicy.MaxSegments + 1).Select(index => $"segment-{index}");
        Assert.Throws<ArgumentOutOfRangeException>(() => BuildCachePolicy.NormalizeSegments(tooMany));
    }

    [Fact]
    public void Evaluate_DistinguishesBuildScopes()
    {
        var now = DateTimeOffset.UtcNow;
        var debug = BuildCachePolicy.Evaluate("build", BuildCacheScope.Debug, new[] { "app" }, Toolchain, Dependencies, now);
        var release = BuildCachePolicy.Evaluate("build", BuildCacheScope.Release, new[] { "app" }, Toolchain, Dependencies, now);
        Assert.NotEqual(debug.CacheKey, release.CacheKey);
    }
}
