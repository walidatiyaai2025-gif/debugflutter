using FlutterBuildDoctor.Flutter.Doctor;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class FlutterDoctorParserTests
{
    private readonly FlutterDoctorParser _parser = new();

    [Fact]
    public void Parse_MapsKnownSectionsStatusesAndEvidence()
    {
        const string output = """
            [✓] Flutter (Channel stable, 3.35.0, on Microsoft Windows 11)
                • Flutter version 3.35.0 on channel stable
            [!] Android toolchain - develop for Android devices (Android SDK version 36.0.0)
                ! Some Android licenses not accepted.
            [✗] Visual Studio - develop Windows apps
                ✗ Visual Studio not installed.
            [-] Chrome - develop for the web
                • Chrome was not checked.
            [✓] Connected device (2 available)
                • Windows (desktop)
            """;

        var result = _parser.Parse(output);

        Assert.Equal(5, result.Sections.Count);
        Assert.Equal(2, result.ReadyCount);
        Assert.Equal(1, result.WarningCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.Equal(1, result.UnavailableCount);
        Assert.True(result.HasErrors);
        Assert.Equal(FlutterDoctorComponent.AndroidToolchain, result.Sections[1].Component);
        Assert.Contains("licenses", result.Sections[1].EvidenceLines.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_PreservesUnknownFutureSectionAndRawEvidence()
    {
        const string output = """
            [?] Experimental Flutter Service (version 1)
                • future evidence A
                • future evidence B
            """;

        var result = _parser.Parse(output);

        var section = Assert.Single(result.Sections);
        Assert.Equal(FlutterDoctorSectionStatus.Unknown, section.Status);
        Assert.Equal(FlutterDoctorComponent.Unknown, section.Component);
        Assert.Contains("Experimental Flutter Service", section.Header, StringComparison.Ordinal);
        Assert.Contains("future evidence A", section.RawText, StringComparison.Ordinal);
        Assert.Contains("future evidence B", section.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_TruncatedOrMalformedOutputReturnsPartialReportWithoutThrowing()
    {
        const string output = "startup noise\r\n[✓] Flutter (Channel stable)\r\n    • first evidence\r\ntruncated tail";

        var result = _parser.Parse(output);

        Assert.Single(result.UnsectionedLines);
        Assert.Equal("startup noise", result.UnsectionedLines[0]);
        var section = Assert.Single(result.Sections);
        Assert.Equal(FlutterDoctorComponent.Flutter, section.Component);
        Assert.Equal(2, section.EvidenceLines.Count);
        Assert.Contains("truncated tail", section.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_EmptyOutputReturnsEmptyReport()
    {
        var result = _parser.Parse(null);

        Assert.Empty(result.Sections);
        Assert.Empty(result.UnsectionedLines);
        Assert.Equal(string.Empty, result.RawOutput);
    }

    [Theory]
    [InlineData("[✓] Android Studio (version 2025.1)", FlutterDoctorComponent.AndroidStudio)]
    [InlineData("[✓] VS Code (version 1.100)", FlutterDoctorComponent.VsCode)]
    [InlineData("[✓] Network resources", FlutterDoctorComponent.NetworkResources)]
    [InlineData("[✓] Windows Version (11 Pro)", FlutterDoctorComponent.Windows)]
    public void Parse_ClassifiesRepresentativeKnownHeaders(string header, FlutterDoctorComponent expected)
    {
        var result = _parser.Parse(header);

        Assert.Equal(expected, Assert.Single(result.Sections).Component);
    }
}
