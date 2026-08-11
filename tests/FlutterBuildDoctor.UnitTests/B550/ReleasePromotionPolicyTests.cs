using FlutterBuildDoctor.Application.Releases;

namespace FlutterBuildDoctor.UnitTests.B550;

public sealed class ReleasePromotionPolicyTests
{
    private const string Fingerprint = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Evaluate_AllowsVerifiedForwardProductionPromotionAndNormalizesUtc()
    {
        var when = new DateTimeOffset(2026, 8, 11, 16, 0, 0, TimeSpan.FromHours(3));
        var request = new ReleasePromotionRequest(ReleaseChannel.Beta, ReleaseChannel.Production, true, true, Fingerprint.ToUpperInvariant(), Fingerprint, " Release ", when);
        var first = ReleasePromotionPolicy.Evaluate(request);
        var second = ReleasePromotionPolicy.Evaluate(request);

        Assert.True(first.Allowed);
        Assert.Equal("promotion-approved", first.ReasonCode);
        Assert.Equal("release", first.BuildMode);
        Assert.Equal(TimeSpan.Zero, first.PromotedAtUtc.Offset);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_DeniesBackwardOrSamePromotion()
    {
        var backward = ReleasePromotionPolicy.Evaluate(new(ReleaseChannel.Beta, ReleaseChannel.Internal, true, true, Fingerprint, Fingerprint, "release", DateTimeOffset.UtcNow));
        var same = ReleasePromotionPolicy.Evaluate(new(ReleaseChannel.Beta, ReleaseChannel.Beta, true, true, Fingerprint, Fingerprint, "release", DateTimeOffset.UtcNow));
        Assert.False(backward.Allowed);
        Assert.False(same.Allowed);
        Assert.Equal("backward-or-same-promotion-denied", backward.ReasonCode);
    }

    [Fact]
    public void Evaluate_DeniesUnverifiedFailedQualityAndFingerprintMismatch()
    {
        var other = new string('a', 64);
        Assert.Equal("artifact-not-verified", ReleasePromotionPolicy.Evaluate(new(ReleaseChannel.Internal, ReleaseChannel.Beta, false, true, Fingerprint, Fingerprint, "release", DateTimeOffset.UtcNow)).ReasonCode);
        Assert.Equal("quality-gates-failed", ReleasePromotionPolicy.Evaluate(new(ReleaseChannel.Internal, ReleaseChannel.Beta, true, false, Fingerprint, Fingerprint, "release", DateTimeOffset.UtcNow)).ReasonCode);
        Assert.Equal("artifact-fingerprint-mismatch", ReleasePromotionPolicy.Evaluate(new(ReleaseChannel.Internal, ReleaseChannel.Beta, true, true, Fingerprint, other, "release", DateTimeOffset.UtcNow)).ReasonCode);
    }

    [Fact]
    public void Evaluate_RequiresReleaseModeForProductionOnly()
    {
        var production = ReleasePromotionPolicy.Evaluate(new(ReleaseChannel.Beta, ReleaseChannel.Production, true, true, Fingerprint, Fingerprint, "profile", DateTimeOffset.UtcNow));
        Assert.False(production.Allowed);
        Assert.Equal("production-requires-release-mode", production.ReasonCode);

        var beta = ReleasePromotionPolicy.Evaluate(new(ReleaseChannel.Internal, ReleaseChannel.Beta, true, true, Fingerprint, Fingerprint, "profile", DateTimeOffset.UtcNow));
        Assert.True(beta.Allowed);
    }

    [Fact]
    public void ValidateChannelAndFingerprintRejectInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReleasePromotionPolicy.ValidateChannel((ReleaseChannel)99, "channel"));
        Assert.Throws<ArgumentException>(() => ReleasePromotionPolicy.NormalizeFingerprint("bad"));
        Assert.Throws<ArgumentException>(() => ReleasePromotionPolicy.NormalizeBuildMode("staging"));
    }
}
