using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class DartEntryTargetDetectorBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "fbd-dart-entry-boundary-" + Guid.NewGuid().ToString("N"));

    public DartEntryTargetDetectorBoundaryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Detect_UnsuccessfulProjectRoot_IsRejected()
    {
        var failed = new FlutterProjectRootResult(
            FlutterProjectRootStatus.NotFlutterProject,
            _root,
            null,
            null,
            Array.Empty<FlutterProjectCandidate>(),
            Array.Empty<string>(),
            "not flutter");

        var result = new DartEntryTargetDetector().Detect(failed);

        Assert.Equal(DartEntryTargetDetectionStatus.ProjectRootUnavailable, result.Status);
        Assert.Empty(result.Targets);
    }

    [Fact]
    public void Detect_StaleProjectRoot_IsRejected()
    {
        var missing = Path.Combine(_root, "missing");

        var result = new DartEntryTargetDetector().Detect(SuccessfulRoot(missing));

        Assert.Equal(DartEntryTargetDetectionStatus.ProjectRootUnavailable, result.Status);
    }

    [Fact]
    public void Detect_MissingLibDirectory_IsTypedExplicitly()
    {
        var result = new DartEntryTargetDetector().Detect(SuccessfulRoot(_root));

        Assert.Equal(DartEntryTargetDetectionStatus.LibDirectoryUnavailable, result.Status);
        Assert.Empty(result.Targets);
    }

    [Fact]
    public void Detect_OversizeCandidate_IsPreservedAsNonRunnable()
    {
        var lib = Path.Combine(_root, "lib");
        Directory.CreateDirectory(lib);
        var path = Path.Combine(lib, "main_big.dart");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            stream.SetLength((512L * 1024) + 1);

        var result = new DartEntryTargetDetector().Detect(SuccessfulRoot(_root));

        Assert.Equal(DartEntryTargetDetectionStatus.Partial, result.Status);
        var target = Assert.Single(result.Targets);
        Assert.Equal(DartEntryTargetInspectionStatus.FileTooLarge, target.InspectionStatus);
        Assert.False(target.IsRunnable);
    }

    [Fact]
    public void Detect_CandidateSymlink_IsPreservedAsUnsafeWhenSupported()
    {
        var lib = Path.Combine(_root, "lib");
        Directory.CreateDirectory(lib);
        var outside = Path.Combine(
            Path.GetTempPath(),
            "fbd-dart-entry-outside-" + Guid.NewGuid().ToString("N") + ".dart");
        File.WriteAllText(outside, "void main() {}");
        var link = Path.Combine(lib, "main_link.dart");

        try
        {
            try
            {
                File.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0)
                return;

            var result = new DartEntryTargetDetector().Detect(SuccessfulRoot(_root));

            Assert.Equal(DartEntryTargetDetectionStatus.Partial, result.Status);
            var target = Assert.Single(result.Targets);
            Assert.Equal(DartEntryTargetInspectionStatus.UnsafePath, target.InspectionStatus);
            Assert.False(target.IsRunnable);
            Assert.Contains(result.Issues, issue => issue.Kind == DartEntryScanIssueKind.ReparsePointSkipped);
        }
        finally
        {
            try
            {
                if (File.Exists(link)) File.Delete(link);
                if (File.Exists(outside)) File.Delete(outside);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Detect_SymlinkDirectory_IsNotTraversedWhenSupported()
    {
        var lib = Path.Combine(_root, "lib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "main.dart"), "void main() {}");
        var outside = Path.Combine(
            Path.GetTempPath(),
            "fbd-dart-entry-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "main_external.dart"), "void main() {}");
        var link = Path.Combine(lib, "linked");

        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, outside);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
            {
                return;
            }

            if ((File.GetAttributes(link) & FileAttributes.ReparsePoint) == 0)
                return;

            var result = new DartEntryTargetDetector().Detect(SuccessfulRoot(_root));

            Assert.Equal(DartEntryTargetDetectionStatus.Partial, result.Status);
            Assert.Single(result.Targets);
            Assert.DoesNotContain(result.Targets, target => target.RelativeTargetPath.Contains("external", StringComparison.Ordinal));
            Assert.Contains(result.Issues, issue => issue.Kind == DartEntryScanIssueKind.ReparsePointSkipped);
        }
        finally
        {
            try
            {
                if (Directory.Exists(link)) Directory.Delete(link);
                if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void Detect_DepthBeyondConfiguredLimit_IsNotTraversedAndReturnsPartial()
    {
        var lib = Path.Combine(_root, "lib");
        var deep = Path.Combine(lib, "a", "b", "c", "d", "e");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(lib, "main.dart"), "void main() {}");
        File.WriteAllText(Path.Combine(deep, "main_deep.dart"), "void main() {}");

        var result = new DartEntryTargetDetector().Detect(SuccessfulRoot(_root));

        Assert.Equal(DartEntryTargetDetectionStatus.Partial, result.Status);
        Assert.Single(result.Targets);
        Assert.Equal("lib/main.dart", result.Targets[0].RelativeTargetPath);
        Assert.Contains(result.Issues, issue => issue.Kind == DartEntryScanIssueKind.DepthLimitReached);
    }

    [Fact]
    public void Detect_CandidateLimit_ReturnsExplicitScanLimitExceeded()
    {
        var lib = Path.Combine(_root, "lib");
        Directory.CreateDirectory(lib);
        for (var index = 0; index < 129; index++)
            File.WriteAllText(Path.Combine(lib, $"main_{index:D3}.dart"), "void main() {}");

        var result = new DartEntryTargetDetector().Detect(SuccessfulRoot(_root));

        Assert.Equal(DartEntryTargetDetectionStatus.ScanLimitExceeded, result.Status);
        Assert.Equal(128, result.Targets.Count);
        Assert.Contains(result.Issues, issue => issue.Kind == DartEntryScanIssueKind.CandidateLimitReached);
    }

    [Fact]
    public void Detect_CandidateLimit_PreservesCanonicalMainBeforeFlavorCandidates()
    {
        var lib = Path.Combine(_root, "lib");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "main.dart"), "void main() {}");
        for (var index = 0; index < 129; index++)
            File.WriteAllText(Path.Combine(lib, $"main_{index:D3}.dart"), "void main() {}");

        var result = new DartEntryTargetDetector().Detect(SuccessfulRoot(_root));

        Assert.Equal(DartEntryTargetDetectionStatus.ScanLimitExceeded, result.Status);
        Assert.Equal(128, result.Targets.Count);
        Assert.Contains(
            result.Targets,
            target => target.Kind == DartEntryTargetKind.CanonicalMain &&
                      target.RelativeTargetPath == "lib/main.dart" &&
                      target.IsRunnable);
        Assert.Equal(DartEntryTargetKind.CanonicalMain, result.Targets[0].Kind);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private FlutterProjectRootResult SuccessfulRoot(string root)
        => new(
            FlutterProjectRootStatus.Succeeded,
            root,
            root,
            Path.Combine(root, "pubspec.yaml"),
            Array.Empty<FlutterProjectCandidate>(),
            new[] { Path.Combine(root, "pubspec.yaml") },
            "Test root.");
}
