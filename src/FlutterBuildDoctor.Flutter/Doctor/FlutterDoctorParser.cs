namespace FlutterBuildDoctor.Flutter.Doctor;

public sealed class FlutterDoctorParser : IFlutterDoctorParser
{
    public FlutterDoctorReport Parse(string? output)
    {
        var rawOutput = output ?? string.Empty;
        var lines = NormalizeLines(rawOutput);
        var sections = new List<FlutterDoctorSection>();
        var unsectioned = new List<string>();
        SectionBuilder? current = null;

        foreach (var line in lines)
        {
            if (TryParseHeader(line, out var marker, out var status, out var header))
            {
                if (current is not null)
                {
                    sections.Add(current.Build());
                }

                current = new SectionBuilder(marker, status, Classify(header), header, line);
                continue;
            }

            if (current is null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    unsectioned.Add(line);
                }

                continue;
            }

            current.AddEvidence(line);
        }

        if (current is not null)
        {
            sections.Add(current.Build());
        }

        return new FlutterDoctorReport(rawOutput, sections, unsectioned);
    }

    private static IReadOnlyList<string> NormalizeLines(string output)
    {
        if (output.Length == 0)
        {
            return Array.Empty<string>();
        }

        var normalized = output.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
        {
            return lines[..^1];
        }

        return lines;
    }

    private static bool TryParseHeader(
        string line,
        out string marker,
        out FlutterDoctorSectionStatus status,
        out string header)
    {
        marker = string.Empty;
        status = FlutterDoctorSectionStatus.Unknown;
        header = string.Empty;

        var trimmed = line.TrimStart();
        if (trimmed.Length < 4 || trimmed[0] != '[')
        {
            return false;
        }

        var closingBracket = trimmed.IndexOf(']');
        if (closingBracket is < 2 or > 3)
        {
            return false;
        }

        marker = trimmed[1..closingBracket].Trim();
        header = trimmed[(closingBracket + 1)..].Trim();
        if (marker.Length == 0 || header.Length == 0)
        {
            return false;
        }

        status = marker switch
        {
            "✓" or "√" => FlutterDoctorSectionStatus.Ready,
            "!" => FlutterDoctorSectionStatus.Warning,
            "✗" or "x" or "X" => FlutterDoctorSectionStatus.Error,
            "-" => FlutterDoctorSectionStatus.Unavailable,
            _ => FlutterDoctorSectionStatus.Unknown
        };

        return true;
    }

    private static FlutterDoctorComponent Classify(string header)
    {
        if (header.StartsWith("Android toolchain", StringComparison.OrdinalIgnoreCase))
        {
            return FlutterDoctorComponent.AndroidToolchain;
        }

        if (header.StartsWith("Android Studio", StringComparison.OrdinalIgnoreCase))
        {
            return FlutterDoctorComponent.AndroidStudio;
        }

        if (header.StartsWith("Visual Studio", StringComparison.OrdinalIgnoreCase))
        {
            return FlutterDoctorComponent.VisualStudio;
        }

        if (header.StartsWith("VS Code", StringComparison.OrdinalIgnoreCase))
        {
            return FlutterDoctorComponent.VsCode;
        }

        if (header.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return FlutterDoctorComponent.Windows;
        }

        if (header.StartsWith("Chrome", StringComparison.OrdinalIgnoreCase))
        {
            return FlutterDoctorComponent.Chrome;
        }

        if (header.StartsWith("Connected device", StringComparison.OrdinalIgnoreCase))
        {
            return FlutterDoctorComponent.ConnectedDevice;
        }

        if (header.StartsWith("Network resources", StringComparison.OrdinalIgnoreCase))
        {
            return FlutterDoctorComponent.NetworkResources;
        }

        if (header.StartsWith("Flutter", StringComparison.OrdinalIgnoreCase))
        {
            return FlutterDoctorComponent.Flutter;
        }

        return FlutterDoctorComponent.Unknown;
    }

    private sealed class SectionBuilder
    {
        private readonly List<string> _evidence = new();
        private readonly List<string> _rawLines;

        public SectionBuilder(
            string marker,
            FlutterDoctorSectionStatus status,
            FlutterDoctorComponent component,
            string header,
            string rawHeader)
        {
            Marker = marker;
            Status = status;
            Component = component;
            Header = header;
            _rawLines = new List<string> { rawHeader };
        }

        private string Marker { get; }
        private FlutterDoctorSectionStatus Status { get; }
        private FlutterDoctorComponent Component { get; }
        private string Header { get; }

        public void AddEvidence(string line)
        {
            _rawLines.Add(line);
            if (!string.IsNullOrWhiteSpace(line))
            {
                _evidence.Add(line.Trim());
            }
        }

        public FlutterDoctorSection Build()
            => new(Marker, Status, Component, Header, _evidence.ToArray(), _rawLines.ToArray());
    }
}
