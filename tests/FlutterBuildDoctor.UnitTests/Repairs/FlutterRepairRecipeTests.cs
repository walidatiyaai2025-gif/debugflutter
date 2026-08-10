using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Application.Repairs;
using FlutterBuildDoctor.Flutter.Commands;
using FlutterBuildDoctor.Flutter.Repairs;

namespace FlutterBuildDoctor.UnitTests.Repairs;

public sealed class FlutterRepairRecipeTests
{
    [Fact]
    public async Task FlutterClean_RejectsWithoutConfirmationThenExecutesTypedCommand()
    {
        var runner = new RecordingRunner(Success());
        var commands = new FlutterCommandService(runner, new FlutterCommandBuilder());
        var recipe = new FlutterCleanRepairRecipe(commands, new RepairVerifier());
        var context = new RepairContext(@"C:\work\app", @"C:\flutter\bin\flutter.bat");

        var rejected = await recipe.ExecuteAsync(context, confirmed: false);
        Assert.Equal(RepairExecutionStatus.Rejected, rejected.Status);
        Assert.Equal(0, runner.CallCount);

        var result = await recipe.ExecuteAsync(context, confirmed: true);
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "clean" }, runner.LastRequest!.Arguments);
    }

    [Fact]
    public async Task DependencyRefresh_IsRiskyAndRunsPubGetAfterConfirmation()
    {
        var runner = new RecordingRunner(Success());
        var commands = new FlutterCommandService(runner, new FlutterCommandBuilder());
        var recipe = new DependencyRefreshRepairRecipe(commands, new RepairVerifier());
        var context = new RepairContext(@"C:\work\app");

        var plan = recipe.Preview(context);
        Assert.Equal(RepairRisk.Risky, plan.Risk);
        Assert.True(plan.RequiresConfirmation);

        var result = await recipe.ExecuteAsync(context, confirmed: true);
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "pub", "get" }, runner.LastRequest!.Arguments);
    }

    private static ProcessResult Success()
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(
            ProcessExecutionStatus.Succeeded,
            0,
            now,
            now.AddMilliseconds(10),
            Array.Empty<ProcessOutputLine>(),
            "flutter");
    }

    private sealed class RecordingRunner : IProcessRunner
    {
        private readonly ProcessResult _result;

        public RecordingRunner(ProcessResult result) => _result = result;
        public int CallCount { get; private set; }
        public ProcessRequest? LastRequest { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }
}
