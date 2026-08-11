using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B750;

public sealed class DownloadMirrorSelectionPolicyTests
{
    [Fact]
    public void Evaluate_PrefersTrustedThenPriorityThenLatency()
    {
        var result = DownloadMirrorSelectionPolicy.Evaluate(new[]
        {
            new DownloadMirrorCandidate("fast-untrusted", new Uri("https://mirror-a.example.com/sdk.zip"), 0, true, false, TimeSpan.FromMilliseconds(5)),
            new DownloadMirrorCandidate("trusted-slow", new Uri("https://mirror-b.example.com/sdk.zip"), 5, true, true, TimeSpan.FromMilliseconds(50)),
            new DownloadMirrorCandidate("trusted-fast", new Uri("https://mirror-c.example.com/sdk.zip"), 1, true, true, TimeSpan.FromMilliseconds(20)),
            new DownloadMirrorCandidate("offline", new Uri("https://mirror-d.example.com/sdk.zip"), 0, false, true, TimeSpan.FromMilliseconds(1))
        });

        Assert.True(result.Available);
        Assert.NotNull(result.Selected);
        Assert.Equal("trusted-fast", result.Selected!.Identity);
        Assert.DoesNotContain(result.Candidates, item => item.Identity == "offline");
        Assert.Equal("mirror-selected", result.ReasonCode);
    }

    [Fact]
    public void Evaluate_IsDeterministicAcrossInputOrder()
    {
        var input = new[]
        {
            new DownloadMirrorCandidate("b", new Uri("https://b.example.com/sdk.zip"), 1, true, true, TimeSpan.FromMilliseconds(10)),
            new DownloadMirrorCandidate("a", new Uri("https://a.example.com/sdk.zip"), 1, true, true, TimeSpan.FromMilliseconds(10))
        };

        var first = DownloadMirrorSelectionPolicy.Evaluate(input);
        var second = DownloadMirrorSelectionPolicy.Evaluate(input.AsEnumerable().Reverse());

        Assert.Equal("a", first.Selected!.Identity);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Theory]
    [InlineData("http://mirror.example.com/sdk.zip")]
    [InlineData("https://user:secret@mirror.example.com/sdk.zip")]
    public void Evaluate_RejectsUnsafeEndpoints(string uri)
        => Assert.Throws<ArgumentException>(() => DownloadMirrorSelectionPolicy.Evaluate(new[]
        {
            new DownloadMirrorCandidate("mirror", new Uri(uri), 1, true, true, TimeSpan.Zero)
        }));

    [Fact]
    public void Evaluate_ReturnsNoHealthyMirrorWhenAllCandidatesAreOffline()
    {
        var result = DownloadMirrorSelectionPolicy.Evaluate(new[]
        {
            new DownloadMirrorCandidate("mirror", new Uri("https://mirror.example.com/sdk.zip"), 1, false, true, TimeSpan.Zero)
        });

        Assert.False(result.Available);
        Assert.Null(result.Selected);
        Assert.Equal("no-healthy-mirror", result.ReasonCode);
    }
}
