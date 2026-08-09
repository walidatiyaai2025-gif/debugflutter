using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Doctor;

public sealed class FlutterDoctorParser : IFlutterDoctorParser
{
    public FlutterDoctorParseResult Parse(FlutterDoctorExecutionResult executionResult)
    {
        ArgumentNullException.ThrowIfNull(executionResult);

        var processResult = executionResult.ProcessResult;
        if (processResult is null)
        {
            return new FlutterDoctorParseResult(
                FlutterDoctorParseStatus.NoProcessEvidence,
                Array.Empty<FlutterDoctorSection>(),
                Array.Empty<ProcessOutputLine>(),
                "Flutter doctor process evidence is required before sections can be parsed.");
        }

        var sections = new List<FlutterDoctorSection>();
        PendingSection? pending = null;

        foreach (var line in processResult.Output)
        {
            if (TryReadHeader(line.Text, out var marker, out var title))
            {
                FlushPending(pending, sections);
                pending = null;

                if (TryClassifySection(title, out var kind))
                {
                    pending = new PendingSection(
                        kind,
                        MapStatus(marker),
                        marker,
                        title,
                        new List<ProcessOutputLine> { line });
                }

                continue;
            }

            pending?.Lines.Add(line);
        }

        FlushPending(pending, sections);

        if (sections.Count == 0)
        {
            return new FlutterDoctorParseResult(
                FlutterDoctorParseStatus.NoRecognizedSections,
                Array.Empty<FlutterDoctorSection>(),
                processResult.Output,
                "Flutter doctor output did not contain a recognized section header. Raw process output was preserved.");
        }

        return new FlutterDoctorParseResult(
            FlutterDoctorParseStatus.Succeeded,
            sections,
            processResult.Output,
            $"Parsed {sections.Count} recognized Flutter doctor section(s).");
    }

    private static void FlushPending(
        PendingSection? pending,
        ICollection<FlutterDoctorSection> sections)
    {
        if (pending is null)
            return;

        sections.Add(new FlutterDoctorSection(
            pending.Kind,
            pending.Status,
            pending.Marker,
            pending.Title,
            pending.Lines.ToArray()));
    }

    private static bool TryReadHeader(string? text, out char marker, out string title)
    {
        marker = default;
        title = string.Empty;

        if (string.IsNullOrEmpty(text) || text.Length < 4 || text[0] != '[' || text[2] != ']')
            return false;

        marker = text[1];
        if (char.IsWhiteSpace(marker))
            return false;

        title = text[3..].TrimStart();
        return title.Length > 0;
    }

    private static FlutterDoctorSectionStatus MapStatus(char marker)
        => marker switch
        {
            '✓' => FlutterDoctorSectionStatus.Ready,
            '!' => FlutterDoctorSectionStatus.Warning,
            '✗' => FlutterDoctorSectionStatus.Error,
            '-' => FlutterDoctorSectionStatus.NotApplicable,
            _ => FlutterDoctorSectionStatus.Unknown
        };

    private static bool TryClassifySection(string title, out FlutterDoctorSectionKind kind)
    {
        if (StartsWith(title, "Flutter"))
            kind = FlutterDoctorSectionKind.Flutter;
        else if (StartsWith(title, "Windows Version"))
            kind = FlutterDoctorSectionKind.WindowsVersion;
        else if (StartsWith(title, "Android toolchain"))
            kind = FlutterDoctorSectionKind.AndroidToolchain;
        else if (StartsWith(title, "Chrome"))
            kind = FlutterDoctorSectionKind.Chrome;
        else if (StartsWith(title, "Visual Studio"))
            kind = FlutterDoctorSectionKind.VisualStudio;
        else if (StartsWith(title, "Android Studio"))
            kind = FlutterDoctorSectionKind.AndroidStudio;
        else if (StartsWith(title, "VS Code"))
            kind = FlutterDoctorSectionKind.VsCode;
        else if (StartsWith(title, "Connected device"))
            kind = FlutterDoctorSectionKind.ConnectedDevice;
        else if (StartsWith(title, "Network resources"))
            kind = FlutterDoctorSectionKind.NetworkResources;
        else if (StartsWith(title, "Xcode"))
            kind = FlutterDoctorSectionKind.Xcode;
        else if (StartsWith(title, "Linux toolchain"))
            kind = FlutterDoctorSectionKind.LinuxToolchain;
        else
        {
            kind = default;
            return false;
        }

        return true;
    }

    private static bool StartsWith(string value, string prefix)
        => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private sealed record PendingSection(
        FlutterDoctorSectionKind Kind,
        FlutterDoctorSectionStatus Status,
        char Marker,
        string Title,
        List<ProcessOutputLine> Lines);
}
