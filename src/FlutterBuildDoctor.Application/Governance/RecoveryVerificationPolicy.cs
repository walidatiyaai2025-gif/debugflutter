using System.Security.Cryptography;
using System.Text;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record RecoveryVerificationCheck(string Name, bool Mandatory, bool Passed, int Weight = 1);

public sealed record RecoveryVerificationDecision(
    string Identity,
    IReadOnlyList<RecoveryVerificationCheck> Checks,
    IReadOnlyList<string> FailedMandatoryChecks,
    int Score,
    bool Complete,
    string ReasonCode,
    string Fingerprint);

public static class RecoveryVerificationPolicy
{
    public const int MaxChecks = 64;

    public static RecoveryVerificationDecision Evaluate(string identity, IEnumerable<RecoveryVerificationCheck> checks)
    {
        var normalizedIdentity = NormalizeName(identity, nameof(identity));
        ArgumentNullException.ThrowIfNull(checks);
        var source = checks.ToArray();
        if (source.Length > MaxChecks)
            throw new ArgumentOutOfRangeException(nameof(checks));

        var normalized = source.Select(item => item with
            {
                Name = NormalizeName(item.Name, nameof(item.Name)),
                Weight = Math.Clamp(item.Weight, 1, 10)
            })
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToArray();

        var duplicate = normalized.GroupBy(item => item.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException("Duplicate verification check.", nameof(checks));

        var failedMandatory = normalized.Where(item => item.Mandatory && !item.Passed)
            .Select(item => item.Name)
            .ToArray();
        var totalWeight = normalized.Sum(item => item.Weight);
        var passedWeight = normalized.Where(item => item.Passed).Sum(item => item.Weight);
        var score = totalWeight == 0 ? 100 : (int)Math.Round((double)passedWeight / totalWeight * 100, MidpointRounding.AwayFromZero);
        var complete = failedMandatory.Length == 0;
        var reason = complete ? "recovery-verified" : "recovery-verification-failed";
        var payload = string.Join("\n", normalized.Select(item => $"{item.Name}|{item.Mandatory}|{item.Passed}|{item.Weight}"));
        payload = $"{normalizedIdentity}\n{payload}\n{score}\n{complete}";

        return new RecoveryVerificationDecision(normalizedIdentity, normalized, failedMandatory, score, complete, reason, Hash(payload));
    }

    private static string NormalizeName(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Verification identity is required.", parameterName);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > 128 || normalized.Any(char.IsControl))
            throw new ArgumentException("Verification identity is invalid.", parameterName);
        return normalized;
    }

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
