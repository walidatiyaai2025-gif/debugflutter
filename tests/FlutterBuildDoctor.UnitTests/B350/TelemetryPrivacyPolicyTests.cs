using FlutterBuildDoctor.Application.Telemetry;

namespace FlutterBuildDoctor.UnitTests.B350;

public sealed class TelemetryPrivacyPolicyTests
{
    [Fact]
    public void Prepare_DefaultsToDisabledWithoutOptIn()
    {
        var payload = TelemetryPrivacyPolicy.Prepare(new TelemetryRequest(
            " Build Completed ",
            "https://github.com/private/repo"));

        Assert.False(payload.Enabled);
        Assert.Equal("build-completed", payload.EventName);
        Assert.Null(payload.RepositoryHash);
        Assert.Empty(payload.Properties);
        Assert.Equal("telemetry-disabled", payload.ReasonCode);
    }

    [Fact]
    public void Prepare_RequiresOptInThenHashesRepositoryAndOrdersProperties()
    {
        var properties = new Dictionary<string, string?>
        {
            ["Z Value"] = " 2 ",
            ["a-value"] = "1",
            ["empty"] = "  "
        };
        var request = new TelemetryRequest(
            "Build Completed",
            " HTTPS://GITHUB.COM/PRIVATE/REPO ",
            OptedIn: true,
            Properties: properties);

        var first = TelemetryPrivacyPolicy.Prepare(request);
        var second = TelemetryPrivacyPolicy.Prepare(request with
        {
            Properties = new Dictionary<string, string?>
            {
                ["empty"] = null,
                ["a-value"] = "1",
                ["Z Value"] = "2"
            }
        });

        Assert.True(first.Enabled);
        Assert.Equal(64, first.RepositoryHash?.Length);
        Assert.Equal(new[] { "a_value", "z_value" }, first.Properties.Keys);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Theory]
    [InlineData("password")]
    [InlineData("api key")]
    [InlineData("AuthORIZATION Header")]
    [InlineData("refresh-token")]
    public void Prepare_RejectsSecretBearingFields(string secretKey)
    {
        Assert.Throws<InvalidOperationException>(() => TelemetryPrivacyPolicy.Prepare(new TelemetryRequest(
            "build",
            "repo",
            OptedIn: true,
            Properties: new Dictionary<string, string?> { [secretKey] = "secret-value" })));
    }

    [Fact]
    public void Prepare_BoundsPropertyCountAndValueLength()
    {
        var tooMany = Enumerable.Range(0, TelemetryPrivacyPolicy.MaxProperties + 1)
            .ToDictionary(index => $"key-{index}", _ => (string?)"value");
        Assert.Throws<ArgumentOutOfRangeException>(() => TelemetryPrivacyPolicy.Prepare(new TelemetryRequest(
            "build", "repo", true, tooMany)));

        var payload = TelemetryPrivacyPolicy.Prepare(new TelemetryRequest(
            "build",
            "repo",
            true,
            new Dictionary<string, string?> { ["message"] = new string('x', 500) }));
        Assert.Equal(TelemetryPrivacyPolicy.MaxValueLength, payload.Properties["message"].Length);
    }

    [Fact]
    public void HashRepositoryIdentity_IsStableAndDoesNotExposeRepositoryName()
    {
        var hash = TelemetryPrivacyPolicy.HashRepositoryIdentity(" MyOrg/MyPrivateRepo ");
        Assert.Equal(hash, TelemetryPrivacyPolicy.HashRepositoryIdentity("myorg/myprivaterepo"));
        Assert.DoesNotContain("myprivate", hash, StringComparison.OrdinalIgnoreCase);
    }
}
