using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Git.Branches;

public sealed class GitPullService : IGitPullService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);
    private readonly IProcessRunner _processRunner;

    public GitPullService(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<GitPullResult> PullAsync(
        GitPullRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                GitPullStatus.Cancelled,
                "Pull was cancelled before it started.");
        }

        if (string.IsNullOrWhiteSpace(request.GitExecutablePath))
        {
            return Failure(
                GitPullStatus.InvalidRequest,
                "Git executable path is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RepositoryPath))
        {
            return Failure(
                GitPullStatus.InvalidRepository,
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
                GitPullStatus.InvalidRepository,
                $"Repository path is invalid: {ex.Message}");
        }

        if (!Directory.Exists(repositoryPath))
        {
            return Failure(
                GitPullStatus.InvalidRepository,
                "Repository path does not exist.");
        }

        var gitMetadataPath = Path.Combine(repositoryPath, ".git");
        if (!Directory.Exists(gitMetadataPath) && !File.Exists(gitMetadataPath))
        {
            return Failure(
                GitPullStatus.InvalidRepository,
                "Selected folder is not a Git working tree.");
        }

        var timeout = request.Timeout ?? DefaultTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            return Failure(
                GitPullStatus.InvalidRequest,
                "Pull timeout must be greater than zero.");
        }

        var environment = new Dictionary<string, string?>
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never"
        };

        var branchProbeResult = await RunGitAsync(
            request.GitExecutablePath,
            repositoryPath,
            new[] { "branch", "--show-current" },
            environment,
            timeout,
            "Read current Git branch",
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!branchProbeResult.IsSuccess)
        {
            return new GitPullResult(
                MapProcessStatus(branchProbeResult.Status),
                Message: DescribeFailure(branchProbeResult, "Could not read the current Git branch."),
                BranchProbeResult: branchProbeResult);
        }

        var currentBranch = FirstStdOutLine(branchProbeResult);
        if (string.IsNullOrWhiteSpace(currentBranch))
        {
            return new GitPullResult(
                GitPullStatus.DetachedHead,
                Message: "The repository is in detached HEAD state; pull requires an attached branch.",
                BranchProbeResult: branchProbeResult);
        }

        var upstreamProbeResult = await RunGitAsync(
            request.GitExecutablePath,
            repositoryPath,
            new[] { "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}" },
            environment,
            timeout,
            "Read Git upstream",
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!upstreamProbeResult.IsSuccess)
        {
            if (upstreamProbeResult.Status is ProcessExecutionStatus.Cancelled or ProcessExecutionStatus.TimedOut)
            {
                return new GitPullResult(
                    MapProcessStatus(upstreamProbeResult.Status),
                    CurrentBranch: currentBranch,
                    Message: DescribeFailure(upstreamProbeResult, "Could not read the branch upstream."),
                    BranchProbeResult: branchProbeResult,
                    UpstreamProbeResult: upstreamProbeResult);
            }

            return new GitPullResult(
                GitPullStatus.NoUpstream,
                CurrentBranch: currentBranch,
                Message: DescribeFailure(
                    upstreamProbeResult,
                    $"Branch '{currentBranch}' does not have a usable upstream branch."),
                BranchProbeResult: branchProbeResult,
                UpstreamProbeResult: upstreamProbeResult);
        }

        var upstream = FirstStdOutLine(upstreamProbeResult);
        if (string.IsNullOrWhiteSpace(upstream))
        {
            return new GitPullResult(
                GitPullStatus.NoUpstream,
                CurrentBranch: currentBranch,
                Message: $"Branch '{currentBranch}' does not have a usable upstream branch.",
                BranchProbeResult: branchProbeResult,
                UpstreamProbeResult: upstreamProbeResult);
        }

        var beforeHeadResult = await RunGitAsync(
            request.GitExecutablePath,
            repositoryPath,
            new[] { "rev-parse", "--verify", "HEAD" },
            environment,
            timeout,
            "Read Git HEAD before pull",
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!beforeHeadResult.IsSuccess)
        {
            return new GitPullResult(
                MapVerificationStatus(beforeHeadResult.Status),
                CurrentBranch: currentBranch,
                Upstream: upstream,
                Message: DescribeFailure(beforeHeadResult, "Could not read HEAD before pulling."),
                BranchProbeResult: branchProbeResult,
                UpstreamProbeResult: upstreamProbeResult,
                BeforeHeadResult: beforeHeadResult);
        }

        var beforeCommit = FirstStdOutLine(beforeHeadResult);
        if (string.IsNullOrWhiteSpace(beforeCommit))
        {
            return new GitPullResult(
                GitPullStatus.VerificationFailed,
                CurrentBranch: currentBranch,
                Upstream: upstream,
                Message: "Git did not report a valid HEAD commit before pulling.",
                BranchProbeResult: branchProbeResult,
                UpstreamProbeResult: upstreamProbeResult,
                BeforeHeadResult: beforeHeadResult);
        }

        var pullResult = await RunGitAsync(
            request.GitExecutablePath,
            repositoryPath,
            new[] { "pull", "--ff-only" },
            environment,
            timeout,
            "Pull current Git branch",
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!pullResult.IsSuccess)
        {
            return new GitPullResult(
                MapProcessStatus(pullResult.Status),
                CurrentBranch: currentBranch,
                Upstream: upstream,
                BeforeCommitSha: beforeCommit,
                Message: DescribeFailure(
                    pullResult,
                    $"Git could not fast-forward branch '{currentBranch}' from '{upstream}'."),
                BranchProbeResult: branchProbeResult,
                UpstreamProbeResult: upstreamProbeResult,
                BeforeHeadResult: beforeHeadResult,
                PullProcessResult: pullResult);
        }

        var postBranchProbeResult = await RunGitAsync(
            request.GitExecutablePath,
            repositoryPath,
            new[] { "branch", "--show-current" },
            environment,
            timeout,
            "Verify current Git branch after pull",
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!postBranchProbeResult.IsSuccess)
        {
            return new GitPullResult(
                MapVerificationStatus(postBranchProbeResult.Status),
                CurrentBranch: currentBranch,
                Upstream: upstream,
                BeforeCommitSha: beforeCommit,
                Message: DescribeFailure(
                    postBranchProbeResult,
                    "Could not verify the current branch after pulling."),
                BranchProbeResult: branchProbeResult,
                UpstreamProbeResult: upstreamProbeResult,
                BeforeHeadResult: beforeHeadResult,
                PullProcessResult: pullResult,
                PostBranchProbeResult: postBranchProbeResult);
        }

        var branchAfterPull = FirstStdOutLine(postBranchProbeResult);
        if (!string.Equals(currentBranch, branchAfterPull, StringComparison.Ordinal))
        {
            return new GitPullResult(
                GitPullStatus.VerificationFailed,
                CurrentBranch: branchAfterPull,
                Upstream: upstream,
                BeforeCommitSha: beforeCommit,
                Message: $"Current branch changed unexpectedly from '{currentBranch}' to '{branchAfterPull ?? "<detached>"}' during pull.",
                BranchProbeResult: branchProbeResult,
                UpstreamProbeResult: upstreamProbeResult,
                BeforeHeadResult: beforeHeadResult,
                PullProcessResult: pullResult,
                PostBranchProbeResult: postBranchProbeResult);
        }

        var afterHeadResult = await RunGitAsync(
            request.GitExecutablePath,
            repositoryPath,
            new[] { "rev-parse", "--verify", "HEAD" },
            environment,
            timeout,
            "Read Git HEAD after pull",
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!afterHeadResult.IsSuccess)
        {
            return new GitPullResult(
                MapVerificationStatus(afterHeadResult.Status),
                CurrentBranch: currentBranch,
                Upstream: upstream,
                BeforeCommitSha: beforeCommit,
                Message: DescribeFailure(afterHeadResult, "Could not read HEAD after pulling."),
                BranchProbeResult: branchProbeResult,
                UpstreamProbeResult: upstreamProbeResult,
                BeforeHeadResult: beforeHeadResult,
                PullProcessResult: pullResult,
                PostBranchProbeResult: postBranchProbeResult,
                AfterHeadResult: afterHeadResult);
        }

        var afterCommit = FirstStdOutLine(afterHeadResult);
        if (string.IsNullOrWhiteSpace(afterCommit))
        {
            return new GitPullResult(
                GitPullStatus.VerificationFailed,
                CurrentBranch: currentBranch,
                Upstream: upstream,
                BeforeCommitSha: beforeCommit,
                Message: "Git did not report a valid HEAD commit after pulling.",
                BranchProbeResult: branchProbeResult,
                UpstreamProbeResult: upstreamProbeResult,
                BeforeHeadResult: beforeHeadResult,
                PullProcessResult: pullResult,
                PostBranchProbeResult: postBranchProbeResult,
                AfterHeadResult: afterHeadResult);
        }

        var changed = !string.Equals(beforeCommit, afterCommit, StringComparison.OrdinalIgnoreCase);
        return new GitPullResult(
            changed ? GitPullStatus.FastForwarded : GitPullStatus.UpToDate,
            currentBranch,
            upstream,
            beforeCommit,
            afterCommit,
            changed
                ? $"Fast-forwarded '{currentBranch}' from {beforeCommit} to {afterCommit}."
                : $"Branch '{currentBranch}' is already up to date at {afterCommit}.",
            branchProbeResult,
            upstreamProbeResult,
            beforeHeadResult,
            pullResult,
            postBranchProbeResult,
            afterHeadResult);
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

    private static string? FirstStdOutLine(ProcessResult result)
        => result.Output
            .FirstOrDefault(static line =>
                line.Stream == ProcessStream.StdOut &&
                !string.IsNullOrWhiteSpace(line.Text))
            ?.Text
            .Trim();

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

    private static GitPullStatus MapProcessStatus(ProcessExecutionStatus status)
        => status switch
        {
            ProcessExecutionStatus.Cancelled => GitPullStatus.Cancelled,
            ProcessExecutionStatus.TimedOut => GitPullStatus.TimedOut,
            _ => GitPullStatus.Failed
        };

    private static GitPullStatus MapVerificationStatus(ProcessExecutionStatus status)
        => status switch
        {
            ProcessExecutionStatus.Cancelled => GitPullStatus.Cancelled,
            ProcessExecutionStatus.TimedOut => GitPullStatus.TimedOut,
            _ => GitPullStatus.VerificationFailed
        };

    private static GitPullResult Failure(GitPullStatus status, string message)
        => new(status, Message: message);
}
