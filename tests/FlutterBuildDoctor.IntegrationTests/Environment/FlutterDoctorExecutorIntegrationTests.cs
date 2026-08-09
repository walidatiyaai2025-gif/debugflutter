using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Detection;
using FlutterBuildDoctor.Flutter.Doctor;
using FlutterBuildDoctor.Infrastructure.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class FlutterDoctorExecutorIntegrationTests
{
    [Fact]
    public async Task ExecuteAsync_WindowsCommandShim_UsesRealProcessRunnerAndPreservesBothStreams()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"fbd-doctor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var flutterPath = Path.Combine(directory, "flutter.cmd");
            await File.WriteAllTextAsync(
                flutterPath,
                "@echo off\r\n" +
                "if \"%1\"==\"doctor\" if \"%2\"==\"-v\" (\r\n" +
                "  echo doctor-stdout\r\n" +
                "  echo doctor-stderr>&2\r\n" +
                "  exit /b 0\r\n" +
                ")\r\n" +
                "exit /b 9\r\n");

            var executor = new FlutterDoctorExecutor(new ProcessRunner());
            var result = await executor.ExecuteAsync(
                new FlutterDoctorExecutionRequest(Flutter(flutterPath), TimeSpan.FromSeconds(15)));

            Assert.True(
                result.IsSuccess,
                BuildFailureEvidence(result));
            Assert.NotNull(result.ProcessResult);
            Assert.Equal(ProcessExecutionStatus.Succeeded, result.ProcessResult!.Status);
            Assert.Equal(0, result.ProcessResult.ExitCode);
            Assert.NotNull(result.ProcessResult.ExecutionReceipt);
            Assert.Contains(result.ProcessResult.Output, line => line.Stream == ProcessStream.StdOut && line.Text == "doctor-stdout");
            Assert.Contains(result.ProcessResult.Output, line => line.Stream == ProcessStream.StdErr && line.Text == "doctor-stderr");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RuntimeDetection_ResolvesSingletonFlutterDoctorExecutor()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IFlutterDoctorExecutor>();
        var second = provider.GetRequiredService<IFlutterDoctorExecutor>();

        Assert.IsType<FlutterDoctorExecutor>(first);
        Assert.Same(first, second);
    }

    private static string BuildFailureEvidence(FlutterDoctorExecutionResult result)
    {
        if (result.ProcessResult is null)
            return $"Doctor status={result.Status}; message={result.Message}; no ProcessResult was returned.";

        var output = string.Join(
            " | ",
            result.ProcessResult.Output.Select(line => $"{line.Stream}:{line.Text}"));
        return $"Doctor status={result.Status}; process={result.ProcessResult.Status}; exit={result.ProcessResult.ExitCode}; command={result.ProcessResult.SanitizedCommand}; failure={result.ProcessResult.FailureReason}; output={output}";
    }

    private static FlutterDetectionResult Flutter(string executablePath)
        => new(
            FlutterSdkDetectionStatus.Succeeded,
            Installed: true,
            FlutterPath: executablePath,
            FlutterSdkPath: Path.GetDirectoryName(Path.GetDirectoryName(executablePath)),
            FlutterVersion: "test",
            Channel: "test",
            Candidates: Array.Empty<FlutterSdkCandidate>(),
            HasConflict: false,
            Message: "Test Flutter command shim.",
            PathDiscovery: new PathExecutableDiscoveryResult(
                PathExecutableDiscoveryStatus.Succeeded,
                "flutter",
                Array.Empty<PathExecutableMatch>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<IgnoredPathEntry>(),
                "Test discovery."));
}
