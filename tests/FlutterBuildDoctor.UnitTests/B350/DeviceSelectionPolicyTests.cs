using FlutterBuildDoctor.Application.Devices;

namespace FlutterBuildDoctor.UnitTests.B350;

public sealed class DeviceSelectionPolicyTests
{
    [Fact]
    public void Select_PrefersExplicitRequestedCompatibleDevice()
    {
        var decision = DeviceSelectionPolicy.Select(
            new DeviceSelectionRequest(DevicePlatform.Android, " Pixel-8 "),
            new[]
            {
                new DeviceCandidate("physical-1", DevicePlatform.Android, DeviceKind.Physical, true),
                new DeviceCandidate("pixel-8", DevicePlatform.Android, DeviceKind.Emulator, false)
            });

        Assert.Equal("pixel-8", decision.Selected?.Id);
        Assert.Equal("requested", decision.ReasonCode);
        Assert.Equal(64, decision.Fingerprint.Length);
    }

    [Fact]
    public void Select_PrefersBootedCompatibleDeviceThenEmulatorFallback()
    {
        var booted = DeviceSelectionPolicy.Select(
            new DeviceSelectionRequest(DevicePlatform.Android),
            new[]
            {
                new DeviceCandidate("emu", DevicePlatform.Android, DeviceKind.Emulator, false),
                new DeviceCandidate("phone", DevicePlatform.Android, DeviceKind.Physical, true)
            });
        var fallback = DeviceSelectionPolicy.Select(
            new DeviceSelectionRequest(DevicePlatform.Android),
            new[]
            {
                new DeviceCandidate("phone", DevicePlatform.Android, DeviceKind.Physical, false),
                new DeviceCandidate("emu", DevicePlatform.Android, DeviceKind.Emulator, false)
            });

        Assert.Equal("phone", booted.Selected?.Id);
        Assert.Equal("booted-compatible", booted.ReasonCode);
        Assert.Equal("emu", fallback.Selected?.Id);
        Assert.Equal("emulator-fallback", fallback.ReasonCode);
    }

    [Fact]
    public void Select_RejectsRequestedPlatformMismatch()
    {
        Assert.Throws<InvalidOperationException>(() => DeviceSelectionPolicy.Select(
            new DeviceSelectionRequest(DevicePlatform.Android, "ios-device"),
            new[] { new DeviceCandidate("ios-device", DevicePlatform.Ios, DeviceKind.Physical, true) }));
    }

    [Fact]
    public void Select_RejectsDuplicateIdsCaseInsensitively()
    {
        Assert.Throws<ArgumentException>(() => DeviceSelectionPolicy.Select(
            new DeviceSelectionRequest(DevicePlatform.Android),
            new[]
            {
                new DeviceCandidate("pixel", DevicePlatform.Android, DeviceKind.Emulator, false),
                new DeviceCandidate("PIXEL", DevicePlatform.Android, DeviceKind.Emulator, true)
            }));
    }

    [Fact]
    public void Select_BoundsCandidatesAndOrdersDeterministically()
    {
        var candidates = Enumerable.Range(0, 10)
            .Select(index => new DeviceCandidate($"emu-{9 - index}", DevicePlatform.Android, DeviceKind.Emulator, false))
            .ToArray();

        var decision = DeviceSelectionPolicy.Select(
            new DeviceSelectionRequest(DevicePlatform.Android, MaxCandidates: 3),
            candidates);

        Assert.Equal(3, decision.Candidates.Count);
        Assert.Equal(new[] { "emu-0", "emu-1", "emu-2" }, decision.Candidates.Select(candidate => candidate.Id));
    }

    [Fact]
    public void Select_ReturnsStableNoCompatibleReason()
    {
        var decision = DeviceSelectionPolicy.Select(
            new DeviceSelectionRequest(DevicePlatform.Android),
            new[] { new DeviceCandidate("windows", DevicePlatform.Windows, DeviceKind.Physical, true) });

        Assert.Null(decision.Selected);
        Assert.Equal("no-compatible-device", decision.ReasonCode);
    }

    [Fact]
    public void Select_IsDeterministicAcrossCandidateOrder()
    {
        var candidates = new[]
        {
            new DeviceCandidate("b", DevicePlatform.Android, DeviceKind.Emulator, false),
            new DeviceCandidate("a", DevicePlatform.Android, DeviceKind.Emulator, false)
        };
        var request = new DeviceSelectionRequest(DevicePlatform.Android);

        var first = DeviceSelectionPolicy.Select(request, candidates);
        var second = DeviceSelectionPolicy.Select(request, candidates.AsEnumerable().Reverse());

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.Selected?.Id, second.Selected?.Id);
    }
}
