using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Git.Repository;

public sealed class GitRepositoryIdentityService : IGitRepositoryIdentityService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private readonly IProcessRunner _processRunner;

    public GitRepositoryIdentityService(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<GitRepositoryIdentityResult> ReadAsync(
        GitRepositoryIdentityRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                GitRepositoryIdentityStatus.Cancelled,
                "Repository identity probe was cancelled before it started.");
        }

        if (string.IsNullOrWhiteSpace(request.GitExecutablePath))
        {
            return Failure(
                GitRepositoryIdentityStatus.InvalidRequest,
                "Git executable path is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RepositoryPath))
        {
            return Failure(
                GitRepositoryIdentityStatus.InvalidRepository,
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
                GitRepositoryIdentityStatus.InvalidRepository,
                $"Repository path is invalid: {ex.Message}");
        }

        if (!Directory.Exists(repositoryPath))
        {
            return Failure(
                GitRepositoryIdentityStatus.InvalidRepository,
                "Repository path does not exist.");
        }

        var gitMetadataPath = Path.Combine(repositoryPath, ".git");
        if (!Directory.Exists(gitMetadataPath) && !File.Exists(gitMetadataPath))
        {
            return Failure(
                GitRepositoryIdentityStatus.InvalidRepository,
                "Selected folder is not a Git working tree.");
        }

        var timeout = request.Timeout ?? DefaultTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            return Failure(
                GitRepositoryIdentityStatus.InvalidRequest,
                "Repository identity timeout must be greater than zero.");
        }

        var environment = new Dictionary<string, string?>
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never"
        };

        var branchResult = await RunGitAsync(
            request.GitExecutablePath,
            repositoryPath,
            new[] { "branch", "--show-current" },
            environment,
            timeout,
            "Read current Git branch",
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!branchResult.IsSuccess)
        {
            return new GitRepositoryIdentityResult(
                MapProcessStatus(branchResult.Status),
                Message: DescribeFailure(branchResult, "Could not read the current Git branch."),
                BranchResult: branchResult);
        }

        var branchName = FirstStdOutLine(branchResult);
        var isDetached = string.IsNullOrWhiteSpace(branchName);

        var commitResult = await RunGitAsync(
            request.GitExecutablePath,
            repositoryPath,
            new[] { "rev-parse", "--verify", "HEAD" },
            environment,
            timeout,
            "Read Git HEAD identity",
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!commitResult.IsSuccess)
        {
            return new GitRepositoryIdentityResult(
                MapVerificationStatus(commitResult.Status),
                Message: DescribeFailure(commitResult, "Could not read the current Git commit."),
                BranchResult: branchResult,
                CommitResult: commitResult);
        }

        var commitSha = FirstStdOutLine(commitResult);
        if (string.IsNullOrWhiteSpace(commitSha))
        {
            return new GitRepositoryIdentityResult(
                GitRepositoryIdentityStatus.VerificationFailed,
                Message: "Git did not report a valid HEAD commit.",
                BranchResult: branchResult,
                CommitResult: commitResult);
        }

        ProcessResult? upstreamResult = null;
        ProcessResult? remoteResult = null;
        string? upstream = null;
        string? remoteName = null;

        if (!isDetached && branchName is not null)
        {
            upstreamResult = await RunGitAsync(
                request.GitExecutablePath,
                repositoryPath,
                new[] { "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}" },
                environment,
                timeout,
                "Read Git upstream identity",
                progress,
                cancellationToken).ConfigureAwait(false);

            if (upstreamResult.Status is ProcessExecutionStatus.Cancelled or ProcessExecutionStatus.TimedOut)
            {
                return new GitRepositoryIdentityResult(
                    MapProcessStatus(upstreamResult.Status),
                    Message: DescribeFailure(upstreamResult, "Could not read the current branch upstream."),
                    BranchResult: branchResult,
                    CommitResult: commitResult,
                    UpstreamResult: upstreamResult);
            }

            if (upstreamResult.IsSuccess)
            {
                upstream = FirstStdOutLine(upstreamResult);
            }

            remoteResult = await RunGitAsync(
                request.GitExecutablePath,
                repositoryPath,
                new[] { "config", "--get", $"branch.{branchName}.remote" },
                environment,
                timeout,
                "Read Git branch remote",
                progress,
                cancellationToken).ConfigureAwait(false);

            if (remoteResult.Status is ProcessExecutionStatus.Cancelled or ProcessExecutionStatus.TimedOut)
            {
                return new GitRepositoryIdentityResult(
                    MapProcessStatus(remoteResult.Status),
                    Message: DescribeFailure(remoteResult, "Could not read the current branch remote."),
                    BranchResult: branchResult,
                    CommitResult: commitResult,
                    UpstreamResult: upstreamResult,
                    RemoteResult: remoteResult);
            }

            if (remoteResult.IsSuccess)
            {
                remoteName = FirstStdOutLine(remoteResult);
            }
        }

        var identity = new GitRepositoryIdentity(
            repositoryPath,
            commitSha,
            branchName,
            upstream,
            remoteName,
            isDetached);

        return new GitRepositoryIdentityResult(
            GitRepositoryIdentityStatus.Succeeded,
            identity,
            isDetached
                ? $"Detached HEAD at {identity.ShortCommitSha}."
                : $"Repository identity: {branchName} at {identity.ShortCommitSha}.",
            branchResult,
            commitResult,
            upstreamResult,
            remoteResult);
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

    private static GitRepositoryIdentityStatus MapProcessStatus(ProcessExecutionStatus status)
        => status switch
        {
            ProcessExecutionStatus.Cancelled => GitRepositoryIdentityStatus.Cancelled,
            ProcessExecutionStatus.TimedOut => GitRepositoryIdentityStatus.TimedOut,
            _ => GitRepositoryIdentityStatus.Failed
        };

    private static GitRepositoryIdentityStatus MapVerificationStatus(ProcessExecutionStatus status)
        => status switch
        {
            ProcessExecutionStatus.Cancelled => GitRepositoryIdentityStatus.Cancelled,
            ProcessExecutionStatus.TimedOut => GitRepositoryIdentityStatus.TimedOut,
            _ => GitRepositoryIdentityStatus.VerificationFailed
        };

    private static GitRepositoryIdentityResult Failure(
        GitRepositoryIdentityStatus status,
        string message)
        => new(status, Message: message);
}
