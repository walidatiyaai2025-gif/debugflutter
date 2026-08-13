using System;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;
using Xunit;

namespace FlutterBuildDoctor.UnitTests.B1550;

public sealed class DiagnosticAttachmentQuotaPolicyTests
{
    [Fact]
    public void Evaluate_PreservesMandatoryFirstAndTrimsDeterministically()
    {
        var mb = 1024L * 1024;
        var attachments = new[]
        {
            new DiagnosticAttachment("optional-a", "log", mb, false, 10),
            new DiagnosticAttachment("mandatory", "trace", mb, true, 1),
            new DiagnosticAttachment("optional-b", "log", mb, false, 5)
        };
        var decision = DiagnosticAttachmentQuotaPolicy.Evaluate(attachments, 2 * mb, 2 * mb);
        Assert.Equal(new[] { "mandatory", "optional-a" }, decision.Retained.Select(a => a.Identity).ToArray());
        Assert.Equal(new[] { "optional-b" }, decision.DroppedAttachmentIds);
        Assert.Equal(2 * mb, decision.RetainedBytes);
        Assert.Equal("diagnostic-attachment-quota-trimmed", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_ClampsQuotasAndRejectsNegativeOrDuplicateEntries()
    {
        var empty = DiagnosticAttachmentQuotaPolicy.Evaluate(Array.Empty<DiagnosticAttachment>(), 0, 0);
        Assert.Equal(DiagnosticAttachmentQuotaPolicy.MinTotalQuota, empty.TotalQuotaBytes);
        Assert.Equal(DiagnosticAttachmentQuotaPolicy.MinPerAttachmentQuota, empty.PerAttachmentQuotaBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => DiagnosticAttachmentQuotaPolicy.Evaluate(new[] { new DiagnosticAttachment("a", "log", -1, false, 0) }, DiagnosticAttachmentQuotaPolicy.MinTotalQuota, DiagnosticAttachmentQuotaPolicy.MinPerAttachmentQuota));
        Assert.Throws<ArgumentException>(() => DiagnosticAttachmentQuotaPolicy.Evaluate(new[] { new DiagnosticAttachment("a", "log", 1, false, 0), new DiagnosticAttachment("a", "trace", 1, true, 1) }, DiagnosticAttachmentQuotaPolicy.MinTotalQuota, DiagnosticAttachmentQuotaPolicy.MinPerAttachmentQuota));
    }
}
