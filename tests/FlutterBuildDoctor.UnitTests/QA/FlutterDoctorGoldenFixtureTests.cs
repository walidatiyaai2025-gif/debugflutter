using FlutterBuildDoctor.Flutter.Doctor;

namespace FlutterBuildDoctor.UnitTests.QA;

public sealed class FlutterDoctorGoldenFixtureTests
{
    private readonly FlutterDoctorParser _parser = new();

    [Theory]
    [InlineData("doctor-stable-3.24-windows.txt", 5, 1, 0)]
    [InlineData("doctor-stable-3.35-windows.txt", 7, 0, 0)]
    [InlineData("doctor-future-unknown-section.txt", 3, 0, 1)]
    public void GoldenFixture_ParsesWithoutDroppingKnownOrFutureSections(
        string fixture,
        int expectedSections,
        int expectedWarnings,
        int expectedUnknown)
    {
        var text = ReadEmbedded("Fixtures.FlutterDoctor." + fixture);

        var report = _parser.Parse(text);

        Assert.Equal(expectedSections, report.Sections.Count);
        Assert.Equal(expectedWarnings, report.WarningCount);
        Assert.Equal(expectedUnknown, report.UnknownCount);
        Assert.Equal(text, report.RawOutput);
        Assert.All(report.Sections, section => Assert.NotEmpty(section.RawLines));
    }

    [Fact]
    public void FutureFixture_PreservesUnknownSectionAndUnsectionedNoiseVerbatim()
    {
        var report = _parser.Parse(ReadEmbedded("Fixtures.FlutterDoctor.doctor-future-unknown-section.txt"));

        Assert.Contains("bootstrap noise retained for evidence", report.UnsectionedLines);
        var unknown = Assert.Single(report.Sections.Where(section => section.Status == FlutterDoctorSectionStatus.Unknown));
        Assert.Equal(FlutterDoctorComponent.Unknown, unknown.Component);
        Assert.Contains("broker=v2", unknown.RawText, StringComparison.Ordinal);
        Assert.Contains("transport=quic", unknown.RawText, StringComparison.Ordinal);
    }

    private static string ReadEmbedded(string suffix)
    {
        var assembly = typeof(FlutterDoctorGoldenFixtureTests).Assembly;
        var name = Assert.Single(assembly.GetManifestResourceNames().Where(resource => resource.EndsWith(suffix, StringComparison.Ordinal)));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
