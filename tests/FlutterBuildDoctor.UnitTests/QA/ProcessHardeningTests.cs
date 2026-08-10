using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Infrastructure.Processes;

namespace FlutterBuildDoctor.UnitTests.QA;

public sealed class ProcessHardeningTests
{
    [Fact]
    public async Task ProcessRunner_CancellationKillsLongRunningProcessTree()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
        var runner = new ProcessRunner();
        var request = new ProcessRequest(
            "powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 10" },
            Timeout: TimeSpan.FromSeconds(20),
            DisplayName: "cancellation regression");

        var result = await runner.RunAsync(request, cancellationToken: source.Token);

        Assert.Equal(ProcessExecutionStatus.Cancelled, result.Status);
        Assert.NotNull(result.ExecutionReceipt);
        Assert.True(result.Duration < TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task ProcessRunner_TimeoutIsDistinctFromUserCancellation()
    {
        if (!OperatingSystem.IsWindows()) return;
        var runner = new ProcessRunner();
        var request = new ProcessRequest(
            "powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 10" },
            Timeout: TimeSpan.FromMilliseconds(350),
            DisplayName: "timeout regression");

        var result = await runner.RunAsync(request);

        Assert.Equal(ProcessExecutionStatus.TimedOut, result.Status);
        Assert.Contains("timed out", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessRunner_RedactsSensitiveValuesFromCommandOutputAndReceipt()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string secret = "FBD-super-secret-12345";
        var runner = new ProcessRunner();
        var request = new ProcessRequest(
            "powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-Command", $"Write-Output 'token={secret}'" },
            Timeout: TimeSpan.FromSeconds(10),
            DisplayName: "redaction regression",
            SensitiveValues: new[] { secret });

        var result = await runner.RunAsync(request);
        var rendered = string.Join("\n", result.Output.Select(line => line.Text)) + "\n" + result.SanitizedCommand + "\n" + result.ExecutionReceipt!.SanitizedCommand;

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(secret, rendered, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", rendered, StringComparison.Ordinal);
    }
}
