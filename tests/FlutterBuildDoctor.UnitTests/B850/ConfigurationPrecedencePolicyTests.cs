using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B850;

public sealed class ConfigurationPrecedencePolicyTests
{
    [Fact]
    public void Resolve_AppliesRepositoryPrecedenceAndStableFingerprint()
    {
        var input = new[]
        {
            new ConfigurationEntry(ConfigurationSource.Default, " Build.Mode ", "debug"),
            new ConfigurationEntry(ConfigurationSource.User, "build.mode", "profile"),
            new ConfigurationEntry(ConfigurationSource.Repository, "BUILD.MODE", " release "),
            new ConfigurationEntry(ConfigurationSource.Default, "sdk.channel", "stable")
        };

        var first = ConfigurationPrecedencePolicy.Resolve(input);
        var second = ConfigurationPrecedencePolicy.Resolve(input.OrderByDescending(item => item.Source));

        Assert.Equal("release", first.Values["build.mode"]);
        Assert.Equal("stable", first.Values["sdk.channel"]);
        Assert.Equal("configuration-resolved", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Resolve_RejectsDuplicateSourceAndKey()
        => Assert.Throws<ArgumentException>(() => ConfigurationPrecedencePolicy.Resolve(new[]
        {
            new ConfigurationEntry(ConfigurationSource.User, "mode", "a"),
            new ConfigurationEntry(ConfigurationSource.User, "MODE", "b")
        }));

    [Theory]
    [InlineData("")]
    [InlineData("bad key")]
    [InlineData("../mode")]
    public void NormalizeKey_RejectsUnsafeKeys(string value)
        => Assert.Throws<ArgumentException>(() => ConfigurationPrecedencePolicy.NormalizeKey(value));

    [Fact]
    public void NormalizeValue_RejectsControlCharacters()
        => Assert.Throws<ArgumentException>(() => ConfigurationPrecedencePolicy.NormalizeValue("abc\nvalue"));
}
