using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.UnitTests.Android;

public sealed class JavaInstallationDetectorTests
{
    [Fact]
    public async Task DetectAsync_PreferredJdk_ReturnsVersionVendorArchitectureAndJavac()
    {
        using var fixture = new JavaFixture();
        var java = fixture.CreateJava("jdk-17", withJavac: true);
        var discovery = new StubPathDiscovery(ResultFor(java));
        var runner = new StubProcessRunner();
        runner.Add(java.ExecutablePath, SuccessfulMetadata(java.Home, "17.0.12", "Eclipse Adoptium", "amd64"));
        var detector = new JavaInstallationDetector(discovery, runner);

        var result = await detector.DetectAsync();

        Assert.True(result.IsSuccess, result.Message);
        Assert.NotNull(result.PreferredInstallation);
        Assert.Equal(java.ExecutablePath, result.PreferredInstallation!.ExecutablePath, ignoreCase: true);
        Assert.Equal(java.Home, result.PreferredInstallation.JavaHome, ignoreCase: true);
        Assert.Equal("17.0.12", result.PreferredInstallation.Version);
        Assert.Equal("Eclipse Adoptium", result.PreferredInstallation.Vendor);
        Assert.Equal("amd64", result.PreferredInstallation.Architecture);
        Assert.True(result.PreferredInstallation.IsJdk);
        Assert.Equal(java.JavacPath, result.PreferredInstallation.JavacPath, ignoreCase: true);
        Assert.False(result.HasConflict);
    }

