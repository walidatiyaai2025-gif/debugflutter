using FlutterBuildDoctor.Application.Diagnostics;

namespace FlutterBuildDoctor.UnitTests.B450;

public sealed class EvidenceCorrelationPolicyTests
{
    [Fact]
    public void Correlate_GroupsDeduplicatesCountsAndOrdersDeterministically()
    {
        var t1 = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.FromHours(3));
        var t2 = t1.AddMinutes(5);
        var evidence = new[]
        {
            new DiagnosticEvidence("gradle-1", "gradle.failure", " failed ", CorrelatedEvidenceSeverity.Error, t1),
            new DiagnosticEvidence("gradle-1", "GRADLE.FAILURE", "failed", CorrelatedEvidenceSeverity.Critical, t1),
            new DiagnosticEvidence("gradle-2", "gradle.failure", "second", CorrelatedEvidenceSeverity.Warning, t2),
            new DiagnosticEvidence("lint-1", "flutter.analyzer", "warning", CorrelatedEvidenceSeverity.Warning, t1)
        };

        var first = EvidenceCorrelationPolicy.Correlate(evidence);
        var second = EvidenceCorrelationPolicy.Correlate(evidence.Reverse());

        Assert.Equal(2, first.Groups.Count);
        Assert.Equal("GRADLE.FAILURE", first.Groups[0].ProblemCode);
        Assert.Equal(3, first.Groups[0].Occurrences);
        Assert.Equal(2, first.Groups[0].Evidence.Count);
        Assert.Equal(CorrelatedEvidenceSeverity.Critical, first.Groups[0].Severity);
        Assert.Equal(TimeSpan.Zero, first.Groups[0].FirstSeenUtc.Offset);
        Assert.Equal(TimeSpan.Zero, first.Groups[0].LastSeenUtc.Offset);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Correlate_BoundsEvidencePayloadPerGroup()
    {
        var values = Enumerable.Range(0, EvidenceCorrelationPolicy.MaxEvidencePerGroup + 5)
            .Select(index => new DiagnosticEvidence($"key-{index}", "code", $"message-{index}", CorrelatedEvidenceSeverity.Info, DateTimeOffset.UtcNow.AddSeconds(index)));

        var result = EvidenceCorrelationPolicy.Correlate(values);
        Assert.Equal(EvidenceCorrelationPolicy.MaxEvidencePerGroup + 5, result.Groups.Single().Occurrences);
        Assert.Equal(EvidenceCorrelationPolicy.MaxEvidencePerGroup, result.Groups.Single().Evidence.Count);
    }

    [Theory]
    [InlineData("bad key")]
    [InlineData("bad/code")]
    public void Normalize_RejectsInvalidEvidenceTokens(string value)
    {
        Assert.Throws<ArgumentException>(() => EvidenceCorrelationPolicy.Normalize(
            new DiagnosticEvidence(value, "code", "message", CorrelatedEvidenceSeverity.Info, DateTimeOffset.UtcNow)));
    }
}
