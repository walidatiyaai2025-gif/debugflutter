using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Doctor;

public sealed record FlutterDoctorProbeResult(
    ProcessExecutionStatus Status,
    FlutterDoctorReport Report,
    ProcessResult ProcessResult)
{
    public bool IsSuccess => Status == ProcessExecutionStatus.Succeeded;
}

public interface IFlutterDoctorProbe
{
    Task<FlutterDoctorProbeResult> ProbeAsync(
        string flutterExecutable,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);
}

public sealed class FlutterDoctorProbe : IFlutterDoctorProbe
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
    private readonly IProcessRunner _processRunner;
    private readonly IFlutterDoctorParser _parser;

    public FlutterDoctorProbe(IProcessRunner processRunner, IFlutterDoctorParser parser)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public async Task<FlutterDoctorProbeResult> ProbeAsync(
        string flutterExecutable,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flutterExecutable);

        var request = new ProcessRequest(
            flutterExecutable,
            new[] { "doctor", "-v" },
            workingDirectory,
            Timeout: DefaultTimeout,
            DisplayName: "flutter doctor -v");

        var result = await _processRunner.RunAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
        var rawOutput = string.Join(Environment.NewLine, result.Output.Select(static line => line.Text));
        var report = _parser.Parse(rawOutput);
        return new FlutterDoctorProbeResult(result.Status, report, result);
    }
}
