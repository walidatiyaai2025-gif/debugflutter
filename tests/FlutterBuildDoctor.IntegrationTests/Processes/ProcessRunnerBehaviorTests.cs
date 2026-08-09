using System.Collections.Concurrent;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Infrastructure.Processes;

namespace FlutterBuildDoctor.IntegrationTests.Processes;

public sealed class ProcessRunnerBehaviorTests
{
    [Fact]
    public async Task Success_StreamsBothChannelsAndCreatesReceipt()
    {
        var runner = new ProcessRunner();
        var progress = new CollectingProgress<ProcessOutputLine>();
        var request = Cmd("echo stdout-line & echo stderr-line 1>&2", displayName: "channel test");

        var result = await runner.RunAsync(request, progress);

        Assert.True(result.IsSuccess, result.FailureReason);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains(result.Output, line => line.Stream == ProcessStream.StdOut && line.Text.Contains("stdout-line"));
        Assert.Contains(result.Output, line => line.Stream == ProcessStream.StdErr && line.Text.Contains("stderr-line"));
        Assert.Contains(progress.Values, line => line.Stream == ProcessStream.StdOut);
        Assert.Contains(progress.Values, line => line.Stream == ProcessStream.StdErr);

        var receipt = Assert.IsType<ProcessExecutionReceipt>(result.ExecutionReceipt);
        Assert.NotEqual(Guid.Empty, receipt.ExecutionId);
        Assert.Equal("channel test", receipt.DisplayName);
        Assert.Equal(ProcessExecutionStatus.Succeeded, receipt.Status);
        Assert.Equal(0, receipt.ExitCode);
        Assert.Equal(result.SanitizedCommand, receipt.SanitizedCommand);
        Assert.True(receipt.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task NonZeroExit_ReturnsFailedResultInsteadOfThrowing()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(Cmd("echo expected-failure 1>&2 & exit /b 7"));

        Assert.Equal(ProcessExecutionStatus.Failed, result.Status);
        Assert.Equal(7, result.ExitCode);
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Output, line => line.Stream == ProcessStream.StdErr && line.Text.Contains("expected-failure"));
        Assert.Equal(7, result.ExecutionReceipt?.ExitCode);
    }

    [Fact]
    public async Task Cancellation_ReturnsCancelledAndTerminatesLongRunningCommand()
    {
        var runner = new ProcessRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));

        var result = await runner.RunAsync(
            Cmd("ping 127.0.0.1 -n 30 >nul", timeout: TimeSpan.FromSeconds(20)),
            cancellationToken: cancellation.Token);

        Assert.Equal(ProcessExecutionStatus.Cancelled, result.Status);
        Assert.Null(result.ExitCode);
        Assert.Equal(ProcessExecutionStatus.Cancelled, result.ExecutionReceipt?.Status);
    }

    [Fact]
    public async Task Timeout_ReturnsExplicitTimedOutStatus()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            Cmd("ping 127.0.0.1 -n 30 >nul", timeout: TimeSpan.FromMilliseconds(350)));

        Assert.Equal(ProcessExecutionStatus.TimedOut, result.Status);
        Assert.Null(result.ExitCode);
        Assert.Equal(ProcessExecutionStatus.TimedOut, result.ExecutionReceipt?.Status);
    }

    [Fact]
    public async Task EnvironmentOverride_IsVisibleOnlyToChildProcess()
    {
        var runner = new ProcessRunner();
        var expected = $"override-{Guid.NewGuid():N}";
        var request = Cmd(
            "echo %FBD_PROCESS_TEST_VALUE%",
            environment: new Dictionary<string, string?>
            {
                ["FBD_PROCESS_TEST_VALUE"] = expected
            });

        var result = await runner.RunAsync(request);

        Assert.True(result.IsSuccess, result.FailureReason);
        Assert.Contains(result.Output, line => line.Stream == ProcessStream.StdOut && line.Text.Trim() == expected);
    }

    [Fact]
    public async Task Secrets_AreRedactedFromCommandStdOutAndStdErr()
    {
        var runner = new ProcessRunner();
        var secret = $"fbd-secret-{Guid.NewGuid():N}";
        var request = Cmd(
            "echo --token %FBD_API_TOKEN% & echo password=%FBD_API_TOKEN% 1>&2",
            environment: new Dictionary<string, string?>
            {
                ["FBD_API_TOKEN"] = secret
            },
            sensitiveValues: new[] { secret });

        var result = await runner.RunAsync(request);

        Assert.True(result.IsSuccess, result.FailureReason);
        Assert.DoesNotContain(secret, result.SanitizedCommand, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.SanitizedCommand, StringComparison.Ordinal);
        Assert.All(result.Output, line => Assert.DoesNotContain(secret, line.Text, StringComparison.Ordinal));
        Assert.Contains(result.Output, line => line.Text.Contains("[REDACTED]", StringComparison.Ordinal));
        Assert.DoesNotContain(secret, result.ExecutionReceipt?.SanitizedCommand ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void Redactor_SanitizesAdjacentAndInlineSensitiveArguments()
    {
        var redactor = new DefaultProcessSecretRedactor();
        var secret = $"fbd-secret-{Guid.NewGuid():N}";
        var request = new ProcessRequest(
            "tool.exe",
            new[] { "--password", secret, $"--api-key={secret}", "--safe", "visible" },
            SensitiveValues: new[] { secret });

        var sanitized = redactor.SanitizeCommand(request);

        Assert.DoesNotContain(secret, sanitized, StringComparison.Ordinal);
        Assert.Contains("--password [REDACTED]", sanitized, StringComparison.Ordinal);
        Assert.Contains("--api-key=[REDACTED]", sanitized, StringComparison.Ordinal);
        Assert.Contains("--safe visible", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LargeOutput_CompletesWithoutRedirectDeadlock()
    {
        const int lineCount = 1200;
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            Cmd($"for /L %i in (1,1,{lineCount}) do @echo line-%i", timeout: TimeSpan.FromSeconds(20)));

        Assert.True(result.IsSuccess, result.FailureReason);
        Assert.Equal(lineCount, result.Output.Count(line => line.Stream == ProcessStream.StdOut));
        Assert.Contains(result.Output, line => line.Text == "line-1");
        Assert.Contains(result.Output, line => line.Text == $"line-{lineCount}");
    }

    private static ProcessRequest Cmd(
        string command,
        TimeSpan? timeout = null,
        string? displayName = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        IReadOnlyCollection<string>? sensitiveValues = null) =>
        new(
            "cmd.exe",
            new[] { "/d", "/c", command },
            Environment: environment,
            Timeout: timeout ?? TimeSpan.FromSeconds(10),
            DisplayName: displayName,
            SensitiveValues: sensitiveValues);

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        private readonly ConcurrentQueue<T> _values = new();

        public IReadOnlyCollection<T> Values => _values.ToArray();

        public void Report(T value) => _values.Enqueue(value);
    }
}
