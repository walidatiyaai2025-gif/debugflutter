using System.IO;
using System.Text;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed class AndroidIdentifierParser : IAndroidIdentifierParser
{
    private const long MaxScriptBytes = 512 * 1024;
    private const int MaxIdentifierLiteralLength = 512;

    public AndroidIdentifierResult Parse(GradleDslDetectionResult gradleDsl)
    {
        ArgumentNullException.ThrowIfNull(gradleDsl);

        if (!gradleDsl.IsSuccess || string.IsNullOrWhiteSpace(gradleDsl.AndroidDirectory))
            return Empty(AndroidIdentifierStatus.GradleDslUnavailable, gradleDsl,
                "A successful FBD-604 Gradle DSL result is required before Android identifier parsing.");

        var appScripts = gradleDsl.Scripts.Where(script => script.Role == GradleScriptRole.AppBuild).ToArray();
        if (appScripts.Length == 0)
            return Empty(AndroidIdentifierStatus.AppBuildScriptUnavailable, gradleDsl,
                "FBD-604 did not provide an Android app build script. Identifiers were not inferred from unrelated files.");
        if (appScripts.Length != 1)
            return Empty(AndroidIdentifierStatus.Ambiguous, gradleDsl,
                "Multiple Android app build scripts were supplied; no identifier source was selected implicitly.");

        var appScript = appScripts[0];
        if (appScript.Dsl is not GradleDslKind.Groovy and not GradleDslKind.Kotlin)
            return Empty(AndroidIdentifierStatus.Ambiguous, gradleDsl,
                "The Android app build script does not have a single Groovy or Kotlin DSL classification.");

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
            return Empty(AndroidIdentifierStatus.UnsafePath, gradleDsl,
                $"Android app build-script path is invalid: {ex.Message}");
        }

        if (!PathsEqual(scriptPath, expectedPath))
            return Empty(AndroidIdentifierStatus.UnsafePath, gradleDsl,
                "FBD-604 supplied an app build script outside the expected android/app Gradle location.");

        string text;
        try
        {
            if (!Directory.Exists(androidDirectory) || IsReparsePoint(androidDirectory) ||
                !Directory.Exists(appDirectory) || IsReparsePoint(appDirectory))
                return Empty(AndroidIdentifierStatus.UnsafePath, gradleDsl,
                    "The Android/app project boundary is missing or is now a reparse point/symbolic link.");

            if (!File.Exists(scriptPath))
                return Empty(AndroidIdentifierStatus.AppBuildScriptUnavailable, gradleDsl,
                    "The FBD-604 app build-script evidence is stale because the file is no longer available.");
            if (IsReparsePoint(scriptPath))
                return Empty(AndroidIdentifierStatus.UnsafePath, gradleDsl,
                    "The Android app build script is a reparse point/symbolic link and was not followed.");
            if (new FileInfo(scriptPath).Length > MaxScriptBytes)
                return Empty(AndroidIdentifierStatus.FileTooLarge, gradleDsl,
                    $"The Android app build script exceeds the {MaxScriptBytes} byte inspection limit.");

            text = File.ReadAllText(scriptPath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Empty(AndroidIdentifierStatus.ReadFailed, gradleDsl,
                $"The Android app build script could not be read: {ex.Message}");
        }

        var mask = BuildStructuralMask(text);
        var document = new BlockRange(0, mask.Length);
        var androidBlocks = FindNamedBlocks(mask, document, "android");
        if (androidBlocks.Count == 0)
            return Empty(AndroidIdentifierStatus.IdentifiersNotFound, gradleDsl,
                "No top-level Android DSL block was found in the Android app build script.");

        var evidence = new List<AndroidIdentifierEvidence>();
        var unresolved = new HashSet<AndroidIdentifierField>();

        foreach (var androidBlock in androidBlocks)
        {
            ParseField(
                text,
                mask,
                androidBlock,
                "namespace",
                AndroidIdentifierField.Namespace,
                scriptPath,
                evidence,
                unresolved);

            foreach (var defaultConfigBlock in FindNamedBlocks(mask, androidBlock, "defaultConfig"))
            {
                ParseField(
                    text,
                    mask,
                    defaultConfigBlock,
                    "applicationId",
                    AndroidIdentifierField.ApplicationId,
                    scriptPath,
                    evidence,
                    unresolved);
            }
        }

        var namespaceOk = TrySelect(
            AndroidIdentifierField.Namespace,
            evidence,
            scriptPath,
            out var namespaceValue,
            out var namespaceAmbiguous);
        var applicationIdOk = TrySelect(
            AndroidIdentifierField.ApplicationId,
            evidence,
            scriptPath,
            out var applicationIdValue,
            out var applicationIdAmbiguous);

        if (!namespaceOk || !applicationIdOk)
        {
            var fields = new List<string>();
            if (namespaceAmbiguous) fields.Add("namespace");
            if (applicationIdAmbiguous) fields.Add("applicationId");

            return Result(
                AndroidIdentifierStatus.Ambiguous,
                gradleDsl,
                namespaceValue,
                applicationIdValue,
                evidence.ToArray(),
                unresolved.OrderBy(field => field).ToArray(),
                $"Conflicting static Android identifier declarations were found for {string.Join(", ", fields)}; no value was selected implicitly.");
        }

        if (unresolved.Contains(AndroidIdentifierField.Namespace))
            namespaceValue = null;
        if (unresolved.Contains(AndroidIdentifierField.ApplicationId))
            applicationIdValue = null;

        var resolvedCount = new[] { namespaceValue, applicationIdValue }.Count(value => value is not null);
        if (resolvedCount == 0)
        {
            return Result(
                AndroidIdentifierStatus.IdentifiersNotFound,
                gradleDsl,
                null,
                null,
                evidence.ToArray(),
                unresolved.OrderBy(field => field).ToArray(),
                unresolved.Count > 0
                    ? "Android identifier declarations included unresolved dynamic or non-literal expressions, so no effective values were selected implicitly."
                    : "No supported static namespace or defaultConfig applicationId declarations were found in the Android app build script.");
        }

        var status = resolvedCount == 2 ? AndroidIdentifierStatus.Succeeded : AndroidIdentifierStatus.Partial;
        var missing = new List<string>();
        if (namespaceValue is null) missing.Add("namespace");
        if (applicationIdValue is null) missing.Add("applicationId");

        return Result(
            status,
            gradleDsl,
            namespaceValue,
            applicationIdValue,
            evidence.ToArray(),
            unresolved.OrderBy(field => field).ToArray(),
            status == AndroidIdentifierStatus.Succeeded
                ? "Android namespace and applicationId were detected from the expected app Gradle DSL scopes without executing Gradle."
                : $"Android identifiers were partially detected; unresolved/missing fields: {string.Join(", ", missing)}.");
    }

    private static AndroidIdentifierResult Empty(
        AndroidIdentifierStatus status,
        GradleDslDetectionResult gradleDsl,
        string message)
        => Result(
            status,
            gradleDsl,
            null,
            null,
            Array.Empty<AndroidIdentifierEvidence>(),
            Array.Empty<AndroidIdentifierField>(),
            message);

    private static void ParseField(
        string text,
        string mask,
        BlockRange block,
        string keyword,
        AndroidIdentifierField field,
        string scriptPath,
        ICollection<AndroidIdentifierEvidence> evidence,
        ISet<AndroidIdentifierField> unresolved)
    {
        foreach (var token in FindDirectTokens(mask, block, keyword))
        {
            if (TryReadStaticStringLiteral(text, token.End, block.End, out var value))
            {
                evidence.Add(new AndroidIdentifierEvidence(field, value, scriptPath));
            }
            else
            {
                unresolved.Add(field);
            }
        }
    }

    private static bool TrySelect(
        AndroidIdentifierField field,
        IReadOnlyList<AndroidIdentifierEvidence> evidence,
        string scriptPath,
        out AndroidIdentifierValue? selected,
        out bool ambiguous)
    {
        var values = evidence
            .Where(item => item.Field == field)
            .Select(item => item.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        ambiguous = values.Length > 1;
        if (ambiguous || values.Length == 0)
        {
            selected = null;
            return !ambiguous;
        }

        selected = new AndroidIdentifierValue(field, values[0], scriptPath);
        return true;
    }

    private static bool TryReadStaticStringLiteral(
        string text,
        int tokenEnd,
        int blockEnd,
        out string value)
    {
        value = string.Empty;
        var cursor = tokenEnd;
        SkipWhitespace(text, ref cursor, blockEnd);

        var parenthesized = false;
        if (cursor < blockEnd && text[cursor] == '=')
        {
            cursor++;
            SkipWhitespace(text, ref cursor, blockEnd);
        }
        else if (cursor < blockEnd && text[cursor] == '(')
        {
            parenthesized = true;
            cursor++;
            SkipWhitespace(text, ref cursor, blockEnd);
        }

        if (cursor >= blockEnd || text[cursor] is not ('\'' or '"'))
            return false;

        var quote = text[cursor];
        if (cursor + 2 < blockEnd && text[cursor + 1] == quote && text[cursor + 2] == quote)
            return false;

        cursor++;
        var start = cursor;
        var escaped = false;
        var interpolation = false;

        while (cursor < blockEnd)
        {
            var current = text[cursor];
            if (current is '\r' or '\n')
                return false;

            if (escaped)
            {
                escaped = false;
                cursor++;
                continue;
            }

            if (current == '\\')
            {
                escaped = true;
                cursor++;
                continue;
            }

            if (quote == '"' && current == '$')
                interpolation = true;

            if (current == quote)
                break;

            cursor++;
        }

        if (cursor >= blockEnd || text[cursor] != quote)
            return false;

        var literal = text[start..cursor];
        cursor++;

        if (escaped || interpolation || string.IsNullOrWhiteSpace(literal) || literal.Length > MaxIdentifierLiteralLength)
            return false;
        if (literal.Any(char.IsControl))
            return false;

        SkipHorizontalWhitespace(text, ref cursor, blockEnd);
        if (parenthesized)
        {
            if (cursor >= blockEnd || text[cursor] != ')')
                return false;
            cursor++;
            SkipHorizontalWhitespace(text, ref cursor, blockEnd);
        }

        if (!HasStaticStatementTail(text, cursor, blockEnd))
            return false;

        value = literal;
        return true;
    }

    private static bool HasStaticStatementTail(string text, int cursor, int blockEnd)
    {
        while (cursor < blockEnd)
        {
            var current = text[cursor];
            if (current is ' ' or '\t' or '\f')
            {
                cursor++;
                continue;
            }

            if (current is '\r' or '\n' or ';' or '}')
                return true;

            if (current == '/' && cursor + 1 < blockEnd && text[cursor + 1] == '/')
                return true;

            if (current == '/' && cursor + 1 < blockEnd && text[cursor + 1] == '*')
            {
                var close = text.IndexOf("*/", cursor + 2, StringComparison.Ordinal);
                if (close < 0 || close >= blockEnd)
                    return false;
                cursor = close + 2;
                continue;
            }

            return false;
        }

        return true;
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
                else output.Append(' ');
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
                else output.Append(current is '\r' or '\n' ? current : ' ');
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
                else output.Append(current is '\r' or '\n' ? current : ' ');
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

    private static AndroidIdentifierResult Result(
        AndroidIdentifierStatus status,
        GradleDslDetectionResult gradleDsl,
        AndroidIdentifierValue? namespaceValue,
        AndroidIdentifierValue? applicationId,
        IReadOnlyList<AndroidIdentifierEvidence> evidence,
        IReadOnlyList<AndroidIdentifierField> unresolvedFields,
        string message)
        => new(status, gradleDsl, namespaceValue, applicationId, evidence, unresolvedFields, message);

    private readonly record struct BlockRange(int Start, int End);
    private readonly record struct TokenRange(int Start, int End);
}
