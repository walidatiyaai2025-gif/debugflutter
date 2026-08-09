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
                null,
                "Flutter doctor process evidence is required before sections can be parsed.");
        }

        var sections = new List<FlutterDoctorSection>();
        PendingSection? pending = null;

        foreach (var line in processResult.Output)
        {
            if (line.Stream == ProcessStream.StdOut &&
                TryReadHeader(line.Text, out var marker, out var title))
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

            if (pending is not null && line.Stream == ProcessStream.StdOut)
                pending.Lines.Add(line);
        }

        FlushPending(pending, sections);

        if (sections.Count == 0)
        {
            return new FlutterDoctorParseResult(
                FlutterDoctorParseStatus.NoRecognizedSections,
                Array.Empty<FlutterDoctorSection>(),
                processResult,
                "Flutter doctor output did not contain a recognized section header. Raw process evidence was preserved.");
        }

        return new FlutterDoctorParseResult(
            FlutterDoctorParseStatus.Succeeded,
            sections,
            processResult,
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

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.Length < 4 || trimmed[0] != '[' || trimmed[2] != ']')
            return false;

        marker = trimmed[1];
        if (char.IsWhiteSpace(marker))
            return false;

        title = trimmed[3..].TrimStart().ToString();
        return title.Length > 0;
    }

    private static FlutterDoctorSectionStatus MapStatus(char marker)
        => marker switch
        {
            '✓' or '√' => FlutterDoctorSectionStatus.Ready,
            '!' => FlutterDoctorSectionStatus.Warning,
            '✗' or 'X' or 'x' => FlutterDoctorSectionStatus.Error,
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
