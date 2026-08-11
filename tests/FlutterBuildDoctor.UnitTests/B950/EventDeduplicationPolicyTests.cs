using System;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B950;

public sealed class EventDeduplicationPolicyTests
{
    [Fact]
    public void Evaluate_CollapsesDuplicatesPreservesSeverityAndFirstSeen()
    {
        var hash = new string('a', 64);
        var start = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.FromHours(3));
        var decision = EventDeduplicationPolicy.Evaluate(new[]
        {
            new EventEvidence("event-b", "Build", start.AddSeconds(10), hash, EventEvidenceSeverity.Critical),
            new EventEvidence("event-a", "build", start, hash.ToUpperInvariant(), EventEvidenceSeverity.Warning)
        }, TimeSpan.FromMinutes(1));

        var retained = Assert.Single(decision.Events);
        Assert.Equal("event-a", retained.Identity);
        Assert.Equal("build", retained.Category);
        Assert.Equal(start.ToUniversalTime(), retained.FirstSeenUtc);
        Assert.Equal(EventEvidenceSeverity.Critical, retained.Severity);
        Assert.Equal(2, retained.OccurrenceCount);
        Assert.Equal("events-deduplicated", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_PreservesEventsOutsideWindowAndOrdersDeterministically()
    {
        var hash = new string('b', 64);
        var start = DateTimeOffset.Parse("2026-08-11T00:00:00Z");
        var first = EventDeduplicationPolicy.Evaluate(new[]
        {
            new EventEvidence("later", "net", start.AddMinutes(5), hash, EventEvidenceSeverity.Info),
            new EventEvidence("early", "net", start, hash, EventEvidenceSeverity.Info)
        }, TimeSpan.FromMinutes(1));
        var second = EventDeduplicationPolicy.Evaluate(new[]
        {
            new EventEvidence("early", "NET", start, hash.ToUpperInvariant(), EventEvidenceSeverity.Info),
            new EventEvidence("later", "net", start.AddMinutes(5), hash, EventEvidenceSeverity.Info)
        }, TimeSpan.FromMinutes(1));

        Assert.Equal(2, first.Events.Count);
        Assert.Equal(new[] { "early", "later" }, first.Events.Select(item => item.Identity));
        Assert.Equal("events-unique", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Evaluate_ClampsWindowAndRejectsMalformedFingerprint()
    {
        var validHash = new string('c', 64);
        var decision = EventDeduplicationPolicy.Evaluate(new[]
        {
            new EventEvidence("event", "build", DateTimeOffset.UtcNow, validHash, EventEvidenceSeverity.Info)
        }, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(1), decision.Window);

        Assert.Throws<ArgumentException>(() => EventDeduplicationPolicy.Evaluate(new[]
        {
            new EventEvidence("event", "build", DateTimeOffset.UtcNow, "bad", EventEvidenceSeverity.Info)
        }, TimeSpan.FromMinutes(1)));
    }
}
