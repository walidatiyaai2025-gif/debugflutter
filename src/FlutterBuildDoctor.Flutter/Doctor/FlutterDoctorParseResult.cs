using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Doctor;

public enum FlutterDoctorParseStatus
{
    Succeeded = 0,
    NoProcessEvidence,
    NoRecognizedSections
}

public enum FlutterDoctorSectionKind
{
    Flutter = 0,
    WindowsVersion,
    AndroidToolchain,
    Chrome,
    VisualStudio,
    AndroidStudio,
    VsCode,
    ConnectedDevice,
    NetworkResources,
    Xcode,
    LinuxToolchain
}

public enum FlutterDoctorSectionStatus
{
    Ready = 0,
    Warning,
    Error,
    NotApplicable,
    Unknown
}

public sealed record FlutterDoctorSection(
    FlutterDoctorSectionKind Kind,
    FlutterDoctorSectionStatus Status,
    char Marker,
    string Title,
    IReadOnlyList<ProcessOutputLine> SourceLines)
{
    public ProcessOutputLine Header => SourceLines[0];
}

public sealed record FlutterDoctorParseResult(
    FlutterDoctorParseStatus Status,
    IReadOnlyList<FlutterDoctorSection> Sections,
    ProcessResult? SourceProcessResult,
    string Message)
{
    public bool IsSuccess => Status == FlutterDoctorParseStatus.Succeeded;

    public IReadOnlyList<ProcessOutputLine> SourceOutput
        => SourceProcessResult?.Output ?? Array.Empty<ProcessOutputLine>();
}

public interface IFlutterDoctorParser
{
    FlutterDoctorParseResult Parse(FlutterDoctorExecutionResult executionResult);
}
