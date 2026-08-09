using FlutterBuildDoctor.Android.Detection;

namespace FlutterBuildDoctor.UnitTests.Android;

public sealed class AndroidBuildToolsDetectorTests
{
    [Fact]
    public void Detect_MultipleCompletePackages_EnumeratesVersionsDescending()
    {
        using var fixture = new BuildToolsFixture(createBuildToolsRoot: true);
        fixture.CreatePackage("34.0.0", "34.0.0", complete: true);
        fixture.CreatePackage("36.0.0", "36.0.0", complete: true);
        fixture.CreatePackage("35.0.1", "35.0.1", complete: true);

        var result = new AndroidBuildToolsDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(new[] { "36.0.0", "35.0.1", "34.0.0" }, result.InstalledVersions);
        Assert.Equal(3, result.Packages.Count);
        Assert.All(result.Packages, package => Assert.True(package.IsUsable));
    }

    [Fact]
    public void Detect_MissingSourceProperties_UsesDirectoryRevisionAndPreservesWarning()
    {
        using var fixture = new BuildToolsFixture(createBuildToolsRoot: true);
        fixture.CreatePackage("35.0.0", revision: null, complete: true);

        var result = new AndroidBuildToolsDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        var package = Assert.Single(result.Packages);
        Assert.Equal("35.0.0", package.Revision);
        Assert.True(package.IsUsable);
        Assert.Contains("source.properties is missing", package.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inferred from directory", package.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_MetadataDirectoryMismatch_RetainsMetadataRevision()
    {
        using var fixture = new BuildToolsFixture(createBuildToolsRoot: true);
        fixture.CreatePackage("35.0.0", "35.0.1", complete: true);

        var result = new AndroidBuildToolsDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        var package = Assert.Single(result.Packages);
        Assert.Equal("35.0.1", package.Revision);
        Assert.Contains("differs from source.properties revision", package.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_OnlyPartialPackage_ReturnsPartialInstallationsOnly()
    {
        using var fixture = new BuildToolsFixture(createBuildToolsRoot: true);
        fixture.CreatePackage("36.0.0", "36.0.0", complete: false);

        var result = new AndroidBuildToolsDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidBuildToolsDetectionStatus.PartialInstallationsOnly, result.Status);
        var package = Assert.Single(result.Packages);
        Assert.False(package.IsUsable);
        Assert.Contains("apksigner is missing", package.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.InstalledVersions);
    }

    [Fact]
    public void Detect_UsableAndPartialPackages_SucceedsAndPreservesPartialEvidence()
    {
        using var fixture = new BuildToolsFixture(createBuildToolsRoot: true);
        fixture.CreatePackage("35.0.0", "35.0.0", complete: true);
        fixture.CreatePackage("36.0.0", "36.0.0", complete: false);

        var result = new AndroidBuildToolsDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(new[] { "35.0.0" }, result.InstalledVersions);
        Assert.Contains("partial/broken", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, result.Packages.Count);
    }

    [Fact]
    public void Detect_MissingBuildToolsDirectory_ReturnsExplicitStatus()
    {
        using var fixture = new BuildToolsFixture(createBuildToolsRoot: false);

        var result = new AndroidBuildToolsDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidBuildToolsDetectionStatus.BuildToolsDirectoryMissing, result.Status);
        Assert.Empty(result.Packages);
    }

    [Fact]
    public void Detect_EmptyBuildToolsDirectory_ReturnsNoBuildToolsInstalled()
    {
        using var fixture = new BuildToolsFixture(createBuildToolsRoot: true);

        var result = new AndroidBuildToolsDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidBuildToolsDetectionStatus.NoBuildToolsInstalled, result.Status);
        Assert.Empty(result.Packages);
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

        var result = new AndroidBuildToolsDetector().Detect(invalid);

        Assert.Equal(AndroidBuildToolsDetectionStatus.AndroidSdkRootUnavailable, result.Status);
        Assert.Empty(result.Packages);
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
            HasPlatformsDirectory: false,
            HasBuildToolsDirectory: Directory.Exists(Path.Combine(sdkRoot, "build-tools")),
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

    private sealed class BuildToolsFixture : IDisposable
    {
        public BuildToolsFixture(bool createBuildToolsRoot)
        {
            SdkRoot = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "BuildTools", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(SdkRoot);
            if (createBuildToolsRoot)
                Directory.CreateDirectory(Path.Combine(SdkRoot, "build-tools"));
        }

        public string SdkRoot { get; }

        public string CreatePackage(string directoryName, string? revision, bool complete)
        {
            var path = Path.Combine(SdkRoot, "build-tools", directoryName);
            Directory.CreateDirectory(path);
            if (revision is not null)
                File.WriteAllText(Path.Combine(path, "source.properties"), $"Pkg.Revision={revision}\n");

            File.WriteAllText(Path.Combine(path, "aapt2.exe"), "fixture");
            File.WriteAllText(Path.Combine(path, "zipalign.exe"), "fixture");
            File.WriteAllText(Path.Combine(path, "d8.bat"), "fixture");
            if (complete)
                File.WriteAllText(Path.Combine(path, "apksigner.bat"), "fixture");
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
