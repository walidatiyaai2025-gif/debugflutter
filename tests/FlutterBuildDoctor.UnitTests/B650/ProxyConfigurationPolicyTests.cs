using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B650;

public sealed class ProxyConfigurationPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesProxyBypassAndFingerprint()
    {
        var first = ProxyConfigurationPolicy.Evaluate(new ProxyConfigurationRequest(
            new Uri("HTTPS://Proxy.Example.COM:8443"),
            new[] { " LOCALHOST ", "api.example.com", "LOCALHOST" }));
        var second = ProxyConfigurationPolicy.Evaluate(new ProxyConfigurationRequest(
            new Uri("https://proxy.example.com:8443"),
            new[] { "api.example.com", "localhost" }));

        Assert.Equal("https://proxy.example.com:8443", first.SafeDisplayText);
        Assert.Equal(new[] { "api.example.com", "localhost" }, first.BypassHosts);
        Assert.Equal("proxy-valid", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Theory]
    [InlineData("ftp://proxy.example.com:21")]
    [InlineData("https://user:password@proxy.example.com:443")]
    public void Evaluate_RejectsUnsafeProxyUris(string value)
        => Assert.Throws<ArgumentException>(() => ProxyConfigurationPolicy.Evaluate(
            new ProxyConfigurationRequest(new Uri(value))));

    [Fact]
    public void NormalizeBypassHosts_DeduplicatesCaseInsensitively()
    {
        var hosts = ProxyConfigurationPolicy.NormalizeBypassHosts(new[] { "API.EXAMPLE.COM", "api.example.com", "*.Example.com" });
        Assert.Equal(new[] { "*.example.com", "api.example.com" }, hosts);
    }

    [Theory]
    [InlineData("bad host")]
    [InlineData("host\nname")]
    public void NormalizeHost_RejectsInvalidHosts(string value)
        => Assert.Throws<ArgumentException>(() => ProxyConfigurationPolicy.NormalizeHost(value));
}
