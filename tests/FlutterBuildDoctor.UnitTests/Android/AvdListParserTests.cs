using FlutterBuildDoctor.Android.Devices;

namespace FlutterBuildDoctor.UnitTests.Android;

public sealed class AvdListParserTests
{
    [Fact]
    public void Parse_ReturnsDistinctAvdNamesAndIgnoresToolNoise()
    {
        const string output = "Pixel_8_API_35\r\nPixel_9_API_36\r\nWARNING | cache note\r\nPixel_8_API_35\r\n";

        var avds = new AvdListParser().Parse(output);

        Assert.Equal(new[] { "Pixel_8_API_35", "Pixel_9_API_36" }, avds.Select(avd => avd.Name));
    }
}
