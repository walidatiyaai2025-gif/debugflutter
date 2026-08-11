using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FlutterBuildDoctor.Application.Commands;

public sealed record CommandArgumentDecision(
    string Executable,
    IReadOnlyList<string> Arguments,
    IReadOnlyList<string> SafeDisplayArguments,
    string SafeDisplayCommand,
    bool ContainsSecrets,
    string Fingerprint);

public static partial class CommandArgumentPolicy
{
    public const int MaxArguments = 128;
    public const int MaxArgumentLength = 2048;

    public static CommandArgumentDecision Prepare(string executable, IEnumerable<string> arguments)
    {
        var normalizedExecutable = NormalizeExecutable(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        var tokens = arguments.ToList();
        if (tokens.Count > MaxArguments)
        {
            throw new ArgumentOutOfRangeException(nameof(arguments), "Argument count exceeds the supported bound.");
        }

        foreach (var token in tokens)
        {
            ValidateArgument(token);
        }

        var safe = Redact(tokens, out var containsSecrets);
        var display = string.Join(' ', new[] { Quote(normalizedExecutable) }.Concat(safe.Select(Quote)));
        var canonical = normalizedExecutable + "\n" + string.Join('\n', safe);
        return new CommandArgumentDecision(normalizedExecutable, tokens.AsReadOnly(), safe, display, containsSecrets, Hash(canonical));
    }

    public static string NormalizeExecutable(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        var normalized = executable.Trim();
        if (normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Executable contains control characters.", nameof(executable));
        }
        return normalized;
    }

    public static void ValidateArgument(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Length > MaxArgumentLength)
        {
            throw new ArgumentOutOfRangeException(nameof(argument), "Argument exceeds the supported bound.");
        }
        if (argument.Any(char.IsControl))
        {
            throw new ArgumentException("Argument contains control characters.", nameof(argument));
        }
    }

    private static IReadOnlyList<string> Redact(IReadOnlyList<string> tokens, out bool containsSecrets)
    {
        containsSecrets = false;
        var result = new string[tokens.Count];
        var redactNext = false;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (redactNext)
            {
                result[index] = "[REDACTED]";
                containsSecrets = true;
                redactNext = false;
                continue;
            }

            if (SecretFlagRegex().IsMatch(token))
            {
                result[index] = token;
                redactNext = true;
                containsSecrets = true;
                continue;
            }

            var match = SecretInlineRegex().Match(token);
            if (match.Success)
            {
                result[index] = match.Groups[1].Value + "=[REDACTED]";
                containsSecrets = true;
                continue;
            }

            result[index] = token;
        }
        return result;
    }

    private static string Quote(string token)
        => token.Length == 0 || token.Any(char.IsWhiteSpace)
            ? '"' + token.Replace("\"", "\\\"", StringComparison.Ordinal) + '"'
            : token;

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex("^--?(password|passwd|token|secret|api[-_]?key|authorization)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretFlagRegex();

    [GeneratedRegex("^(--?(?:password|passwd|token|secret|api[-_]?key|authorization))=(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretInlineRegex();
}
