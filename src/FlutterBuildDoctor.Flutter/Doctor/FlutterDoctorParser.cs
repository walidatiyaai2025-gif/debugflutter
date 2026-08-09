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
        var unknownEvidence = new List<FlutterDoctorUnknownEvidence>();
        SectionBuilder? current = null;

        for (var index = 0; index < processResult.Output.Count; index++)
        {
            var line = processResult.Output[index];

            if (line.Stream == ProcessStream.StdOut && TryParseHeader(line, out var header))
            {
                FlushCurrent(current, sections, unknownEvidence);
                current = new SectionBuilder(header.Kind, header.Status, header.Title, line, index);
                continue;
            }

            if (line.Stream == ProcessStream.StdOut && LooksLikeMalformedHeader(line.Text))
            {
                unknownEvidence.Add(new FlutterDoctorUnknownEvidence(
                    FlutterDoctorUnknownEvidenceKind.MalformedSectionHeader,
                    index,
                    new[] { line },
                    "The line resembles a Flutter Doctor section header but could not be parsed."));
            }
            else if (line.Stream == ProcessStream.StdErr)
            {
                unknownEvidence.Add(new FlutterDoctorUnknownEvidence(
                    FlutterDoctorUnknownEvidenceKind.UnclassifiedLine,
                    index,
                    new[] { line },
                    "stderr is preserved as unclassified evidence and is never interpreted as a section header."));
            }

            if (current is null)
            {
                unsectioned.Add(line);

                if (line.Stream == ProcessStream.StdOut && !LooksLikeMalformedHeader(line.Text))
                {
                    unknownEvidence.Add(new FlutterDoctorUnknownEvidence(
                        FlutterDoctorUnknownEvidenceKind.UnclassifiedLine,
                        index,
                        new[] { line },
                        "The line occurred outside every Flutter Doctor section."));
                }
            }
            else
            {
                // FBD-503 deliberately preserves FBD-502 section membership. A line can therefore
                // remain contextual section evidence while also being explicitly surfaced as unknown.
                current.Lines.Add(line);
            }
        }

        FlushCurrent(current, sections, unknownEvidence);

        if (sections.Count == 0)
        {
            return new FlutterDoctorParseResult(
                FlutterDoctorParseStatus.NoSections,
                execution,
                Array.Empty<FlutterDoctorSection>(),
                unsectioned.ToArray(),
                "No Flutter doctor section headers were found. Raw process evidence was preserved.")
            {
                UnknownEvidence = unknownEvidence.ToArray()
            };
        }

        return new FlutterDoctorParseResult(
            FlutterDoctorParseStatus.Parsed,
            execution,
            sections.ToArray(),
            unsectioned.ToArray(),
            $"Parsed {sections.Count} Flutter doctor section(s). Raw and unknown evidence was preserved.")
        {
            UnknownEvidence = unknownEvidence.ToArray()
        };
    }

    private static void FlushCurrent(
        SectionBuilder? current,
        ICollection<FlutterDoctorSection> sections,
        ICollection<FlutterDoctorUnknownEvidence> unknownEvidence)
    {
        if (current is null)
            return;

        var section = current.Build();
        sections.Add(section);

        if (!section.IsRecognized)
        {
            unknownEvidence.Add(new FlutterDoctorUnknownEvidence(
                FlutterDoctorUnknownEvidenceKind.UnknownSection,
                current.StartIndex,
                section.Lines,
                $"Unrecognized Flutter Doctor section: {section.Title}"));
        }
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

    private static bool LooksLikeMalformedHeader(string text)
    {
        var candidate = text.TrimStart();
        return candidate.StartsWith("[", StringComparison.Ordinal) &&
               !TryParseHeader(
                   new ProcessOutputLine(DateTimeOffset.UnixEpoch, ProcessStream.StdOut, text),
                   out _);
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
            ProcessOutputLine header,
            int startIndex)
        {
            Kind = kind;
            Status = status;
            Title = title;
            Header = header;
            StartIndex = startIndex;
            Lines = new List<ProcessOutputLine> { header };
        }

        public FlutterDoctorSectionKind Kind { get; }
        public FlutterDoctorSectionStatus Status { get; }
        public string Title { get; }
        public ProcessOutputLine Header { get; }
        public int StartIndex { get; }
        public List<ProcessOutputLine> Lines { get; }

        public FlutterDoctorSection Build()
            => new(Kind, Status, Title, Header, Lines.ToArray());
    }
}
