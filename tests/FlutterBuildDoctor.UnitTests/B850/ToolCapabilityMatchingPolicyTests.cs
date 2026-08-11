using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B850;

public sealed class ToolCapabilityMatchingPolicyTests
{
    [Fact]
    public void Match_RanksFullyCapableToolFirstAndIsDeterministic()
    {
        var candidates = new[]
        {
            new ToolCapabilityCandidate("flutter-beta", "4.0", new[] { "build" }),
            new ToolCapabilityCandidate("flutter-stable", "3.35", new[] { " doctor ", "build", "BUILD" })
        };

        var first = ToolCapabilityMatchingPolicy.Match(candidates, new[] { "build", "doctor" });
        var second = ToolCapabilityMatchingPolicy.Match(candidates.OrderByDescending(item => item.Identity), new[] { "doctor", "build" });

        Assert.True(first.Matches[0].FullyCapable);
        Assert.Equal("flutter-stable", first.Matches[0].Identity);
        Assert.Equal("capable-tool-found", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Match_ReportsMissingCapabilities()
    {
        var resolution = ToolCapabilityMatchingPolicy.Match(
            new[] { new ToolCapabilityCandidate("tool", "1", new[] { "build" }) },
            new[] { "build", "doctor" });

        Assert.False(resolution.Matches[0].FullyCapable);
        Assert.Equal(new[] { "doctor" }, resolution.Matches[0].MissingCapabilities);
        Assert.Equal("capabilities-missing", resolution.ReasonCode);
    }

    [Fact]
    public void Match_RejectsDuplicateToolIdentity()
        => Assert.Throws<ArgumentException>(() => ToolCapabilityMatchingPolicy.Match(new[]
        {
            new ToolCapabilityCandidate("flutter", "1", new[] { "build" }),
            new ToolCapabilityCandidate("FLUTTER", "2", new[] { "doctor" })
        }, Array.Empty<string>()));
}
