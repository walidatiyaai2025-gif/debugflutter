using FlutterBuildDoctor.Application.Artifacts;

namespace FlutterBuildDoctor.UnitTests.B250;

public sealed class ArtifactTrustPolicyTests
{
    [Fact]
    public void Evaluate_VerifiesCompleteApkEvidenceAndProducesStableReceipt()
    {
        var evidence = new ArtifactTrustEvidence(
            @"C:\work\app\build\app-release.apk",
            Exists: true,
            SizeBytes: 123456,
            Sha256: new string('a', 64),
            Kind: TrustedArtifactKind.Apk,
            Mode: TrustedBuildMode.Release,
            BuildId: "build-42",
            CreatedAt: new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.FromHours(3)));

        var first = ArtifactTrustPolicy.Evaluate(evidence);
        var second = ArtifactTrustPolicy.Evaluate(evidence);

        Assert.True(first.Exists);
        Assert.True(first.SizeValid);
        Assert.True(first.Sha256Valid);
        Assert.True(first.ModeValid);
        Assert.Equal(100, first.TrustScore);
        Assert.True(first.Verified);
        Assert.Equal(64, first.ProvenanceFingerprint.Length);
        Assert.Equal(first.ProvenanceFingerprint, second.ProvenanceFingerprint);
        Assert.Equal(10, first.EvidenceLines.Count);
        Assert.Equal("kind=apk", first.EvidenceLines[1]);
    }

    [Theory]
    [InlineData(TrustedBuildMode.Debug)]
    [InlineData(TrustedBuildMode.Profile)]
    [InlineData(TrustedBuildMode.Release)]
    public void Evaluate_ApkSupportsAllBuildModes(TrustedBuildMode mode)
    {
        var result = ArtifactTrustPolicy.Evaluate(new ArtifactTrustEvidence(
            "app.apk",
            true,
            10,
            new string('f', 64),
            TrustedArtifactKind.Apk,
            mode));

        Assert.True(result.ModeValid);
        Assert.True(result.Verified);
    }

    [Fact]
    public void Evaluate_AabRequiresReleaseMode()
    {
        var debug = ArtifactTrustPolicy.Evaluate(new ArtifactTrustEvidence(
            "app.aab",
            true,
            10,
            new string('1', 64),
            TrustedArtifactKind.Aab,
            TrustedBuildMode.Debug));
        var release = ArtifactTrustPolicy.Evaluate(new ArtifactTrustEvidence(
            "app.aab",
            true,
            10,
            new string('1', 64),
            TrustedArtifactKind.Aab,
            TrustedBuildMode.Release));

        Assert.False(debug.ModeValid);
        Assert.False(debug.Verified);
        Assert.True(release.ModeValid);
        Assert.True(release.Verified);
    }

    [Fact]
    public void Evaluate_ModelsMissingInvalidSizeAndInvalidHashEvidence()
    {
        var result = ArtifactTrustPolicy.Evaluate(new ArtifactTrustEvidence(
            "missing.apk",
            Exists: false,
            SizeBytes: 0,
            Sha256: "bad",
            Kind: TrustedArtifactKind.Apk,
            Mode: TrustedBuildMode.Debug));

        Assert.False(result.Exists);
        Assert.False(result.SizeValid);
        Assert.False(result.Sha256Valid);
        Assert.False(result.Verified);
        Assert.Equal(0, result.TrustScore);
        Assert.Equal("sha256=invalid", result.EvidenceLines[5]);
    }

    [Fact]
    public void IsValidSha256_RequiresExactly64HexCharacters()
    {
        Assert.True(ArtifactTrustPolicy.IsValidSha256(new string('A', 64)));
        Assert.False(ArtifactTrustPolicy.IsValidSha256(new string('g', 64)));
        Assert.False(ArtifactTrustPolicy.IsValidSha256("abc"));
    }
}
