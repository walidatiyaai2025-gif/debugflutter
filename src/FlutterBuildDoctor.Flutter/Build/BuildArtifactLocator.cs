namespace FlutterBuildDoctor.Flutter.Build;

public sealed class BuildArtifactLocator : IBuildArtifactLocator
{
    public FlutterBuildArtifact? Locate(FlutterBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var projectRoot = Path.GetFullPath(request.Context.WorkingDirectory);
        var buildRoot = Path.GetFullPath(Path.Combine(projectRoot, "build"));
        if (!Directory.Exists(buildRoot))
        {
            return null;
        }

        foreach (var expected in ExpectedPaths(request))
        {
            var fullPath = Path.GetFullPath(Path.Combine(projectRoot, expected));
            if (IsUnder(fullPath, buildRoot) && File.Exists(fullPath))
            {
                return ToArtifact(request.ArtifactType, fullPath);
            }
        }

        try
        {
            var extension = request.ArtifactType == FlutterBuildArtifactType.Apk ? ".apk" : ".aab";
            var modeToken = request.Mode.ToString().ToLowerInvariant();
            var flavorToken = request.Flavor?.Trim();
            var candidates = Directory.EnumerateFiles(buildRoot, $"*{extension}", SearchOption.AllDirectories)
                .Where(path => IsUnder(Path.GetFullPath(path), buildRoot))
                .Where(path => Path.GetFileName(path).Contains(modeToken, StringComparison.OrdinalIgnoreCase))
                .Where(path => string.IsNullOrWhiteSpace(flavorToken) ||
                               Path.GetFileName(path).Contains(flavorToken, StringComparison.OrdinalIgnoreCase))
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Length)
                .FirstOrDefault();

            return candidates is null
                ? null
                : new FlutterBuildArtifact(
                    request.ArtifactType,
                    candidates.FullName,
                    candidates.Length,
                    new DateTimeOffset(candidates.LastWriteTimeUtc, TimeSpan.Zero));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ExpectedPaths(FlutterBuildRequest request)
    {
        var mode = request.Mode.ToString().ToLowerInvariant();
        if (request.ArtifactType == FlutterBuildArtifactType.Apk)
        {
            var fileName = string.IsNullOrWhiteSpace(request.Flavor)
                ? $"app-{mode}.apk"
                : $"app-{request.Flavor!.Trim()}-{mode}.apk";
            yield return Path.Combine("build", "app", "outputs", "flutter-apk", fileName);
            yield break;
        }

        if (string.IsNullOrWhiteSpace(request.Flavor))
        {
            yield return Path.Combine("build", "app", "outputs", "bundle", "release", "app-release.aab");
            yield break;
        }

        var flavor = request.Flavor!.Trim();
        yield return Path.Combine("build", "app", "outputs", "bundle", $"{flavor}Release", $"app-{flavor}-release.aab");
        yield return Path.Combine("build", "app", "outputs", "bundle", flavor, "release", $"app-{flavor}-release.aab");
    }

    private static FlutterBuildArtifact ToArtifact(FlutterBuildArtifactType type, string path)
    {
        var file = new FileInfo(path);
        return new FlutterBuildArtifact(
            type,
            file.FullName,
            file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
    }

    private static bool IsUnder(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
