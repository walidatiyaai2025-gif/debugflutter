using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Filesystem;

public sealed record FilesystemMutationTarget(string Path, bool Destructive, bool IsReparsePoint = false);

public sealed record FilesystemMutationDecision(
    bool Allowed,
    string ProjectRoot,
    IReadOnlyList<FilesystemMutationTarget> Targets,
    bool RequiresBackup,
    bool RequiresConfirmation,
    string ReasonCode,
    string Fingerprint);

public static class FilesystemMutationGuard
{
    public const int MaxTargets = 100;

    public static FilesystemMutationDecision Evaluate(
        string projectRoot,
        IEnumerable<FilesystemMutationTarget> targets,
        bool BackupAvailable,
        bool Confirmed)
    {
        var root = NormalizeProjectRoot(projectRoot);
        ArgumentNullException.ThrowIfNull(targets);
        var input = targets.ToArray();
        if (input.Length == 0)
        {
            throw new ArgumentException("At least one mutation target is required.", nameof(targets));
        }
        if (input.Length > MaxTargets)
        {
            throw new ArgumentOutOfRangeException(nameof(targets), "Mutation batch exceeds the supported bound.");
        }

        var normalized = input.Select(target => NormalizeTarget(root, target)).OrderBy(target => target.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Select(target => target.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalized.Length)
        {
            throw new ArgumentException("Mutation batch contains duplicate targets.", nameof(targets));
        }

        var destructive = normalized.Any(target => target.Destructive);
        var requiresBackup = destructive;
        var requiresConfirmation = destructive;
        var allowed = !destructive || (BackupAvailable && Confirmed);
        var reason = !destructive ? "safe-mutation"
            : !BackupAvailable ? "backup-required"
            : !Confirmed ? "confirmation-required"
            : "destructive-approved";

        var canonical = string.Join('|', root.ToUpperInvariant(), BackupAvailable, Confirmed, reason,
            string.Join(';', normalized.Select(target => $"{target.Path.ToUpperInvariant()}:{target.Destructive}:{target.IsReparsePoint}")));
        return new FilesystemMutationDecision(allowed, root, normalized, requiresBackup, requiresConfirmation, reason, Hash(canonical));
    }

    public static string NormalizeProjectRoot(string projectRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        if (!Path.IsPathFullyQualified(projectRoot.Trim()))
        {
            throw new ArgumentException("Project root must be fully qualified.", nameof(projectRoot));
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot.Trim()));
    }

    public static FilesystemMutationTarget NormalizeTarget(string projectRoot, FilesystemMutationTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.IsReparsePoint)
        {
            throw new ArgumentException("Reparse/symlink mutation targets are not allowed.", nameof(target));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(target.Path);
        var full = Path.GetFullPath(target.Path.Trim());
        var rootWithSeparator = Path.EndsInDirectorySeparator(projectRoot) ? projectRoot : projectRoot + Path.DirectorySeparatorChar;
        var inside = string.Equals(full, projectRoot, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        if (!inside)
        {
            throw new ArgumentException("Mutation target escapes the project root.", nameof(target));
        }
        if (target.Destructive && string.Equals(full, projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Deleting the project root is not allowed.", nameof(target));
        }
        return target with { Path = Path.TrimEndingDirectorySeparator(full) };
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
