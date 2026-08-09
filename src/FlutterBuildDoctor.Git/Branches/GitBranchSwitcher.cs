using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Git.Branches;

public sealed class GitBranchSwitcher : IGitBranchSwitcher
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);
    private readonly IProcessRunner _processRunner;

    public GitBranchSwitcher(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<GitBranchSwitchResult> SwitchAsync(
        GitBranchSwitchRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return Failure(
                GitBranchSwitchStatus.Cancelled,
                "Branch switch was cancelled before it started.");
        }

        if (string.IsNullOrWhiteSpace(request.GitExecutablePath))
        {
            return Failure(
                GitBranchSwitchStatus.InvalidRequest,
                "Git executable path is required.");
        }

        if (string.IsNullOrWhiteSpace(request.RepositoryPath))
        {
            return Failure(
                GitBranchSwitchStatus.InvalidRepository,
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
                GitBranchSwitchStatus.InvalidRepository,
                $"Repository path is invalid: {ex.Message}");
        }

        if (!Directory.Exists(repositoryPath))
        {
            return Failure(
                GitBranchSwitchStatus.InvalidRepository,
                "Repository path does not exist.");
        }

        var gitMetadataPath = Path.Combine(repositoryPath, ".git");
        if (!Directory.Exists(gitMetadataPath) && !File.Exists(gitMetadataPath))
        {
            return Failure(
                GitBranchSwitchStatus.InvalidRepository,
                "Selected folder is not a Git working tree.");
        }

        var branchValidationError = ValidateBranch(request.Branch);
        if (branchValidationError is not null)
        {
            return Failure(
                GitBranchSwitchStatus.InvalidBranch,
                branchValidationError);
        }

        var timeout = request.Timeout ?? DefaultTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            return Failure(
                GitBranchSwitchStatus.InvalidRequest,
                "Branch switch timeout must be greater than zero.");
        }

        var environment = new Dictionary<string, string?>
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GCM_INTERACTIVE"] = "Never"
        };

        var switchArguments = BuildSwitchArguments(request.Branch);
        var switchResult = await RunGitAsync(
            request.GitExecutablePath,
            repositoryPath,
            switchArguments,
            environment,
            timeout,
            "Switch Git branch",
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!switchResult.IsSuccess)
        {
            return new GitBranchSwitchResult(
                MapProcessStatus(switchResult.Status),
                Message: DescribeFailure(
                    switchResult,
                    $"Git could not switch to branch '{request.Branch.Name}'."),
                SwitchResult: switchResult);
        }

        var branchVerificationResult = await RunGitAsync(
            request.GitExecutablePath,
            repositoryPath,
            new[] { "branch", "--show-current" },
            environment,
            timeout,
            "Verify current Git branch",
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!branchVerificationResult.IsSuccess)
        {
            return new GitBranchSwitchResult(
                MapVerificationStatus(branchVerificationResult.Status),
                Message: DescribeFailure(
                    branchVerificationResult,
                    "Git branch verification failed after the switch command succeeded."),
                SwitchResult: switchResult,
                BranchVerificationResult: branchVerificationResult);
        }

        var currentBranch = FirstStdOutLine(branchVerificationResult);
        if (string.IsNullOrWhiteSpace(currentBranch))
        {
            return new GitBranchSwitchResult(
                GitBranchSwitchStatus.VerificationFailed,
                Message: "Git did not report an attached current branch after switching.",
                SwitchResult: switchResult,
                BranchVerificationResult: branchVerificationResult);
        }

        if (!string.Equals(currentBranch, request.Branch.Name, StringComparison.Ordinal))
        {
            return new GitBranchSwitchResult(
                GitBranchSwitchStatus.VerificationFailed,
                CurrentBranch: currentBranch,
                Message: $"Git reported current branch '{currentBranch}', expected '{request.Branch.Name}'.",
                SwitchResult: switchResult,
                BranchVerificationResult: branchVerificationResult);
        }

        var commitVerificationResult = await RunGitAsync(
            request.GitExecutablePath,
            repositoryPath,
            new[] { "rev-parse", "--verify", "HEAD" },
            environment,
            timeout,
            "Verify Git HEAD",
            progress,
            cancellationToken).ConfigureAwait(false);

        if (!commitVerificationResult.IsSuccess)
        {
            return new GitBranchSwitchResult(
                MapVerificationStatus(commitVerificationResult.Status),
                CurrentBranch: currentBranch,
                Message: DescribeFailure(
                    commitVerificationResult,
                    "Git HEAD verification failed after switching branches."),
                SwitchResult: switchResult,
                BranchVerificationResult: branchVerificationResult,
                CommitVerificationResult: commitVerificationResult);
        }

        var commitSha = FirstStdOutLine(commitVerificationResult);
        if (string.IsNullOrWhiteSpace(commitSha))
        {
            return new GitBranchSwitchResult(
                GitBranchSwitchStatus.VerificationFailed,
                CurrentBranch: currentBranch,
                Message: "Git did not report a HEAD commit after switching branches.",
                SwitchResult: switchResult,
                BranchVerificationResult: branchVerificationResult,
                CommitVerificationResult: commitVerificationResult);
        }

        return new GitBranchSwitchResult(
            GitBranchSwitchStatus.Succeeded,
            currentBranch,
            commitSha,
            $"Switched to branch '{currentBranch}' at {commitSha}.",
            switchResult,
            branchVerificationResult,
            commitVerificationResult);
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

    private static IReadOnlyList<string> BuildSwitchArguments(GitBranchInfo branch)
        => branch.Kind switch
        {
            GitBranchKind.Local => new[]
            {
                "switch",
                "--",
                branch.Name
            },
            GitBranchKind.Remote => new[]
            {
                "switch",
                "--track",
                "--",
                $"{branch.RemoteName}/{branch.Name}"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(branch))
        };

    private static string? ValidateBranch(GitBranchInfo? branch)
    {
        if (branch is null)
        {
            return "A branch selection is required.";
        }

        if (string.IsNullOrWhiteSpace(branch.Name))
        {
            return "Selected branch name is empty.";
        }

        if (branch.Name.Any(char.IsControl))
        {
            return "Selected branch name contains invalid control characters.";
        }

        if (branch.Kind == GitBranchKind.Local)
        {
            var expectedFullName = $"refs/heads/{branch.Name}";
            return string.Equals(branch.FullName, expectedFullName, StringComparison.Ordinal)
                ? null
                : "Selected local branch metadata is inconsistent with its full Git ref.";
        }

        if (branch.Kind == GitBranchKind.Remote)
        {
            if (string.IsNullOrWhiteSpace(branch.RemoteName))
            {
                return "Selected remote branch does not identify its remote.";
            }

            if (branch.RemoteName.Any(char.IsControl))
            {
                return "Selected remote name contains invalid control characters.";
            }

            var expectedFullName = $"refs/remotes/{branch.RemoteName}/{branch.Name}";
            return string.Equals(branch.FullName, expectedFullName, StringComparison.Ordinal)
                ? null
                : "Selected remote branch metadata is inconsistent with its full Git ref.";
        }

        return "Selected branch kind is not supported.";
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

    private static GitBranchSwitchStatus MapProcessStatus(ProcessExecutionStatus status)
        => status switch
        {
            ProcessExecutionStatus.Cancelled => GitBranchSwitchStatus.Cancelled,
            ProcessExecutionStatus.TimedOut => GitBranchSwitchStatus.TimedOut,
            _ => GitBranchSwitchStatus.Failed
        };

    private static GitBranchSwitchStatus MapVerificationStatus(ProcessExecutionStatus status)
        => status switch
        {
            ProcessExecutionStatus.Cancelled => GitBranchSwitchStatus.Cancelled,
            ProcessExecutionStatus.TimedOut => GitBranchSwitchStatus.TimedOut,
            _ => GitBranchSwitchStatus.VerificationFailed
        };

    private static GitBranchSwitchResult Failure(
        GitBranchSwitchStatus status,
        string message)
        => new(status, Message: message);
}
