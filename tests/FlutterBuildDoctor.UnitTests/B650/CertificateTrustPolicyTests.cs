using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B650;

public sealed class CertificateTrustPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluate_TrustsValidCertificateAndNormalizesEvidence()
    {
        var evidence = new CertificateTrustEvidence(
            string.Join(':', Enumerable.Repeat("AA", 20)),
            " CN=Example   TLS ",
            Now.AddDays(-1).ToOffset(TimeSpan.FromHours(3)),
            Now.AddDays(30).ToOffset(TimeSpan.FromHours(3)),
            true,
            "API.EXAMPLE.COM",
            new[] { "*.example.com", "api.example.com" });

        var first = CertificateTrustPolicy.Evaluate(evidence, Now);
        var second = CertificateTrustPolicy.Evaluate(evidence, Now.ToOffset(TimeSpan.FromHours(3)));

        Assert.True(first.Trusted);
        Assert.Equal("certificate-trusted", first.ReasonCode);
        Assert.Equal(new string('a', 40), first.Thumbprint);
        Assert.Equal("CN=Example TLS", first.Subject);
        Assert.Equal(TimeSpan.Zero, first.NotBeforeUtc.Offset);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_DetectsExpiryAndHostMismatch()
    {
        var expired = CertificateTrustPolicy.Evaluate(new CertificateTrustEvidence(
            new string('b', 40), "CN=expired", Now.AddDays(-10), Now.AddDays(-1), true,
            "api.example.com", new[] { "api.example.com" }), Now);
        var mismatch = CertificateTrustPolicy.Evaluate(new CertificateTrustEvidence(
            new string('c', 40), "CN=other", Now.AddDays(-1), Now.AddDays(10), true,
            "api.example.com", new[] { "other.example.com" }), Now);

        Assert.False(expired.Trusted);
        Assert.Equal("certificate-expired", expired.ReasonCode);
        Assert.False(mismatch.Trusted);
        Assert.Equal("certificate-host-mismatch", mismatch.ReasonCode);
    }

    [Fact]
    public void Evaluate_DetectsUntrustedChainAndFutureValidity()
    {
        var chain = CertificateTrustPolicy.Evaluate(new CertificateTrustEvidence(
            new string('d', 64), "CN=chain", Now.AddDays(-1), Now.AddDays(1), false,
            "api.example.com", new[] { "api.example.com" }), Now);
        var future = CertificateTrustPolicy.Evaluate(new CertificateTrustEvidence(
            new string('e', 64), "CN=future", Now.AddDays(1), Now.AddDays(2), true,
            "api.example.com", new[] { "api.example.com" }), Now);

        Assert.Equal("certificate-chain-untrusted", chain.ReasonCode);
        Assert.Equal("certificate-not-yet-valid", future.ReasonCode);
    }

    [Theory]
    [InlineData("xyz")]
    [InlineData("1234")]
    public void NormalizeThumbprint_RejectsMalformedValues(string value)
        => Assert.Throws<ArgumentException>(() => CertificateTrustPolicy.NormalizeThumbprint(value));
}
