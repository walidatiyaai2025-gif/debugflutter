namespace FlutterBuildDoctor.Application.Compatibility;

public sealed class CompatibilityEngine : ICompatibilityEngine
{
    private readonly IReadOnlyList<ICompatibilityRule> _rules;

    public CompatibilityEngine(IEnumerable<ICompatibilityRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.ToArray();
    }

    public CompatibilityReport Evaluate(CompatibilityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var findings = _rules
            .SelectMany(rule => rule.Evaluate(context))
            .OrderByDescending(static finding => finding.Severity)
            .ThenBy(static finding => finding.RuleId, StringComparer.Ordinal)
            .ToArray();
        return new CompatibilityReport(findings);
    }

    public static CompatibilityEngine CreateDefault()
        => new(new ICompatibilityRule[]
        {
            new JavaGradleCompatibilityRule(),
            new GradleAgpCompatibilityRule(),
            new AgpCompileSdkCompatibilityRule(),
            new KotlinGradleAgpCompatibilityRule(),
            new DartConstraintCompatibilityRule(),
            new AndroidPackageAvailabilityRule()
        });
}
