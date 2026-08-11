using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record WorkspaceQuotaDecision(
    string WorkspaceIdentity,
    long UsedBytes,
    long QuotaBytes,
    long ReservedHeadroomBytes,
    long EffectiveQuotaBytes,
    long RemainingBytes,
    int UsagePercent,
    bool Exhausted,
    string ReasonCode,
    string Fingerprint);

public static class WorkspaceQuotaPolicy
{
    public const long MinQuotaBytes = 64L * 1024 * 1024;
    public const long MaxQuotaBytes = 1024L * 1024 * 1024 * 1024;

    public static WorkspaceQuotaDecision Evaluate(
        string workspaceIdentity,
        long usedBytes,
        long requestedQuotaBytes,
        long reservedHeadroomBytes)
    {
        if (string.IsNullOrWhiteSpace(workspaceIdentity))
            throw new ArgumentException("Workspace identity is required.", nameof(workspaceIdentity));
        if (usedBytes < 0 || requestedQuotaBytes < 0 || reservedHeadroomBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(usedBytes));

        var identity = workspaceIdentity.Trim().ToLowerInvariant();
        if (identity.Length > 128 || identity.Any(char.IsControl))
            throw new ArgumentException("Workspace identity is invalid.", nameof(workspaceIdentity));

        var quota = Math.Clamp(requestedQuotaBytes, MinQuotaBytes, MaxQuotaBytes);
        var reserved = Math.Min(reservedHeadroomBytes, quota / 2);
        var effective = quota - reserved;
        var remaining = Math.Max(0, effective - usedBytes);
        var exhausted = usedBytes >= effective;
        var percent = effective == 0
            ? 100
            : (int)Math.Clamp(Math.Round((double)usedBytes / effective * 100, MidpointRounding.AwayFromZero), 0, 100);
        var reason = exhausted ? "workspace-quota-exhausted" : "workspace-quota-available";
        var payload = $"{identity}|{usedBytes}|{quota}|{reserved}|{effective}|{remaining}|{percent}|{exhausted}";

        return new WorkspaceQuotaDecision(identity, usedBytes, quota, reserved, effective, remaining, percent, exhausted, reason, Hash(payload));
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
