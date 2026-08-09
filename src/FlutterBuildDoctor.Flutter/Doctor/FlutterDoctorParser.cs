using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Doctor;

public sealed class FlutterDoctorParser : IFlutterDoctorParser
{
    public FlutterDoctorParseResult Parse(FlutterDoctorExecutionResult execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        if (execution.ProcessResult is not { } processResult)
        {
            return new FlutterDoctorParseResult(
                FlutterDoctorParseStatus.NoProcessEvidence,
                execution,
                Array.Empty<FlutterDoctorSection>(),
                Array.Empty<ProcessOutputLine>(),
                "Flutter doctor process evidence is unavailable.");
        }

        var sections = new List<FlutterDoctorSection>();
        var unsectioned = new List<ProcessOutputLine>();
        SectionBuilder? current = null;

        foreach (var line in processResult.Output)
        {
            if (line.Stream == ProcessStream.StdOut && TryParseHeader(line, out var header))
            {
                if (current is not null)
                {
                    sections.Add(current.Build());
                }

                current = new SectionBuilder(header.Kind, header.Status, header.Title, line);
                continue;
            }

            if (current is null)
            {
                unsectioned.Add(line);
            }
            else
            {
                current.Lines.Add(line);
            }
        }

        if (current is not null)
        {
            sections.Add(current.Build());
        }

        if (sections.Count == 0)
        {
            return new FlutterDoctorParseResult(
                FlutterDoctorParseStatus.NoSections,
                execution,
                Array.Empty<FlutterDoctorSection>(),
                unsectioned.ToArray(),
                "No Flutter doctor section headers were found. Raw process evidence was preserved.");
        }

        return new FlutterDoctorParseResult(
            FlutterDoctorParseStatus.Parsed,
            execution,
            sections.ToArray(),
            unsectioned.ToArray(),
            $"Parsed {sections.Count} Flutter doctor section(s). Raw process evidence was preserved.");
    }

    private static bool TryParseHeader(ProcessOutputLine line, out ParsedHeader header)
    {
        var candidate = line.Text.TrimStart();
        if (candidate.Length < 4 || candidate[0] != '[')
        {
            header = default;
            return false;
        }

        var closingBracket = candidate.IndexOf(']');
        if (closingBracket <= 1 || closingBracket >= candidate.Length - 1)
        {
            header = default;
            return false;
        }

        var marker = candidate[1..closingBracket].Trim();
        var title = candidate[(closingBracket + 1)..].Trim();
        if (marker.Length == 0 || title.Length == 0)
        {
            header = default;
            return false;
        }

        header = new ParsedHeader(
            ClassifyKind(title),
            ClassifyStatus(marker),
            title);
        return true;
    }

    private static FlutterDoctorSectionKind ClassifyKind(string title)
    {
        if (title.StartsWith("Flutter", StringComparison.OrdinalIgnoreCase))
            return FlutterDoctorSectionKind.Flutter;
        if (title.StartsWith("Android toolchain", StringComparison.OrdinalIgnoreCase))
            return FlutterDoctorSectionKind.AndroidToolchain;
        if (title.StartsWith("Android Studio", StringComparison.OrdinalIgnoreCase))
            return FlutterDoctorSectionKind.AndroidStudio;
        if (title.StartsWith("Connected device", StringComparison.OrdinalIgnoreCase) ||
            title.StartsWith("Connected devices", StringComparison.OrdinalIgnoreCase))
            return FlutterDoctorSectionKind.ConnectedDevice;
        if (title.StartsWith("Windows Version", StringComparison.OrdinalIgnoreCase))
            return FlutterDoctorSectionKind.WindowsVersion;
        if (title.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase))
            return FlutterDoctorSectionKind.Chrome;
        if (title.StartsWith("Visual Studio", StringComparison.OrdinalIgnoreCase))
            return FlutterDoctorSectionKind.VisualStudio;
        if (title.StartsWith("VS Code", StringComparison.OrdinalIgnoreCase))
            return FlutterDoctorSectionKind.VsCode;
        if (title.StartsWith("Network resources", StringComparison.OrdinalIgnoreCase))
            return FlutterDoctorSectionKind.NetworkResources;

        return FlutterDoctorSectionKind.Unknown;
    }

    private static FlutterDoctorSectionStatus ClassifyStatus(string marker)
        => marker switch
        {
            "✓" or "√" => FlutterDoctorSectionStatus.Passed,
            "!" => FlutterDoctorSectionStatus.Warning,
            "X" or "x" or "✗" or "×" => FlutterDoctorSectionStatus.Failed,
            _ => FlutterDoctorSectionStatus.Unknown
        };

    private readonly record struct ParsedHeader(
        FlutterDoctorSectionKind Kind,
        FlutterDoctorSectionStatus Status,
        string Title);

    private sealed class SectionBuilder
    {
        public SectionBuilder(
            FlutterDoctorSectionKind kind,
            FlutterDoctorSectionStatus status,
            string title,
            ProcessOutputLine header)
        {
            Kind = kind;
            Status = status;
            Title = title;
            Header = header;
            Lines = new List<ProcessOutputLine> { header };
        }

        public FlutterDoctorSectionKind Kind { get; }
        public FlutterDoctorSectionStatus Status { get; }
        public string Title { get; }
        public ProcessOutputLine Header { get; }
        public List<ProcessOutputLine> Lines { get; }

        public FlutterDoctorSection Build()
            => new(Kind, Status, Title, Header, Lines.ToArray());
    }
}
