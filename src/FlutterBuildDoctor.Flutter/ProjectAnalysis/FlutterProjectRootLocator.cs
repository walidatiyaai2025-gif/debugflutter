using System.IO;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed class FlutterProjectRootLocator : IFlutterProjectRootLocator
{
    private const int MaxDirectories = 4096;
    private const int MaxPubspecs = 512;

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".dart_tool",
        ".gradle",
        ".idea",
        ".vscode",
        "build",
        "node_modules"
    };

    public FlutterProjectRootResult Locate(string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
            return Failure(FlutterProjectRootStatus.InvalidRequest, null, "A repository path is required.");

        string normalizedRepositoryPath;
        try
        {
            normalizedRepositoryPath = Path.GetFullPath(repositoryPath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(FlutterProjectRootStatus.InvalidRequest, repositoryPath, $"Repository path is invalid: {ex.Message}");
        }

        if (!Directory.Exists(normalizedRepositoryPath))
        {
            return Failure(
                FlutterProjectRootStatus.RepositoryNotFound,
                normalizedRepositoryPath,
                "Repository directory does not exist or is not accessible.");
        }

        try
        {
            var scan = DiscoverPubspecs(normalizedRepositoryPath);
            if (scan.HitTraversalLimit)
            {
                return new FlutterProjectRootResult(
                    FlutterProjectRootStatus.InspectionFailed,
                    normalizedRepositoryPath,
                    null,
                    null,
                    Array.Empty<FlutterProjectCandidate>(),
                    scan.PubspecPaths,
                    $"Project discovery stopped at its safety limit ({MaxDirectories} directories / {MaxPubspecs} pubspec files). Narrow the imported repository before retrying.");
            }

            if (scan.PubspecPaths.Count == 0)
            {
                return new FlutterProjectRootResult(
                    scan.InspectionErrors > 0 ? FlutterProjectRootStatus.InspectionFailed : FlutterProjectRootStatus.PubspecNotFound,
                    normalizedRepositoryPath,
                    null,
                    null,
                    Array.Empty<FlutterProjectCandidate>(),
                    scan.PubspecPaths,
                    scan.InspectionErrors > 0
                        ? "No pubspec.yaml could be confirmed because one or more repository directories could not be inspected."
                        : "No pubspec.yaml was found in the imported repository.");
            }

            var candidates = scan.PubspecPaths
                .Select(BuildCandidate)
                .Where(candidate => candidate.HasFlutterProjectEvidence)
                .OrderBy(candidate => RelativeDepth(normalizedRepositoryPath, candidate.RootPath))
                .ThenBy(candidate => candidate.RootPath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (candidates.Length == 0)
            {
                return new FlutterProjectRootResult(
                    scan.InspectionErrors > 0 ? FlutterProjectRootStatus.InspectionFailed : FlutterProjectRootStatus.NotFlutterProject,
                    normalizedRepositoryPath,
                    null,
                    null,
                    candidates,
                    scan.PubspecPaths,
                    scan.InspectionErrors > 0
                        ? "pubspec.yaml files were found, but repository inspection was incomplete and no Flutter project root could be confirmed from filesystem evidence."
                        : "pubspec.yaml files were found, but none had Flutter filesystem evidence. Pubspec contents were intentionally not parsed by root discovery.");
            }

            var repositoryCandidate = candidates.FirstOrDefault(candidate =>
                PathsEqual(candidate.RootPath, normalizedRepositoryPath));
            if (repositoryCandidate is not null)
                return Success(normalizedRepositoryPath, repositoryCandidate, candidates, scan.PubspecPaths, rootPreferred: true);

            if (candidates.Length == 1 && scan.InspectionErrors > 0)
            {
                return new FlutterProjectRootResult(
                    FlutterProjectRootStatus.InspectionFailed,
                    normalizedRepositoryPath,
                    null,
                    null,
                    candidates,
                    scan.PubspecPaths,
                    "One nested Flutter project candidate was found, but repository inspection was incomplete, so a unique project root cannot be proven safely.");
            }

            if (candidates.Length == 1)
                return Success(normalizedRepositoryPath, candidates[0], candidates, scan.PubspecPaths, rootPreferred: false);

            return new FlutterProjectRootResult(
                FlutterProjectRootStatus.Ambiguous,
                normalizedRepositoryPath,
                null,
                null,
                candidates,
                scan.PubspecPaths,
                $"Multiple Flutter project roots were found ({candidates.Length}). Select a project root explicitly before analysis continues.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Failure(
                FlutterProjectRootStatus.InspectionFailed,
                normalizedRepositoryPath,
                $"Repository inspection failed: {ex.Message}");
        }
    }

    private static FlutterProjectRootResult Success(
        string repositoryPath,
        FlutterProjectCandidate candidate,
        IReadOnlyList<FlutterProjectCandidate> candidates,
        IReadOnlyList<string> inspectedPubspecPaths,
        bool rootPreferred)
        => new(
            FlutterProjectRootStatus.Succeeded,
            repositoryPath,
            candidate.RootPath,
            candidate.PubspecPath,
            candidates,
            inspectedPubspecPaths,
            rootPreferred && candidates.Count > 1
                ? $"Flutter project found at the repository root; {candidates.Count - 1} nested Flutter project(s) were retained as evidence."
                : "Flutter project root located using read-only filesystem evidence.");

    private static FlutterProjectRootResult Failure(
        FlutterProjectRootStatus status,
        string? repositoryPath,
        string message)
        => new(
            status,
            repositoryPath,
            null,
            null,
            Array.Empty<FlutterProjectCandidate>(),
            Array.Empty<string>(),
            message);

    private static DiscoveryScan DiscoverPubspecs(string repositoryPath)
    {
        var pending = new Stack<string>();
        pending.Push(repositoryPath);
        var pubspecPaths = new List<string>();
        var visitedDirectories = 0;
        var inspectionErrors = 0;

        while (pending.Count > 0)
        {
            if (visitedDirectories >= MaxDirectories || pubspecPaths.Count >= MaxPubspecs)
                return new DiscoveryScan(pubspecPaths, inspectionErrors, HitTraversalLimit: true);

            var directory = pending.Pop();
            visitedDirectories++;

            try
            {
                var pubspecPath = Path.Combine(directory, "pubspec.yaml");
                if (File.Exists(pubspecPath))
                    pubspecPaths.Add(Path.GetFullPath(pubspecPath));

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    var name = Path.GetFileName(child);
                    if (IgnoredDirectoryNames.Contains(name))
                        continue;

                    try
                    {
                        var attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        inspectionErrors++;
                        continue;
                    }

                    pending.Push(child);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                inspectionErrors++;
            }
        }

        return new DiscoveryScan(pubspecPaths, inspectionErrors, HitTraversalLimit: false);
    }

    private static FlutterProjectCandidate BuildCandidate(string pubspecPath)
    {
        var root = Path.GetDirectoryName(pubspecPath) ?? throw new IOException($"Could not resolve project root for '{pubspecPath}'.");
        return new FlutterProjectCandidate(
            root,
            pubspecPath,
            File.Exists(Path.Combine(root, ".metadata")),
            Directory.Exists(Path.Combine(root, "lib")),
            Directory.Exists(Path.Combine(root, "android")),
            Directory.Exists(Path.Combine(root, "ios")),
            Directory.Exists(Path.Combine(root, "web")),
            Directory.Exists(Path.Combine(root, "windows")),
            Directory.Exists(Path.Combine(root, "macos")),
            Directory.Exists(Path.Combine(root, "linux")));
    }

    private static int RelativeDepth(string repositoryPath, string candidatePath)
    {
        var relative = Path.GetRelativePath(repositoryPath, candidatePath);
        if (relative == ".")
            return 0;

        return relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed record DiscoveryScan(
        IReadOnlyList<string> PubspecPaths,
        int InspectionErrors,
        bool HitTraversalLimit);
}
