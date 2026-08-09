using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Doctor;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterDoctorUnknownEvidenceTests
{
    private readonly FlutterDoctorParser _parser = new();

    [Fact]
    public void Parse_ExposesUnknownEvidenceInOriginalOutputOrder()
    {
        var preamble = Out("Doctor summary:");
        var malformedBefore = Out("[!]   ");
        var unknownHeader = Out("[?] Future toolchain capability");
        var unknownDetail = Out("    • future detail remains verbatim  ");
        var flutterHeader = Out("[✓] Flutter (Channel stable)");
        var malformedInsideKnown = Out("[broken-header");
        var process = Process(
            preamble,
            malformedBefore,
            unknownHeader,
            unknownDetail,
            flutterHeader,
            malformedInsideKnown);

        var result = _parser.Parse(Execution(process));

        Assert.True(result.HasUnknownEvidence);
        Assert.Collection(
            result.UnknownEvidence,
            evidence =>
            {
                Assert.Equal(FlutterDoctorUnknownEvidenceKind.UnclassifiedLine, evidence.Kind);
                Assert.Equal(0, evidence.StartIndex);
                Assert.Single(evidence.Lines);
                Assert.Same(preamble, evidence.Lines[0]);
            },
            evidence =>
            {
                Assert.Equal(FlutterDoctorUnknownEvidenceKind.MalformedSectionHeader, evidence.Kind);
                Assert.Equal(1, evidence.StartIndex);
                Assert.Same(malformedBefore, Assert.Single(evidence.Lines));
            },
            evidence =>
            {
                Assert.Equal(FlutterDoctorUnknownEvidenceKind.UnknownSection, evidence.Kind);
                Assert.Equal(2, evidence.StartIndex);
                Assert.Collection(
                    evidence.Lines,
                    line => Assert.Same(unknownHeader, line),
                    line => Assert.Same(unknownDetail, line));
                Assert.Equal("    • future detail remains verbatim  ", evidence.Lines[1].Text);
            },
            evidence =>
            {
                Assert.Equal(FlutterDoctorUnknownEvidenceKind.MalformedSectionHeader, evidence.Kind);
                Assert.Equal(5, evidence.StartIndex);
                Assert.Same(malformedInsideKnown, Assert.Single(evidence.Lines));
            });

        var knownFlutter = Assert.Single(result.Sections.Where(section => section.Kind == FlutterDoctorSectionKind.Flutter));
        Assert.Collection(
            knownFlutter.Lines,
            line => Assert.Same(flutterHeader, line),
            line => Assert.Same(malformedInsideKnown, line));
        Assert.Same(process, result.ProcessResult);
    }

    [Fact]
    public void Parse_KnownSectionsOnlyHaveNoUnknownEvidence()
    {
        var result = _parser.Parse(Execution(Process(
            Out("[√] Flutter (Channel stable)"),
            Out("    • detail"),
            Out("[!] Android toolchain - develop for Android devices"))));

        Assert.Equal(FlutterDoctorParseStatus.Parsed, result.Status);
        Assert.False(result.HasUnknownEvidence);
        Assert.Empty(result.UnknownEvidence);
    }

    [Fact]
    public void Parse_NoSectionsClassifiesEveryUnsectionedLineWithoutChangingIt()
    {
        var plain = Out("future preamble");
        var stderr = Err("stderr remains raw");
        var malformed = Out("[X]");
        var process = Process(plain, stderr, malformed);

        var result = _parser.Parse(Execution(process));

        Assert.Equal(FlutterDoctorParseStatus.NoSections, result.Status);
        Assert.Equal(3, result.UnsectionedLines.Count);
        Assert.Equal(3, result.UnknownEvidence.Count);
        Assert.Equal(FlutterDoctorUnknownEvidenceKind.UnclassifiedLine, result.UnknownEvidence[0].Kind);
        Assert.Equal(FlutterDoctorUnknownEvidenceKind.UnclassifiedLine, result.UnknownEvidence[1].Kind);
        Assert.Equal(FlutterDoctorUnknownEvidenceKind.MalformedSectionHeader, result.UnknownEvidence[2].Kind);
        Assert.Same(plain, result.UnknownEvidence[0].Lines[0]);
        Assert.Same(stderr, result.UnknownEvidence[1].Lines[0]);
        Assert.Same(malformed, result.UnknownEvidence[2].Lines[0]);
    }

    [Fact]
    public void Parse_NoProcessEvidenceHasNoInventedUnknownEvidence()
    {
        var execution = new FlutterDoctorExecutionResult(
            FlutterDoctorExecutionStatus.FlutterUnavailable,
            null,
            "Flutter unavailable.");

        var result = _parser.Parse(execution);

        Assert.Equal(FlutterDoctorParseStatus.NoProcessEvidence, result.Status);
        Assert.Empty(result.UnknownEvidence);
        Assert.False(result.HasUnknownEvidence);
    }

    private static FlutterDoctorExecutionResult Execution(ProcessResult process)
        => new(
            FlutterDoctorExecutionStatus.Succeeded,
            @"C:\flutter\bin\flutter.bat",
            "test execution",
            process);

    private static ProcessResult Process(params ProcessOutputLine[] output)
    {
        var started = DateTimeOffset.UtcNow;
        return new ProcessResult(
            ProcessExecutionStatus.Succeeded,
            0,
            started,
            started.AddMilliseconds(10),
            output,
            "flutter doctor -v");
    }

    private static ProcessOutputLine Out(string text)
        => new(DateTimeOffset.UtcNow, ProcessStream.StdOut, text);

    private static ProcessOutputLine Err(string text)
        => new(DateTimeOffset.UtcNow, ProcessStream.StdErr, text);
}
