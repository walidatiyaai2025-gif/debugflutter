using FlutterBuildDoctor.Android.Detection;

namespace FlutterBuildDoctor.UnitTests.Android;

public sealed class AndroidCommandLineToolsDetectorTests
{
    [Fact]
    public void Detect_LatestAliasExists_SelectsLatestAndPreservesOtherVersions()
    {
        using var fixture = new CommandLineToolsFixture();
        fixture.CreateInstallation("12.0", "12.0", sdkManager: true);
        var latest = fixture.CreateInstallation("latest", "19.0", sdkManager: true);
        var detector = new AndroidCommandLineToolsDetector();

        var result = detector.Detect(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.HasMultipleInstallations);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(AndroidCommandLineToolsLayout.LatestAlias, result.EffectiveCandidate!.Layout);
        Assert.Equal("19.0", result.EffectiveCandidate.Revision);
        Assert.Equal(Path.GetFullPath(latest), result.EffectiveCandidate.InstallationPath, ignoreCase: true);
        Assert.EndsWith("sdkmanager.bat", result.EffectiveCandidate.SdkManagerPath!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pkg.Revision=19.0", result.EffectiveCandidate.RawSourceProperties, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_NoLatestAlias_SelectsHighestInstalledRevision()
    {
        using var fixture = new CommandLineToolsFixture();
        fixture.CreateInstallation("8.0", "8.0", sdkManager: true);
        fixture.CreateInstallation("old-name", "11.0", sdkManager: true);
        var highest = fixture.CreateInstallation("12.0", "12.0", sdkManager: true);

        var result = new AndroidCommandLineToolsDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("12.0", result.EffectiveCandidate!.Revision);
        Assert.Equal(Path.GetFullPath(highest), result.EffectiveCandidate.InstallationPath, ignoreCase: true);
        Assert.Equal(3, result.Candidates.Count);
    }

    [Fact]
    public void Detect_BrokenLatest_DoesNotPromoteOlderUsableInstallation()
    {
        using var fixture = new CommandLineToolsFixture();
        var latest = fixture.CreateInstallation("latest", "19.0", sdkManager: false);
        fixture.CreateInstallation("12.0", "12.0", sdkManager: true);

        var result = new AndroidCommandLineToolsDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidCommandLineToolsDetectionStatus.EffectiveSdkManagerMissing, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal(Path.GetFullPath(latest), result.EffectiveCandidate!.InstallationPath, ignoreCase: true);
        Assert.False(result.EffectiveCandidate.SdkManagerExists);
        Assert.Contains(result.Candidates, candidate => candidate.Revision == "12.0" && candidate.SdkManagerExists && !candidate.IsEffective);
        Assert.Contains("not promoted automatically", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_SdkManagerWithoutRevision_PreservesRawMetadataAndReturnsMetadataInvalid()
    {
        using var fixture = new CommandLineToolsFixture();
        fixture.CreateInstallationWithRawProperties(
            "latest",
            "Pkg.Path=cmdline-tools;latest\nDisplayName=Android SDK Command-line Tools\n",
            sdkManager: true);

        var result = new AndroidCommandLineToolsDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidCommandLineToolsDetectionStatus.MetadataInvalid, result.Status);
        Assert.NotNull(result.EffectiveCandidate);
        Assert.Null(result.EffectiveCandidate!.Revision);
        Assert.Contains("Pkg.Path", result.EffectiveCandidate.RawSourceProperties, StringComparison.Ordinal);
        Assert.Contains("Pkg.Revision", result.EffectiveCandidate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_NoCommandLineTools_ReturnsMissing()
    {
        using var fixture = new CommandLineToolsFixture();

        var result = new AndroidCommandLineToolsDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.Equal(AndroidCommandLineToolsDetectionStatus.CommandLineToolsMissing, result.Status);
        Assert.Null(result.EffectiveCandidate);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Detect_LegacyToolsOnly_ReportsLegacySdkManagerAndRevision()
    {
        using var fixture = new CommandLineToolsFixture();
        var legacy = fixture.CreateLegacyInstallation("26.1.1");

        var result = new AndroidCommandLineToolsDetector().Detect(ValidRootResult(fixture.SdkRoot));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal(AndroidCommandLineToolsLayout.LegacyTools, result.EffectiveCandidate!.Layout);
        Assert.Equal("26.1.1", result.EffectiveCandidate.Revision);
        Assert.Equal(Path.GetFullPath(legacy), result.EffectiveCandidate.InstallationPath, ignoreCase: true);
    }

    [Fact]
    public void Detect_InvalidAndroidSdkRoot_DoesNotInspectCommandLineTools()
    {
        var rootResult = new AndroidSdkRootDetectionResult(
            AndroidSdkRootDetectionStatus.EffectiveRootInvalid,
            EffectiveCandidate: new AndroidSdkRootCandidate(
                @"Z:\missing-sdk",
                Array.Empty<AndroidSdkRootSourceEvidence>(),
                IsEffective: true,
                Exists: false,
                HasRecognizedSdkLayout: false,
                HasPlatformToolsDirectory: false,
                HasPlatformsDirectory: false,
                HasBuildToolsDirectory: false,
                HasCmdlineToolsDirectory: false,
                HasLicensesDirectory: false,
                ValidationMessage: "missing"),
            Candidates: Array.Empty<AndroidSdkRootCandidate>(),
            HasConflict: false,
            Message: "invalid");

        var result = new AndroidCommandLineToolsDetector().Detect(rootResult);

        Assert.Equal(AndroidCommandLineToolsDetectionStatus.AndroidSdkRootUnavailable, result.Status);
        Assert.Empty(result.Candidates);
        Assert.Null(result.EffectiveCandidate);
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
            HasBuildToolsDirectory: false,
            HasCmdlineToolsDirectory: true,
            HasLicensesDirectory: false,
            ValidationMessage: null);
        return new AndroidSdkRootDetectionResult(
            AndroidSdkRootDetectionStatus.Succeeded,
            candidate,
            new[] { candidate },
            HasConflict: false,
            Message: "valid");
    }

    private sealed class CommandLineToolsFixture : IDisposable
    {
        public CommandLineToolsFixture()
        {
            SdkRoot = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "CmdlineTools", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(SdkRoot, "cmdline-tools"));
        }

        public string SdkRoot { get; }

        public string CreateInstallation(string directoryName, string revision, bool sdkManager)
            => CreateInstallationWithRawProperties(directoryName, $"Pkg.Revision={revision}\nPkg.Path=cmdline-tools;{directoryName}\n", sdkManager);

        public string CreateInstallationWithRawProperties(string directoryName, string properties, bool sdkManager)
        {
            var root = Path.Combine(SdkRoot, "cmdline-tools", directoryName);
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            File.WriteAllText(Path.Combine(root, "source.properties"), properties);
            if (sdkManager)
                File.WriteAllText(Path.Combine(root, "bin", "sdkmanager.bat"), "@echo off");
            return root;
        }

        public string CreateLegacyInstallation(string revision)
        {
            var root = Path.Combine(SdkRoot, "tools");
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            File.WriteAllText(Path.Combine(root, "source.properties"), $"Pkg.Revision={revision}\n");
            File.WriteAllText(Path.Combine(root, "bin", "sdkmanager.bat"), "@echo off");
            return root;
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
