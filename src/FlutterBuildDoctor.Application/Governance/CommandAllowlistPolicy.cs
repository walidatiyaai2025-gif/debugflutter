using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Governance;

public sealed record CommandAllowlistDecision(
    string CommandIdentity,
    string Executable,
    IReadOnlyList<string> Arguments,
    bool Allowed,
    string SafeSummary,
    string ReasonCode,
    string Fingerprint);

public static class CommandAllowlistPolicy
{
    private static readonly Regex IdentityPattern = new("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] ShellOperators = { "&&", "||", ";", "|", ">", "<", "`", "\r", "\n" };
    private static readonly string[] SecretMarkers = { "--password=", "--token=", "--secret=", "--apikey=", "--api-key=", "authorization:" };

    public static CommandAllowlistDecision Evaluate(
        string commandIdentity,
        string executable,
        IEnumerable<string>? arguments,
        IEnumerable<string> approvedExecutables,
        IEnumerable<string>? approvedArgumentPrefixes = null)
    {
        ArgumentNullException.ThrowIfNull(approvedExecutables);
        var identity = NormalizeIdentity(commandIdentity);
        var normalizedExecutable = NormalizeExecutable(executable);
        var normalizedArguments = (arguments ?? Array.Empty<string>()).Select(NormalizeArgument).ToArray();
        var approved = approvedExecutables.Select(NormalizeExecutable).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allowedPrefixes = approvedArgumentPrefixes?.Select(prefix => prefix.Trim()).Where(prefix => prefix.Length > 0).ToArray();

        foreach (var argument in normalizedArguments)
        {
            if (ContainsShellOperator(argument))
            {
                throw new ArgumentException($"Shell-control operator is not allowed in argument '{argument}'.", nameof(arguments));
            }

            if (ContainsSecret(argument))
            {
                throw new ArgumentException("Inline secret-bearing command arguments are not allowed.", nameof(arguments));
            }
        }

        var executableAllowed = approved.Contains(normalizedExecutable);
        var argumentsAllowed = allowedPrefixes is null || normalizedArguments.All(argument =>
            allowedPrefixes.Any(prefix => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

        var allowed = executableAllowed && argumentsAllowed;
        var reason = !executableAllowed
            ? "command-executable-not-allowlisted"
            : !argumentsAllowed
                ? "command-argument-not-allowlisted"
                : "command-allowlisted";
        var safeSummary = string.Join(' ', new[] { normalizedExecutable }.Concat(normalizedArguments));
        if (safeSummary.Length > 512)
        {
            safeSummary = safeSummary[..512];
        }

        var canonical = $"{identity}|{normalizedExecutable}|{string.Join('|', normalizedArguments)}|{allowed}|{reason}";
        return new CommandAllowlistDecision(identity, normalizedExecutable, normalizedArguments, allowed, safeSummary, reason, Hash(canonical));
    }

    private static string NormalizeIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Command identity is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!IdentityPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Unsafe command identity '{value}'.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeExecutable(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Executable is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Contains("..", StringComparison.Ordinal) || normalized.Contains('/') || normalized.Contains('\\'))
        {
            throw new ArgumentException("Executable token must not contain traversal or path separators.", nameof(value));
        }

        if (normalized.EndsWith(".exe", StringComparison.Ordinal))
        {
            normalized = normalized[..^4];
        }

        if (!IdentityPattern.IsMatch(normalized))
        {
            throw new ArgumentException($"Unsafe executable token '{value}'.", nameof(value));
        }

        return normalized;
    }

    private static string NormalizeArgument(string value)
    {
        if (value is null)
        {
            throw new ArgumentException("Command argument cannot be null.", nameof(value));
        }

        var normalized = value.Trim();
        if (normalized.Length > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Command argument exceeds 512 characters.");
        }

        return normalized;
    }

    private static bool ContainsShellOperator(string value)
        => ShellOperators.Any(token => value.Contains(token, StringComparison.Ordinal));

    private static bool ContainsSecret(string value)
        => SecretMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
