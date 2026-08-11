using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Builds;

public enum BuildExecutionMode
{
    Debug = 0,
    Profile = 1,
    Release = 2
}

public enum BuildFailureKind
{
    None = 0,
    Transient = 1,
    Deterministic = 2,
    Cancelled = 3
}

public sealed record BuildExecutionRequest(
    string CommandId,
    string Mode,
    bool IsWorkingTreeClean = true,
    TimeSpan? Timeout = null,
    int? RetryCount = null,
    BuildFailureKind PreviousFailure = BuildFailureKind.None,
    string? CancellationReason = null);

public sealed record BuildExecutionDecision(
    string CommandId,
    BuildExecutionMode Mode,
    bool Allowed,
    TimeSpan Timeout,
    int RetryCount,
    bool CanRetry,
    string? CancellationReason,
    string ExecutionKey,
    string ReasonCode);

public static class BuildExecutionPolicy
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan MinTimeout = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(60);
    public const int DefaultRetryCount = 1;
    public const int MaxRetryCount = 3;

    public static BuildExecutionDecision Evaluate(BuildExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var commandId = NormalizeCommandId(request.CommandId);
        var mode = NormalizeMode(request.Mode);
        var timeout = BoundTimeout(request.Timeout ?? DefaultTimeout);
        var retryCount = Math.Clamp(request.RetryCount ?? DefaultRetryCount, 0, MaxRetryCount);
        var cancellationReason = string.IsNullOrWhiteSpace(request.CancellationReason) ? null : request.CancellationReason.Trim();

        if (request.PreviousFailure == BuildFailureKind.Cancelled)
        {
            retryCount = 0;
            return BuildDecision(commandId, mode, false, timeout, retryCount, false, cancellationReason, request.IsWorkingTreeClean, "cancelled");
        }

        if (mode == BuildExecutionMode.Release && !request.IsWorkingTreeClean)
        {
            return BuildDecision(commandId, mode, false, timeout, retryCount, false, cancellationReason, request.IsWorkingTreeClean, "dirty-release-tree");
        }

        var canRetry = request.PreviousFailure == BuildFailureKind.Transient && retryCount > 0;
        var reason = request.PreviousFailure switch
        {
            BuildFailureKind.Transient when canRetry => "retry-transient",
            BuildFailureKind.Transient => "transient-retry-exhausted",
            BuildFailureKind.Deterministic => "deterministic-failure",
            _ => "ready"
        };

        return BuildDecision(commandId, mode, true, timeout, retryCount, canRetry, cancellationReason, request.IsWorkingTreeClean, reason);
    }

    public static string NormalizeCommandId(string commandId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandId);
        var value = commandId.Trim().ToLowerInvariant();
        if (value.Length > 128 || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Build command identity must be compact and whitespace-free.", nameof(commandId));
        }

        return value;
    }

    public static BuildExecutionMode NormalizeMode(string mode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mode);
        return mode.Trim().ToLowerInvariant() switch
        {
            "debug" => BuildExecutionMode.Debug,
            "profile" => BuildExecutionMode.Profile,
            "release" => BuildExecutionMode.Release,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported build mode.")
        };
    }

    public static TimeSpan BoundTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return MinTimeout;
        }

        if (timeout < MinTimeout)
        {
            return MinTimeout;
        }

        return timeout > MaxTimeout ? MaxTimeout : timeout;
    }

    private static BuildExecutionDecision BuildDecision(
        string commandId,
        BuildExecutionMode mode,
        bool allowed,
        TimeSpan timeout,
        int retryCount,
        bool canRetry,
        string? cancellationReason,
        bool isWorkingTreeClean,
        string reasonCode)
    {
        var canonical = string.Join('|',
            commandId,
            mode,
            allowed,
            timeout.Ticks,
            retryCount,
            canRetry,
            cancellationReason ?? string.Empty,
            isWorkingTreeClean,
            reasonCode);
        var executionKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new BuildExecutionDecision(commandId, mode, allowed, timeout, retryCount, canRetry, cancellationReason, executionKey, reasonCode);
    }
}
