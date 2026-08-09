using FlutterBuildDoctor.Android.Detection;

namespace FlutterBuildDoctor.UnitTests.Android;

public sealed class AndroidPlatformDetectorTests
{
    [Fact]
    public void Detect_StablePlatform_ReturnsApiRevisionAndPackageEvidence()
    {
        using var fixture = new PlatformFixture(createPlatformsDirectory: true);
        var path = fixture.CreatePlatform(
            "android-35",
            "Pkg.Revision=2\nAndroidVersion.ApiLevel=35\nAndroidVersion.CodeName=REL\n",
            androidJar: true,
            frameworkAidl: true);

        var result = new AndroidPlatformDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        var platform = Assert.Single(result.Platforms);
        Assert.Equal("android-35", platform.PackageId);
        Assert.Equal(Path.GetFullPath(path), platform.InstallationPath, ignoreCase: true);
        Assert.Equal(35, platform.ApiLevel);
        Assert.Equal("REL", platform.CodeName);
        Assert.Equal("2", platform.Revision);
        Assert.True(platform.AndroidJarExists);
        Assert.True(platform.FrameworkAidlExists);
        Assert.False(platform.IsPreview);
        Assert.True(platform.IsUsable);
        Assert.Equal(new[] { 35 }, result.InstalledApiLevels);
        Assert.Contains("AndroidVersion.ApiLevel=35", platform.RawSourceProperties, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_PreviewPlatform_UsesMetadataWhenDirectoryIsNotNumeric()
    {
        using var fixture = new PlatformFixture(createPlatformsDirectory: true);
        fixture.CreatePlatform(
            "android-VanillaIceCream",
            "Pkg.Revision=1\nAndroidVersion.ApiLevel=35\nAndroidVersion.CodeName=VanillaIceCream\n",
            androidJar: true,
            frameworkAidl: false);

        var result = new AndroidPlatformDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        var platform = Assert.Single(result.Platforms);
        Assert.Equal(35, platform.ApiLevel);
        Assert.Equal("VanillaIceCream", platform.CodeName);
        Assert.True(platform.IsPreview);
        Assert.True(platform.IsUsable);
    }

    [Fact]
    public void Detect_MissingMetadata_InfersNumericDirectoryApiAndPreservesWarning()
    {
        using var fixture = new PlatformFixture(createPlatformsDirectory: true);
        fixture.CreatePlatform("android-34", properties: null, androidJar: true, frameworkAidl: false);

        var result = new AndroidPlatformDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        var platform = Assert.Single(result.Platforms);
        Assert.Equal(34, platform.ApiLevel);
        Assert.Null(platform.Revision);
        Assert.True(platform.IsUsable);
        Assert.Contains("source.properties is missing", platform.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inferred from directory", platform.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_MetadataDirectoryMismatch_RetainsMetadataAndReportsConflict()
    {
        using var fixture = new PlatformFixture(createPlatformsDirectory: true);
        fixture.CreatePlatform(
            "android-33",
            "Pkg.Revision=3\nAndroidVersion.ApiLevel=34\nAndroidVersion.CodeName=REL\n",
            androidJar: true,
            frameworkAidl: false);

        var result = new AndroidPlatformDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        var platform = Assert.Single(result.Platforms);
        Assert.Equal(34, platform.ApiLevel);
        Assert.Contains("Directory API 33 differs", platform.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_OnlyPartialPlatforms_ReturnsPartialInstallationsOnly()
    {
        using var fixture = new PlatformFixture(createPlatformsDirectory: true);
        fixture.CreatePlatform(
            "android-36",
            "Pkg.Revision=1\nAndroidVersion.ApiLevel=36\nAndroidVersion.CodeName=REL\n",
            androidJar: false,
            frameworkAidl: true);

        var result = new AndroidPlatformDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidPlatformDetectionStatus.PartialInstallationsOnly, result.Status);
        var platform = Assert.Single(result.Platforms);
        Assert.False(platform.IsUsable);
        Assert.Contains("android.jar is missing", platform.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.InstalledApiLevels);
    }

    [Fact]
    public void Detect_UsableAndPartialPlatforms_SucceedsAndPreservesPartialEvidence()
    {
        using var fixture = new PlatformFixture(createPlatformsDirectory: true);
        fixture.CreatePlatform("android-35", "AndroidVersion.ApiLevel=35\nPkg.Revision=1\n", androidJar: true, frameworkAidl: false);
        fixture.CreatePlatform("android-36", "AndroidVersion.ApiLevel=36\nPkg.Revision=1\n", androidJar: false, frameworkAidl: false);

        var result = new AndroidPlatformDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(2, result.Platforms.Count);
        Assert.Equal(new[] { 35 }, result.InstalledApiLevels);
        Assert.Contains("partial/broken", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_MissingPlatformsDirectory_ReturnsExplicitStatus()
    {
        using var fixture = new PlatformFixture(createPlatformsDirectory: false);

        var result = new AndroidPlatformDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidPlatformDetectionStatus.PlatformsDirectoryMissing, result.Status);
        Assert.Empty(result.Platforms);
    }

    [Fact]
    public void Detect_EmptyPlatformsDirectory_ReturnsNoPlatformsInstalled()
    {
        using var fixture = new PlatformFixture(createPlatformsDirectory: true);
        Directory.CreateDirectory(Path.Combine(fixture.SdkRoot, "platforms", "not-a-platform"));

        var result = new AndroidPlatformDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidPlatformDetectionStatus.NoPlatformsInstalled, result.Status);
        Assert.Empty(result.Platforms);
    }

    [Fact]
    public void Detect_InvalidSdkRoot_ReturnsRootUnavailable()
    {
        var invalid = new AndroidSdkRootDetectionResult(
            AndroidSdkRootDetectionStatus.EffectiveRootInvalid,
            EffectiveCandidate: null,
            Candidates: Array.Empty<AndroidSdkRootCandidate>(),
            HasConflict: false,
            Message: "invalid");

        var result = new AndroidPlatformDetector().Detect(invalid);

        Assert.Equal(AndroidPlatformDetectionStatus.AndroidSdkRootUnavailable, result.Status);
        Assert.Empty(result.Platforms);
    }

    private static AndroidSdkRootDetectionResult ValidRootResult(string sdkRoot)
    {
        var candidate = new AndroidSdkRootCandidate(
            Path.GetFullPath(sdkRoot),
            Array.Empty<AndroidSdkRootSourceEvidence>(),
            IsEffective: true,
            Exists: true,
            HasRecognizedSdkLayout: true,
            HasPlatformToolsDirectory: false,
            HasPlatformsDirectory: Directory.Exists(Path.Combine(sdkRoot, "platforms")),
            HasBuildToolsDirectory: false,
            HasCmdlineToolsDirectory: false,
            HasLicensesDirectory: false,
            ValidationMessage: null);
        return new AndroidSdkRootDetectionResult(
            AndroidSdkRootDetectionStatus.Succeeded,
            candidate,
            new[] { candidate },
            HasConflict: false,
            Message: "valid");
    }

    private sealed class PlatformFixture : IDisposable
    {
        public PlatformFixture(bool createPlatformsDirectory)
        {
            SdkRoot = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "Platforms", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(SdkRoot);
            if (createPlatformsDirectory)
                Directory.CreateDirectory(Path.Combine(SdkRoot, "platforms"));
        }

        public string SdkRoot { get; }

        public string CreatePlatform(
            string packageId,
            string? properties,
            bool androidJar,
            bool frameworkAidl)
        {
            var path = Path.Combine(SdkRoot, "platforms", packageId);
            Directory.CreateDirectory(path);
            if (properties is not null)
                File.WriteAllText(Path.Combine(path, "source.properties"), properties);
            if (androidJar)
                File.WriteAllText(Path.Combine(path, "android.jar"), "fixture");
            if (frameworkAidl)
                File.WriteAllText(Path.Combine(path, "framework.aidl"), "fixture");
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(SdkRoot))
                    Directory.Delete(SdkRoot, recursive: true);
            }
            catch
            {
                // Best-effort test cleanup.
            }
        }
    }
}
