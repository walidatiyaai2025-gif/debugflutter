using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.UnitTests.B450;

public sealed class EnvironmentSafetyPolicyTests
{
    [Fact]
    public void Evaluate_OmitsSecretsOrdersEvidenceAndFingerprintsDeterministically()
    {
        var variables = new[]
        {
            new KeyValuePair<string, string?>("JAVA_HOME", @"C:\Java"),
            new KeyValuePair<string, string?>("API_TOKEN", "top-secret"),
            new KeyValuePair<string, string?>("ANDROID_HOME", @"C:\Android")
        };

        var first = EnvironmentSafetyPolicy.Evaluate(variables, new[] { @"C:\Tools\", @"c:\tools", @"C:\Flutter" });
        var second = EnvironmentSafetyPolicy.Evaluate(variables.Reverse(), new[] { @"C:\Flutter", @"C:\Tools" });

        Assert.Equal(2, first.SafeEntries.Count);
        Assert.Equal("ANDROID_HOME", first.SafeEntries[0].Name);
        Assert.Equal("JAVA_HOME", first.SafeEntries[1].Name);
        Assert.Equal(1, first.OmittedSecretCount);
        Assert.Equal(2, first.NormalizedPathSegments.Count);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Theory]
    [InlineData("1BAD")]
    [InlineData("BAD-NAME")]
    [InlineData("BAD NAME")]
    public void NormalizeName_RejectsInvalidNames(string value)
        => Assert.Throws<ArgumentException>(() => EnvironmentSafetyPolicy.NormalizeName(value));

    [Fact]
    public void ValidateValue_RejectsControlCharactersAndOversizedValues()
    {
        Assert.Throws<ArgumentException>(() => EnvironmentSafetyPolicy.ValidateValue("hello\nworld"));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvironmentSafetyPolicy.ValidateValue(new string('a', EnvironmentSafetyPolicy.MaxValueLength + 1)));
    }

    [Fact]
    public void NormalizePathSegments_RejectsRelativePaths()
        => Assert.Throws<ArgumentException>(() => EnvironmentSafetyPolicy.NormalizePathSegments(new[] { "relative\\tool" }));

    [Fact]
    public void Evaluate_RejectsUnboundedVariableCount()
    {
        var values = Enumerable.Range(0, EnvironmentSafetyPolicy.MaxVariables + 1)
            .Select(index => new KeyValuePair<string, string?>($"VAR_{index}", "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvironmentSafetyPolicy.Evaluate(values));
    }
}
