using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Infrastructure.Environment;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class AndroidStudioDetectorIntegrationTests
{
    [Fact]
    public void Detect_ComposesWithActualWindowsEvidenceAndBoundedLocalSearchRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "StudioIntegration", Guid.NewGuid().ToString("N"));
        try
        {
            var install = Path.Combine(root, "Android Studio");
            var bin = Path.Combine(install, "bin");
            Directory.CreateDirectory(bin);
            var executable = Path.Combine(bin, "studio64.exe");
            File.WriteAllText(executable, "fixture");
            File.WriteAllText(Path.Combine(install, "product-info.json"), "{\"name\":\"Android Studio\",\"version\":\"2025.1.2\",\"buildNumber\":\"AI-251.12345\",\"productCode\":\"AI\"}");

            var windows = new WindowsEnvironmentDetector(new SystemWindowsRuntimeInfoSource()).Detect();
            var source = new WindowsAndroidStudioInstallationSource(new SingleRootProvider(new AndroidStudioSearchRoot(install, AndroidStudioDiscoverySource.LocalAppDataPrograms, Recursive: false)));
            var result = new AndroidStudioDetector(source).Detect(windows);

            Assert.True(windows.IsSuccess, windows.Message);
            Assert.True(result.IsSuccess, result.Message);
            var studio = Assert.Single(result.Installations);
            Assert.Equal(executable, studio.ExecutablePath, ignoreCase: true);
            Assert.Equal("2025.1.2", studio.Version);
            Assert.Equal("AI-251.12345", studio.BuildNumber);
            Assert.Equal(AndroidStudioMetadataSource.ProductInfoJson, studio.MetadataSource);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private sealed class SingleRootProvider : IAndroidStudioSearchRootProvider
    {
        private readonly IReadOnlyList<AndroidStudioSearchRoot> _roots;
        public SingleRootProvider(params AndroidStudioSearchRoot[] roots) => _roots = roots;
        public IReadOnlyList<AndroidStudioSearchRoot> GetRoots() => _roots;
    }
}
