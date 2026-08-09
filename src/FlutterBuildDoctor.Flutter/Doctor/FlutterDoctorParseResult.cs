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

public enum FlutterDoctorUnknownEvidenceKind
{
    UnknownSection = 0,
    MalformedSectionHeader,
    UnclassifiedLine
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

public sealed record FlutterDoctorUnknownEvidence(
    FlutterDoctorUnknownEvidenceKind Kind,
    int StartIndex,
    IReadOnlyList<ProcessOutputLine> Lines,
    string Reason);

public sealed record FlutterDoctorParseResult(
    FlutterDoctorParseStatus Status,
    FlutterDoctorExecutionResult Execution,
    IReadOnlyList<FlutterDoctorSection> Sections,
    IReadOnlyList<ProcessOutputLine> UnsectionedLines,
    string Message)
{
    public ProcessResult? ProcessResult => Execution.ProcessResult;

    public IReadOnlyList<FlutterDoctorUnknownEvidence> UnknownEvidence { get; init; }
        = Array.Empty<FlutterDoctorUnknownEvidence>();

    public IReadOnlyList<FlutterDoctorSection> UnknownSections
        => Sections.Where(section => !section.IsRecognized).ToArray();

    public bool HasRecognizedSections
        => Sections.Any(section => section.IsRecognized);

    public bool HasUnknownEvidence => UnknownEvidence.Count > 0;
}

public interface IFlutterDoctorParser
{
    FlutterDoctorParseResult Parse(FlutterDoctorExecutionResult execution);
}
