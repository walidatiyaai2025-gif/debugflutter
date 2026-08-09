using FlutterBuildDoctor.Application.Processes;
using FlutterBuildDoctor.Git.Validation;

namespace FlutterBuildDoctor.Git.Cloning;

public sealed class GitCloneService : IGitCloneService
{
    private static readonly TimeSpan DefaultCloneTimeout = TimeSpan.FromMinutes(15);

    private readonly IProcessRunner _processRunner;
    private readonly IGitRepositoryUrlValidator _urlValidator;

    public GitCloneService(
        IProcessRunner processRunner,
        IGitRepositoryUrlValidator? urlValidator = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _urlValidator = urlValidator ?? new GitRepositoryUrlValidator();
    }

    public async Task<GitCloneResult> CloneAsync(
        GitCloneRequest request,
        IProgress<ProcessOutputLine>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (cancellationToken.IsCancellationRequested)
        {
            return new GitCloneResult(
                GitCloneStatus.Cancelled,
                null,
                "Repository clone was cancelled before it started.");
        }

        if (string.IsNullOrWhiteSpace(request.GitExecutablePath))
        {
            return new GitCloneResult(
                GitCloneStatus.InvalidRequest,
                null,
                "Git executable path is required.");
        }

        var validation = _urlValidator.Validate(request.RepositoryUrl);
        if (!validation.IsValid || string.IsNullOrWhiteSpace(validation.NormalizedUrl))
        {
            return new GitCloneResult(
                GitCloneStatus.InvalidRepositoryUrl,
                null,
                validation.Message ?? "Repository URL is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.WorkspaceDirectory))
        {
            return new GitCloneResult(
                GitCloneStatus.InvalidWorkspace,
                null,
                "Target workspace is required.");
        }

        string workspacePath;
        try
        {
            workspacePath = Path.GetFullPath(request.WorkspaceDirectory.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new GitCloneResult(
                GitCloneStatus.InvalidWorkspace,
                null,
                $"Target workspace is invalid: {ex.Message}");
        }

        if (File.Exists(workspacePath))
        {
            return new GitCloneResult(
                GitCloneStatus.InvalidWorkspace,
                null,
                "Target workspace points to a file instead of a directory.");
        }

        var targetName = ResolveTargetDirectoryName(validation.NormalizedUrl, request.TargetDirectoryName);
        if (!IsValidTargetDirectoryName(targetName))
        {
            return new GitCloneResult(
                GitCloneStatus.InvalidTargetDirectory,
                null,
                "Target directory name must be a single safe folder name without path traversal or invalid characters.");
        }

        var timeout = request.Timeout ?? DefaultCloneTimeout;
        if (timeout <= TimeSpan.Zero)
        {
            return new GitCloneResult(
                GitCloneStatus.InvalidRequest,
                null,
                "Clone timeout must be greater than zero.");
        }

        string targetPath;
        try
        {
            Directory.CreateDirectory(workspacePath);
            targetPath = Path.GetFullPath(Path.Combine(workspacePath, targetName!));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new GitCloneResult(
                GitCloneStatus.InvalidWorkspace,
                null,
                $"Target workspace could not be prepared: {ex.Message}");
        }

        if (File.Exists(targetPath))
        {
            return new GitCloneResult(
                GitCloneStatus.InvalidTargetDirectory,
                targetPath,
                "Target path already exists as a file.");
        }

        if (Directory.Exists(targetPath) && Directory.EnumerateFileSystemEntries(targetPath).Any())
        {
            return new GitCloneResult(
                GitCloneStatus.TargetNotEmpty,
                targetPath,
                "Target directory already exists and is not empty. Existing content will not be overwritten.");
        }

        var processRequest = new ProcessRequest(
            request.GitExecutablePath.Trim(),
            new[]
            {
                "clone",
                "--progress",
                "--",
                validation.NormalizedUrl,
                targetName!
            },
            WorkingDirectory: workspacePath,
            Environment: new Dictionary<string, string?>
            {
                ["GIT_TERMINAL_PROMPT"] = "0",
                ["GCM_INTERACTIVE"] = "Never"
            },
            Timeout: timeout,
            DisplayName: $"Clone Git repository '{targetName}'");

        ProcessResult processResult;
        try
        {
            processResult = await _processRunner
                .RunAsync(processRequest, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new GitCloneResult(
                GitCloneStatus.Cancelled,
                targetPath,
                "Repository clone was cancelled.");
        }
        catch (Exception ex)
        {
            return new GitCloneResult(
                GitCloneStatus.Failed,
                targetPath,
                $"Repository clone could not be started: {ex.Message}");
        }

        if (!processResult.IsSuccess)
        {
            return new GitCloneResult(
                MapStatus(processResult.Status),
                targetPath,
                processResult.FailureReason ?? "Git clone failed.",
                processResult);
        }

        var gitMetadataPath = Path.Combine(targetPath, ".git");
        if (!Directory.Exists(gitMetadataPath) && !File.Exists(gitMetadataPath))
        {
            return new GitCloneResult(
                GitCloneStatus.Failed,
                targetPath,
                "Git reported a successful clone, but the target does not contain Git metadata.",
                processResult);
        }

        return new GitCloneResult(
            GitCloneStatus.Succeeded,
            targetPath,
            "Repository cloned successfully.",
            processResult);
    }

    private static GitCloneStatus MapStatus(ProcessExecutionStatus status)
        => status switch
        {
            ProcessExecutionStatus.Cancelled => GitCloneStatus.Cancelled,
            ProcessExecutionStatus.TimedOut => GitCloneStatus.TimedOut,
            _ => GitCloneStatus.Failed
        };

    private static string? ResolveTargetDirectoryName(string repositoryUrl, string? requestedName)
    {
        if (requestedName is not null)
        {
            return requestedName == requestedName.Trim()
                ? requestedName
                : null;
        }

        var candidate = repositoryUrl.Trim().TrimEnd('/');
        var separator = Math.Max(candidate.LastIndexOf('/'), candidate.LastIndexOf(':'));
        var name = separator >= 0 ? candidate[(separator + 1)..] : candidate;

        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        try
        {
            name = Uri.UnescapeDataString(name);
        }
        catch (UriFormatException)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static bool IsValidTargetDirectoryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            return false;
        }

        if (name != name.Trim() || name.EndsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        if (Path.IsPathRooted(name) ||
            name.Contains(Path.DirectorySeparatorChar) ||
            name.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }
}
