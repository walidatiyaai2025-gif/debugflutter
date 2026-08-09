using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Infrastructure.Environment;

namespace FlutterBuildDoctor.UnitTests.EnvironmentDiscovery;

public sealed class AndroidStudioDetectionTests
{
    [Fact]
    public void InstallationSource_DiscoversDirectAndBoundedToolboxInstallations()
    {
        using var fixture = new StudioFixture();
        var direct = fixture.CreateStudio("direct", Array.Empty<string>());
        var toolbox = fixture.CreateStudio("toolbox", new[] { "ch-0", "241.12345" });
        var provider = new StubRootProvider(
            new AndroidStudioSearchRoot(direct.Root, AndroidStudioDiscoverySource.ProgramFiles, Recursive: false),
            new AndroidStudioSearchRoot(Path.Combine(fixture.Root, "toolbox"), AndroidStudioDiscoverySource.JetBrainsToolbox, Recursive: true, MaxDepth: 4));
        var result = new WindowsAndroidStudioInstallationSource(provider).Discover();
        Assert.Equal(2, result.Count);
        Assert.Contains(result, item => string.Equals(item.ExecutablePath, direct.Executable, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result, item => string.Equals(item.ExecutablePath, toolbox.Executable, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Detect_ProductInfoJson_ReturnsTypedVersionBuildAndProductCode()
    {
        using var fixture = new StudioFixture();
        var studio = fixture.CreateStudio("stable", Array.Empty<string>());
        File.WriteAllText(Path.Combine(studio.Root, "product-info.json"), "{\"name\":\"Android Studio\",\"version\":\"2025.1.2\",\"buildNumber\":\"AI-251.12345.67\",\"productCode\":\"AI\"}");
        var result = new AndroidStudioDetector(new StubInstallationSource(new AndroidStudioExecutableEvidence(studio.Executable, AndroidStudioDiscoverySource.ProgramFiles))).Detect(WindowsInfo());
        Assert.True(result.IsSuccess, result.Message);
        var install = Assert.Single(result.Installations);
        Assert.Equal("2025.1.2", install.Version);
        Assert.Equal("AI-251.12345.67", install.BuildNumber);
        Assert.Equal("AI", install.ProductCode);
        Assert.Equal(AndroidStudioMetadataSource.ProductInfoJson, install.MetadataSource);
    }

    [Fact]
    public void Detect_MalformedProductInfo_FallsBackToBuildTxt()
    {
        using var fixture = new StudioFixture();
        var studio = fixture.CreateStudio("fallback", Array.Empty<string>());
        File.WriteAllText(Path.Combine(studio.Root, "product-info.json"), "{ malformed json");
        File.WriteAllText(Path.Combine(studio.Root, "build.txt"), "AI-242.21829.142");
        var result = new AndroidStudioDetector(new StubInstallationSource(new AndroidStudioExecutableEvidence(studio.Executable, AndroidStudioDiscoverySource.LocalAppDataPrograms))).Detect(WindowsInfo());
        var install = Assert.Single(result.Installations);
        Assert.Equal("AI-242.21829.142", install.BuildNumber);
        Assert.Equal(AndroidStudioMetadataSource.BuildTxt, install.MetadataSource);
        Assert.Contains("could not be parsed", install.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_NoInstallations_ReturnsMissing()
    {
        var result = new AndroidStudioDetector(new StubInstallationSource()).Detect(WindowsInfo());
        Assert.Equal(AndroidStudioDetectionStatus.Missing, result.Status);
        Assert.Empty(result.Installations);
    }

    [Fact]
    public void Detect_NotWindows_DoesNotQueryInstallationSource()
    {
        var windows = WindowsInfo() with { Status = WindowsEnvironmentDetectionStatus.NotWindows };
        var result = new AndroidStudioDetector(new ThrowingInstallationSource()).Detect(windows);
        Assert.Equal(AndroidStudioDetectionStatus.NotWindows, result.Status);
    }

    [Fact]
    public void Detect_SourceFailure_ReturnsInspectionFailed()
    {
        var result = new AndroidStudioDetector(new ThrowingInstallationSource()).Detect(WindowsInfo());
        Assert.Equal(AndroidStudioDetectionStatus.InspectionFailed, result.Status);
        Assert.Contains("fixture source failure", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static WindowsEnvironmentInfo WindowsInfo()
        => new(WindowsEnvironmentDetectionStatus.Succeeded, "Microsoft Windows", "10.0.26100", 10, 0, 26100, "X64", "X64", true, true, "ready");

    private sealed class StubRootProvider : IAndroidStudioSearchRootProvider
    {
        private readonly IReadOnlyList<AndroidStudioSearchRoot> _roots;
        public StubRootProvider(params AndroidStudioSearchRoot[] roots) => _roots = roots;
        public IReadOnlyList<AndroidStudioSearchRoot> GetRoots() => _roots;
    }
    private sealed class StubInstallationSource : IAndroidStudioInstallationSource
    {
        private readonly IReadOnlyList<AndroidStudioExecutableEvidence> _items;
        public StubInstallationSource(params AndroidStudioExecutableEvidence[] items) => _items = items;
        public IReadOnlyList<AndroidStudioExecutableEvidence> Discover() => _items;
    }
    private sealed class ThrowingInstallationSource : IAndroidStudioInstallationSource
    {
        public IReadOnlyList<AndroidStudioExecutableEvidence> Discover() => throw new InvalidOperationException("fixture source failure");
    }
    private sealed class StudioFixture : IDisposable
    {
        public StudioFixture() { Root = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "AndroidStudio", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Root); }
        public string Root { get; }
        public StudioPaths CreateStudio(string name, IReadOnlyList<string> segments)
        {
            var root = Path.Combine(Root, name);
            foreach (var segment in segments) root = Path.Combine(root, segment);
            var bin = Path.Combine(root, "bin"); Directory.CreateDirectory(bin);
            var executable = Path.Combine(bin, "studio64.exe"); File.WriteAllText(executable, "fixture");
            return new StudioPaths(root, executable);
        }
        public void Dispose() { try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { } }
    }
    private sealed record StudioPaths(string Root, string Executable);
}
