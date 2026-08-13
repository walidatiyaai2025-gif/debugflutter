using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B1650;

public sealed class DiagnosticSeverityPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesSeverityDeterministically()
    {
        var result = DiagnosticSeverityNormalizationPolicy.Evaluate(new[]
        {
            new DiagnosticSeverityInput("first", "info", 95),
            new DiagnosticSeverityInput("second", "warning", 20)
        });

        Assert.Equal("critical", result.HighestSeverity);
        Assert.Equal(new[] { "first", "second" }, result.Diagnostics.Select(item => item.Identity));
        Assert.Equal(64, result.Fingerprint.Length);
    }
}
