using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.UnitTests.Android;

public sealed class AndroidSdkRootDetectorTests
{
    [Fact]
    public void Detect_ValidProcessSdkRoot_ReturnsEffectiveValidatedCandidate()
    {
        using var fixture = new AndroidSdkFixture();
        var sdk = fixture.CreateSdk("primary", "platform-tools", "platforms", "build-tools");
        var detector = new AndroidSdkRootDetector();

        var result = detector.Detect(Snapshot(androidSdkRootProcess: sdk));

        Assert.True(result.IsSuccess, result.Message);
        Assert.False(result.HasConflict);
        Assert.NotNull(result.EffectiveCandidate);
        Assert.Equal(Path.GetFullPath(sdk), result.EffectiveCandidate!.NormalizedPath, ignoreCase: true);
        Assert.True(result.EffectiveCandidate.IsEffective);
        Assert.True(result.EffectiveCandidate.Exists);
        Assert.True(result.EffectiveCandidate.HasRecognizedSdkLayout);
        Assert.True(result.EffectiveCandidate.HasPlatformToolsDirectory);
        Assert.True(result.EffectiveCandidate.HasPlatformsDirectory);
        Assert.True(result.EffectiveCandidate.HasBuildToolsDirectory);
        Assert.Single(result.EffectiveCandidate.Sources);
        Assert.Equal("ANDROID_SDK_ROOT", result.EffectiveCandidate.Sources[0].VariableName);
        Assert.Equal(VariableScope.Process, result.EffectiveCandidate.Sources[0].Scope);
    }

    [Fact]
    public void Detect_SameProcessSdkRootAndAndroidHome_DeduplicatesCandidateAndSources()
    {
        using var fixture = new AndroidSdkFixture();
        var sdk = fixture.CreateSdk("same", "cmdline-tools");
        var detector = new AndroidSdkRootDetector();

        var result = detector.Detect(Snapshot(
            androidSdkRootProcess: $"  \"{sdk}{Path.DirectorySeparatorChar}\"  ",
            androidHomeProcess: sdk));

        Assert.True(result.IsSuccess, result.Message);
        Assert.False(result.HasConflict);
        var candidate = Assert.Single(result.Candidates);
        Assert.True(candidate.IsEffective);
        Assert.Equal(2, candidate.Sources.Count);
        Assert.Equal("ANDROID_SDK_ROOT", candidate.Sources[0].VariableName);
        Assert.Equal("ANDROID_HOME", candidate.Sources[1].VariableName);
    }

