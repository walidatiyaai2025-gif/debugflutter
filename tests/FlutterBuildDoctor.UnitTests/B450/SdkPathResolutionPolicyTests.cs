using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.UnitTests.B450;

public sealed class SdkPathResolutionPolicyTests
{
    [Fact]
    public void Resolve_PrefersExplicitExistingCandidateAndFingerprintsDeterministically()
    {
        var candidates = new[]
        {
            new SdkPathCandidate(@"C:\Sdk\flutter", SdkCandidateSource.Discovery, true),
            new SdkPathCandidate(@"C:\Sdk\android", SdkCandidateSource.Explicit, true),
            new SdkPathCandidate(@"c:\sdk\android\", SdkCandidateSource.Environment, false)
        };

        var first = SdkPathResolutionPolicy.Resolve(candidates, new[] { @"C:\Sdk" });
        var second = SdkPathResolutionPolicy.Resolve(candidates.Reverse(), new[] { @"C:\Sdk" });

        Assert.NotNull(first.Selected);
        Assert.Equal(SdkCandidateSource.Explicit, first.Selected!.Source);
        Assert.True(first.Selected.Exists);
        Assert.Equal("explicit-existing", first.ReasonCode);
        Assert.Equal(2, first.Candidates.Count);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Resolve_FallsBackToBestExistingThenMissing()
    {
        var existing = SdkPathResolutionPolicy.Resolve(new[]
        {
            new SdkPathCandidate(@"C:\Sdk\missing", SdkCandidateSource.Explicit, false),
            new SdkPathCandidate(@"C:\Sdk\found", SdkCandidateSource.Discovery, true)
        });
        Assert.Equal(@"C:\Sdk\found", existing.Selected!.Path);
        Assert.Equal("best-existing", existing.ReasonCode);

        var missing = SdkPathResolutionPolicy.Resolve(new[]
        {
            new SdkPathCandidate(@"C:\Sdk\missing", SdkCandidateSource.Explicit, false)
        });
        Assert.Equal("selected-missing", missing.ReasonCode);
    }

    [Fact]
    public void Resolve_RejectsRelativeOrOutOfRootCandidates()
    {
        Assert.Throws<ArgumentException>(() => SdkPathResolutionPolicy.Resolve(new[]
        {
            new SdkPathCandidate("relative\\sdk", SdkCandidateSource.Discovery, true)
        }));

        Assert.Throws<ArgumentException>(() => SdkPathResolutionPolicy.Resolve(new[]
        {
            new SdkPathCandidate(@"D:\Other\sdk", SdkCandidateSource.Discovery, true)
        }, new[] { @"C:\Sdk" }));
    }

    [Fact]
    public void Resolve_BoundsCandidateCount()
    {
        var candidates = Enumerable.Range(0, SdkPathResolutionPolicy.MaxCandidates + 1)
            .Select(index => new SdkPathCandidate($@"C:\Sdk\{index}", SdkCandidateSource.Discovery, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => SdkPathResolutionPolicy.Resolve(candidates));
    }
}
