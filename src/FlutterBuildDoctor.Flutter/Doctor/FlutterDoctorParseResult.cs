using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Doctor;

public enum FlutterDoctorParseStatus
{
    Parsed = 0,
    NoProcessEvidence,
    NoSections
}

public enum FlutterDoctorSectionKind
{
    Flutter = 0,
    AndroidToolchain,
    AndroidStudio,
    ConnectedDevice,
    WindowsVersion,
    Chrome,
    VisualStudio,
    VsCode,
    NetworkResources,
    Unknown
}

public enum FlutterDoctorSectionStatus
{
    Passed = 0,
    Warning,
    Failed,
    Unknown
}

public sealed record FlutterDoctorSection(
    FlutterDoctorSectionKind Kind,
    FlutterDoctorSectionStatus Status,
    string Title,
    ProcessOutputLine Header,
    IReadOnlyList<ProcessOutputLine> Lines)
{
    public bool IsRecognized => Kind != FlutterDoctorSectionKind.Unknown;
}

public sealed record FlutterDoctorParseResult(
    FlutterDoctorParseStatus Status,
    FlutterDoctorExecutionResult Execution,
    IReadOnlyList<FlutterDoctorSection> Sections,
    IReadOnlyList<ProcessOutputLine> UnsectionedLines,
    string Message)
{
    public ProcessResult? ProcessResult => Execution.ProcessResult;

    public bool HasRecognizedSections
        => Sections.Any(section => section.IsRecognized);
}

public interface IFlutterDoctorParser
{
    FlutterDoctorParseResult Parse(FlutterDoctorExecutionResult execution);
}
