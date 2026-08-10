using FlutterBuildDoctor.Application.Repairs;

namespace FlutterBuildDoctor.Infrastructure.Repairs;

public sealed class FileSystemRepairBackupService : IRepairBackupService
{
    public Task<RepairRestorePoint> CreateAsync(
        string projectRoot,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(paths);
        var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var backupRoot = Path.Combine(
            Path.GetTempPath(),
            "FlutterBuildDoctor",
            "repair-backups",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupRoot);
        var entries = new List<RepairRestoreEntry>();

        foreach (var input in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var original = ResolveUnderRoot(root, input);
            var isDirectory = Directory.Exists(original);
            var isFile = File.Exists(original);
            if (!isDirectory && !isFile)
                continue;

            RejectReparsePoint(original);
            var relative = Path.GetRelativePath(root, original);
            var backup = Path.Combine(backupRoot, "data", relative);
            if (isDirectory)
                CopyDirectory(original, backup, cancellationToken);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(original, backup, overwrite: true);
            }

            entries.Add(new RepairRestoreEntry(original, backup, isDirectory));
        }

        return Task.FromResult(new RepairRestorePoint(
            Guid.NewGuid(),
            root,
            backupRoot,
            DateTimeOffset.UtcNow,
            entries));
    }

    public Task RollbackAsync(
        RepairRestorePoint restorePoint,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(restorePoint);
        if (!confirmed)
            throw new InvalidOperationException("Explicit confirmation is required before rollback overwrites current files.");
        if (!Directory.Exists(restorePoint.BackupRoot))
            throw new DirectoryNotFoundException("Repair backup root no longer exists.");

        foreach (var entry in restorePoint.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory)
            {
                if (Directory.Exists(entry.OriginalPath))
                    Directory.Delete(entry.OriginalPath, recursive: true);
                CopyDirectory(entry.BackupPath, entry.OriginalPath, cancellationToken);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(entry.OriginalPath)!);
                File.Copy(entry.BackupPath, entry.OriginalPath, overwrite: true);
            }
        }

        return Task.CompletedTask;
    }

    private static string ResolveUnderRoot(string root, string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        var candidate = Path.GetFullPath(Path.IsPathRooted(input) ? input : Path.Combine(root, input));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Backup path escapes the selected project root.");
        return candidate;
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        RejectReparsePoint(source);
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(directory);
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)), cancellationToken);
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectReparsePoint(file);
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Refusing to backup or restore through reparse point '{path}'.");
    }
}
