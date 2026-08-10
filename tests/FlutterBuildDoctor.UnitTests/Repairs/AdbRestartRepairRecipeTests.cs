using FlutterBuildDoctor.Android.Repairs;
using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Application.Repairs;

namespace FlutterBuildDoctor.UnitTests.Repairs;

public sealed class AdbRestartRepairRecipeTests
{
    [Fact]
    public async Task Restart_RequiresConfirmationAndVerifiesServerWithDevices()
    {
        var runner = new SequencedRunner(
            Result(ProcessExecutionStatus.Failed, 1, "server was not running"),
            Result(ProcessExecutionStatus.Succeeded, 0),
            Result(ProcessExecutionStatus.Succeeded, 0, null, "List of devices attached"));
        var recipe = new AdbRestartRepairRecipe(runner);
        var context = new RepairContext(@"C:\work\app", AdbExecutable: "adb");

        var rejected = await recipe.ExecuteAsync(context, confirmed: false);
        Assert.Equal(RepairExecutionStatus.Rejected, rejected.Status);
        Assert.Equal(0, runner.Requests.Count);

        var completed = await recipe.ExecuteAsync(context, confirmed: true);
        Assert.True(completed.IsSuccess);
        Assert.Equal(3, runner.Requests.Count);
        Assert.Equal(new[] { "kill-server" }, runner.Requests[0].Arguments);
        Assert.Equal(new[] { "start-server" }, runner.Requests[1].Arguments);
        Assert.Equal(new[] { "devices" }, runner.Requests[2].Arguments);
    }

    private static ProcessResult Result(
        ProcessExecutionStatus status,
        int? exitCode,
        string? failureReason = null,
        params string[] output)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(
            status,
            exitCode,
            now,
            now.AddMilliseconds(10),
            output.Select(line => new ProcessOutputLine(now, ProcessStream.StdOut, line)).ToArray(),
            "adb",
            failureReason);
    }

    private sealed class SequencedRunner : IProcessRunner
    {
        private readonly Queue<ProcessResult> _results;

        public SequencedRunner(params ProcessResult[] results) => _results = new Queue<ProcessResult>(results);
        public List<ProcessRequest> Requests { get; } = new();

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_results.Dequeue());
        }
    }
}
