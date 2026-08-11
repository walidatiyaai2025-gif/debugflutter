using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B650;

public sealed class SafeEvidenceExportPolicyTests
{
    [Fact]
    public void Prepare_NormalizesOrdersRedactsAndHashesRecords()
    {
        var records = new[]
        {
            new EvidenceExportRecord("zeta", " value "),
            new EvidenceExportRecord("api_token", "super-secret"),
            new EvidenceExportRecord("alpha", "first")
        };

        var first = SafeEvidenceExportPolicy.Prepare(" SUPPORT-1 ", " Support Bundle.JSON ", records);
        var second = SafeEvidenceExportPolicy.Prepare("support-1", "support-bundle.json", records.AsEnumerable().Reverse());

        Assert.Equal("support-1", first.Identity);
        Assert.Equal("support-bundle.json", first.FileName);
        Assert.Equal(new[] { "alpha", "api_token", "zeta" }, first.Records.Select(item => item.Key));
        Assert.Equal("[REDACTED]", first.Records.Single(item => item.Key == "api_token").Value);
        Assert.All(first.Records, item => Assert.Equal(64, item.Sha256.Length));
        Assert.Equal("evidence-export-ready", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Prepare_RedactsExplicitSensitiveRecords()
    {
        var result = SafeEvidenceExportPolicy.Prepare("bundle", "evidence.txt", new[]
        {
            new EvidenceExportRecord("note", "hidden", Sensitive: true)
        });
        Assert.Equal("[REDACTED]", result.Records[0].Value);
    }

    [Theory]
    [InlineData("../evidence.json")]
    [InlineData("folder/evidence.json")]
    [InlineData("evidence.exe")]
    public void NormalizeFileName_RejectsUnsafeNames(string value)
        => Assert.Throws<ArgumentException>(() => SafeEvidenceExportPolicy.NormalizeFileName(value));

    [Fact]
    public void Prepare_RejectsControlCharactersInNonSensitiveValues()
        => Assert.Throws<ArgumentException>(() => SafeEvidenceExportPolicy.Prepare("bundle", "evidence.json", new[]
        {
            new EvidenceExportRecord("message", "bad\nvalue")
        }));
}