    [Fact]
    public async Task DetectAsync_MultiplePathMatches_PreservesConflictAndProbesEveryCandidate()
    {
        using var fixture = new JavaFixture();
        var preferred = fixture.CreateJava("jdk-21", withJavac: true);
        var shadowed = fixture.CreateJava("jdk-17", withJavac: true);
        var discovery = new StubPathDiscovery(ResultFor(preferred, shadowed));
        var runner = new StubProcessRunner();
        runner.Add(preferred.ExecutablePath, SuccessfulMetadata(preferred.Home, "21.0.4", "Oracle Corporation", "amd64"));
        runner.Add(shadowed.ExecutablePath, SuccessfulMetadata(shadowed.Home, "17.0.12", "Eclipse Adoptium", "amd64"));
        var detector = new JavaInstallationDetector(discovery, runner);

        var result = await detector.DetectAsync();

        Assert.True(result.IsSuccess, result.Message);
        Assert.True(result.HasConflict);
        Assert.Equal(2, result.Installations.Count);
        Assert.Equal(2, runner.Requests.Count);
        Assert.True(result.Installations[0].IsPreferred);
        Assert.True(result.Installations[1].IsShadowed);
        Assert.Equal("21.0.4", result.PreferredInstallation!.Version);
        Assert.Contains("shadowed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAsync_RuntimeWithoutJavac_IsReportedAsJavaRuntime()
    {
        using var fixture = new JavaFixture();
        var java = fixture.CreateJava("jre", withJavac: false);
        var runner = new StubProcessRunner();
        runner.Add(java.ExecutablePath, SuccessfulMetadata(java.Home, "11.0.24", "Microsoft", "amd64"));
        var detector = new JavaInstallationDetector(new StubPathDiscovery(ResultFor(java)), runner);

        var result = await detector.DetectAsync();

        Assert.True(result.IsSuccess, result.Message);
        Assert.False(result.PreferredInstallation!.IsJdk);
        Assert.Null(result.PreferredInstallation.JavacPath);
        Assert.Contains("Java runtime", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DetectAsync_PreferredProbeTimeout_ReturnsTimedOutAndKeepsCandidate()
    {
        using var fixture = new JavaFixture();
        var java = fixture.CreateJava("jdk-timeout", withJavac: true);
        var runner = new StubProcessRunner();
        runner.Add(java.ExecutablePath, ProcessResultFor(ProcessExecutionStatus.TimedOut, failureReason: "timeout"));
        var detector = new JavaInstallationDetector(new StubPathDiscovery(ResultFor(java)), runner);

        var result = await detector.DetectAsync(new JavaDetectionRequest(ProbeTimeout: TimeSpan.FromSeconds(1)));

        Assert.Equal(JavaDetectionStatus.TimedOut, result.Status);
        Assert.Single(result.Installations);
        Assert.Equal(java.ExecutablePath, result.PreferredInstallation!.ExecutablePath, ignoreCase: true);
        Assert.Equal(TimeSpan.FromSeconds(1), runner.Requests.Single().Timeout);
    }

    [Fact]
    public async Task DetectAsync_PreferredProbeFailure_DoesNotPromoteShadowedJavaSilently()
    {
        using var fixture = new JavaFixture();
        var preferred = fixture.CreateJava("broken", withJavac: true);
        var shadowed = fixture.CreateJava("good", withJavac: true);
        var runner = new StubProcessRunner();
        runner.Add(preferred.ExecutablePath, ProcessResultFor(ProcessExecutionStatus.Failed, exitCode: 1, failureReason: "broken"));
        runner.Add(shadowed.ExecutablePath, SuccessfulMetadata(shadowed.Home, "21.0.4", "Oracle Corporation", "amd64"));
        var detector = new JavaInstallationDetector(new StubPathDiscovery(ResultFor(preferred, shadowed)), runner);

        var result = await detector.DetectAsync();

        Assert.Equal(JavaDetectionStatus.ProbeFailed, result.Status);
        Assert.Equal(preferred.ExecutablePath, result.PreferredInstallation!.ExecutablePath, ignoreCase: true);
        Assert.Equal("21.0.4", result.Installations[1].Version);
        Assert.True(result.HasConflict);
    }

    [Fact]
    public async Task DetectAsync_VersionBannerFallback_ParsesVersionWhenPropertyIsMissing()
    {
        using var fixture = new JavaFixture();
        var java = fixture.CreateJava("banner", withJavac: true);
        var output = new[]
        {
            Line(ProcessStream.StdErr, "openjdk version \"25.0.2\" 2026-01-20"),
            Line(ProcessStream.StdErr, $"    java.home = {java.Home}"),
            Line(ProcessStream.StdErr, "    java.vendor = JetBrains s.r.o."),
            Line(ProcessStream.StdErr, "    os.arch = amd64")
        };
        var runner = new StubProcessRunner();
        runner.Add(java.ExecutablePath, ProcessResultFor(ProcessExecutionStatus.Succeeded, 0, output));
        var detector = new JavaInstallationDetector(new StubPathDiscovery(ResultFor(java)), runner);

        var result = await detector.DetectAsync();

        Assert.True(result.IsSuccess, result.Message);
        Assert.Equal("25.0.2", result.PreferredInstallation!.Version);
        Assert.Equal("JetBrains s.r.o.", result.PreferredInstallation.Vendor);
    }

    [Fact]
    public async Task DetectAsync_MissingJava_ReturnsMissingWithoutStartingProcess()
    {
        var discovery = new StubPathDiscovery(new PathExecutableDiscoveryResult(
            PathExecutableDiscoveryStatus.Succeeded,
            "java",
            Array.Empty<PathExecutableMatch>(),
            Array.Empty<string>(),
            new[] { ".EXE" },
            Array.Empty<IgnoredPathEntry>()));
        var runner = new StubProcessRunner();
        var detector = new JavaInstallationDetector(discovery, runner);

        var result = await detector.DetectAsync();

        Assert.Equal(JavaDetectionStatus.Missing, result.Status);
        Assert.Empty(result.Installations);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public async Task DetectAsync_AlreadyCancelled_ReturnsCancelledWithoutDiscoverySideEffects()
    {
        var discovery = new StubPathDiscovery(new PathExecutableDiscoveryResult(
            PathExecutableDiscoveryStatus.Succeeded,
            "java",
            Array.Empty<PathExecutableMatch>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<IgnoredPathEntry>()));
        var runner = new StubProcessRunner();
        var detector = new JavaInstallationDetector(discovery, runner);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await detector.DetectAsync(cancellationToken: cts.Token);

        Assert.Equal(JavaDetectionStatus.Cancelled, result.Status);
        Assert.Equal(0, discovery.CallCount);
        Assert.Empty(runner.Requests);
    }

    private static PathExecutableDiscoveryResult ResultFor(params JavaPath[] paths)
    {
        var matches = paths.Select((path, index) => new PathExecutableMatch(
            path.ExecutablePath,
            Path.GetDirectoryName(path.ExecutablePath)!,
            "java.exe",
            ".exe",
            index,
            index,
            IsPreferred: index == 0,
            IsShadowed: index > 0)).ToArray();

        return new PathExecutableDiscoveryResult(
            PathExecutableDiscoveryStatus.Succeeded,
            "java",
            matches,
            matches.Select(match => match.DirectoryPath).ToArray(),
            new[] { ".EXE" },
            Array.Empty<IgnoredPathEntry>());
    }

    private static ProcessResult SuccessfulMetadata(string home, string version, string vendor, string architecture)
        => ProcessResultFor(
            ProcessExecutionStatus.Succeeded,
            0,
            new[]
            {
                Line(ProcessStream.StdErr, "Property settings:"),
                Line(ProcessStream.StdErr, $"    java.home = {home}"),
                Line(ProcessStream.StdErr, $"    java.vendor = {vendor}"),
                Line(ProcessStream.StdErr, $"    java.version = {version}"),
                Line(ProcessStream.StdErr, $"    os.arch = {architecture}"),
                Line(ProcessStream.StdErr, $"openjdk version \"{version}\"")
            });

    private static ProcessResult ProcessResultFor(
        ProcessExecutionStatus status,
        int? exitCode = null,
        IReadOnlyList<ProcessOutputLine>? output = null,
        string? failureReason = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new ProcessResult(
            status,
            exitCode,
            now,
            now,
            output ?? Array.Empty<ProcessOutputLine>(),
            "java probe",
            failureReason);
    }

    private static ProcessOutputLine Line(ProcessStream stream, string text)
        => new(DateTimeOffset.UtcNow, stream, text);

    private sealed class StubPathDiscovery : IPathExecutableDiscovery
    {
        private readonly PathExecutableDiscoveryResult _result;

        public StubPathDiscovery(PathExecutableDiscoveryResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public PathExecutableDiscoveryResult Discover(PathExecutableDiscoveryRequest request)
        {
            CallCount++;
            return _result;
        }
    }

    private sealed class StubProcessRunner : IProcessRunner
    {
        private readonly Dictionary<string, ProcessResult> _results = new(StringComparer.OrdinalIgnoreCase);

        public List<ProcessRequest> Requests { get; } = new();

        public void Add(string executablePath, ProcessResult result) => _results[executablePath] = result;

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (!_results.TryGetValue(request.FileName, out var result))
            {
                throw new InvalidOperationException($"No result configured for {request.FileName}");
            }

            foreach (var line in result.Output)
            {
                progress?.Report(line);
            }

            return Task.FromResult(result);
        }
    }

    private sealed class JavaFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-java-unit-" + Guid.NewGuid().ToString("N"));

        public JavaFixture()
        {
            Directory.CreateDirectory(_root);
        }

        public JavaPath CreateJava(string name, bool withJavac)
        {
            var home = Path.Combine(_root, name);
            var bin = Path.Combine(home, "bin");
            Directory.CreateDirectory(bin);
            var java = Path.Combine(bin, "java.exe");
            File.WriteAllText(java, "fixture");
            string? javac = null;
            if (withJavac)
            {
                javac = Path.Combine(bin, "javac.exe");
                File.WriteAllText(javac, "fixture");
            }

            return new JavaPath(home, java, javac);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Best effort test cleanup.
            }
        }
    }

    private sealed record JavaPath(string Home, string ExecutablePath, string? JavacPath);
}
