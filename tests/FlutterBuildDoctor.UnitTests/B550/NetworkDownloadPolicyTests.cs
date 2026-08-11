using FlutterBuildDoctor.Application.Network;

namespace FlutterBuildDoctor.UnitTests.B550;

public sealed class NetworkDownloadPolicyTests
{
    [Fact]
    public void Evaluate_AllowsApprovedHttpsAndNormalizesBoundsAndFingerprint()
    {
        var request = new NetworkDownloadRequest(
            new Uri("https://storage.example.com/flutter.zip"),
            " Flutter SDK.ZIP ",
            TimeSpan.FromSeconds(1),
            long.MaxValue,
            ApprovedHosts: new[] { "STORAGE.EXAMPLE.COM" });

        var first = NetworkDownloadPolicy.Evaluate(request);
        var second = NetworkDownloadPolicy.Evaluate(request);

        Assert.True(first.Allowed);
        Assert.Equal("download-approved", first.ReasonCode);
        Assert.Equal("flutter-sdk.zip", first.DestinationFileName);
        Assert.Equal(NetworkDownloadPolicy.MinTimeout, first.Timeout);
        Assert.Equal(NetworkDownloadPolicy.MaxMaxBytes, first.MaxBytes);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsUnapprovedRedirectHost()
    {
        var decision = NetworkDownloadPolicy.Evaluate(new NetworkDownloadRequest(
            new Uri("https://storage.example.com/flutter.zip"),
            "flutter.zip",
            TimeSpan.FromMinutes(1),
            10_000,
            new Uri("https://other.example.com/flutter.zip"),
            new[] { "storage.example.com" }));

        Assert.False(decision.Allowed);
        Assert.Equal("host-not-approved", decision.ReasonCode);
    }

    [Theory]
    [InlineData("http://example.com/file.zip")]
    [InlineData("ftp://example.com/file.zip")]
    [InlineData("https://user:pass@example.com/file.zip")]
    public void Evaluate_RejectsUnsafeUrls(string value)
        => Assert.Throws<ArgumentException>(() => NetworkDownloadPolicy.Evaluate(new NetworkDownloadRequest(
            new Uri(value), "file.zip", TimeSpan.FromMinutes(1), 10_000)));

    [Fact]
    public void Evaluate_RejectsRedirectDowngradeToHttp()
        => Assert.Throws<ArgumentException>(() => NetworkDownloadPolicy.Evaluate(new NetworkDownloadRequest(
            new Uri("https://example.com/file.zip"), "file.zip", TimeSpan.FromMinutes(1), 10_000,
            new Uri("http://example.com/file.zip"))));

    [Theory]
    [InlineData("../file.zip")]
    [InlineData("folder/file.zip")]
    [InlineData("payload.exe")]
    public void NormalizeDestinationFileName_RejectsUnsafeNames(string value)
        => Assert.Throws<ArgumentException>(() => NetworkDownloadPolicy.NormalizeDestinationFileName(value));
}
