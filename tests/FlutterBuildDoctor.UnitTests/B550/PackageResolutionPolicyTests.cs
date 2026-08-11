using FlutterBuildDoctor.Application.Packages;

namespace FlutterBuildDoctor.UnitTests.B550;

public sealed class PackageResolutionPolicyTests
{
    [Fact]
    public void Resolve_PrefersStableHighestCompatibleAndIsDeterministic()
    {
        var candidates = new[]
        {
            new PackageCandidate("http", "1.2.0-beta.1"),
            new PackageCandidate("http", "1.1.0"),
            new PackageCandidate("http", "1.3.0"),
            new PackageCandidate("http", "2.0.0", Blocked: true)
        };

        var first = PackageResolutionPolicy.Resolve(" HTTP ", ">=1.0.0", candidates);
        var second = PackageResolutionPolicy.Resolve("http", ">=1.0.0", candidates.AsEnumerable().Reverse());

        Assert.NotNull(first.Selected);
        Assert.Equal("http", first.Selected!.Name);
        Assert.Equal("1.3.0", first.Selected.Version);
        Assert.False(first.Selected.Prerelease);
        Assert.Equal("stable-compatible", first.ReasonCode);
        Assert.DoesNotContain(first.Candidates, item => item.Version == "2.0.0");
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Resolve_ExactConstraintPrefersExactAndBlockedCandidateIsRejected()
    {
        var exact = PackageResolutionPolicy.Resolve("dio", "5.0.0", new[]
        {
            new PackageCandidate("dio", "5.0.0"),
            new PackageCandidate("dio", "5.1.0")
        });
        Assert.Equal("5.0.0", exact.Selected!.Version);
        Assert.Equal("exact-compatible", exact.ReasonCode);

        var blocked = PackageResolutionPolicy.Resolve("dio", "5.0.0", new[] { new PackageCandidate("dio", "5.0.0", true) });
        Assert.Null(blocked.Selected);
        Assert.Equal("no-compatible-package", blocked.ReasonCode);
    }

    [Fact]
    public void Resolve_UsesPrereleaseOnlyAsFallback()
    {
        var result = PackageResolutionPolicy.Resolve("pkg", "*", new[] { new PackageCandidate("pkg", "1.0.0-beta") });
        Assert.True(result.Selected!.Prerelease);
        Assert.Equal("prerelease-fallback", result.ReasonCode);
    }

    [Theory]
    [InlineData("Bad-Package")]
    [InlineData("1bad")]
    [InlineData("bad package")]
    public void NormalizePackageName_RejectsInvalidNames(string value)
        => Assert.Throws<ArgumentException>(() => PackageResolutionPolicy.NormalizePackageName(value));

    [Fact]
    public void Resolve_BoundsCandidateCountAndNormalizesConstraint()
    {
        Assert.Equal(">=1.2.3", PackageResolutionPolicy.NormalizeVersionConstraint(" >=1.2.3 "));
        var many = Enumerable.Range(0, PackageResolutionPolicy.MaxCandidates + 1)
            .Select(index => new PackageCandidate("pkg", $"1.0.{index}"));
        Assert.Throws<ArgumentOutOfRangeException>(() => PackageResolutionPolicy.Resolve("pkg", "*", many));
    }
}