    [Fact]
    public void Detect_DifferentProcessRoots_PreservesConflictAndSdkRootPrecedence()
    {
        using var fixture = new AndroidSdkFixture();
        var sdkRoot = fixture.CreateSdk("sdk-root", "platform-tools");
        var androidHome = fixture.CreateSdk("android-home", "platforms");
        var detector = new AndroidSdkRootDetector();

        var result = detector.Detect(Snapshot(
            androidSdkRootProcess: sdkRoot,
            androidHomeProcess: androidHome));

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.HasConflict);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(Path.GetFullPath(sdkRoot), result.EffectiveCandidate!.NormalizedPath, ignoreCase: true);
        Assert.True(result.Candidates[0].IsEffective);
        Assert.False(result.Candidates[1].IsEffective);
        Assert.Contains("additional configured SDK root", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_InvalidEffectiveSdkRoot_DoesNotPromoteValidAndroidHome()
    {
        using var fixture = new AndroidSdkFixture();
        var missing = Path.Combine(fixture.Root, "missing-sdk");
        var validHome = fixture.CreateSdk("valid-home", "platform-tools");
        var detector = new AndroidSdkRootDetector();

        var result = detector.Detect(Snapshot(
            androidSdkRootProcess: missing,
            androidHomeProcess: validHome));

        Assert.Equal(AndroidSdkRootDetectionStatus.EffectiveRootInvalid, result.Status);
        Assert.False(result.IsSuccess);
        Assert.True(result.HasConflict);
        Assert.Equal(Path.GetFullPath(missing), result.EffectiveCandidate!.NormalizedPath, ignoreCase: true);
        Assert.False(result.EffectiveCandidate.IsValid);
        Assert.Contains(result.Candidates, candidate =>
            string.Equals(candidate.NormalizedPath, Path.GetFullPath(validHome), StringComparison.OrdinalIgnoreCase) &&
            candidate.IsValid &&
            !candidate.IsEffective);
        Assert.Contains("not promoted automatically", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_OnlyPersistedRoots_ReportsMissingEffectiveRootButPreservesCandidates()
    {
        using var fixture = new AndroidSdkFixture();
        var userSdk = fixture.CreateSdk("user-sdk", "platform-tools");
        var machineSdk = fixture.CreateSdk("machine-sdk", "build-tools");
        var detector = new AndroidSdkRootDetector();

        var result = detector.Detect(Snapshot(
            androidSdkRootUser: userSdk,
            androidHomeMachine: machineSdk));

        Assert.Equal(AndroidSdkRootDetectionStatus.MissingEffectiveRoot, result.Status);
        Assert.Null(result.EffectiveCandidate);
        Assert.Equal(2, result.Candidates.Count);
        Assert.True(result.HasConflict);
        Assert.All(result.Candidates, candidate => Assert.False(candidate.IsEffective));
        Assert.Contains("current process has no effective", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_NoConfiguredRoots_ReturnsMissingWithoutCandidates()
    {
        var result = new AndroidSdkRootDetector().Detect(Snapshot());

        Assert.Equal(AndroidSdkRootDetectionStatus.MissingEffectiveRoot, result.Status);
        Assert.Null(result.EffectiveCandidate);
        Assert.Empty(result.Candidates);
        Assert.False(result.HasConflict);
    }

    [Fact]
    public void Detect_ExistingEmptyDirectory_IsNotAcceptedAsAndroidSdk()
    {
        using var fixture = new AndroidSdkFixture();
        var empty = fixture.CreateDirectory("empty-sdk");

        var result = new AndroidSdkRootDetector().Detect(Snapshot(androidSdkRootProcess: empty));

        Assert.Equal(AndroidSdkRootDetectionStatus.EffectiveRootInvalid, result.Status);
        Assert.True(result.EffectiveCandidate!.Exists);
        Assert.False(result.EffectiveCandidate.HasRecognizedSdkLayout);
        Assert.Contains("does not contain a recognized Android SDK layout marker", result.EffectiveCandidate.ValidationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_UncEffectiveRoot_IsRejectedWithoutNetworkProbe()
    {
        const string unc = @"\\server\share\Android\Sdk";

        var result = new AndroidSdkRootDetector().Detect(Snapshot(androidSdkRootProcess: unc));

        Assert.Equal(AndroidSdkRootDetectionStatus.EffectiveRootInvalid, result.Status);
        Assert.False(result.EffectiveCandidate!.Exists);
        Assert.Contains("UNC/network", result.EffectiveCandidate.ValidationMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Detect_UnresolvedEnvironmentReference_IsRejectedWithoutExpansion()
    {
        const string unresolved = @"%LOCALAPPDATA%\Android\Sdk";

        var result = new AndroidSdkRootDetector().Detect(Snapshot(androidSdkRootProcess: unresolved));

        Assert.Equal(AndroidSdkRootDetectionStatus.EffectiveRootInvalid, result.Status);
        Assert.Contains("unresolved environment-variable reference", result.EffectiveCandidate!.ValidationMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static EnvironmentVariableSnapshot Snapshot(
        string? androidSdkRootProcess = null,
        string? androidSdkRootUser = null,
        string? androidSdkRootMachine = null,
        string? androidHomeProcess = null,
        string? androidHomeUser = null,
        string? androidHomeMachine = null)
        => new(
            DateTimeOffset.UtcNow,
            MissingRecord("PATH"),
            MissingRecord("JAVA_HOME"),
            Record("ANDROID_HOME", androidHomeProcess, androidHomeUser, androidHomeMachine),
            Record("ANDROID_SDK_ROOT", androidSdkRootProcess, androidSdkRootUser, androidSdkRootMachine));

    private static VariableRecord MissingRecord(string name) => Record(name, null, null, null);

    private static VariableRecord Record(string name, string? process, string? user, string? machine)
        => new(
            name,
            Scope(VariableScope.Process, process),
            Scope(VariableScope.User, user),
            Scope(VariableScope.Machine, machine));

    private static VariableScopeValue Scope(VariableScope scope, string? value)
        => new(
            scope,
            value is null ? VariableReadStatus.Missing : VariableReadStatus.Present,
            value);

    private sealed class AndroidSdkFixture : IDisposable
    {
        public AndroidSdkFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "FlutterBuildDoctorTests", "AndroidSdk", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string CreateSdk(string name, params string[] markers)
        {
            var root = CreateDirectory(name);
            foreach (var marker in markers)
                Directory.CreateDirectory(Path.Combine(root, marker));

            return root;
        }

        public string CreateDirectory(string name)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch
            {
                // Test cleanup is best effort.
            }
        }
    }
}
