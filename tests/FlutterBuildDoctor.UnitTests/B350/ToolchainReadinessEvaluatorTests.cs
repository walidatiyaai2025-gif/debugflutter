using FlutterBuildDoctor.Application.Toolchains;

namespace FlutterBuildDoctor.UnitTests.B350;

public sealed class ToolchainReadinessEvaluatorTests
{
    [Fact]
    public void Evaluate_NormalizesVersionsPathsUtcAndProducesDeterministicFingerprint()
    {
        var requirements = new[]
        {
            new ToolRequirement(" Flutter ", "v3.22.1"),
            new ToolRequirement("java", "17.0.0"),
            new ToolRequirement("adb", "1.0", Required: false)
        };
        var evidence = new[]
        {
            new ToolEvidence("JAVA", true, "17.0.10+7", " C:/Java/bin/java.exe "),
            new ToolEvidence("flutter", true, "3.22.2-stable", " C:/flutter/bin/flutter.bat ")
        };
        var now = new DateTimeOffset(2026, 8, 11, 9, 0, 0, TimeSpan.FromHours(3));

        var first = ToolchainReadinessEvaluator.Evaluate(requirements, evidence, now);
        var second = ToolchainReadinessEvaluator.Evaluate(requirements.AsEnumerable().Reverse(), evidence.AsEnumerable().Reverse(), now);

        Assert.Equal(67, first.ReadinessScore);
        Assert.Empty(first.Blockers);
        Assert.Equal(TimeSpan.Zero, first.EvaluatedAtUtc.Offset);
        Assert.Equal("C:/flutter/bin/flutter.bat", first.Items.Single(item => item.Name == "flutter").ExecutablePath);
        Assert.Equal("3.22.2", first.Items.Single(item => item.Name == "flutter").DiscoveredVersion);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ReportsRequiredMissingAndBelowMinimumAsSortedBlockers()
    {
        var decision = ToolchainReadinessEvaluator.Evaluate(
            new[]
            {
                new ToolRequirement("java", "17.0"),
                new ToolRequirement("flutter", "3.22.0")
            },
            new[]
            {
                new ToolEvidence("flutter", true, "3.19.0")
            },
            DateTimeOffset.UtcNow);

        Assert.Equal(new[] { "flutter:below-minimum", "java:missing" }, decision.Blockers);
        Assert.Equal(0, decision.ReadinessScore);
    }

    [Fact]
    public void Evaluate_UsesNewestEvidenceForDuplicateDiscoveries()
    {
        var older = new DateTimeOffset(2026, 8, 11, 1, 0, 0, TimeSpan.Zero);
        var newer = older.AddMinutes(1);
        var decision = ToolchainReadinessEvaluator.Evaluate(
            new[] { new ToolRequirement("java", "17.0") },
            new[]
            {
                new ToolEvidence("java", true, "11.0", DiscoveredAt: older),
                new ToolEvidence("java", true, "21.0", DiscoveredAt: newer)
            },
            newer);

        Assert.Equal(100, decision.ReadinessScore);
        Assert.Equal("21.0", decision.Items[0].DiscoveredVersion);
    }

    [Theory]
    [InlineData("v3.22.1", "3.22.1")]
    [InlineData("17.0.10+7", "17.0.10")]
    [InlineData("3.22.0-stable", "3.22.0")]
    public void NormalizeVersion_NormalizesSemanticEvidence(string input, string expected)
    {
        Assert.Equal(expected, ToolchainReadinessEvaluator.NormalizeVersion(input));
    }

    [Fact]
    public void Evaluate_RejectsInvalidOrDuplicateRequiredToolIdentities()
    {
        Assert.Throws<ArgumentException>(() => ToolchainReadinessEvaluator.Evaluate(
            new[] { new ToolRequirement("bad tool", "1.0") },
            Array.Empty<ToolEvidence>(),
            DateTimeOffset.UtcNow));

        Assert.Throws<ArgumentException>(() => ToolchainReadinessEvaluator.Evaluate(
            new[] { new ToolRequirement("java", "17"), new ToolRequirement("JAVA", "17") },
            Array.Empty<ToolEvidence>(),
            DateTimeOffset.UtcNow));
    }
}
