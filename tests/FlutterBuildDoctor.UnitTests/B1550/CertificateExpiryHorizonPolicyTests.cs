using System;
using FlutterBuildDoctor.Application.Governance;
using Xunit;

namespace FlutterBuildDoctor.UnitTests.B1550;

public sealed class CertificateExpiryHorizonPolicyTests
{
    [Fact]
    public void Evaluate_ClassifiesHorizonAndNormalizesUtc()
    {
        var now = new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.FromHours(3));
        var start = now.AddDays(-10);
        var healthy = CertificateExpiryHorizonPolicy.Evaluate("cert-a", start, now.AddDays(90), now, TimeSpan.FromDays(30));
        var warning = CertificateExpiryHorizonPolicy.Evaluate("cert-a", start, now.AddDays(20), now, TimeSpan.FromDays(30));
        var critical = CertificateExpiryHorizonPolicy.Evaluate("cert-a", start, now.AddDays(5), now, TimeSpan.FromDays(30));
        var expired = CertificateExpiryHorizonPolicy.Evaluate("cert-a", start, now.AddMinutes(-1), now, TimeSpan.FromDays(30));
        var future = CertificateExpiryHorizonPolicy.Evaluate("cert-a", now.AddDays(1), now.AddDays(60), now, TimeSpan.FromDays(30));
        Assert.Equal("healthy", healthy.Classification);
        Assert.Equal("warning", warning.Classification);
        Assert.Equal("critical", critical.Classification);
        Assert.Equal("expired", expired.Classification);
        Assert.Equal("not-yet-valid", future.Classification);
        Assert.Equal(TimeSpan.Zero, healthy.NotBeforeUtc.Offset);
        Assert.Equal(TimeSpan.Zero, expired.RemainingLifetime);
        Assert.Equal(64, critical.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ClampsAndRejectsInvertedValidity()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(CertificateExpiryHorizonPolicy.MinWarning, CertificateExpiryHorizonPolicy.Evaluate("cert", now.AddDays(-1), now.AddDays(10), now, TimeSpan.Zero).WarningHorizon);
        Assert.Equal(CertificateExpiryHorizonPolicy.MaxWarning, CertificateExpiryHorizonPolicy.Evaluate("cert", now.AddDays(-1), now.AddDays(300), now, TimeSpan.FromDays(1000)).WarningHorizon);
        Assert.Throws<ArgumentException>(() => CertificateExpiryHorizonPolicy.Evaluate("cert", now, now, now, TimeSpan.FromDays(30)));
    }
}
