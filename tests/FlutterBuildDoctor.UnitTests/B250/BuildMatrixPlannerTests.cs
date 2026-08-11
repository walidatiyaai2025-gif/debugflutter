using FlutterBuildDoctor.Application.Builds;

namespace FlutterBuildDoctor.UnitTests.B250;

public sealed class BuildMatrixPlannerTests
{
    [Fact]
    public void Create_ProvidesDeterministicDefaultDebugVariant()
    {
        var first = BuildMatrixPlanner.Create(Array.Empty<BuildVariant>());
        var second = BuildMatrixPlanner.Create(Array.Empty<BuildVariant>());

        var variant = Assert.Single(first.Variants);
        Assert.Equal(PlannedBuildMode.Debug, variant.Mode);
        Assert.Equal(PlannedArtifactKind.Apk, variant.Artifact);
        Assert.Equal("lib/main.dart", variant.Target);
        Assert.Equal(0, variant.RetryBudget);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
    }

    [Fact]
    public void Create_NormalizesDeduplicatesOrdersAndAssignsRetryBudgets()
    {
        var plan = BuildMatrixPlanner.Create(new[]
        {
            new BuildVariant(" PROD ", @".\lib\main.dart", PlannedBuildMode.Release, PlannedArtifactKind.Aab),
            new BuildVariant("prod", "lib/main.dart", PlannedBuildMode.Release, PlannedArtifactKind.Aab),
            new BuildVariant("QA_ENV", "lib/qa.dart", PlannedBuildMode.Profile, PlannedArtifactKind.Apk, 2)
        });

        Assert.Equal(2, plan.Variants.Count);
        Assert.Equal(PlannedBuildMode.Profile, plan.Variants[0].Mode);
        Assert.Equal("qa-env", plan.Variants[0].Flavor);
        Assert.Equal(2, plan.Variants[0].RetryBudget);
        Assert.Equal(PlannedBuildMode.Release, plan.Variants[1].Mode);
        Assert.Equal("prod", plan.Variants[1].Flavor);
        Assert.Equal(1, plan.Variants[1].RetryBudget);
        Assert.Equal(64, plan.Fingerprint.Length);
    }

    [Fact]
    public void Create_RejectsInvalidVariantContractsAndUnboundedMatrix()
    {
        Assert.Throws<InvalidOperationException>(() => BuildMatrixPlanner.Create(new[]
        {
            new BuildVariant(null, "lib/main.dart", PlannedBuildMode.Debug, PlannedArtifactKind.Aab)
        }));

        Assert.Throws<ArgumentException>(() => BuildMatrixPlanner.Create(new[]
        {
            new BuildVariant(null, "../lib/main.dart", PlannedBuildMode.Debug, PlannedArtifactKind.Apk)
        }));

        Assert.Throws<ArgumentOutOfRangeException>(() => BuildMatrixPlanner.Create(
            Enumerable.Range(0, BuildMatrixPlanner.MaxVariants + 1)
                .Select(index => new BuildVariant($"f{index}", "lib/main.dart", PlannedBuildMode.Debug, PlannedArtifactKind.Apk))));
    }

    [Theory]
    [InlineData(" DEV_TEST ", "dev-test")]
    [InlineData(null, null)]
    public void NormalizeFlavor_IsStable(string? input, string? expected)
    {
        Assert.Equal(expected, BuildMatrixPlanner.NormalizeFlavor(input));
    }
}
