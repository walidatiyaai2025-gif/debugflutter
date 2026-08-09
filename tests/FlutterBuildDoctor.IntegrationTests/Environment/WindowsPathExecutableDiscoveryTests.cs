using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Infrastructure.Environment;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class WindowsPathExecutableDiscoveryTests
{
    [Fact]
    public void Discover_ReturnsAllMatchesInPathOrderAndMarksShadowedCopies()
    {
        using var fixture = new PathFixture();
        var first = fixture.CreateDirectory("first");
        var second = fixture.CreateDirectory("second");
        fixture.CreateExecutable(first, "flutter.bat");
        fixture.CreateExecutable(second, "flutter.bat");
        var discovery = new WindowsPathExecutableDiscovery();

        var result = discovery.Discover(new PathExecutableDiscoveryRequest(
            "flutter",
            $"{first};{second}",
            ".BAT;.EXE"));

        Assert.True(result.IsSuccess);
        Assert.True(result.IsFound);
        Assert.True(result.HasConflict);
        Assert.Equal(2, result.Matches.Count);
        Assert.Equal(Path.Combine(first, "flutter.bat"), result.Matches[0].FullPath, ignoreCase: true);
        Assert.Equal(Path.Combine(second, "flutter.bat"), result.Matches[1].FullPath, ignoreCase: true);
        Assert.True(result.Matches[0].IsPreferred);
        Assert.False(result.Matches[0].IsShadowed);
        Assert.False(result.Matches[1].IsPreferred);
        Assert.True(result.Matches[1].IsShadowed);
        Assert.Same(result.Matches[0], result.PreferredMatch);
        Assert.Contains("shadowed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Discover_UsesPathExtOrderWithinSameDirectory()
    {
        using var fixture = new PathFixture();
        var bin = fixture.CreateDirectory("bin");
        fixture.CreateExecutable(bin, "tool.exe");
        fixture.CreateExecutable(bin, "tool.bat");
        var discovery = new WindowsPathExecutableDiscovery();

        var result = discovery.Discover(new PathExecutableDiscoveryRequest(
            "tool",
            bin,
            ".BAT;.EXE"));

        Assert.Equal(2, result.Matches.Count);
        Assert.EndsWith("tool.bat", result.Matches[0].FullPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("tool.exe", result.Matches[1].FullPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { ".BAT", ".EXE" }, result.Extensions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Discover_ExplicitExtensionSearchesOnlyExactFileName()
    {
        using var fixture = new PathFixture();
        var bin = fixture.CreateDirectory("bin");
        fixture.CreateExecutable(bin, "tool.exe");
        fixture.CreateExecutable(bin, "tool.bat");
        var discovery = new WindowsPathExecutableDiscovery();

        var result = discovery.Discover(new PathExecutableDiscoveryRequest(
            "tool.exe",
            bin,
            ".BAT;.EXE"));

        var match = Assert.Single(result.Matches);
        Assert.EndsWith("tool.exe", match.FullPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { ".exe" }, result.Extensions, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Discover_DeduplicatesRepeatedPathDirectoriesWithoutChangingFirstPathIndex()
    {
        using var fixture = new PathFixture();
        var bin = fixture.CreateDirectory("bin");
        fixture.CreateExecutable(bin, "java.exe");
        var discovery = new WindowsPathExecutableDiscovery();

        var result = discovery.Discover(new PathExecutableDiscoveryRequest(
            "java",
            $"{bin};{bin.ToUpperInvariant()};{bin}",
            ".EXE"));

        var match = Assert.Single(result.Matches);
        Assert.Equal(0, match.PathIndex);
        Assert.Single(result.SearchDirectories);
        Assert.Equal(2, result.IgnoredPathEntries.Count);
        Assert.All(
            result.IgnoredPathEntries,
            entry => Assert.Contains("Duplicate", entry.Reason, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Discover_TrimsQuotedPathWithSpaces()
    {
        using var fixture = new PathFixture();
        var bin = fixture.CreateDirectory("folder with spaces");
        fixture.CreateExecutable(bin, "adb.exe");
        var discovery = new WindowsPathExecutableDiscovery();

        var result = discovery.Discover(new PathExecutableDiscoveryRequest(
            "adb",
            $"  \"{bin}\"  ",
            ".EXE"));

        var match = Assert.Single(result.Matches);
        Assert.Equal(bin, match.DirectoryPath, ignoreCase: true);
    }

    [Fact]
    public void Discover_IgnoresEmptyPathSegmentsInsteadOfImplicitCurrentDirectoryLookup()
    {
        using var fixture = new PathFixture();
        var bin = fixture.CreateDirectory("bin");
        fixture.CreateExecutable(bin, "sdkmanager.bat");
        var discovery = new WindowsPathExecutableDiscovery();

        var result = discovery.Discover(new PathExecutableDiscoveryRequest(
            "sdkmanager",
            $";{bin};;",
            ".BAT"));

        Assert.Single(result.SearchDirectories);
        Assert.Equal(bin, result.SearchDirectories[0], ignoreCase: true);
        Assert.Equal(3, result.IgnoredPathEntries.Count);
        Assert.All(
            result.IgnoredPathEntries,
            entry => Assert.Contains("current-directory lookup", entry.Reason, StringComparison.OrdinalIgnoreCase));
        Assert.Single(result.Matches);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..\\flutter")]
    [InlineData("C:\\flutter.exe")]
    [InlineData("folder/flutter")]
    public void Discover_RejectsExecutableNamesThatAreNotSimpleFileNames(string executableName)
    {
        var discovery = new WindowsPathExecutableDiscovery();

        var result = discovery.Discover(new PathExecutableDiscoveryRequest(
            executableName,
            @"C:\Windows",
            ".EXE"));

        Assert.Equal(PathExecutableDiscoveryStatus.InvalidRequest, result.Status);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void Discover_MissingExecutableIsSuccessfulEmptyDiscovery()
    {
        using var fixture = new PathFixture();
        var bin = fixture.CreateDirectory("empty");
        var discovery = new WindowsPathExecutableDiscovery();

        var result = discovery.Discover(new PathExecutableDiscoveryRequest(
            "not-present-tool",
            bin,
            ".EXE;.BAT"));

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFound);
        Assert.False(result.HasConflict);
        Assert.Null(result.PreferredMatch);
        Assert.Empty(result.Matches);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Discover_PreservesWindowsDriveRootWhenItIsAnExplicitPathEntry()
    {
        var systemDriveRoot = Path.GetPathRoot(System.Environment.SystemDirectory);
        Assert.False(string.IsNullOrWhiteSpace(systemDriveRoot));
        var discovery = new WindowsPathExecutableDiscovery();

        var result = discovery.Discover(new PathExecutableDiscoveryRequest(
            "definitely-not-a-real-fbd-command",
            systemDriveRoot,
            ".EXE"));

        Assert.Single(result.SearchDirectories);
        Assert.Equal(systemDriveRoot, result.SearchDirectories[0], ignoreCase: true);
    }

    [Fact]
    public void Discover_WithoutOverrides_FindsWhereExeFromCurrentWindowsPath()
    {
        var discovery = new WindowsPathExecutableDiscovery();

        var result = discovery.Discover(new PathExecutableDiscoveryRequest("where"));

        Assert.True(result.IsSuccess);
        Assert.True(result.IsFound, result.Message);
        Assert.NotNull(result.PreferredMatch);
        Assert.EndsWith("where.exe", result.PreferredMatch!.FullPath, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class PathFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "FlutterBuildDoctorTests",
            Guid.NewGuid().ToString("N"));

        public PathFixture()
        {
            Directory.CreateDirectory(_root);
        }

        public string CreateDirectory(string name)
        {
            var path = Path.Combine(_root, name);
            Directory.CreateDirectory(path);
            return Path.GetFullPath(path);
        }

        public void CreateExecutable(string directory, string fileName)
        {
            File.WriteAllText(Path.Combine(directory, fileName), "fixture");
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
