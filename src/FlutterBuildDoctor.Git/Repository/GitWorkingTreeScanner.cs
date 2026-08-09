using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Git.Repository;

public sealed class GitWorkingTreeScanner : IGitWorkingTreeScanner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly IProcessRunner _processRunner;

    public GitWorkingTreeScanner(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<GitWorkingTreeScanResult> ScanAsync(
        GitWorkingTreeScanRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                GitWorkingTreeScanStatus.Cancelled,
                "Working-tree scan was cancelled before it started.");
        }

        if (string.IsNullOrWhiteSpace(request.GitExecutablePath))
        {
            return Failure(
                GitWorkingTreeScanStatus.InvalidRequest,
                "Git executable path is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RepositoryPath))
        {
            return Failure(
                GitWorkingTreeScanStatus.InvalidRepository,
                "Repository path is required.");
        }

        string repositoryPath;
        try
        {
            repositoryPath = Path.GetFullPath(request.RepositoryPath.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(
                GitWorkingTreeScanStatus.InvalidRepository,
                $"Repository path is invalid: {ex.Message}");
        }

        if (!Directory.Exists(repositoryPath))
        {
            return Failure(
                GitWorkingTreeScanStatus.InvalidRepository,
                "Repository path does not exist.");
        }

        var gitMetadataPath = Path.Combine(repositoryPath, ".git");
        if (!Directory.Exists(gitMetadataPath) && !File.Exists(gitMetadataPath))
        {
            return Failure(
                GitWorkingTreeScanStatus.InvalidRepository,
                "Selected folder is not a Git working tree.");
        }

        var timeout = request.Timeout ?? DefaultTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            return Failure(
                GitWorkingTreeScanStatus.InvalidRequest,
                "Working-tree scan timeout must be greater than zero.");
        }

        var environment = new Dictionary<string, string?>
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never"
        };

        var processResult = await RunGitAsync(
            request.GitExecutablePath,
            repositoryPath,
            new[] { "status", "--porcelain=v1", "-z", "--untracked-files=all" },
            environment,
            timeout,
            progress,
            cancellationToken).ConfigureAwait(false);

        var rawStatus = string.Concat(
            processResult.Output
                .Where(static line => line.Stream == ProcessStream.StdOut)
                .Select(static line => line.Text));

        if (!processResult.IsSuccess)
        {
            return new GitWorkingTreeScanResult(
                MapProcessStatus(processResult.Status),
                Array.Empty<GitWorkingTreeChange>(),
                DescribeFailure(processResult, "Git working-tree scan failed."),
                rawStatus,
                processResult);
        }

        if (!TryParsePorcelainV1Z(rawStatus, out var changes, out var parseError))
        {
            return new GitWorkingTreeScanResult(
                GitWorkingTreeScanStatus.ParseFailed,
                Array.Empty<GitWorkingTreeChange>(),
                parseError ?? "Git working-tree status could not be parsed.",
                rawStatus,
                processResult);
        }

        return new GitWorkingTreeScanResult(
            GitWorkingTreeScanStatus.Succeeded,
            changes,
            changes.Count == 0
                ? "Working tree is clean."
                : $"Working tree has {changes.Count} change(s).",
            rawStatus,
            processResult);
    }

    private async Task<ProcessResult> RunGitAsync(
        string gitExecutablePath,
        string repositoryPath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment,
        TimeSpan timeout,
        IProgress<ProcessOutputLine>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _processRunner.RunAsync(
                new ProcessRequest(
                    gitExecutablePath.Trim(),
                    arguments,
                    WorkingDirectory: repositoryPath,
                    Environment: environment,
                    Timeout: timeout,
                    DisplayName: "Inspect Git working tree"),
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var timestamp = DateTimeOffset.UtcNow;
            return new ProcessResult(
                ProcessExecutionStatus.Cancelled,
                null,
                timestamp,
                timestamp,
                Array.Empty<ProcessOutputLine>(),
                "Inspect Git working tree",
                "Process was cancelled.");
        }
        catch (Exception ex)
        {
            var timestamp = DateTimeOffset.UtcNow;
            return new ProcessResult(
                ProcessExecutionStatus.Failed,
                null,
                timestamp,
                timestamp,
                Array.Empty<ProcessOutputLine>(),
                "Inspect Git working tree",
                ex.Message);
        }
    }

    internal static bool TryParsePorcelainV1Z(
        string rawStatus,
        out IReadOnlyList<GitWorkingTreeChange> changes,
        out string? error)
    {
        var parsed = new List<GitWorkingTreeChange>();
        changes = parsed;
        error = null;

        if (string.IsNullOrEmpty(rawStatus))
        {
            return true;
        }

        var records = rawStatus.Split('\0');
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            if (record.Length == 0)
            {
                if (index == records.Length - 1)
                {
                    continue;
                }

                error = $"Git status contained an empty record at index {index}.";
                changes = Array.Empty<GitWorkingTreeChange>();
                return false;
            }

            if (record.Length < 4 || record[2] != ' ')
            {
                error = $"Git status record {index} does not match porcelain v1 format.";
                changes = Array.Empty<GitWorkingTreeChange>();
                return false;
            }

            var indexStatus = record[0];
            var workTreeStatus = record[1];
            var statusCode = record[..2];
            var path = record[3..];
            if (string.IsNullOrEmpty(path))
            {
                error = $"Git status record {index} does not contain a path.";
                changes = Array.Empty<GitWorkingTreeChange>();
                return false;
            }

            string? originalPath = null;
            if (IsRenameOrCopy(indexStatus) || IsRenameOrCopy(workTreeStatus))
            {
                if (++index >= records.Length || string.IsNullOrEmpty(records[index]))
                {
                    error = "Git rename/copy status is missing its original path.";
                    changes = Array.Empty<GitWorkingTreeChange>();
                    return false;
                }

                originalPath = records[index];
            }

            parsed.Add(new GitWorkingTreeChange(
                path,
                Classify(statusCode),
                statusCode,
                IsStaged(indexStatus, statusCode),
                IsUnstaged(workTreeStatus, statusCode),
                originalPath));
        }

        changes = parsed;
        return true;
    }

    private static GitWorkingTreeChangeKind Classify(string statusCode)
    {
        if (statusCode == "??")
        {
            return GitWorkingTreeChangeKind.Untracked;
        }

        if (IsUnmerged(statusCode))
        {
            return GitWorkingTreeChangeKind.Unmerged;
        }

        if (statusCode.Contains('R'))
        {
            return GitWorkingTreeChangeKind.Renamed;
        }

        if (statusCode.Contains('C'))
        {
            return GitWorkingTreeChangeKind.Copied;
        }

        if (statusCode.Contains('A'))
        {
            return GitWorkingTreeChangeKind.Added;
        }

        if (statusCode.Contains('D'))
        {
            return GitWorkingTreeChangeKind.Deleted;
        }

        if (statusCode.Contains('T'))
        {
            return GitWorkingTreeChangeKind.TypeChanged;
        }

        if (statusCode.Contains('M'))
        {
            return GitWorkingTreeChangeKind.Modified;
        }

        return GitWorkingTreeChangeKind.Unknown;
    }

    private static bool IsUnmerged(string statusCode)
        => statusCode is "DD" or "AU" or "UD" or "UA" or "DU" or "AA" or "UU";

    private static bool IsRenameOrCopy(char status)
        => status is 'R' or 'C';

    private static bool IsStaged(char indexStatus, string statusCode)
        => statusCode != "??" && indexStatus is not (' ' or '?' or '!');

    private static bool IsUnstaged(char workTreeStatus, string statusCode)
        => statusCode == "??" || workTreeStatus is not (' ' or '?' or '!');

    private static string DescribeFailure(ProcessResult result, string fallback)
    {
        var evidence = result.Output
            .LastOrDefault(static line =>
                line.Stream == ProcessStream.StdErr &&
                !string.IsNullOrWhiteSpace(line.Text))
            ?.Text
            .Trim();

        if (!string.IsNullOrWhiteSpace(evidence))
        {
            return evidence;
        }

        return string.IsNullOrWhiteSpace(result.FailureReason)
            ? fallback
            : result.FailureReason;
    }

    private static GitWorkingTreeScanStatus MapProcessStatus(ProcessExecutionStatus status)
        => status switch
        {
            ProcessExecutionStatus.Cancelled => GitWorkingTreeScanStatus.Cancelled,
            ProcessExecutionStatus.TimedOut => GitWorkingTreeScanStatus.TimedOut,
            _ => GitWorkingTreeScanStatus.Failed
        };

    private static GitWorkingTreeScanResult Failure(
        GitWorkingTreeScanStatus status,
        string message)
        => new(status, Array.Empty<GitWorkingTreeChange>(), message);
}
