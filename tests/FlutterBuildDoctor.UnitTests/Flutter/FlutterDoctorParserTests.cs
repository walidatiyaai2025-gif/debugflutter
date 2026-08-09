using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Flutter.Doctor;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterDoctorParserTests
{
    [Fact]
    public void Parse_OfficialStyleOutput_ReturnsTypedRecognizedSectionsAndStatuses()
    {
        var output = Lines(
            "Doctor summary (to see all details, run flutter doctor -v):",
            "[✓] Flutter (Channel stable, 3.44.7, on Microsoft Windows)",
            "    • Flutter version 3.44.7 at C:\\src\\flutter",
            "",
            "[!] Android toolchain - develop for Android devices (Android SDK version 36.0.0)",
            "    ✗ cmdline-tools component is missing",
            "",
            "[✓] Android Studio (version 2025.1)",
            "    • Android Studio at C:\\Program Files\\Android\\Android Studio",
            "",
            "[✓] Connected device (2 available)",
            "    • Windows (desktop) • windows • windows-x64",
            "",
            "[✓] Network resources");
        var execution = Execution(output);

        var result = new FlutterDoctorParser().Parse(execution);

        Assert.True(result.IsSuccess);
        Assert.Equal(FlutterDoctorParseStatus.Succeeded, result.Status);
        Assert.Equal(5, result.Sections.Count);
        Assert.Same(execution.ProcessResult, result.SourceProcessResult);
        Assert.Same(output, result.SourceOutput);

        var flutter = Assert.Single(result.Sections, section => section.Kind == FlutterDoctorSectionKind.Flutter);
        Assert.Equal(FlutterDoctorSectionStatus.Ready, flutter.Status);
        Assert.Equal('✓', flutter.Marker);
        Assert.Contains("Channel stable", flutter.Title, StringComparison.Ordinal);
        Assert.Contains(flutter.SourceLines, line => line.Text.Contains("Flutter version 3.44.7", StringComparison.Ordinal));

        var android = Assert.Single(result.Sections, section => section.Kind == FlutterDoctorSectionKind.AndroidToolchain);
        Assert.Equal(FlutterDoctorSectionStatus.Warning, android.Status);
        Assert.Contains(android.SourceLines, line => line.Text.Contains("cmdline-tools", StringComparison.Ordinal));

        Assert.Equal(
            FlutterDoctorSectionStatus.Ready,
            Assert.Single(result.Sections, section => section.Kind == FlutterDoctorSectionKind.AndroidStudio).Status);
        Assert.Equal(
            FlutterDoctorSectionStatus.Ready,
            Assert.Single(result.Sections, section => section.Kind == FlutterDoctorSectionKind.ConnectedDevice).Status);
        Assert.Equal(
            FlutterDoctorSectionStatus.Ready,
            Assert.Single(result.Sections, section => section.Kind == FlutterDoctorSectionKind.NetworkResources).Status);
    }

    [Fact]
    public void Parse_UnknownHeader_ClosesRecognizedSectionWithoutLosingRawEvidence()
    {
        var output = Lines(
            "[✓] Flutter (Channel stable)",
            "    • known Flutter detail",
            "[✓] Experimental Doctor Plugin",
            "    • unknown plugin detail",
            "[✗] Android toolchain - develop for Android devices",
            "    ✗ Android SDK missing");

        var result = new FlutterDoctorParser().Parse(Execution(output));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Sections.Count);
        Assert.Same(output, result.SourceOutput);

        var flutter = Assert.Single(result.Sections, section => section.Kind == FlutterDoctorSectionKind.Flutter);
        Assert.Equal(2, flutter.SourceLines.Count);
        Assert.DoesNotContain(flutter.SourceLines, line => line.Text.Contains("unknown plugin", StringComparison.Ordinal));

        var android = Assert.Single(result.Sections, section => section.Kind == FlutterDoctorSectionKind.AndroidToolchain);
        Assert.Equal(FlutterDoctorSectionStatus.Error, android.Status);
    }

    [Fact]
    public void Parse_WindowsMarkersLeadingWhitespaceAndStderrHeaders_AreHandledWithoutLosingRawEvidence()
    {
        var output = new[]
        {
            Line(ProcessStream.StdOut, "  [√] Flutter (Channel stable)"),
            Line(ProcessStream.StdOut, "    • Flutter detail"),
            Line(ProcessStream.StdErr, "[✗] Android Studio fake stderr header"),
            Line(ProcessStream.StdErr, "stderr detail"),
            Line(ProcessStream.StdOut, "[X] Android toolchain - develop for Android devices"),
            Line(ProcessStream.StdOut, "    X Android SDK unavailable"),
            Line(ProcessStream.StdOut, "[x] Connected device (probe failed)")
        };
        var execution = Execution(output);

        var result = new FlutterDoctorParser().Parse(execution);

        Assert.True(result.IsSuccess);
        Assert.Same(execution.ProcessResult, result.SourceProcessResult);
        Assert.Same(output, result.SourceOutput);
        Assert.Equal(3, result.Sections.Count);

        var flutter = result.Sections[0];
        Assert.Equal(FlutterDoctorSectionKind.Flutter, flutter.Kind);
        Assert.Equal(FlutterDoctorSectionStatus.Ready, flutter.Status);
        Assert.Equal('√', flutter.Marker);
        Assert.Equal(output[0], flutter.Header);
        Assert.Equal(2, flutter.SourceLines.Count);
        Assert.All(flutter.SourceLines, line => Assert.Equal(ProcessStream.StdOut, line.Stream));

        Assert.Equal(FlutterDoctorSectionKind.AndroidToolchain, result.Sections[1].Kind);
        Assert.Equal(FlutterDoctorSectionStatus.Error, result.Sections[1].Status);
        Assert.Equal(FlutterDoctorSectionKind.ConnectedDevice, result.Sections[2].Kind);
        Assert.Equal(FlutterDoctorSectionStatus.Error, result.Sections[2].Status);
        Assert.DoesNotContain(result.Sections, section => section.Kind == FlutterDoctorSectionKind.AndroidStudio);
        Assert.Contains(result.SourceOutput, line => line.Stream == ProcessStream.StdErr);
    }

    [Fact]
    public void Parse_ProcessFailureWithDoctorEvidence_StillParsesUsefulSections()
    {
        var output = Lines(
            "[!] Flutter (Channel stable)",
            "    ! doctor produced a warning");
        var execution = Execution(
            output,
            FlutterDoctorExecutionStatus.Failed,
            ProcessExecutionStatus.Failed,
            exitCode: 1);

        var result = new FlutterDoctorParser().Parse(execution);

        Assert.True(result.IsSuccess);
        Assert.Same(execution.ProcessResult, result.SourceProcessResult);
        var section = Assert.Single(result.Sections);
        Assert.Equal(FlutterDoctorSectionKind.Flutter, section.Kind);
        Assert.Equal(FlutterDoctorSectionStatus.Warning, section.Status);
        Assert.Same(output, result.SourceOutput);
    }

    [Theory]
    [InlineData('-', FlutterDoctorSectionStatus.NotApplicable)]
    [InlineData('?', FlutterDoctorSectionStatus.Unknown)]
    public void Parse_RecognizedSection_MapsNonReadyMarkers(char marker, FlutterDoctorSectionStatus expectedStatus)
    {
        var output = Lines($"[{marker}] Visual Studio - develop Windows apps");

        var result = new FlutterDoctorParser().Parse(Execution(output));

        Assert.True(result.IsSuccess);
        var section = Assert.Single(result.Sections);
        Assert.Equal(FlutterDoctorSectionKind.VisualStudio, section.Kind);
        Assert.Equal(expectedStatus, section.Status);
    }

    [Fact]
    public void Parse_NoProcessResult_ReturnsExplicitNoEvidenceState()
    {
        var execution = new FlutterDoctorExecutionResult(
            FlutterDoctorExecutionStatus.FlutterUnavailable,
            null,
            "Flutter is unavailable.");

        var result = new FlutterDoctorParser().Parse(execution);

        Assert.False(result.IsSuccess);
        Assert.Equal(FlutterDoctorParseStatus.NoProcessEvidence, result.Status);
        Assert.Empty(result.Sections);
        Assert.Null(result.SourceProcessResult);
        Assert.Empty(result.SourceOutput);
    }

    [Fact]
    public void Parse_NoRecognizedHeaders_PreservesEntireSourceOutput()
    {
        var output = Lines(
            "Doctor summary",
            "[✓] Future Toolchain",
            "    • future detail");
        var execution = Execution(output);

        var result = new FlutterDoctorParser().Parse(execution);

        Assert.False(result.IsSuccess);
        Assert.Equal(FlutterDoctorParseStatus.NoRecognizedSections, result.Status);
        Assert.Empty(result.Sections);
        Assert.Same(execution.ProcessResult, result.SourceProcessResult);
        Assert.Same(output, result.SourceOutput);
    }

    private static FlutterDoctorExecutionResult Execution(
        IReadOnlyList<ProcessOutputLine> output,
        FlutterDoctorExecutionStatus executionStatus = FlutterDoctorExecutionStatus.Succeeded,
        ProcessExecutionStatus processStatus = ProcessExecutionStatus.Succeeded,
        int? exitCode = 0)
        => new(
            executionStatus,
            @"C:\\flutter\\bin\\flutter.bat",
            "Doctor execution evidence.",
            new ProcessResult(
                processStatus,
                exitCode,
                DateTimeOffset.UtcNow.AddSeconds(-1),
                DateTimeOffset.UtcNow,
                output,
                "flutter doctor -v"));

    private static ProcessOutputLine[] Lines(params string[] values)
        => values
            .Select((text, index) => new ProcessOutputLine(
                DateTimeOffset.UnixEpoch.AddMilliseconds(index),
                ProcessStream.StdOut,
                text))
            .ToArray();

    private static ProcessOutputLine Line(ProcessStream stream, string text)
        => new(DateTimeOffset.UnixEpoch, stream, text);
}
