using FlutterBuildDoctor.Android.Detection;

namespace FlutterBuildDoctor.UnitTests.Android;

public sealed class AndroidAvdManagerDetectorTests
{
    [Fact]
    public void Detect_EffectiveInstallationContainsAvdManager_ReturnsSuccess()
    {
        using var fixture = new AvdManagerFixture();
        var latest = fixture.CreateInstallation("latest", avdManager: true);

        var result = new AndroidAvdManagerDetector().Detect(CommandLineResult(
            fixture.SdkRoot,
            Candidate(latest, "19.0", AndroidCommandLineToolsLayout.LatestAlias, effective: true)));

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("19.0", result.EffectiveCandidate!.CommandLineToolsRevision);
        Assert.EndsWith("avdmanager.bat", result.EffectiveCandidate.AvdManagerPath!, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.EffectiveCandidate.Exists);
    }

    [Fact]
    public void Detect_BrokenEffectiveInstallation_DoesNotPromoteOlderCandidate()
    {
        using var fixture = new AvdManagerFixture();
        var latest = fixture.CreateInstallation("latest", avdManager: false);
        var older = fixture.CreateInstallation("12.0", avdManager: true);

        var result = new AndroidAvdManagerDetector().Detect(CommandLineResult(
            fixture.SdkRoot,
            Candidate(latest, "19.0", AndroidCommandLineToolsLayout.LatestAlias, effective: true),
            Candidate(older, "12.0", AndroidCommandLineToolsLayout.Versioned, effective: false)));

        Assert.Equal(AndroidAvdManagerDetectionStatus.AvdManagerMissing, result.Status);
        Assert.False(result.EffectiveCandidate!.Exists);
        Assert.Contains(result.Candidates, candidate => candidate.Exists && !candidate.IsEffective);
        Assert.Contains("not promoted automatically", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_MultipleInstallations_PreservesAllAvailabilityEvidence()
    {
        using var fixture = new AvdManagerFixture();
        var latest = fixture.CreateInstallation("latest", avdManager: true);
        var older = fixture.CreateInstallation("11.0", avdManager: false);

        var result = new AndroidAvdManagerDetector().Detect(CommandLineResult(
            fixture.SdkRoot,
            Candidate(latest, "19.0", AndroidCommandLineToolsLayout.LatestAlias, effective: true),
            Candidate(older, "11.0", AndroidCommandLineToolsLayout.Versioned, effective: false)));

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.HasMultipleInstallations);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Single(result.Candidates.Where(candidate => candidate.IsEffective));
        Assert.Single(result.Candidates.Where(candidate => !candidate.Exists));
    }

    [Fact]
    public void Detect_NoCommandLineToolsEffectiveCandidate_ReturnsUnavailable()
    {
        var commandLine = new AndroidCommandLineToolsDetectionResult(
            AndroidCommandLineToolsDetectionStatus.CommandLineToolsMissing,
            @"C:\Android\Sdk",
            EffectiveCandidate: null,
            Candidates: Array.Empty<AndroidCommandLineToolsCandidate>(),
            HasMultipleInstallations: false,
            Message: "missing");

        var result = new AndroidAvdManagerDetector().Detect(commandLine);

        Assert.Equal(AndroidAvdManagerDetectionStatus.CommandLineToolsUnavailable, result.Status);
        Assert.Null(result.EffectiveCandidate);
        Assert.Empty(result.Candidates);
    }

    private static AndroidCommandLineToolsDetectionResult CommandLineResult(
        string sdkRoot,
        params AndroidCommandLineToolsCandidate[] candidates)
        => new(
            AndroidCommandLineToolsDetectionStatus.Succeeded,
            sdkRoot,
            candidates.Single(candidate => candidate.IsEffective),
            candidates,
            candidates.Length > 1,
            "ready");

    private static AndroidCommandLineToolsCandidate Candidate(
        string installationPath,
        string revision,
        AndroidCommandLineToolsLayout layout,
        bool effective)
        => new(
            Path.GetFullPath(installationPath),
            SdkManagerPath: Path.Combine(installationPath, "bin", "sdkmanager.bat"),
            Revision: revision,
            Layout: layout,
            IsEffective: effective,
            SdkManagerExists: true,
            SourcePropertiesPath: null,
            RawSourceProperties: null,
            Message: null);

    private sealed class AvdManagerFixture : IDisposable
    {
        public AvdManagerFixture()
        {
            SdkRoot = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "AvdManager", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(SdkRoot, "cmdline-tools"));
        }

        public string SdkRoot { get; }

        public string CreateInstallation(string name, bool avdManager)
        {
            var path = Path.Combine(SdkRoot, "cmdline-tools", name);
            var bin = Path.Combine(path, "bin");
            Directory.CreateDirectory(bin);
            File.WriteAllText(Path.Combine(bin, "sdkmanager.bat"), "fixture");
            if (avdManager)
                File.WriteAllText(Path.Combine(bin, "avdmanager.bat"), "fixture");
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
