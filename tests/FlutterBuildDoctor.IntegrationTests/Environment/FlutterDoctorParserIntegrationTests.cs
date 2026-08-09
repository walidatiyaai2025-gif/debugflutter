using FlutterBuildDoctor.App.DependencyInjection;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Doctor;
using Microsoft.Extensions.DependencyInjection;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class FlutterDoctorParserIntegrationTests
{
    [Fact]
    public void RuntimeDetection_ResolvesSingletonFlutterDoctorParser()
    {
        var services = new ServiceCollection();
        services.AddFlutterBuildDoctorRuntimeDetection();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IFlutterDoctorParser>();
        var second = provider.GetRequiredService<IFlutterDoctorParser>();

        Assert.IsType<FlutterDoctorParser>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void Parse_ConsumesExistingExecutionEvidenceWithoutRunningAProcess()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var process = new ProcessResult(
            ProcessExecutionStatus.Succeeded,
            0,
            timestamp,
            timestamp.AddMilliseconds(5),
            new[]
            {
                new ProcessOutputLine(timestamp, ProcessStream.StdOut, "Doctor summary:"),
                new ProcessOutputLine(timestamp, ProcessStream.StdOut, "[✓] Flutter (Channel stable)"),
                new ProcessOutputLine(timestamp, ProcessStream.StdOut, "    • Flutter version test"),
                new ProcessOutputLine(timestamp, ProcessStream.StdOut, "[!] Android toolchain - develop for Android devices")
            },
            "flutter doctor -v");
        var execution = new FlutterDoctorExecutionResult(
            FlutterDoctorExecutionStatus.Succeeded,
            @"C:\flutter\bin\flutter.bat",
            "completed",
            process);

        var parser = new FlutterDoctorParser();
        var result = parser.Parse(execution);

        Assert.Same(process, result.ProcessResult);
        Assert.Equal(2, result.Sections.Count);
        Assert.Equal(FlutterDoctorSectionKind.Flutter, result.Sections[0].Kind);
        Assert.Equal(FlutterDoctorSectionKind.AndroidToolchain, result.Sections[1].Kind);
    }
}
