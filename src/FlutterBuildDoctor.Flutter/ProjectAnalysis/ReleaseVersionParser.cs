using System.Globalization;
using System.IO;
using System.Text;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed class ReleaseVersionParser : IReleaseVersionParser
{
    private const long MaxScriptBytes = 512 * 1024;
    private const int MaxVersionNameLength = 512;

    public ReleaseVersionResult Parse(
        PubspecParseResult pubspec,
        GradleDslDetectionResult gradleDsl)
    {
        ArgumentNullException.ThrowIfNull(pubspec);
        ArgumentNullException.ThrowIfNull(gradleDsl);

        if (!pubspec.IsSuccess || pubspec.Metadata is null || string.IsNullOrWhiteSpace(pubspec.PubspecPath))
        {
            return Empty(
                ReleaseVersionStatus.PubspecUnavailable,
                gradleDsl,
                pubspec,
                "A successful FBD-602 pubspec result is required before release-version parsing.");
        }

        if (!gradleDsl.IsSuccess || string.IsNullOrWhiteSpace(gradleDsl.AndroidDirectory))
        {
            return Empty(
                ReleaseVersionStatus.GradleDslUnavailable,
                gradleDsl,
                pubspec,
                "A successful FBD-604 Gradle DSL result is required before release-version parsing.");
        }

        var appScripts = gradleDsl.Scripts
            .Where(script => script.Role == GradleScriptRole.AppBuild)
            .ToArray();

        if (appScripts.Length == 0)
        {
            return Empty(
                ReleaseVersionStatus.AppBuildScriptUnavailable,
                gradleDsl,
                pubspec,
                "FBD-604 did not provide an Android app build script. Release version was not inferred from unrelated files.");
        }

        if (appScripts.Length != 1)
        {
            return Empty(
                ReleaseVersionStatus.Ambiguous,
                gradleDsl,
                pubspec,
                "Multiple Android app build scripts were supplied; no release-version source was selected implicitly.");
        }

        var appScript = appScripts[0];
        if (appScript.Dsl is not GradleDslKind.Groovy and not GradleDslKind.Kotlin)
        {
            return Empty(
                ReleaseVersionStatus.Ambiguous,
                gradleDsl,
                pubspec,
                "The Android app build script does not have a single Groovy or Kotlin DSL classification.");
        }

        string androidDirectory;
        string appDirectory;
        string scriptPath;
        string expectedPath;
        try
        {
            androidDirectory = Path.GetFullPath(gradleDsl.AndroidDirectory);
            appDirectory = Path.GetFullPath(Path.Combine(androidDirectory, "app"));
            scriptPath = Path.GetFullPath(appScript.Path);
            expectedPath = Path.GetFullPath(Path.Combine(
                appDirectory,
                appScript.Dsl == GradleDslKind.Kotlin ? "build.gradle.kts" : "build.gradle"));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Empty(
                ReleaseVersionStatus.UnsafePath,
                gradleDsl,
                pubspec,
                $"Android app build-script path is invalid: {ex.Message}");
        }

        if (!PathsEqual(scriptPath, expectedPath))
        {
            return Empty(
                ReleaseVersionStatus.UnsafePath,
                gradleDsl,
                pubspec,
                "FBD-604 supplied an app build script outside the expected android/app Gradle location.");
        }

        string text;
        try
        {
            if (!Directory.Exists(androidDirectory) || IsReparsePoint(androidDirectory) ||
                !Directory.Exists(appDirectory) || IsReparsePoint(appDirectory))
            {
                return Empty(
                    ReleaseVersionStatus.UnsafePath,
                    gradleDsl,
                    pubspec,
                    "The Android/app project boundary is missing or is now a reparse point/symbolic link.");
            }

            if (!File.Exists(scriptPath))
            {
                return Empty(
                    ReleaseVersionStatus.AppBuildScriptUnavailable,
                    gradleDsl,
                    pubspec,
                    "The FBD-604 app build-script evidence is stale because the file is no longer available.");
            }

            if (IsReparsePoint(scriptPath))
            {
                return Empty(
                    ReleaseVersionStatus.UnsafePath,
                    gradleDsl,
                    pubspec,
                    "The Android app build script is a reparse point/symbolic link and was not followed.");
            }

            if (new FileInfo(scriptPath).Length > MaxScriptBytes)
            {
                return Empty(
                    ReleaseVersionStatus.FileTooLarge,
                    gradleDsl,
                    pubspec,
                    $"The Android app build script exceeds the {MaxScriptBytes} byte inspection limit.");
            }

            text = File.ReadAllText(scriptPath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Empty(
                ReleaseVersionStatus.ReadFailed,
                gradleDsl,
                pubspec,
                $"The Android app build script could not be read: {ex.Message}");
        }

        var pubspecVersion = pubspec.Metadata.Version?.Trim();
        var pubspecVersionName = TryParsePubspecVersionName(pubspecVersion, out var parsedName)
            ? parsedName
            : null;
        var pubspecVersionCode = TryParsePubspecVersionCode(pubspecVersion, out var parsedCode)
            ? parsedCode
            : (int?)null;

        var mask = BuildStructuralMask(text);
        var document = new BlockRange(0, mask.Length);
        var androidBlocks = FindNamedBlocks(mask, document, "android");
        if (androidBlocks.Count == 0)
        {
            return Empty(
                ReleaseVersionStatus.VersionNotFound,
                gradleDsl,
                pubspec,
                "No top-level Android DSL block was found in the Android app build script.");
        }

        var evidence = new List<ReleaseVersionEvidence>();
        var unresolved = new HashSet<ReleaseVersionField>();
        var defaultConfigCount = 0;

        foreach (var androidBlock in androidBlocks)
        {
            foreach (var defaultConfigBlock in FindNamedBlocks(mask, androidBlock, "defaultConfig"))
            {
                defaultConfigCount++;
                ParseVersionName(
                    text,
                    mask,
                    defaultConfigBlock,
                    scriptPath,
                    pubspec.PubspecPath,
                    pubspecVersionName,
                    evidence,
                    unresolved);
                ParseVersionCode(
                    text,
                    mask,
                    defaultConfigBlock,
                    scriptPath,
                    pubspec.PubspecPath,
                    pubspecVersionCode,
                    evidence,
                    unresolved);
            }
        }

        if (defaultConfigCount == 0)
        {
            return Empty(
                ReleaseVersionStatus.VersionNotFound,
                gradleDsl,
                pubspec,
                "No direct defaultConfig block was found in the Android app build script.");
        }

        var versionNameOk = TrySelect(
            ReleaseVersionField.VersionName,
            evidence,
            out var versionName,
            out var versionNameAmbiguous);
        var versionCodeOk = TrySelect(
            ReleaseVersionField.VersionCode,
            evidence,
            out var versionCode,
            out var versionCodeAmbiguous);

        if (!versionNameOk || !versionCodeOk)
        {
            var fields = new List<string>();
            if (versionNameAmbiguous) fields.Add("versionName");
            if (versionCodeAmbiguous) fields.Add("versionCode");

            return Result(
                ReleaseVersionStatus.Ambiguous,
                gradleDsl,
                pubspec,
                versionName,
                versionCode,
                evidence.ToArray(),
                unresolved.OrderBy(field => field).ToArray(),
                $"Conflicting configured release-version declarations were found for {string.Join(", ", fields)}; no value was selected implicitly.");
        }

        if (unresolved.Contains(ReleaseVersionField.VersionName))
            versionName = null;
        if (unresolved.Contains(ReleaseVersionField.VersionCode))
            versionCode = null;

        var resolvedCount = new[] { versionName, versionCode }.Count(value => value is not null);
        if (resolvedCount == 0)
        {
            return Result(
                ReleaseVersionStatus.VersionNotFound,
                gradleDsl,
                pubspec,
                null,
                null,
                evidence.ToArray(),
                unresolved.OrderBy(field => field).ToArray(),
                unresolved.Count > 0
                    ? "Release-version declarations included unresolved dynamic/non-literal values or Flutter references that could not be resolved from pubspec metadata. Values were not guessed."
                    : "No supported defaultConfig versionName or versionCode declarations were found in the Android app build script.");
        }

        var status = resolvedCount == 2
            ? ReleaseVersionStatus.Succeeded
            : ReleaseVersionStatus.Partial;

        var missing = new List<string>();
        if (versionName is null) missing.Add("versionName");
        if (versionCode is null) missing.Add("versionCode");

        return Result(
            status,
            gradleDsl,
            pubspec,
            versionName,
            versionCode,
            evidence.ToArray(),
            unresolved.OrderBy(field => field).ToArray(),
            status == ReleaseVersionStatus.Succeeded
                ? "Configured default Android release version was resolved without executing Gradle. CLI build-name/build-number overrides are outside this static analyzer result."
                : $"Configured default Android release version was partially resolved; unresolved/missing fields: {string.Join(", ", missing)}.");
    }

    private static void ParseVersionName(
        string text,
        string mask,
        BlockRange block,
        string scriptPath,
        string pubspecPath,
        string? pubspecVersionName,
        ICollection<ReleaseVersionEvidence> evidence,
        ISet<ReleaseVersionField> unresolved)
    {
        foreach (var token in FindDirectTokens(mask, block, "versionName"))
        {
            if (!TryReadStatementExpression(text, token.End, block.End, out var expression))
            {
                unresolved.Add(ReleaseVersionField.VersionName);
                continue;
            }

            if (TryParseStaticString(expression, out var value))
            {
                evidence.Add(new ReleaseVersionEvidence(
                    ReleaseVersionField.VersionName,
                    ReleaseVersionSourceKind.StaticGradle,
                    value,
                    null,
                    scriptPath,
                    null));
                continue;
            }

            if (IsFlutterVersionNameReference(expression))
            {
                if (pubspecVersionName is not null)
                {
                    evidence.Add(new ReleaseVersionEvidence(
                        ReleaseVersionField.VersionName,
                        ReleaseVersionSourceKind.FlutterPubspecReference,
                        pubspecVersionName,
                        null,
                        scriptPath,
                        pubspecPath));
                }
                else
                {
                    unresolved.Add(ReleaseVersionField.VersionName);
                }
                continue;
            }

            unresolved.Add(ReleaseVersionField.VersionName);
        }
    }

    private static void ParseVersionCode(
        string text,
        string mask,
        BlockRange block,
        string scriptPath,
        string pubspecPath,
        int? pubspecVersionCode,
        ICollection<ReleaseVersionEvidence> evidence,
        ISet<ReleaseVersionField> unresolved)
    {
        foreach (var token in FindDirectTokens(mask, block, "versionCode"))
        {
            if (!TryReadStatementExpression(text, token.End, block.End, out var expression))
            {
                unresolved.Add(ReleaseVersionField.VersionCode);
                continue;
            }

            if (TryParsePositiveInteger(expression, out var value))
            {
                evidence.Add(new ReleaseVersionEvidence(
                    ReleaseVersionField.VersionCode,
                    ReleaseVersionSourceKind.StaticGradle,
                    value.ToString(CultureInfo.InvariantCulture),
                    value,
                    scriptPath,
                    null));
                continue;
            }

            if (IsFlutterVersionCodeReference(expression))
            {
                if (pubspecVersionCode is { } code)
                {
                    evidence.Add(new ReleaseVersionEvidence(
                        ReleaseVersionField.VersionCode,
                        ReleaseVersionSourceKind.FlutterPubspecReference,
                        code.ToString(CultureInfo.InvariantCulture),
                        code,
                        scriptPath,
                        pubspecPath));
                }
                else
                {
                    unresolved.Add(ReleaseVersionField.VersionCode);
                }
                continue;
            }

            unresolved.Add(ReleaseVersionField.VersionCode);
        }
    }

    private static bool TrySelect(
        ReleaseVersionField field,
        IReadOnlyList<ReleaseVersionEvidence> evidence,
        out ReleaseVersionValue? selected,
        out bool ambiguous)
    {
        var candidates = evidence
            .Where(item => item.Field == field)
            .ToArray();

        var distinctValues = candidates
            .Select(item => (item.Value, item.NumericValue))
            .Distinct()
            .ToArray();

        ambiguous = distinctValues.Length > 1;
        if (ambiguous || candidates.Length == 0)
        {
            selected = null;
            return !ambiguous;
        }

        var preferred = candidates
            .OrderBy(item => item.SourceKind == ReleaseVersionSourceKind.StaticGradle ? 0 : 1)
            .First();

        selected = new ReleaseVersionValue(
            field,
            preferred.SourceKind,
            preferred.Value,
            preferred.NumericValue,
            preferred.ScriptPath,
            preferred.PubspecPath);
        return true;
    }

    private static bool TryParsePubspecVersionName(string? version, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(version) || version.Length > MaxVersionNameLength)
            return false;

        var plus = version.IndexOf('+');
        if (plus >= 0 && version.IndexOf('+', plus + 1) >= 0)
            return false;

        var candidate = (plus >= 0 ? version[..plus] : version).Trim();
        if (candidate.Length == 0 || candidate.Length > MaxVersionNameLength)
            return false;
        if (candidate.Any(char.IsControl) || candidate.Any(char.IsWhiteSpace))
            return false;

        value = candidate;
        return true;
    }

    private static bool TryParsePubspecVersionCode(string? version, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(version))
            return false;

        var plus = version.IndexOf('+');
        if (plus < 0 || plus == version.Length - 1 || version.IndexOf('+', plus + 1) >= 0)
            return false;

        var candidate = version[(plus + 1)..].Trim();
        return int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private static bool TryParseStaticString(string expression, out string value)
    {
        value = string.Empty;
        var candidate = TrimSingleOuterParentheses(expression);
        if (candidate.Length < 2 || candidate[0] is not ('\'' or '"') || candidate[^1] != candidate[0])
            return false;

        var quote = candidate[0];
        var literal = candidate[1..^1];
        if (string.IsNullOrWhiteSpace(literal) || literal.Length > MaxVersionNameLength)
            return false;
        if (literal.Any(char.IsControl) || literal.Contains('\\'))
            return false;
        if (quote == '"' && literal.Contains('$'))
            return false;

        value = literal;
        return true;
    }

    private static bool TryParsePositiveInteger(string expression, out int value)
    {
        var candidate = TrimSingleOuterParentheses(expression);
        return int.TryParse(candidate, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private static bool IsFlutterVersionNameReference(string expression)
    {
        var candidate = TrimSingleOuterParentheses(expression);
        return string.Equals(candidate, "flutter.versionName", StringComparison.Ordinal) ||
               string.Equals(candidate, "flutterVersionName", StringComparison.Ordinal);
    }

    private static bool IsFlutterVersionCodeReference(string expression)
    {
        var candidate = TrimSingleOuterParentheses(expression);
        return string.Equals(candidate, "flutter.versionCode", StringComparison.Ordinal) ||
               string.Equals(candidate, "flutterVersionCode", StringComparison.Ordinal) ||
               string.Equals(candidate, "flutter.versionCode.toInteger()", StringComparison.Ordinal) ||
               string.Equals(candidate, "flutterVersionCode.toInteger()", StringComparison.Ordinal);
    }

    private static string TrimSingleOuterParentheses(string expression)
    {
        var candidate = expression.Trim();
        if (candidate.Length >= 2 && candidate[0] == '(' && candidate[^1] == ')' &&
            IsSingleOuterParenthesizedExpression(candidate))
        {
            return candidate[1..^1].Trim();
        }

        return candidate;
    }

    private static bool IsSingleOuterParenthesizedExpression(string expression)
    {
        var depth = 0;
        char quote = '\0';
        var escaped = false;

        for (var index = 0; index < expression.Length; index++)
        {
            var current = expression[index];
            if (quote != '\0')
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == quote) quote = '\0';
                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                continue;
            }

            if (current == '(') depth++;
            else if (current == ')') depth--;

            if (depth == 0 && index < expression.Length - 1)
                return false;
            if (depth < 0)
                return false;
        }

        return depth == 0 && quote == '\0';
    }

    private static bool TryReadStatementExpression(
        string text,
        int tokenEnd,
        int blockEnd,
        out string expression)
    {
        expression = string.Empty;
        var cursor = tokenEnd;
        SkipHorizontalWhitespace(text, ref cursor, blockEnd);

        if (cursor < blockEnd && text[cursor] == '=')
        {
            cursor++;
            SkipHorizontalWhitespace(text, ref cursor, blockEnd);
        }

        if (cursor >= blockEnd || text[cursor] is '\r' or '\n')
            return false;

        var output = new StringBuilder();
        char quote = '\0';
        var escaped = false;
        var parenthesisDepth = 0;

        while (cursor < blockEnd)
        {
            var current = text[cursor];
            var next = cursor + 1 < blockEnd ? text[cursor + 1] : '\0';

            if (quote != '\0')
            {
                output.Append(current);
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == quote) quote = '\0';
                cursor++;
                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                output.Append(current);
                cursor++;
                continue;
            }

            if (current == '/' && next == '/')
                break;

            if (current == '/' && next == '*')
            {
                var close = text.IndexOf("*/", cursor + 2, StringComparison.Ordinal);
                if (close < 0 || close >= blockEnd)
                    return false;

                output.Append(' ');
                cursor = close + 2;
                continue;
            }

            if (current == '(')
            {
                parenthesisDepth++;
                output.Append(current);
                cursor++;
                continue;
            }

            if (current == ')')
            {
                if (parenthesisDepth == 0)
                    return false;
                parenthesisDepth--;
                output.Append(current);
                cursor++;
                continue;
            }

            if (parenthesisDepth == 0 && current is '\r' or '\n' or ';' or '}')
                break;

            output.Append(current);
            cursor++;
        }

        if (quote != '\0' || parenthesisDepth != 0)
            return false;

        expression = output.ToString().Trim();
        return expression.Length > 0;
    }

    private static IReadOnlyList<BlockRange> FindNamedBlocks(string mask, BlockRange parent, string name)
    {
        var blocks = new List<BlockRange>();
        var depth = 0;
        var index = parent.Start;

        while (index < parent.End)
        {
            var current = mask[index];
            if (current == '{')
            {
                depth++;
                index++;
                continue;
            }

            if (current == '}')
            {
                if (depth > 0) depth--;
                index++;
                continue;
            }

            if (depth == 0 && IsIdentifierStart(current))
            {
                var tokenStart = index;
                index++;
                while (index < parent.End && IsIdentifierPart(mask[index]))
                    index++;

                if (!mask.AsSpan(tokenStart, index - tokenStart).Equals(name.AsSpan(), StringComparison.Ordinal))
                    continue;

                var cursor = index;
                SkipWhitespace(mask, ref cursor, parent.End);
                if (cursor >= parent.End || mask[cursor] != '{')
                    continue;

                var close = FindMatchingBrace(mask, cursor, parent.End);
                if (close < 0)
                    continue;

                blocks.Add(new BlockRange(cursor + 1, close));
                index = close + 1;
                continue;
            }

            index++;
        }

        return blocks;
    }

    private static IReadOnlyList<TokenRange> FindDirectTokens(string mask, BlockRange block, string keyword)
    {
        var tokens = new List<TokenRange>();
        var depth = 0;
        var index = block.Start;

        while (index < block.End)
        {
            var current = mask[index];
            if (current == '{')
            {
                depth++;
                index++;
                continue;
            }

            if (current == '}')
            {
                if (depth > 0) depth--;
                index++;
                continue;
            }

            if (depth == 0 && IsIdentifierStart(current))
            {
                var tokenStart = index;
                index++;
                while (index < block.End && IsIdentifierPart(mask[index]))
                    index++;

                if (mask.AsSpan(tokenStart, index - tokenStart).Equals(keyword.AsSpan(), StringComparison.Ordinal) &&
                    IsStatementLeadingToken(mask, block.Start, tokenStart))
                {
                    tokens.Add(new TokenRange(tokenStart, index));
                }
                continue;
            }

            index++;
        }

        return tokens;
    }

    private static bool IsStatementLeadingToken(string mask, int blockStart, int tokenStart)
    {
        for (var index = tokenStart - 1; index >= blockStart; index--)
        {
            var current = mask[index];
            if (current is ' ' or '\t' or '\f')
                continue;

            return current is '\r' or '\n' or ';';
        }

        return true;
    }

    private static int FindMatchingBrace(string mask, int openBrace, int end)
    {
        var depth = 1;
        for (var index = openBrace + 1; index < end; index++)
        {
            if (mask[index] == '{') depth++;
            else if (mask[index] == '}') depth--;

            if (depth == 0)
                return index;
        }

        return -1;
    }

    private static string BuildStructuralMask(string text)
    {
        var output = new StringBuilder(text.Length);
        var inLineComment = false;
        var inBlockComment = false;
        char quote = '\0';
        char tripleQuote = '\0';
        var escaped = false;

        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            var third = index + 2 < text.Length ? text[index + 2] : '\0';

            if (inLineComment)
            {
                if (current is '\r' or '\n')
                {
                    inLineComment = false;
                    output.Append(current);
                }
                else
                {
                    output.Append(' ');
                }
                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    output.Append("  ");
                    index++;
                    inBlockComment = false;
                }
                else
                {
                    output.Append(current is '\r' or '\n' ? current : ' ');
                }
                continue;
            }

            if (tripleQuote != '\0')
            {
                if (current == tripleQuote && next == tripleQuote && third == tripleQuote)
                {
                    output.Append("   ");
                    index += 2;
                    tripleQuote = '\0';
                }
                else
                {
                    output.Append(current is '\r' or '\n' ? current : ' ');
                }
                continue;
            }

            if (quote != '\0')
            {
                output.Append(current is '\r' or '\n' ? current : ' ');
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == quote) quote = '\0';
                continue;
            }

            if (current == '/' && next == '/')
            {
                output.Append("  ");
                index++;
                inLineComment = true;
                continue;
            }

            if (current == '/' && next == '*')
            {
                output.Append("  ");
                index++;
                inBlockComment = true;
                continue;
            }

            if (current is '\'' or '"')
            {
                if (next == current && third == current)
                {
                    output.Append("   ");
                    index += 2;
                    tripleQuote = current;
                }
                else
                {
                    output.Append(' ');
                    quote = current;
                }
                continue;
            }

            output.Append(current);
        }

        return output.ToString();
    }

    private static void SkipWhitespace(string text, ref int cursor, int end)
    {
        while (cursor < end && char.IsWhiteSpace(text[cursor]))
            cursor++;
    }

    private static void SkipHorizontalWhitespace(string text, ref int cursor, int end)
    {
        while (cursor < end && text[cursor] is ' ' or '\t' or '\f')
            cursor++;
    }

    private static bool IsIdentifierStart(char value)
        => char.IsLetter(value) || value == '_';

    private static bool IsIdentifierPart(char value)
        => char.IsLetterOrDigit(value) || value == '_';

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(left),
            Path.TrimEndingDirectorySeparator(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static ReleaseVersionResult Empty(
        ReleaseVersionStatus status,
        GradleDslDetectionResult gradleDsl,
        PubspecParseResult pubspec,
        string message)
        => Result(
            status,
            gradleDsl,
            pubspec,
            null,
            null,
            Array.Empty<ReleaseVersionEvidence>(),
            Array.Empty<ReleaseVersionField>(),
            message);

    private static ReleaseVersionResult Result(
        ReleaseVersionStatus status,
        GradleDslDetectionResult gradleDsl,
        PubspecParseResult pubspec,
        ReleaseVersionValue? versionName,
        ReleaseVersionValue? versionCode,
        IReadOnlyList<ReleaseVersionEvidence> evidence,
        IReadOnlyList<ReleaseVersionField> unresolvedFields,
        string message)
        => new(
            status,
            gradleDsl,
            pubspec.PubspecPath,
            pubspec.Metadata?.Version?.Trim(),
            versionName,
            versionCode,
            evidence,
            unresolvedFields,
            message);

    private readonly record struct BlockRange(int Start, int End);
    private readonly record struct TokenRange(int Start, int End);
}
