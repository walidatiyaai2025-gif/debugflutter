using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Flutter.Build;

public sealed class BuildRetryPolicy : IBuildRetryPolicy
{
    private static readonly string[] TransientSignatures =
    {
        "daemon disappeared",
        "connection reset",
        "temporarily unavailable",
        "connection closed before full header was received",
        "could not resolve host"
    };

    public BuildRetryPolicy(int maxRetries = 1)
    {
        if (maxRetries is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "Retry count must be between 0 and 3.");
        }

        MaxRetries = maxRetries;
    }

    public int MaxRetries { get; }

    public BuildRetryDecision Evaluate(int completedAttempts, ProcessResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (completedAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAttempts));
        }

        if (result.IsSuccess || result.Status is ProcessExecutionStatus.Cancelled or ProcessExecutionStatus.TimedOut)
        {
            return new BuildRetryDecision(false);
        }

        if (completedAttempts > MaxRetries)
        {
            return new BuildRetryDecision(false, "Bounded retry limit reached.");
        }

        var evidence = string.Join(
            "\n",
            new[] { result.FailureReason ?? string.Empty }
                .Concat(result.Output.Select(static line => line.Text)));
        var signature = TransientSignatures.FirstOrDefault(candidate =>
            evidence.Contains(candidate, StringComparison.OrdinalIgnoreCase));

        return signature is null
            ? new BuildRetryDecision(false, "Failure is not classified as transient; automatic retry is disabled.")
            : new BuildRetryDecision(true, $"Transient build failure matched '{signature}'.");
    }
}
