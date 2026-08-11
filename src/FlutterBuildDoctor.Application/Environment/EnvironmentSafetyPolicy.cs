using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Environment;

public sealed record SafeEnvironmentEntry(string Name, string Value);

public sealed record EnvironmentSafetyResult(
    IReadOnlyList<SafeEnvironmentEntry> SafeEntries,
    IReadOnlyList<string> NormalizedPathSegments,
    int OmittedSecretCount,
    string Fingerprint);

public static partial class EnvironmentSafetyPolicy
{
    public const int MaxVariables = 256;
    public const int MaxValueLength = 4096;

    public static EnvironmentSafetyResult Evaluate(
        IEnumerable<KeyValuePair<string, string?>> variables,
        IEnumerable<string>? pathSegments = null)
    {
        ArgumentNullException.ThrowIfNull(variables);
        var materialized = variables.ToList();
        if (materialized.Count > MaxVariables)
        {
            throw new ArgumentOutOfRangeException(nameof(variables), "Environment variable count exceeds the supported bound.");
        }

        var safe = new List<SafeEnvironmentEntry>();
        var omitted = 0;
        foreach (var pair in materialized)
        {
            var name = NormalizeName(pair.Key);
            var value = pair.Value ?? string.Empty;
            ValidateValue(value);
            if (IsSecretName(name))
            {
                omitted++;
                continue;
            }

            safe.Add(new SafeEnvironmentEntry(name, value));
        }

        safe = safe
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Value, StringComparer.Ordinal)
            .ToList();

        var paths = NormalizePathSegments(pathSegments ?? Array.Empty<string>());
        var canonical = string.Join('\n', safe.Select(item => $"{item.Name}={item.Value}"))
            + "\nPATH=" + string.Join('|', paths)
            + $"\nOMITTED={omitted}";

        return new EnvironmentSafetyResult(safe, paths, omitted, Hash(canonical));
    }

    public static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var normalized = name.Trim();
        if (!NameRegex().IsMatch(normalized))
        {
            throw new ArgumentException("Environment variable name is invalid.", nameof(name));
        }
        return normalized;
    }

    public static void ValidateValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaxValueLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Environment value exceeds the supported bound.");
        }
        if (value.Any(ch => char.IsControl(ch) && ch is not '\t'))
        {
            throw new ArgumentException("Environment value contains control characters.", nameof(value));
        }
    }

    public static bool IsSecretName(string name)
    {
        var normalized = NormalizeName(name);
        return SecretNameRegex().IsMatch(normalized);
    }

    public static IReadOnlyList<string> NormalizePathSegments(IEnumerable<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var comparer = StringComparer.OrdinalIgnoreCase;
        var set = new HashSet<string>(comparer);
        var result = new List<string>();
        foreach (var segment in segments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(segment);
            var trimmed = segment.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Path.IsPathFullyQualified(trimmed))
            {
                throw new ArgumentException("PATH segment must be fully qualified.", nameof(segments));
            }
            var normalized = Path.GetFullPath(trimmed);
            if (set.Add(normalized))
            {
                result.Add(normalized);
            }
        }

        return result.OrderBy(item => item, comparer).ToArray();
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    [GeneratedRegex("(PASSWORD|PASSWD|TOKEN|SECRET|API[_-]?KEY|AUTHORIZATION|PRIVATE[_-]?KEY)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretNameRegex();
}
