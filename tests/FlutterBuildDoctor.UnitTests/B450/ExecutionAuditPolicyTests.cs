using FlutterBuildDoctor.Application.Logging;

namespace FlutterBuildDoctor.UnitTests.B450;

public sealed class ExecutionAuditPolicyTests
{
    [Fact]
    public void Sanitize_RedactsSecretsNormalizesUtcOrdersAndFingerprintsDeterministically()
    {
        var t1 = new DateTimeOffset(2026, 8, 11, 15, 0, 0, TimeSpan.FromHours(3));
        var events = new[]
        {
            new ExecutionAuditEvent(" Build.Start ", "token=abc Authorization: Bearer xyz", AuditSeverity.Info, t1.AddMinutes(1)),
            new ExecutionAuditEvent("build.error", "password:hunter2", AuditSeverity.Critical, t1)
        };

        var first = ExecutionAuditPolicy.Sanitize(events);
        var second = ExecutionAuditPolicy.Sanitize(events.Reverse());

        Assert.Equal("build.error", first.Events[0].Name);
        Assert.Equal(AuditSeverity.Critical, first.Events[0].Severity);
        Assert.Equal(TimeSpan.Zero, first.Events[0].TimestampUtc.Offset);
        Assert.DoesNotContain("hunter2", first.Events[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("abc", first.Events[1].Message, StringComparison.Ordinal);
        Assert.DoesNotContain("xyz", first.Events[1].Message, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", first.Events[1].Message, StringComparison.Ordinal);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Normalize_BoundsMessageLength()
    {
        var item = ExecutionAuditPolicy.Normalize(new ExecutionAuditEvent(
            "event", new string('x', ExecutionAuditPolicy.MaxMessageLength + 50), AuditSeverity.Warning, DateTimeOffset.UtcNow));
        Assert.Equal(ExecutionAuditPolicy.MaxMessageLength, item.Message.Length);
        Assert.EndsWith("...", item.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("bad/name")]
    public void NormalizeName_RejectsInvalidNames(string value)
        => Assert.Throws<ArgumentException>(() => ExecutionAuditPolicy.NormalizeName(value));

    [Fact]
    public void Sanitize_BoundsEventCount()
    {
        var values = Enumerable.Range(0, ExecutionAuditPolicy.MaxEvents + 1)
            .Select(index => new ExecutionAuditEvent($"event-{index}", "ok", AuditSeverity.Info, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => ExecutionAuditPolicy.Sanitize(values));
    }
}
