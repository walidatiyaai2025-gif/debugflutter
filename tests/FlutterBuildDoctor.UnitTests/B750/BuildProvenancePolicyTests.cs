using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B750;

public sealed class BuildProvenancePolicyTests
{
    [Fact]
    public void Evaluate_NormalizesCanonicalPayloadAndPreservesDirtyEvidence()
    {
        var request = new BuildProvenanceRequest(
            new string('A', 40),
            " Agent/Feature ",
            true,
            new string('B', 64),
            " APK ",
            " Release ",
            new DateTimeOffset(2026, 8, 11, 15, 30, 0, TimeSpan.FromHours(3)));

        var first = BuildProvenancePolicy.Evaluate(request);
        var second = BuildProvenancePolicy.Evaluate(request);

        Assert.Equal(new string('a', 40), first.CommitSha);
        Assert.Equal("agent/feature", first.Branch);
        Assert.True(first.IsDirty);
        Assert.Equal(new string('b', 64), first.ToolchainFingerprint);
        Assert.Equal("apk", first.Target);
        Assert.Equal("release", first.Mode);
        Assert.Equal(TimeSpan.Zero, first.BuiltAtUtc.Offset);
        Assert.Contains("dirty", first.CanonicalPayload, StringComparison.Ordinal);
        Assert.Equal("build-provenance-valid", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void NormalizeCommitSha_RejectsInvalidValues(string value)
        => Assert.ThrowsAny<ArgumentException>(() => BuildProvenancePolicy.NormalizeCommitSha(value));

    [Theory]
    [InlineData("fast")]
    [InlineData("prod")]
    public void Evaluate_RejectsUnknownBuildModes(string mode)
        => Assert.Throws<ArgumentException>(() => BuildProvenancePolicy.Evaluate(new BuildProvenanceRequest(
            new string('a', 40), "main", false, new string('b', 64), "apk", mode, DateTimeOffset.UtcNow)));

    [Fact]
    public void Evaluate_RejectsMalformedToolchainFingerprint()
        => Assert.Throws<ArgumentException>(() => BuildProvenancePolicy.Evaluate(new BuildProvenanceRequest(
            new string('a', 40), "main", false, "bad", "apk", "release", DateTimeOffset.UtcNow)));
}
