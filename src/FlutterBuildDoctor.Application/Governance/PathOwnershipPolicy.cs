using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record PathOwnershipDecision(
    bool Allowed,
    string Scope,
    string RootPath,
    string CandidatePath,
    string RelativePath,
    int Depth,
    string ReasonCode,
    string Fingerprint);

public static partial class PathOwnershipPolicy
{
    public const int MaxDepth = 32;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ScopePattern();

    public static PathOwnershipDecision Evaluate(string scope, string rootPath, string candidatePath, bool forbidRootMutation = true)
    {
        var normalizedScope = NormalizeScope(scope);
        var root = NormalizeAbsolutePath(rootPath, nameof(rootPath));
        var candidate = NormalizeAbsolutePath(candidatePath, nameof(candidatePath));

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var rootWithSeparator = EnsureTrailingSeparator(root);
        var isRoot = string.Equals(root, candidate, comparison);
        var isInside = candidate.StartsWith(rootWithSeparator, comparison);

        if (!isRoot && !isInside)
        {
            return Decision(false, normalizedScope, root, candidate, string.Empty, 0, "path-outside-owner-root");
        }

        if (isRoot && forbidRootMutation)
        {
            return Decision(false, normalizedScope, root, candidate, ".", 0, "owner-root-mutation-forbidden");
        }

        var relative = isRoot ? "." : Path.GetRelativePath(root, candidate).Replace('\\', '/');
        var depth = relative == "." ? 0 : relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
        if (depth > MaxDepth)
        {
            return Decision(false, normalizedScope, root, candidate, relative, depth, "path-depth-exceeded");
        }

        return Decision(true, normalizedScope, root, candidate, relative, depth, "path-owned");
    }

    public static string NormalizeScope(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (!ScopePattern().IsMatch(normalized))
        {
            throw new ArgumentException("Owner scope is invalid.", nameof(value));
        }
        return normalized;
    }

    private static string NormalizeAbsolutePath(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Path.IsPathRooted(value))
        {
            throw new ArgumentException("Path must be absolute.", parameterName);
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    private static string EnsureTrailingSeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static PathOwnershipDecision Decision(bool allowed, string scope, string root, string candidate, string relative, int depth, string reason)
    {
        var canonical = $"{scope}|{root}|{candidate}|{relative}|{depth}|{reason}";
        return new PathOwnershipDecision(allowed, scope, root, candidate, relative, depth, reason, Hash(canonical));
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
