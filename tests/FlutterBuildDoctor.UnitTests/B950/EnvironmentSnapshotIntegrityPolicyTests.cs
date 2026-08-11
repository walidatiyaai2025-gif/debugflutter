using System;
using System.Collections.Generic;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B950;

public sealed class EnvironmentSnapshotIntegrityPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesRedactsOrdersAndFingerprintsSnapshot()
    {
        var first = EnvironmentSnapshotIntegrityPolicy.Evaluate(" Build-Env ", new[]
        {
            new KeyValuePair<string, string?>("Path", " C:\\Flutter ; C:\\Git ; C:\\Flutter "),
            new KeyValuePair<string, string?>("api_token", "super-secret"),
            new KeyValuePair<string, string?>("JAVA_HOME", " C:\\Java ")
        });
        var second = EnvironmentSnapshotIntegrityPolicy.Evaluate("build-env", new[]
        {
            new KeyValuePair<string, string?>("JAVA_HOME", "C:\\Java"),
            new KeyValuePair<string, string?>("API_TOKEN", "different-secret"),
            new KeyValuePair<string, string?>("PATH", "C:\\Flutter;C:\\Git")
        });

        Assert.Equal("build-env", first.SnapshotIdentity);
        Assert.Equal(new[] { "API_TOKEN", "JAVA_HOME", "PATH" }, first.Variables.Select(item => item.Name));
        Assert.Equal("[REDACTED]", first.Variables.Single(item => item.Name == "API_TOKEN").Value);
        Assert.True(first.Variables.Single(item => item.Name == "API_TOKEN").Redacted);
        Assert.Equal("C:\\Flutter;C:\\Git", first.Variables.Single(item => item.Name == "PATH").Value);
        Assert.DoesNotContain("super-secret", first.CanonicalPayload, StringComparison.Ordinal);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal("environment-snapshot-valid", first.ReasonCode);
    }

    [Fact]
    public void Evaluate_RejectsDuplicateNamesAfterNormalization()
        => Assert.Throws<ArgumentException>(() => EnvironmentSnapshotIntegrityPolicy.Evaluate("env", new[]
        {
            new KeyValuePair<string, string?>("Path", "A"),
            new KeyValuePair<string, string?>("PATH", "B")
        }));

    [Fact]
    public void Evaluate_BoundsVariableCountAndRejectsUnsafeNames()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EnvironmentSnapshotIntegrityPolicy.Evaluate("env", new[]
        {
            new KeyValuePair<string, string?>("A", "1"),
            new KeyValuePair<string, string?>("B", "2")
        }, 1));
        Assert.Throws<ArgumentException>(() => EnvironmentSnapshotIntegrityPolicy.Evaluate("env", new[]
        {
            new KeyValuePair<string, string?>("BAD NAME", "1")
        }));
    }
}
