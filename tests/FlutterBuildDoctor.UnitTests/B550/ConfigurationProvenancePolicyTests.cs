using FlutterBuildDoctor.Application.Configuration;

namespace FlutterBuildDoctor.UnitTests.B550;

public sealed class ConfigurationProvenancePolicyTests
{
    [Fact]
    public void Resolve_PrefersExplicitUserPreservesEvidenceAndFingerprintsDeterministically()
    {
        var t = new DateTimeOffset(2026, 8, 11, 16, 0, 0, TimeSpan.FromHours(3));
        var evidence = new[]
        {
            new ConfigurationEvidence("flutter.sdk", @"C:\auto", ConfigurationSource.Discovery, t, "scan"),
            new ConfigurationEvidence("FLUTTER.SDK", @"C:\user", ConfigurationSource.ExplicitUser, t.AddMinutes(1), "settings"),
            new ConfigurationEvidence("java.home", @"C:\java", ConfigurationSource.Environment, t, "env")
        };

        var first = ConfigurationProvenancePolicy.Resolve(evidence);
        var second = ConfigurationProvenancePolicy.Resolve(evidence.AsEnumerable().Reverse());
        var flutter = first.Resolutions.Single(item => item.Key == "flutter.sdk");

        Assert.Equal(@"C:\user", flutter.Value);
        Assert.Equal(ConfigurationSource.ExplicitUser, flutter.Source);
        Assert.False(flutter.Conflict);
        Assert.Equal("explicit-user-selected", flutter.ReasonCode);
        Assert.Equal(2, flutter.Evidence.Count);
        Assert.All(flutter.Evidence, item => Assert.Equal(TimeSpan.Zero, item.ObservedAt.Offset));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Resolve_DetectsConflictingHighestPriorityValues()
    {
        var result = ConfigurationProvenancePolicy.Resolve(new[]
        {
            new ConfigurationEvidence("sdk.path", @"C:\one", ConfigurationSource.ExplicitUser, DateTimeOffset.UtcNow, "one"),
            new ConfigurationEvidence("sdk.path", @"C:\two", ConfigurationSource.ExplicitUser, DateTimeOffset.UtcNow.AddMinutes(1), "two"),
            new ConfigurationEvidence("sdk.path", @"C:\lower", ConfigurationSource.Project, DateTimeOffset.UtcNow, "project")
        });
        var resolution = result.Resolutions.Single();
        Assert.True(resolution.Conflict);
        Assert.Null(resolution.Value);
        Assert.Equal("high-priority-conflict", resolution.ReasonCode);
    }

    [Theory]
    [InlineData("bad key")]
    [InlineData("1bad")]
    public void NormalizeKey_RejectsInvalidKeys(string value)
        => Assert.Throws<ArgumentException>(() => ConfigurationProvenancePolicy.NormalizeKey(value));

    [Fact]
    public void NormalizeValue_RejectsControlCharactersAndOversize()
    {
        Assert.Throws<ArgumentException>(() => ConfigurationProvenancePolicy.NormalizeValue("bad\nvalue"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationProvenancePolicy.NormalizeValue(new string('x', ConfigurationProvenancePolicy.MaxValueLength + 1)));
    }

    [Fact]
    public void Resolve_BoundsRecordsPerKey()
    {
        var records = Enumerable.Range(0, ConfigurationProvenancePolicy.MaxRecordsPerKey + 1)
            .Select(index => new ConfigurationEvidence("sdk.path", $"value-{index}", ConfigurationSource.Discovery, DateTimeOffset.UtcNow, "scan"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfigurationProvenancePolicy.Resolve(records));
    }
}
