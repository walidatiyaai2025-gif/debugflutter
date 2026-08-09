using System.Text.RegularExpressions;
using FlutterBuildDoctor.Application.Processes;

namespace FlutterBuildDoctor.Infrastructure.Processes;

public sealed class DefaultProcessSecretRedactor : IProcessSecretRedactor
{
    private const string Redacted = "[REDACTED]";

    private static readonly string[] SensitiveKeySuffixes =
    [
        "password",
        "passwd",
        "pwd",
        "token",
        "apikey",
        "secret",
        "storepass",
        "keypass",
        "keystorepassword"
    ];

    private static readonly Regex SecretAssignmentPattern = new(
        @"(?ix)(?<key>(?:--?|/)?[\w.\-]*(?:password|passwd|pwd|token|api[_\-]?key|secret|storepass|keypass|keystore[_\-]?password)[\w.\-]*)(?<sep>\s*(?:=|:)\s*|\s+)(?<value>""[^""]*""|'[^']*'|[^\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string SanitizeCommand(ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var executable = Quote(RedactText(request.FileName, request));
        if (request.RedactCommand)
            return $"{executable} {Redacted}";

        var sanitizedArguments = new List<string>(request.Arguments.Count);
        for (var index = 0; index < request.Arguments.Count; index++)
        {
            var argument = request.Arguments[index];

            if (TryRedactInlineSecret(argument, out var redactedArgument))
            {
                sanitizedArguments.Add(Quote(redactedArgument));
                continue;
            }

            if (IsSensitiveKey(argument) && index + 1 < request.Arguments.Count)
            {
                sanitizedArguments.Add(Quote(argument));
                sanitizedArguments.Add(Redacted);
                index++;
                continue;
            }

            sanitizedArguments.Add(Quote(RedactText(argument, request)));
        }

        return sanitizedArguments.Count == 0
            ? executable
            : $"{executable} {string.Join(' ', sanitizedArguments)}";
    }

    public string RedactText(string value, ProcessRequest request)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(request);

        var result = value;
        foreach (var secret in GetSensitiveValues(request))
            result = result.Replace(secret, Redacted, StringComparison.Ordinal);

        return SecretAssignmentPattern.Replace(
            result,
            match => $"{match.Groups["key"].Value}{match.Groups["sep"].Value}{Redacted}");
    }

    private static IEnumerable<string> GetSensitiveValues(ProcessRequest request)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);

        if (request.SensitiveValues is not null)
        {
            foreach (var value in request.SensitiveValues)
            {
                if (!string.IsNullOrEmpty(value))
                    values.Add(value);
            }
        }

        if (request.Environment is not null)
        {
            foreach (var pair in request.Environment)
            {
                if (IsSensitiveKey(pair.Key) && !string.IsNullOrEmpty(pair.Value))
                    values.Add(pair.Value);
            }
        }

        return values.OrderByDescending(static value => value.Length);
    }

    private static bool TryRedactInlineSecret(string argument, out string redacted)
    {
        var separatorIndex = argument.IndexOfAny(['=', ':']);
        if (separatorIndex <= 0)
        {
            redacted = argument;
            return false;
        }

        var key = argument[..separatorIndex];
        if (!IsSensitiveKey(key))
        {
            redacted = argument;
            return false;
        }

        redacted = $"{key}{argument[separatorIndex]}{Redacted}";
        return true;
    }

    private static bool IsSensitiveKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = new string(
            value
                .TrimStart('-', '/')
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());

        return SensitiveKeySuffixes.Any(normalized.EndsWith);
    }

    private static string Quote(string value)
    {
        if (value.Length == 0)
            return "\"\"";

        return value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }
}
