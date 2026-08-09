using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Infrastructure.Environment;

public sealed class WindowsPathExecutableDiscovery : IPathExecutableDiscovery
{
    private const string DefaultPathExt = ".COM;.EXE;.BAT;.CMD";
    private static readonly char[] DirectorySeparators = ['\\', '/', ':'];

    public PathExecutableDiscoveryResult Discover(PathExecutableDiscoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executableName = request.ExecutableName?.Trim() ?? string.Empty;
        if (!IsValidExecutableName(executableName))
        {
            return Invalid(
                executableName,
                "Executable name must be a simple file name without a directory, drive prefix, or traversal segment.");
        }

        var rawPath = request.PathValue ?? System.Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var rawPathExt = request.PathExtValue ?? System.Environment.GetEnvironmentVariable("PATHEXT");

        var ignoredEntries = new List<IgnoredPathEntry>();
        var searchDirectories = NormalizePathEntries(rawPath, ignoredEntries);
        var extensions = ResolveExtensions(executableName, rawPathExt);
        var candidateNames = BuildCandidateNames(executableName, extensions);
        var matches = new List<PathExecutableMatch>();
        var seenMatches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in searchDirectories)
        {
            foreach (var candidateName in candidateNames)
            {
                string candidatePath;
                try
                {
                    candidatePath = Path.GetFullPath(Path.Combine(directory.Path, candidateName));
                }
                catch (Exception ex) when (IsPathException(ex))
                {
                    continue;
                }

                if (!File.Exists(candidatePath) || !seenMatches.Add(candidatePath))
                {
                    continue;
                }

                var order = matches.Count;
                matches.Add(new PathExecutableMatch(
                    candidatePath,
                    directory.Path,
                    Path.GetFileName(candidatePath),
                    Path.GetExtension(candidatePath),
                    directory.PathIndex,
                    order,
                    IsPreferred: order == 0,
                    IsShadowed: order > 0));
            }
        }

        var publicDirectories = searchDirectories
            .Select(static item => item.Path)
            .ToArray();
        var publicExtensions = extensions.ToArray();
        var publicMatches = matches.ToArray();
        var message = publicMatches.Length switch
        {
            0 => $"'{executableName}' was not found in the effective Windows PATH.",
            1 => $"Found '{executableName}' at '{publicMatches[0].FullPath}'.",
            _ => $"Found {publicMatches.Length} matches for '{executableName}'. PATH resolves to '{publicMatches[0].FullPath}' and {publicMatches.Length - 1} later match(es) are shadowed."
        };

        return new PathExecutableDiscoveryResult(
            PathExecutableDiscoveryStatus.Succeeded,
            executableName,
            publicMatches,
            publicDirectories,
            publicExtensions,
            ignoredEntries.ToArray(),
            message);
    }

    private static PathExecutableDiscoveryResult Invalid(string executableName, string message)
        => new(
            PathExecutableDiscoveryStatus.InvalidRequest,
            executableName,
            Array.Empty<PathExecutableMatch>(),
            Array.Empty<string>(),
            Array.Empty<string>(),
            Array.Empty<IgnoredPathEntry>(),
            message);

    private static bool IsValidExecutableName(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName) ||
            executableName is "." or ".." ||
            executableName.IndexOfAny(DirectorySeparators) >= 0)
        {
            return false;
        }

        return executableName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static IReadOnlyList<PathEntry> NormalizePathEntries(
        string rawPath,
        ICollection<IgnoredPathEntry> ignoredEntries)
    {
        var result = new List<PathEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = rawPath.Split(';', StringSplitOptions.None);

        for (var index = 0; index < entries.Length; index++)
        {
            var rawEntry = entries[index];
            var trimmed = rawEntry.Trim();
            if (trimmed.Length == 0)
            {
                ignoredEntries.Add(new IgnoredPathEntry(
                    index,
                    rawEntry,
                    "Empty PATH entry ignored; current-directory lookup is intentionally disabled."));
                continue;
            }

            trimmed = TrimMatchingQuotes(trimmed);
            if (trimmed.Length == 0)
            {
                ignoredEntries.Add(new IgnoredPathEntry(index, rawEntry, "Quoted PATH entry resolved to an empty value."));
                continue;
            }

            string fullPath;
            try
            {
                var expanded = System.Environment.ExpandEnvironmentVariables(trimmed);
                fullPath = NormalizeDirectoryPath(Path.GetFullPath(expanded));
            }
            catch (Exception ex) when (IsPathException(ex))
            {
                ignoredEntries.Add(new IgnoredPathEntry(index, rawEntry, $"Invalid PATH entry: {ex.Message}"));
                continue;
            }

            if (fullPath.Length == 0)
            {
                ignoredEntries.Add(new IgnoredPathEntry(index, rawEntry, "PATH entry did not resolve to a directory."));
                continue;
            }

            if (!seen.Add(fullPath))
            {
                ignoredEntries.Add(new IgnoredPathEntry(index, rawEntry, "Duplicate PATH directory ignored after its first occurrence."));
                continue;
            }

            result.Add(new PathEntry(fullPath, index));
        }

        return result;
    }

    private static IReadOnlyList<string> ResolveExtensions(string executableName, string? rawPathExt)
    {
        if (Path.HasExtension(executableName))
        {
            return new[] { Path.GetExtension(executableName) };
        }

        var value = string.IsNullOrWhiteSpace(rawPathExt) ? DefaultPathExt : rawPathExt;
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawExtension in value.Split(';', StringSplitOptions.None))
        {
            var extension = rawExtension.Trim();
            if (extension.Length == 0)
            {
                continue;
            }

            if (!extension.StartsWith(".", StringComparison.Ordinal))
            {
                extension = "." + extension;
            }

            if (extension.IndexOfAny(['\\', '/', ':']) >= 0 ||
                extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                continue;
            }

            if (seen.Add(extension))
            {
                result.Add(extension);
            }
        }

        if (result.Count == 0)
        {
            return DefaultPathExt.Split(';', StringSplitOptions.RemoveEmptyEntries);
        }

        return result;
    }

    private static IReadOnlyList<string> BuildCandidateNames(
        string executableName,
        IReadOnlyList<string> extensions)
    {
        if (Path.HasExtension(executableName))
        {
            return new[] { executableName };
        }

        return extensions
            .Select(extension => executableName + extension)
            .ToArray();
    }

    private static string NormalizeDirectoryPath(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(root) &&
            string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string TrimMatchingQuotes(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1].Trim();
        }

        return value;
    }

    private static bool IsPathException(Exception ex)
        => ex is ArgumentException or NotSupportedException or PathTooLongException;

    private sealed record PathEntry(string Path, int PathIndex);
}
