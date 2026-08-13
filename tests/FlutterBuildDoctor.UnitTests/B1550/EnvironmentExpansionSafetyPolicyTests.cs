using System;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;
using Xunit;

namespace FlutterBuildDoctor.UnitTests.B1550;

public sealed class EnvironmentExpansionSafetyPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesExpandsAndRedactsSecretDerivedValues()
    {
        var variables = new[]
        {
            new EnvironmentExpansionVariable("root", "C:/sdk", false),
            new EnvironmentExpansionVariable("token", "abc123", true),
            new EnvironmentExpansionVariable("path", "${ROOT}/bin/${TOKEN}", false)
        };
        var decision = EnvironmentExpansionSafetyPolicy.Evaluate(variables, 20);
        Assert.Equal(EnvironmentExpansionSafetyPolicy.MaximumSupportedDepth, decision.MaximumDepth);
        Assert.True(decision.SecretsRedacted);
        Assert.Equal(new[] { "PATH", "ROOT", "TOKEN" }, decision.Variables.Select(v => v.Name).ToArray());
        Assert.Equal("C:/sdk/bin/[redacted]", decision.Variables.Single(v => v.Name == "PATH").RedactedValue);
        Assert.Equal("[redacted]", decision.Variables.Single(v => v.Name == "TOKEN").RedactedValue);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsDuplicateUnknownRecursiveAndDepthOverflow()
    {
        Assert.Throws<ArgumentException>(() => EnvironmentExpansionSafetyPolicy.Evaluate(new[] { new EnvironmentExpansionVariable("A", "x", false), new EnvironmentExpansionVariable("a", "y", false) }));
        Assert.Throws<ArgumentException>(() => EnvironmentExpansionSafetyPolicy.Evaluate(new[] { new EnvironmentExpansionVariable("A", "${MISSING}", false) }));
        Assert.Throws<InvalidOperationException>(() => EnvironmentExpansionSafetyPolicy.Evaluate(new[] { new EnvironmentExpansionVariable("A", "${B}", false), new EnvironmentExpansionVariable("B", "${A}", false) }));
        Assert.Throws<InvalidOperationException>(() => EnvironmentExpansionSafetyPolicy.Evaluate(new[] { new EnvironmentExpansionVariable("A", "${B}", false), new EnvironmentExpansionVariable("B", "${C}", false), new EnvironmentExpansionVariable("C", "${D}", false), new EnvironmentExpansionVariable("D", "ok", false) }, 2));
    }
}
