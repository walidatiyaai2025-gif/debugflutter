using FlutterBuildDoctor.Application.Integrity;

namespace FlutterBuildDoctor.UnitTests.B550;

public sealed class ChecksumVerificationPolicyTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Evaluate_VerifiesMatchingEvidenceNormalizesUtcAndFingerprint()
    {
        var when = new DateTimeOffset(2026, 8, 11, 16, 0, 0, TimeSpan.FromHours(3));
        var request = new ChecksumVerificationRequest(true, Sha.ToUpperInvariant(), Sha, 1024, 1024, when);

        var first = ChecksumVerificationPolicy.Evaluate(request);
        var second = ChecksumVerificationPolicy.Evaluate(request);

        Assert.True(first.Verified);
        Assert.Equal("verified", first.ReasonCode);
        Assert.Equal(Sha, first.ExpectedSha256);
        Assert.Equal(TimeSpan.Zero, first.VerifiedAtUtc.Offset);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ReportsMissingHashAndSizeFailures()
    {
        var other = new string('a', 64);
        Assert.Equal("artifact-missing", ChecksumVerificationPolicy.Evaluate(new(false, Sha, Sha, 10, 10, DateTimeOffset.UtcNow)).ReasonCode);
        Assert.Equal("checksum-mismatch", ChecksumVerificationPolicy.Evaluate(new(true, Sha, other, 10, 10, DateTimeOffset.UtcNow)).ReasonCode);
        Assert.Equal("size-mismatch", ChecksumVerificationPolicy.Evaluate(new(true, Sha, Sha, 10, 11, DateTimeOffset.UtcNow)).ReasonCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void NormalizeSha256_RejectsMalformedValues(string value)
        => Assert.Throws<ArgumentException>(() => ChecksumVerificationPolicy.NormalizeSha256(value));

    [Fact]
    public void Evaluate_RejectsNonPositiveSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChecksumVerificationPolicy.Evaluate(new(true, Sha, Sha, 0, null, DateTimeOffset.UtcNow)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChecksumVerificationPolicy.Evaluate(new(true, Sha, Sha, 10, 0, DateTimeOffset.UtcNow)));
    }
}
