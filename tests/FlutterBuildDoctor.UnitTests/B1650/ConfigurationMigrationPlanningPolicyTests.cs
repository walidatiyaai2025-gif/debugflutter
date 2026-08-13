using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B1650;

public sealed class ConfigurationMigrationPlanningPolicyTests
{
    [Fact]
    public void Evaluate_BuildsContiguousPlanAndDetectsMissingStep()
    {
        var ready = ConfigurationMigrationPlanningPolicy.Evaluate("config", 1, 3, new[]
        {
            new ConfigurationMigrationStep("two-to-three", 2, 3),
            new ConfigurationMigrationStep("one-to-two", 1, 2)
        });
        Assert.True(ready.Ready);
        Assert.Equal(new[] { 1, 2 }, ready.Plan.Select(step => step.FromVersion));
        Assert.Equal("configuration-migration-ready", ready.ReasonCode);

        var missing = ConfigurationMigrationPlanningPolicy.Evaluate("config", 1, 3, new[]
        {
            new ConfigurationMigrationStep("one-to-two", 1, 2)
        });
        Assert.False(missing.Ready);
        Assert.Equal(new[] { 2 }, missing.MissingFromVersions);
    }

    [Fact]
    public void Evaluate_RejectsRegressionAndNonContiguousStep()
    {
        Assert.Throws<ArgumentException>(() => ConfigurationMigrationPlanningPolicy.Evaluate("config", 3, 2, Array.Empty<ConfigurationMigrationStep>()));
        Assert.Throws<ArgumentException>(() => ConfigurationMigrationPlanningPolicy.Evaluate("config", 1, 3, new[] { new ConfigurationMigrationStep("bad", 1, 3) }));
    }
}
