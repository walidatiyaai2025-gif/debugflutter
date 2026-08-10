using FlutterBuildDoctor.Android.Devices;

namespace FlutterBuildDoctor.UnitTests.Android;

public sealed class AdbDevicesParserTests
{
    [Fact]
    public void Parse_MapsOnlineOfflineUnauthorizedAndMetadata()
    {
        const string output = """
            List of devices attached
            emulator-5554 device product:sdk_gphone64_x86_64 model:sdk_gphone64_x86_64 device:emu64xa transport_id:1
            R58M123456 offline product:dreamlte model:SM_G950F device:dreamlte transport_id:2
            ABCDEF unauthorized usb:1-3 transport_id:3
            """;
        var parser = new AdbDevicesParser();

        var devices = parser.Parse(output);

        Assert.Equal(3, devices.Count);
        Assert.Equal(AndroidDeviceState.Online, devices[0].State);
        Assert.Equal("sdk_gphone64_x86_64", devices[0].Model);
        Assert.Equal(AndroidDeviceState.Offline, devices[1].State);
        Assert.Equal(AndroidDeviceState.Unauthorized, devices[2].State);
        Assert.Equal("3", devices[2].TransportId);
    }

    [Fact]
    public void MetadataProjector_DetectsEmulatorAndHumanizesModel()
    {
        var device = Assert.Single(new AdbDevicesParser().Parse(
            "emulator-5554 device product:sdk_gphone64_x86_64 model:Pixel_9_Pro device:emu64xa transport_id:4"));

        var metadata = new AndroidDeviceMetadataProjector().Project(device);

        Assert.True(metadata.IsEmulator);
        Assert.Equal("Pixel 9 Pro", metadata.DisplayName);
        Assert.Equal("4", metadata.TransportId);
    }
}
