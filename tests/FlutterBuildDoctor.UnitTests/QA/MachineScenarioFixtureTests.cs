using System.Text.Json;

namespace FlutterBuildDoctor.UnitTests.QA;

public sealed class MachineScenarioFixtureTests
{
    [Theory]
    [InlineData("clean-machine.json", "clean-machine", 4)]
    [InlineData("partial-toolchain.json", "partial-toolchain", 3)]
    public void ScenarioFixture_IsDeterministicAndDeclaresExpectedReadinessRisk(
        string fixture,
        string scenarioId,
        int expectedMinimumBlockers)
    {
        var scenario = JsonSerializer.Deserialize<MachineScenario>(
            ReadEmbedded("Fixtures.QA." + fixture),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(scenario);
        Assert.Equal(scenarioId, scenario!.ScenarioId);
        Assert.NotEmpty(scenario.PresentTools);
        Assert.NotEmpty(scenario.MissingTools);
        Assert.Equal(expectedMinimumBlockers, scenario.ExpectedMinimumBlockers);
        Assert.Empty(scenario.PresentTools.Intersect(scenario.MissingTools, StringComparer.OrdinalIgnoreCase));
    }

    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(MachineScenarioFixtureTests).Assembly;
        var name = Assert.Single(assembly.GetManifestResourceNames().Where(resource => resource.EndsWith(suffix, StringComparison.Ordinal)));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record MachineScenario(
        string ScenarioId,
        string Description,
        IReadOnlyList<string> PresentTools,
        IReadOnlyList<string> MissingTools,
        int ExpectedMinimumBlockers);
}
