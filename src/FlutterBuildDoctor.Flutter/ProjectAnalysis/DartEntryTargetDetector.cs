using System.IO;
using System.Text;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed class DartEntryTargetDetector : IDartEntryTargetDetector
{
    private const int MaxCandidateFiles = 128;
    private const int MaxVisitedDirectories = 256;
    private const int MaxDepth = 4;
    private const long MaxFileBytes = 512 * 1024;
    private const int MaxIssues = 128;

    public DartEntryTargetDetectionResult Detect(FlutterProjectRootResult projectRoot)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);

        if (!projectRoot.IsSuccess || string.IsNullOrWhiteSpace(projectRoot.EffectiveRoot))
        {
            return Result(
                DartEntryTargetDetectionStatus.ProjectRootUnavailable,
                projectRoot,
                null,
                Array.Empty<DartEntryTarget>(),
                Array.Empty<DartEntryScanIssue>(),
                0,
                "A successful FBD-601 Flutter project root is required before Dart entry-target detection.");
        }

        string root;
        string libDirectory;
        try
        {
            root = Path.GetFullPath(projectRoot.EffectiveRoot);
            libDirectory = Path.GetFullPath(Path.Combine(root, "lib"));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result(
                DartEntryTargetDetectionStatus.UnsafePath,
                projectRoot,
                null,
                Array.Empty<DartEntryTarget>(),
                Array.Empty<DartEntryScanIssue>(),
                0,
                $"Flutter project/lib path is invalid: {ex.Message}");
        }

        if (!IsWithinPath(libDirectory, root))
        {
            return Result(
                DartEntryTargetDetectionStatus.UnsafePath,
                projectRoot,
                libDirectory,
                Array.Empty<DartEntryTarget>(),
                Array.Empty<DartEntryScanIssue>(),
                0,
                "The computed lib directory escapes the FBD-601 project root.");
        }

        try
        {
            if (!Directory.Exists(root))
            {
                return Result(
                    DartEntryTargetDetectionStatus.ProjectRootUnavailable,
                    projectRoot,
                    libDirectory,
                    Array.Empty<DartEntryTarget>(),
                    Array.Empty<DartEntryScanIssue>(),
                    0,
                    "The FBD-601 project root is stale because the directory no longer exists.");
            }

            if (IsReparsePoint(root))
            {
                return Result(
                    DartEntryTargetDetectionStatus.UnsafePath,
                    projectRoot,
                    libDirectory,
                    Array.Empty<DartEntryTarget>(),
                    Array.Empty<DartEntryScanIssue>(),
                    0,
                    "The Flutter project root is a reparse point/symbolic link and was not traversed.");
            }

            if (!Directory.Exists(libDirectory))
            {
                return Result(
                    DartEntryTargetDetectionStatus.LibDirectoryUnavailable,
                    projectRoot,
                    libDirectory,
                    Array.Empty<DartEntryTarget>(),
                    Array.Empty<DartEntryScanIssue>(),
                    0,
                    "The Flutter project does not currently contain a lib directory.");
            }

            if (IsReparsePoint(libDirectory))
            {
                return Result(
                    DartEntryTargetDetectionStatus.UnsafePath,
                    projectRoot,
                    libDirectory,
                    Array.Empty<DartEntryTarget>(),
                    Array.Empty<DartEntryScanIssue>(),
                    0,
                    "The Flutter lib directory is a reparse point/symbolic link and was not traversed.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Result(
                DartEntryTargetDetectionStatus.InspectionFailed,
                projectRoot,
                libDirectory,
                Array.Empty<DartEntryTarget>(),
                Array.Empty<DartEntryScanIssue>(),
                0,
                $"The Flutter project/lib boundary could not be inspected: {ex.Message}");
        }

        var targets = new List<DartEntryTarget>();
        var issues = new List<DartEntryScanIssue>();
        var pending = new Stack<DirectoryWorkItem>();
        pending.Push(new DirectoryWorkItem(libDirectory, 0));

        var visitedDirectories = 0;
        var candidateLimitReached = false;
        var directoryLimitReached = false;

        while (pending.Count > 0 && !candidateLimitReached && !directoryLimitReached)
        {
            if (visitedDirectories >= MaxVisitedDirectories)
            {
                directoryLimitReached = true;
                AddIssue(
                    issues,
                    DartEntryScanIssueKind.DirectoryLimitReached,
                    "lib",
                    $"Directory scan stopped after {MaxVisitedDirectories} visited directories.");
                break;
            }

            var current = pending.Pop();
            visitedDirectories++;

            string[] entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current.Path)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                AddIssue(
                    issues,
                    DartEntryScanIssueKind.EnumerationFailed,
                    ToSafeRelativeProjectPath(root, current.Path),
                    $"Directory could not be enumerated: {ex.Message}");
                continue;
            }

            for (var index = entries.Length - 1; index >= 0; index--)
            {
                var entry = entries[index];
                string fullPath;
                FileAttributes attributes;
                try
                {
                    fullPath = Path.GetFullPath(entry);
                    if (!IsWithinPath(fullPath, libDirectory))
                    {
                        AddIssue(
                            issues,
                            DartEntryScanIssueKind.ReparsePointSkipped,
                            ToSafeRelativeProjectPath(root, entry),
                            "Entry resolved outside the lib boundary and was skipped.");
                        continue;
                    }

                    attributes = File.GetAttributes(fullPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
                {
                    AddIssue(
                        issues,
                        DartEntryScanIssueKind.EnumerationFailed,
                        ToSafeRelativeProjectPath(root, entry),
                        $"Entry metadata could not be inspected: {ex.Message}");
                    continue;
                }

                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;

                if (isDirectory)
                {
                    if (isReparsePoint)
                    {
                        AddIssue(
                            issues,
                            DartEntryScanIssueKind.ReparsePointSkipped,
                            ToSafeRelativeProjectPath(root, fullPath),
                            "Reparse/symlink directory was not traversed.");
                        continue;
                    }

                    if (current.Depth >= MaxDepth)
                    {
                        AddIssue(
                            issues,
                            DartEntryScanIssueKind.DepthLimitReached,
                            ToSafeRelativeProjectPath(root, fullPath),
                            $"Directory was not traversed beyond the configured depth limit of {MaxDepth}.");
                        continue;
                    }

                    pending.Push(new DirectoryWorkItem(fullPath, current.Depth + 1));
                    continue;
                }

                if (!TryClassifyCandidate(libDirectory, fullPath, out var kind, out var flavorHint))
                    continue;

                if (targets.Count >= MaxCandidateFiles)
                {
                    candidateLimitReached = true;
                    AddIssue(
                        issues,
                        DartEntryScanIssueKind.CandidateLimitReached,
                        ToSafeRelativeProjectPath(root, fullPath),
                        $"Candidate scan stopped after {MaxCandidateFiles} main-like Dart files.");
                    break;
                }

                var relativeTargetPath = ToSafeRelativeProjectPath(root, fullPath);
                if (isReparsePoint)
                {
                    targets.Add(new DartEntryTarget(
                        fullPath,
                        relativeTargetPath,
                        kind,
                        flavorHint,
                        DartEntryTargetInspectionStatus.UnsafePath,
                        null,
                        "Candidate is a reparse point/symbolic link and was not read."));
                    AddIssue(
                        issues,
                        DartEntryScanIssueKind.ReparsePointSkipped,
                        relativeTargetPath,
                        "Main-like candidate is a reparse point/symbolic link.");
                    continue;
                }

                targets.Add(InspectCandidate(fullPath, relativeTargetPath, kind, flavorHint));
            }
        }

        targets.Sort(CompareTargets);

        if (candidateLimitReached || directoryLimitReached)
        {
            return Result(
                DartEntryTargetDetectionStatus.ScanLimitExceeded,
                projectRoot,
                libDirectory,
                targets,
                issues,
                visitedDirectories,
                "Dart entry-target scanning stopped at a configured safety limit; the result is intentionally incomplete.");
        }

        if (targets.Count == 0)
        {
            return Result(
                issues.Count == 0
                    ? DartEntryTargetDetectionStatus.NoTargets
                    : DartEntryTargetDetectionStatus.Partial,
                projectRoot,
                libDirectory,
                targets,
                issues,
                visitedDirectories,
                issues.Count == 0
                    ? "No canonical or conventional main-like Dart entry targets were found under lib."
                    : "No entry target was confirmed, and one or more lib paths could not be inspected completely.");
        }

        var hasNonRunnableTarget = targets.Any(target => !target.IsRunnable);
        var status = hasNonRunnableTarget || issues.Count > 0
            ? DartEntryTargetDetectionStatus.Partial
            : DartEntryTargetDetectionStatus.Succeeded;

        return Result(
            status,
            projectRoot,
            libDirectory,
            targets,
            issues,
            visitedDirectories,
            status == DartEntryTargetDetectionStatus.Succeeded
                ? $"Detected {targets.Count} runnable canonical/conventional Dart entry target(s) without executing Flutter or Dart."
                : $"Detected {targets.Count} main-like Dart candidate(s); unresolved or unsafe candidates are preserved explicitly.");
    }

    private static DartEntryTarget InspectCandidate(
        string fullPath,
        string relativeTargetPath,
        DartEntryTargetKind kind,
        string? flavorHint)
    {
        long size;
        try
        {
            var info = new FileInfo(fullPath);
            size = info.Length;
            if (size > MaxFileBytes)
            {
                return new DartEntryTarget(
                    fullPath,
                    relativeTargetPath,
                    kind,
                    flavorHint,
                    DartEntryTargetInspectionStatus.FileTooLarge,
                    size,
                    $"Candidate exceeds the {MaxFileBytes} byte static-inspection limit.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new DartEntryTarget(
                fullPath,
                relativeTargetPath,
                kind,
                flavorHint,
                DartEntryTargetInspectionStatus.ReadFailed,
                null,
                $"Candidate metadata could not be read: {ex.Message}");
        }

        string text;
        try
        {
            text = File.ReadAllText(fullPath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new DartEntryTarget(
                fullPath,
                relativeTargetPath,
                kind,
                flavorHint,
                DartEntryTargetInspectionStatus.ReadFailed,
                size,
                $"Candidate source could not be read: {ex.Message}");
        }

        if (!HasTopLevelMainDeclaration(text))
        {
            return new DartEntryTarget(
                fullPath,
                relativeTargetPath,
                kind,
                flavorHint,
                DartEntryTargetInspectionStatus.MainDeclarationMissing,
                size,
                "Filename matches an entry-target convention, but no top-level main(...) declaration was found.");
        }

        return new DartEntryTarget(
            fullPath,
            relativeTargetPath,
            kind,
            flavorHint,
            DartEntryTargetInspectionStatus.Runnable,
            size,
            "Top-level main(...) declaration detected by bounded static inspection.");
    }

    private static bool TryClassifyCandidate(
        string libDirectory,
        string fullPath,
        out DartEntryTargetKind kind,
        out string? flavorHint)
    {
        kind = default;
        flavorHint = null;

        var fileName = Path.GetFileName(fullPath);
        if (!fileName.EndsWith(".dart", StringComparison.Ordinal))
            return false;

        var relativeToLib = Path.GetRelativePath(libDirectory, fullPath);
        var isLibRootFile = !relativeToLib.Contains(Path.DirectorySeparatorChar) &&
                            !relativeToLib.Contains(Path.AltDirectorySeparatorChar);

        if (string.Equals(fileName, "main.dart", StringComparison.Ordinal))
        {
            kind = isLibRootFile
                ? DartEntryTargetKind.CanonicalMain
                : DartEntryTargetKind.NestedMain;
            return true;
        }

        if (TryExtractFlavorHint(fileName, "main_", ".dart", out flavorHint) ||
            TryExtractFlavorHint(fileName, "main.", ".dart", out flavorHint) ||
            TryExtractFlavorHint(fileName, "main-", ".dart", out flavorHint))
        {
            kind = DartEntryTargetKind.ConventionalFlavorMain;
            return true;
        }

        return false;
    }

    private static bool TryExtractFlavorHint(
        string fileName,
        string prefix,
        string suffix,
        out string? hint)
    {
        hint = null;
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(suffix, StringComparison.Ordinal) ||
            fileName.Length <= prefix.Length + suffix.Length)
        {
            return false;
        }

        var candidate = fileName[prefix.Length..^suffix.Length];
        if (candidate.Length == 0 || candidate.Length > 128 ||
            candidate.Any(char.IsControl) || candidate.Any(char.IsWhiteSpace) ||
            candidate.Contains('/') || candidate.Contains('\\'))
        {
            return true;
        }

        hint = candidate;
        return true;
    }

    private static bool HasTopLevelMainDeclaration(string text)
    {
        var mask = BuildStructuralMask(text);
        var braceDepth = 0;
        var index = 0;

        while (index < mask.Length)
        {
            var current = mask[index];
            if (current == '{')
            {
                braceDepth++;
                index++;
                continue;
            }

            if (current == '}')
            {
                if (braceDepth > 0)
                    braceDepth--;
                index++;
                continue;
            }

            if (braceDepth != 0 || !IsIdentifierStart(current))
            {
                index++;
                continue;
            }

            var tokenStart = index;
            index++;
            while (index < mask.Length && IsIdentifierPart(mask[index]))
                index++;

            if (!mask.AsSpan(tokenStart, index - tokenStart).Equals("main".AsSpan(), StringComparison.Ordinal))
                continue;

            var cursor = index;
            SkipWhitespace(mask, ref cursor);
            if (cursor >= mask.Length || mask[cursor] != '(')
                continue;

            var closeParen = FindMatchingDelimiter(mask, cursor, '(', ')');
            if (closeParen < 0)
                continue;

            cursor = closeParen + 1;
            SkipWhitespace(mask, ref cursor);

            if (TryConsumeIdentifier(mask, ref cursor, "async") ||
                TryConsumeIdentifier(mask, ref cursor, "sync"))
            {
                SkipWhitespace(mask, ref cursor);
                if (cursor < mask.Length && mask[cursor] == '*')
                {
                    cursor++;
                    SkipWhitespace(mask, ref cursor);
                }
            }

            if (cursor < mask.Length && mask[cursor] == '{')
                return true;

            if (cursor + 1 < mask.Length && mask[cursor] == '=' && mask[cursor + 1] == '>')
                return true;
        }

        return false;
    }

    private static bool TryConsumeIdentifier(string text, ref int cursor, string identifier)
    {
        if (cursor + identifier.Length > text.Length ||
            !text.AsSpan(cursor, identifier.Length).Equals(identifier.AsSpan(), StringComparison.Ordinal))
        {
            return false;
        }

        var after = cursor + identifier.Length;
        if (after < text.Length && IsIdentifierPart(text[after]))
            return false;

        cursor = after;
        return true;
    }

    private static int FindMatchingDelimiter(string text, int open, char openToken, char closeToken)
    {
        var depth = 1;
        for (var index = open + 1; index < text.Length; index++)
        {
            if (text[index] == openToken)
                depth++;
            else if (text[index] == closeToken)
                depth--;

            if (depth == 0)
                return index;
        }

        return -1;
    }

    private static string BuildStructuralMask(string text)
    {
        var output = new StringBuilder(text.Length);
        var inLineComment = false;
        var blockCommentDepth = 0;
        char quote = '\0';
        var tripleQuoted = false;
        var escaped = false;
        var rawString = false;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            var third = index + 2 < text.Length ? text[index + 2] : '\0';

            if (inLineComment)
            {
                if (current is '\r' or '\n')
                {
                    inLineComment = false;
                    output.Append(current);
                }
                else
                {
                    output.Append(' ');
                }
                continue;
            }

            if (blockCommentDepth > 0)
            {
                if (current == '/' && next == '*')
                {
                    output.Append("  ");
                    index++;
                    blockCommentDepth++;
                    continue;
                }

                if (current == '*' && next == '/')
                {
                    output.Append("  ");
                    index++;
                    blockCommentDepth--;
                    continue;
                }

                output.Append(current is '\r' or '\n' ? current : ' ');
                continue;
            }

            if (quote != '\0')
            {
                if (tripleQuoted && current == quote && next == quote && third == quote)
                {
                    output.Append("   ");
                    index += 2;
                    quote = '\0';
                    tripleQuoted = false;
                    escaped = false;
                    rawString = false;
                    continue;
                }

                output.Append(current is '\r' or '\n' ? current : ' ');
                if (tripleQuoted)
                    continue;

                if (!rawString && escaped)
                {
                    escaped = false;
                    continue;
                }

                if (!rawString && current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == quote)
                {
                    quote = '\0';
                    escaped = false;
                    rawString = false;
                }
                continue;
            }

            if (current == '/' && next == '/')
            {
                output.Append("  ");
                index++;
                inLineComment = true;
                continue;
            }

            if (current == '/' && next == '*')
            {
                output.Append("  ");
                index++;
                blockCommentDepth = 1;
                continue;
            }

            if (current is '\'' or '"')
            {
                rawString = IsRawStringPrefix(text, index);
                quote = current;
                tripleQuoted = next == current && third == current;
                if (tripleQuoted)
                {
                    output.Append("   ");
                    index += 2;
                }
                else
                {
                    output.Append(' ');
                }
                continue;
            }

            output.Append(current);
        }

        return output.ToString();
    }

    private static bool IsRawStringPrefix(string text, int quoteIndex)
    {
        if (quoteIndex == 0 || text[quoteIndex - 1] is not ('r' or 'R'))
            return false;

        var prefixIndex = quoteIndex - 1;
        return prefixIndex == 0 || !IsIdentifierPart(text[prefixIndex - 1]);
    }

    private static int CompareTargets(DartEntryTarget left, DartEntryTarget right)
    {
        var kindCompare = left.Kind.CompareTo(right.Kind);
        return kindCompare != 0
            ? kindCompare
            : StringComparer.Ordinal.Compare(left.RelativeTargetPath, right.RelativeTargetPath);
    }

    private static void AddIssue(
        ICollection<DartEntryScanIssue> issues,
        DartEntryScanIssueKind kind,
        string relativePath,
        string message)
    {
        if (issues.Count >= MaxIssues)
            return;

        issues.Add(new DartEntryScanIssue(kind, relativePath, message));
    }

    private static string ToSafeRelativeProjectPath(string root, string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!IsWithinPath(fullPath, root))
                return "<outside-project>";

            return Path.GetRelativePath(root, fullPath)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
        }
        catch
        {
            return "<invalid-path>";
        }
    }

    private static bool IsWithinPath(string candidate, string parent)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));

        if (string.Equals(normalizedCandidate, normalizedParent, comparison))
            return true;

        var prefix = normalizedParent + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, comparison);
    }

    private static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsIdentifierStart(char value)
        => char.IsLetter(value) || value is '_' or '$';

    private static bool IsIdentifierPart(char value)
        => char.IsLetterOrDigit(value) || value is '_' or '$';

    private static void SkipWhitespace(string text, ref int cursor)
    {
        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            cursor++;
    }

    private static DartEntryTargetDetectionResult Result(
        DartEntryTargetDetectionStatus status,
        FlutterProjectRootResult projectRoot,
        string? libDirectory,
        IReadOnlyList<DartEntryTarget> targets,
        IReadOnlyList<DartEntryScanIssue> issues,
        int visitedDirectories,
        string message)
        => new(
            status,
            projectRoot,
            libDirectory,
            targets,
            issues,
            visitedDirectories,
            targets.Count,
            message);

    private readonly record struct DirectoryWorkItem(string Path, int Depth);
}
