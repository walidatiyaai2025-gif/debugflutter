using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Processes;

public sealed record ProcessTerminationRequest(
    int ProcessId,
    string ProcessName,
    bool OwnedByApplication,
    bool ProtectedProcess,
    bool ForceRequested,
    bool ExternalForceConfirmed,
    TimeSpan GracefulTimeout,
    string? CancellationReason = null);

public sealed record ProcessTerminationDecision(
    bool Allowed,
    string ProcessName,
    int ProcessId,
    bool OwnedByApplication,
    bool ForceAllowed,
    TimeSpan GracefulTimeout,
    IReadOnlyList<string> Steps,
    string? CancellationReason,
    string ReasonCode,
    string Fingerprint);

public static class ProcessTerminationPolicy
{
    public static readonly TimeSpan MinGracefulTimeout = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxGracefulTimeout = TimeSpan.FromMinutes(2);

    public static ProcessTerminationDecision Evaluate(ProcessTerminationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Process ID must be positive.");
        }

        var processName = NormalizeProcessName(request.ProcessName);
        var gracefulTimeout = TimeSpan.FromSeconds(Math.Clamp(request.GracefulTimeout.TotalSeconds, MinGracefulTimeout.TotalSeconds, MaxGracefulTimeout.TotalSeconds));
        var cancellationReason = NormalizeCancellationReason(request.CancellationReason);
        var forceAllowed = request.ForceRequested
            && !request.ProtectedProcess
            && (request.OwnedByApplication || request.ExternalForceConfirmed);
        var allowed = !request.ForceRequested || forceAllowed;

        var steps = request.ForceRequested && forceAllowed
            ? new[] { "graceful-stop", "force-kill-if-running" }
            : new[] { "graceful-stop" };

        var reason = request.ProtectedProcess && request.ForceRequested ? "protected-force-denied"
            : request.ForceRequested && !request.OwnedByApplication && !request.ExternalForceConfirmed ? "external-force-confirmation-required"
            : request.ForceRequested ? "graceful-then-force"
            : request.OwnedByApplication ? "owned-graceful-stop"
            : "external-graceful-stop";

        var canonical = string.Join('|', request.ProcessId, processName, request.OwnedByApplication, request.ProtectedProcess, request.ForceRequested, forceAllowed,
            gracefulTimeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationReason ?? string.Empty, reason, string.Join(',', steps));
        return new ProcessTerminationDecision(allowed, processName, request.ProcessId, request.OwnedByApplication, forceAllowed, gracefulTimeout, steps, cancellationReason, reason, Hash(canonical));
    }

    public static string NormalizeProcessName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(char.IsControl))
        {
            throw new ArgumentException("Process name contains control characters.", nameof(value));
        }
        var normalized = value.Trim();
        if (normalized.Length > 128)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Process name exceeds the supported bound.");
        }
        return normalized;
    }

    private static string? NormalizeCancellationReason(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (value.Any(char.IsControl)) throw new ArgumentException("Cancellation reason contains control characters.", nameof(value));
        return value.Trim().Length <= 256 ? value.Trim() : value.Trim()[..256];
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
