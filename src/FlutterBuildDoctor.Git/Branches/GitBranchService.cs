using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Git.Branches;

public sealed class GitBranchService : IGitBranchService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
    private readonly IProcessRunner _processRunner;

    public GitBranchService(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<GitBranchDiscoveryResult> GetBranchesAsync(
        GitBranchDiscoveryRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                GitBranchDiscoveryStatus.Cancelled,
                "Branch discovery was cancelled before it started.");
        }

        if (string.IsNullOrWhiteSpace(request.GitExecutablePath))
        {
            return Failure(
                GitBranchDiscoveryStatus.InvalidRequest,
                "Git executable path is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RepositoryPath))
        {
            return Failure(
                GitBranchDiscoveryStatus.InvalidRepository,
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
                GitBranchDiscoveryStatus.InvalidRepository,
                $"Repository path is invalid: {ex.Message}");
        }

        if (!Directory.Exists(repositoryPath))
        {
            return Failure(
                GitBranchDiscoveryStatus.InvalidRepository,
                "Repository path does not exist.");
        }

        var gitMetadataPath = Path.Combine(repositoryPath, ".git");
        if (!Directory.Exists(gitMetadataPath) && !File.Exists(gitMetadataPath))
        {
            return Failure(
                GitBranchDiscoveryStatus.InvalidRepository,
                "Selected folder is not a Git working tree.");
        }

        var timeout = request.Timeout ?? DefaultTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            return Failure(
                GitBranchDiscoveryStatus.InvalidRequest,
                "Branch discovery timeout must be greater than zero.");
        }

        var environment = new Dictionary<string, string?>
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never"
        };

        ProcessResult? refreshResult = null;
        string? refreshWarning = null;

        if (request.RefreshRemotes)
        {
            refreshResult = await RunGitAsync(
                request.GitExecutablePath,
                repositoryPath,
                new[] { "fetch", "--prune", "--all", "--no-tags" },
                environment,
                timeout,
                "Refresh Git remotes",
                progress,
                cancellationToken).ConfigureAwait(false);

            if (!refreshResult.IsSuccess)
            {
                if (refreshResult.Status is ProcessExecutionStatus.Cancelled or ProcessExecutionStatus.TimedOut)
                {
                    return new GitBranchDiscoveryResult(
                        MapStatus(refreshResult.Status),
                        Array.Empty<GitBranchInfo>(),
                        refreshResult.FailureReason ?? "Remote refresh did not complete.",
                        refreshResult);
                }

                refreshWarning = refreshResult.FailureReason ??
                    "Remote refresh failed; cached local and remote references will be shown.";
            }
        }

        var listResult = await RunGitAsync(
            request.GitExecutablePath,
            repositoryPath,
            new[]
            {
                "for-each-ref",
                "--format=%(refname)%09%(objectname)%09%(upstream:short)%09%(HEAD)%09%(symref)",
                "refs/heads",
                "refs/remotes"
            },
            environment,
            timeout,
            "List Git branches",
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!listResult.IsSuccess)
        {
            return new GitBranchDiscoveryResult(
                MapStatus(listResult.Status),
                Array.Empty<GitBranchInfo>(),
                listResult.FailureReason ?? "Git branch enumeration failed.",
                refreshResult,
                listResult);
        }

        var branches = ParseBranches(listResult);
        var status = refreshWarning is null
            ? GitBranchDiscoveryStatus.Succeeded
            : GitBranchDiscoveryStatus.SucceededWithWarning;
        var message = refreshWarning is null
            ? $"Found {branches.Count} local/remote branch reference(s)."
            : $"{refreshWarning} Showing {branches.Count} cached branch reference(s).";

        return new GitBranchDiscoveryResult(
            status,
            branches,
            message,
            refreshResult,
            listResult);
    }

    private async Task<ProcessResult> RunGitAsync(
        string gitExecutablePath,
        string repositoryPath,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment,
        TimeSpan timeout,
        string displayName,
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
                    DisplayName: displayName),
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
                displayName,
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
                displayName,
                ex.Message);
        }
    }

    private static IReadOnlyList<GitBranchInfo> ParseBranches(ProcessResult result)
    {
        var branches = new List<GitBranchInfo>();

        foreach (var output in result.Output.Where(static line => line.Stream == ProcessStream.StdOut))
        {
            if (TryParseBranch(output.Text, out var branch))
            {
                branches.Add(branch);
            }
        }

        return branches
            .OrderByDescending(static branch => branch.IsCurrent)
            .ThenBy(static branch => branch.Kind)
            .ThenBy(static branch => branch.RemoteName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static branch => branch.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryParseBranch(string line, out GitBranchInfo branch)
    {
        branch = default!;
        var fields = line.Split('\t');
        if (fields.Length < 5)
        {
            return false;
        }

        var fullName = fields[0].Trim();
        var commitSha = fields[1].Trim();
        var upstream = NullIfEmpty(fields[2]);
        var isCurrent = fields[3].Trim() == "*";
        var symbolicTarget = NullIfEmpty(fields[4]);

        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(commitSha))
        {
            return false;
        }

        if (fullName.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            var name = fullName["refs/heads/".Length..];
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            branch = new GitBranchInfo(
                name,
                fullName,
                GitBranchKind.Local,
                commitSha,
                isCurrent,
                Upstream: upstream);
            return true;
        }

        if (!fullName.StartsWith("refs/remotes/", StringComparison.Ordinal) || symbolicTarget is not null)
        {
            return false;
        }

        var remoteRef = fullName["refs/remotes/".Length..];
        var separatorIndex = remoteRef.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == remoteRef.Length - 1)
        {
            return false;
        }

        var remoteName = remoteRef[..separatorIndex];
        var name = remoteRef[(separatorIndex + 1)..];

        branch = new GitBranchInfo(
            name,
            fullName,
            GitBranchKind.Remote,
            commitSha,
            false,
            remoteName,
            upstream);
        return true;
    }

    private static string? NullIfEmpty(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static GitBranchDiscoveryStatus MapStatus(ProcessExecutionStatus status)
        => status switch
        {
            ProcessExecutionStatus.Cancelled => GitBranchDiscoveryStatus.Cancelled,
            ProcessExecutionStatus.TimedOut => GitBranchDiscoveryStatus.TimedOut,
            _ => GitBranchDiscoveryStatus.Failed
        };

    private static GitBranchDiscoveryResult Failure(
        GitBranchDiscoveryStatus status,
        string message)
        => new(status, Array.Empty<GitBranchInfo>(), message);
}
