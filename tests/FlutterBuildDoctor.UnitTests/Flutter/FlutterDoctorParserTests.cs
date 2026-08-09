using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Doctor;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterDoctorParserTests
{
    private readonly FlutterDoctorParser _parser = new();

    [Fact]
    public void Parse_RecognizesCoreSectionsAndPreservesExactEvidence()
    {
        var preamble = Out("Doctor summary (to see all details, run flutter doctor -v):");
        var flutterHeader = Out("[√] Flutter (Channel stable, 3.44.8, on Microsoft Windows)");
        var flutterDetail = Out("    • Flutter version 3.44.8 at C:\\flutter");
        var androidHeader = Out("[!] Android toolchain - develop for Android devices (Android SDK version 36.0.0)");
        var androidStderr = Err("    ! Some Android licenses not accepted.");
        var studioHeader = Out("[X] Android Studio (not installed)");
        var deviceHeader = Out("[✓] Connected device (2 available)");
        var process = Process(
            ProcessExecutionStatus.Succeeded,
            preamble,
            flutterHeader,
            flutterDetail,
            androidHeader,
            androidStderr,
            studioHeader,
            deviceHeader);
        var execution = Execution(process);

        var result = _parser.Parse(execution);

        Assert.Equal(FlutterDoctorParseStatus.Parsed, result.Status);
        Assert.Same(execution, result.Execution);
        Assert.Same(process, result.ProcessResult);
        Assert.Single(result.UnsectionedLines);
        Assert.Same(preamble, result.UnsectionedLines[0]);
        Assert.Equal(4, result.Sections.Count);

        var flutter = result.Sections[0];
        Assert.Equal(FlutterDoctorSectionKind.Flutter, flutter.Kind);
        Assert.Equal(FlutterDoctorSectionStatus.Passed, flutter.Status);
        Assert.Equal("Flutter (Channel stable, 3.44.8, on Microsoft Windows)", flutter.Title);
        Assert.Same(flutterHeader, flutter.Header);
        Assert.Collection(
            flutter.Lines,
            line => Assert.Same(flutterHeader, line),
            line => Assert.Same(flutterDetail, line));

        var android = result.Sections[1];
        Assert.Equal(FlutterDoctorSectionKind.AndroidToolchain, android.Kind);
        Assert.Equal(FlutterDoctorSectionStatus.Warning, android.Status);
        Assert.Collection(
            android.Lines,
            line => Assert.Same(androidHeader, line),
            line => Assert.Same(androidStderr, line));

        Assert.Equal(FlutterDoctorSectionKind.AndroidStudio, result.Sections[2].Kind);
        Assert.Equal(FlutterDoctorSectionStatus.Failed, result.Sections[2].Status);
        Assert.Equal(FlutterDoctorSectionKind.ConnectedDevice, result.Sections[3].Kind);
        Assert.Equal(FlutterDoctorSectionStatus.Passed, result.Sections[3].Status);
        Assert.True(result.HasRecognizedSections);
    }

    [Fact]
    public void Parse_RecognizesAdditionalKnownSectionsCaseInsensitively()
    {
        var execution = Execution(Process(
            ProcessExecutionStatus.Succeeded,
            Out("  [✓] Windows Version (11 Pro 64-bit)"),
            Out("[✓] Chrome - develop for the web"),
            Out("[!] Visual Studio - develop Windows apps"),
            Out("[✓] VS Code (version 1.99.0)"),
            Out("[✓] Network resources")));

        var result = _parser.Parse(execution);

        Assert.Equal(
            new[]
            {
                FlutterDoctorSectionKind.WindowsVersion,
                FlutterDoctorSectionKind.Chrome,
                FlutterDoctorSectionKind.VisualStudio,
                FlutterDoctorSectionKind.VsCode,
                FlutterDoctorSectionKind.NetworkResources
            },
            result.Sections.Select(section => section.Kind));
    }

    [Fact]
    public void Parse_UnknownSectionIsRetainedAndDoesNotCorruptFollowingKnownSection()
    {
        var unknownHeader = Out("[?] Future Flutter capability");
        var unknownDetail = Out("    • future detail");
        var flutterHeader = Out("[✓] Flutter (Channel stable)");
        var execution = Execution(Process(
            ProcessExecutionStatus.Succeeded,
            unknownHeader,
            unknownDetail,
            flutterHeader));

        var result = _parser.Parse(execution);

        Assert.Equal(2, result.Sections.Count);
        Assert.Equal(FlutterDoctorSectionKind.Unknown, result.Sections[0].Kind);
        Assert.Equal(FlutterDoctorSectionStatus.Unknown, result.Sections[0].Status);
        Assert.False(result.Sections[0].IsRecognized);
        Assert.Collection(
            result.Sections[0].Lines,
            line => Assert.Same(unknownHeader, line),
            line => Assert.Same(unknownDetail, line));
        Assert.Equal(FlutterDoctorSectionKind.Flutter, result.Sections[1].Kind);
    }

    [Fact]
    public void Parse_StderrBracketTextDoesNotStartASection()
    {
        var stderr = Err("[X] transport warning from stderr");
        var flutterHeader = Out("[✓] Flutter (Channel stable)");
        var execution = Execution(Process(
            ProcessExecutionStatus.Succeeded,
            stderr,
            flutterHeader));

        var result = _parser.Parse(execution);

        Assert.Single(result.UnsectionedLines);
        Assert.Same(stderr, result.UnsectionedLines[0]);
        Assert.Single(result.Sections);
        Assert.Equal(FlutterDoctorSectionKind.Flutter, result.Sections[0].Kind);
    }

    [Fact]
    public void Parse_NoProcessEvidenceReturnsExplicitStatus()
    {
        var execution = new FlutterDoctorExecutionResult(
            FlutterDoctorExecutionStatus.FlutterUnavailable,
            null,
            "Flutter unavailable.");

        var result = _parser.Parse(execution);

        Assert.Equal(FlutterDoctorParseStatus.NoProcessEvidence, result.Status);
        Assert.Empty(result.Sections);
        Assert.Empty(result.UnsectionedLines);
        Assert.Same(execution, result.Execution);
    }

    [Fact]
    public void Parse_NoSectionHeadersPreservesAllRawLinesAsUnsectioned()
    {
        var first = Out("Doctor output without a section header");
        var second = Err("diagnostic stderr");
        var process = Process(ProcessExecutionStatus.Succeeded, first, second);

        var result = _parser.Parse(Execution(process));

        Assert.Equal(FlutterDoctorParseStatus.NoSections, result.Status);
        Assert.Empty(result.Sections);
        Assert.Collection(
            result.UnsectionedLines,
            line => Assert.Same(first, line),
            line => Assert.Same(second, line));
        Assert.Same(process, result.ProcessResult);
    }

    [Fact]
    public void Parse_FailedExecutionWithEvidenceStillParsesAvailableSections()
    {
        var process = Process(
            ProcessExecutionStatus.Failed,
            Out("[X] Android toolchain - develop for Android devices"),
            Err("doctor terminated with an error"));
        var execution = new FlutterDoctorExecutionResult(
            FlutterDoctorExecutionStatus.Failed,
            @"C:\\flutter\\bin\\flutter.bat",
            "flutter doctor failed.",
            process);

        var result = _parser.Parse(execution);

        Assert.Equal(FlutterDoctorParseStatus.Parsed, result.Status);
        Assert.Single(result.Sections);
        Assert.Equal(FlutterDoctorSectionKind.AndroidToolchain, result.Sections[0].Kind);
        Assert.Equal(FlutterDoctorSectionStatus.Failed, result.Sections[0].Status);
        Assert.Same(process, result.ProcessResult);
    }

    [Fact]
    public void Parse_MalformedBracketLineDoesNotThrowOrInventSection()
    {
        var malformed = Out("[!]   ");
        var process = Process(ProcessExecutionStatus.Succeeded, malformed);

        var result = _parser.Parse(Execution(process));

        Assert.Equal(FlutterDoctorParseStatus.NoSections, result.Status);
        Assert.Single(result.UnsectionedLines);
        Assert.Same(malformed, result.UnsectionedLines[0]);
    }

    private static FlutterDoctorExecutionResult Execution(ProcessResult process)
        => new(
            process.IsSuccess ? FlutterDoctorExecutionStatus.Succeeded : FlutterDoctorExecutionStatus.Failed,
            @"C:\\flutter\\bin\\flutter.bat",
            "test execution",
            process);

    private static ProcessResult Process(ProcessExecutionStatus status, params ProcessOutputLine[] output)
    {
        var started = DateTimeOffset.UtcNow;
        return new ProcessResult(
            status,
            status == ProcessExecutionStatus.Succeeded ? 0 : 1,
            started,
            started.AddMilliseconds(10),
            output,
            "flutter doctor -v",
            status == ProcessExecutionStatus.Succeeded ? null : "test failure");
    }

    private static ProcessOutputLine Out(string text)
        => new(DateTimeOffset.UtcNow, ProcessStream.StdOut, text);

    private static ProcessOutputLine Err(string text)
        => new(DateTimeOffset.UtcNow, ProcessStream.StdErr, text);
}
