using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Doctor;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterDoctorProbeTests
{
    [Fact]
    public async Task ProbeAsync_ExecutesDoctorVerboseAndReturnsParsedReport()
    {
        var now = DateTimeOffset.UtcNow;
        var processResult = new ProcessResult(
            ProcessExecutionStatus.Succeeded,
            0,
            now,
            now.AddMilliseconds(50),
            new[]
            {
                new ProcessOutputLine(now, ProcessStream.StdOut, "[✓] Flutter (Channel stable, 3.35.0)"),
                new ProcessOutputLine(now, ProcessStream.StdOut, "    • Flutter version 3.35.0")
            },
            "flutter doctor -v");
        var runner = new StubProcessRunner(processResult);
        var probe = new FlutterDoctorProbe(runner, new FlutterDoctorParser());

        var result = await probe.ProbeAsync(@"C:\flutter\bin\flutter.bat");

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "doctor", "-v" }, runner.LastRequest!.Arguments);
        Assert.Equal("flutter doctor -v", runner.LastRequest.DisplayName);
        Assert.Equal(FlutterDoctorComponent.Flutter, Assert.Single(result.Report.Sections).Component);
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly ProcessResult _result;

        public StubProcessRunner(ProcessResult result)
        {
            _result = result;
        }

        public ProcessRequest? LastRequest { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }
}
