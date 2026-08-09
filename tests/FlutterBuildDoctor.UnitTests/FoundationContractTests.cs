using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Domain.Diagnostics;

namespace FlutterBuildDoctor.UnitTests;

public sealed class FoundationContractTests
{
    [Fact]
    public void DiagnosticItem_PreservesReadinessMetadata()
    {
        var item = new DiagnosticItem(
            Id: "flutter-sdk",
            Name: "Flutter SDK",
            Status: DiagnosticStatus.Missing,
            Severity: DiagnosticSeverity.Critical,
            RequiredVersion: ">=3.0.0",
            Summary: "Flutter is required.",
            CanRepair: true);

        Assert.Equal("flutter-sdk", item.Id);
        Assert.Equal(DiagnosticStatus.Missing, item.Status);
        Assert.Equal(DiagnosticSeverity.Critical, item.Severity);
        Assert.Equal(">=3.0.0", item.RequiredVersion);
        Assert.True(item.CanRepair);
    }

    [Fact]
    public void ProcessResult_ComputesDurationAndSuccessState()
    {
        var startedAt = new DateTimeOffset(2026, 8, 9, 4, 0, 0, TimeSpan.Zero);
        var finishedAt = startedAt.AddSeconds(3);
        var result = new ProcessResult(
            ProcessExecutionStatus.Succeeded,
            ExitCode: 0,
            startedAt,
            finishedAt,
            Array.Empty<ProcessOutputLine>(),
            "dotnet --version");

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Duration);
        Assert.Equal(0, result.ExitCode);
    }
}
