using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Detection;
using FlutterBuildDoctor.Infrastructure.Processes;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class FlutterVersionProbeIntegrationTests
{
    [Fact]
    public async Task ProbeAsync_WindowsCommandShim_UsesRealProcessRunnerAndParsesStructuredVersions()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"fbd-flutter-version-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var flutterPath = Path.Combine(directory, "flutter.cmd");
            await File.WriteAllTextAsync(
                flutterPath,
                "@echo off\r\n" +
                "if not \"%1\"==\"--version\" exit /b 9\r\n" +
                "pwsh.exe -NoLogo -NoProfile -NonInteractive -Command \"$b=[char]0x2022; Write-Output ('Flutter 3.44.8 ' + $b + ' channel stable ' + $b + ' https://github.com/flutter/flutter.git'); Write-Output ('Framework ' + $b + ' revision abc123def (today) ' + $b + ' 2026-08-09'); Write-Output ('Engine ' + $b + ' revision engine987'); Write-Output ('Tools ' + $b + ' Dart 3.12.2 ' + $b + ' DevTools 2.57.0')\"\r\n" +
                "exit /b %errorlevel%\r\n");

            var probe = new FlutterVersionProbe(new ProcessRunner());
            var result = await probe.ProbeAsync(
                new FlutterVersionProbeRequest(Flutter(flutterPath), TimeSpan.FromSeconds(20)));

            Assert.True(result.IsSuccess, BuildFailureEvidence(result));
            Assert.Equal("3.44.8", result.FlutterVersion);
            Assert.Equal("stable", result.Channel);
            Assert.Equal("abc123def", result.FrameworkRevision);
            Assert.Equal("3.12.2", result.DartVersion);
            Assert.Equal("engine987", result.EngineRevision);
            Assert.Equal("2.57.0", result.DevToolsVersion);
            Assert.NotNull(result.ProcessResult);
            Assert.Equal(ProcessExecutionStatus.Succeeded, result.ProcessResult!.Status);
            Assert.Equal(0, result.ProcessResult.ExitCode);
            Assert.NotNull(result.ProcessResult.ExecutionReceipt);
            Assert.Contains(result.ProcessResult.Output, line => line.Text.Contains("Flutter 3.44.8", StringComparison.Ordinal));
            Assert.Contains(result.ProcessResult.Output, line => line.Text.Contains("Dart 3.12.2", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RuntimeDetection_ResolvesSingletonFlutterVersionProbe()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IFlutterVersionProbe>();
        var second = provider.GetRequiredService<IFlutterVersionProbe>();

        Assert.IsType<FlutterVersionProbe>(first);
        Assert.Same(first, second);
    }

    private static string BuildFailureEvidence(FlutterVersionProbeResult result)
    {
        if (result.ProcessResult is null)
            return $"Probe status={result.Status}; message={result.Message}; no ProcessResult was returned.";

        var output = string.Join(
            " | ",
            result.ProcessResult.Output.Select(line => $"{line.Stream}:{line.Text}"));
        return $"Probe status={result.Status}; process={result.ProcessResult.Status}; exit={result.ProcessResult.ExitCode}; command={result.ProcessResult.SanitizedCommand}; failure={result.ProcessResult.FailureReason}; output={output}";
    }

    private static FlutterDetectionResult Flutter(string executablePath)
        => new(
            FlutterSdkDetectionStatus.Succeeded,
            Installed: true,
            FlutterPath: executablePath,
            FlutterSdkPath: Path.GetDirectoryName(Path.GetDirectoryName(executablePath)),
            FlutterVersion: "metadata-version",
            Channel: "metadata-channel",
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
