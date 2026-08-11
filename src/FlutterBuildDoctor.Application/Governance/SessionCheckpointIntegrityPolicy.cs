using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record SessionCheckpoint(
    string Identity,
    int Sequence,
    DateTimeOffset TimestampUtc,
    string StateFingerprint);

public sealed record SessionCheckpointResolution(
    string SessionIdentity,
    IReadOnlyList<SessionCheckpoint> Checkpoints,
    SessionCheckpoint? Latest,
    string ReasonCode,
    string Fingerprint);

public static class SessionCheckpointIntegrityPolicy
{
    public const int MaxCheckpoints = 128;

    public static SessionCheckpointResolution Resolve(string sessionIdentity, IEnumerable<SessionCheckpoint> checkpoints)
    {
        var session = NormalizeIdentity(sessionIdentity, nameof(sessionIdentity));
        ArgumentNullException.ThrowIfNull(checkpoints);
        var source = checkpoints.ToArray();
        if (source.Length > MaxCheckpoints)
            throw new ArgumentOutOfRangeException(nameof(checkpoints));

        var normalized = source.Select(item =>
            {
                if (item.Sequence < 0)
                    throw new ArgumentOutOfRangeException(nameof(checkpoints), "Checkpoint sequence cannot be negative.");
                return item with
                {
                    Identity = NormalizeIdentity(item.Identity, nameof(item.Identity)),
                    TimestampUtc = item.TimestampUtc.ToUniversalTime(),
                    StateFingerprint = NormalizeFingerprint(item.StateFingerprint)
                };
            })
            .OrderBy(item => item.Sequence)
            .ThenBy(item => item.TimestampUtc)
            .ThenBy(item => item.Identity, StringComparer.Ordinal)
            .ToArray();

        var duplicateSequence = normalized.GroupBy(item => item.Sequence).FirstOrDefault(group => group.Count() > 1);
        if (duplicateSequence is not null)
            throw new ArgumentException("Duplicate checkpoint sequence.", nameof(checkpoints));

        for (var index = 1; index < normalized.Length; index++)
        {
            if (normalized[index].Sequence <= normalized[index - 1].Sequence)
                throw new ArgumentException("Checkpoint sequence must be monotonic.", nameof(checkpoints));
        }

        var latest = normalized.LastOrDefault();
        var payload = string.Join("\n", normalized.Select(item =>
            $"{item.Sequence}|{item.Identity}|{item.TimestampUtc:O}|{item.StateFingerprint}"));
        payload = $"{session}\n{payload}";
        var reason = latest is null ? "checkpoint-set-empty" : "checkpoint-set-valid";
        return new SessionCheckpointResolution(session, normalized, latest, reason, Hash(payload));
    }

    public static string NormalizeFingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("State fingerprint is required.", nameof(value));
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("State fingerprint must be a 64-character hexadecimal SHA-256 value.", nameof(value));
        return normalized;
    }

    private static string NormalizeIdentity(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Checkpoint identity is required.", parameterName);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 128 || normalized.Any(char.IsControl))
            throw new ArgumentException("Checkpoint identity is invalid.", parameterName);
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
