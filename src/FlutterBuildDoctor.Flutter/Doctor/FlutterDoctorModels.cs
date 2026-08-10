namespace FlutterBuildDoctor.Flutter.Doctor;

public enum FlutterDoctorSectionStatus
{
    Ready = 0,
    Warning,
    Error,
    Unavailable,
    Unknown
}

public enum FlutterDoctorComponent
{
    Unknown = 0,
    Flutter,
    Windows,
    AndroidToolchain,
    Chrome,
    VisualStudio,
    AndroidStudio,
    VsCode,
    ConnectedDevice,
    NetworkResources
}

public sealed record FlutterDoctorSection(
    string Marker,
    FlutterDoctorSectionStatus Status,
    FlutterDoctorComponent Component,
    string Header,
    IReadOnlyList<string> EvidenceLines,
    IReadOnlyList<string> RawLines)
{
    public string RawText => string.Join(Environment.NewLine, RawLines);
}

public sealed record FlutterDoctorReport(
    string RawOutput,
    IReadOnlyList<FlutterDoctorSection> Sections,
    IReadOnlyList<string> UnsectionedLines)
{
    public int ReadyCount => Sections.Count(static section => section.Status == FlutterDoctorSectionStatus.Ready);
    public int WarningCount => Sections.Count(static section => section.Status == FlutterDoctorSectionStatus.Warning);
    public int ErrorCount => Sections.Count(static section => section.Status == FlutterDoctorSectionStatus.Error);
    public int UnavailableCount => Sections.Count(static section => section.Status == FlutterDoctorSectionStatus.Unavailable);
    public int UnknownCount => Sections.Count(static section => section.Status == FlutterDoctorSectionStatus.Unknown);
    public bool HasErrors => ErrorCount > 0;
}

public interface IFlutterDoctorParser
{
    FlutterDoctorReport Parse(string? output);
}
