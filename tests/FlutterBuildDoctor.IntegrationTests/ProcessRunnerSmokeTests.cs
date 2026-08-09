using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Infrastructure.Processes;

namespace FlutterBuildDoctor.IntegrationTests;

public sealed class ProcessRunnerSmokeTests
{
    [Fact]
    public async Task DotNetVersion_CompletesAndStreamsOutput()
    {
        var runner = new ProcessRunner();
        var request = new ProcessRequest(
            FileName: "dotnet",
            Arguments: new[] { "--version" },
            Timeout: TimeSpan.FromSeconds(30),
            DisplayName: "dotnet version smoke test");

        var result = await runner.RunAsync(request);

        Assert.True(result.IsSuccess, result.FailureReason);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Output, line =>
            line.Stream == ProcessStream.StdOut && !string.IsNullOrWhiteSpace(line.Text));
        Assert.Contains("dotnet", result.SanitizedCommand, StringComparison.OrdinalIgnoreCase);
    }
}
