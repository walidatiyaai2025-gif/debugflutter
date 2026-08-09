using System.IO;
using System.Text;

namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public sealed class ProductFlavorDetector : IProductFlavorDetector
{
    private const long MaxScriptBytes = 512 * 1024;
    private const int MaxStaticValueLength = 512;
    private const int MaxFlavorNameLength = 128;

    private static readonly HashSet<string> ReservedContainerBlocks = new(StringComparer.Ordinal)
    {
        "all",
        "configureEach",
        "matching",
        "named",
        "getByName",
        "create",
        "register",
        "maybeCreate",
        "whenObjectAdded",
        "withType"
    };

    public ProductFlavorDetectionResult Detect(GradleDslDetectionResult gradleDsl)
    {
        ArgumentNullException.ThrowIfNull(gradleDsl);

        if (!gradleDsl.IsSuccess || string.IsNullOrWhiteSpace(gradleDsl.AndroidDirectory))
        {
            return Empty(
                ProductFlavorDetectionStatus.GradleDslUnavailable,
                gradleDsl,
                "A successful FBD-604 Gradle DSL result is required before product-flavor detection.");
        }

        var appScripts = gradleDsl.Scripts
            .Where(script => script.Role == GradleScriptRole.AppBuild)
            .ToArray();

        if (appScripts.Length == 0)
        {
            return Empty(
                ProductFlavorDetectionStatus.AppBuildScriptUnavailable,
                gradleDsl,
                "FBD-604 did not provide an Android app build script. Product flavors were not inferred from unrelated files.");
        }

        if (appScripts.Length != 1)
        {
            return Empty(
                ProductFlavorDetectionStatus.Ambiguous,
                gradleDsl,
                "Multiple Android app build scripts were supplied; no product-flavor source was selected implicitly.");
        }

        var appScript = appScripts[0];
        if (appScript.Dsl is not GradleDslKind.Groovy and not GradleDslKind.Kotlin)
        {
            return Empty(
                ProductFlavorDetectionStatus.Ambiguous,
                gradleDsl,
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
                ProductFlavorDetectionStatus.UnsafePath,
                gradleDsl,
                $"Android app build-script path is invalid: {ex.Message}");
        }

        if (!PathsEqual(scriptPath, expectedPath))
        {
            return Empty(
                ProductFlavorDetectionStatus.UnsafePath,
                gradleDsl,
                "FBD-604 supplied an app build script outside the expected android/app Gradle location.");
        }

        string text;
        try
        {
            if (!Directory.Exists(androidDirectory) || IsReparsePoint(androidDirectory) ||
                !Directory.Exists(appDirectory) || IsReparsePoint(appDirectory))
            {
                return Empty(
                    ProductFlavorDetectionStatus.UnsafePath,
                    gradleDsl,
                    "The Android/app project boundary is missing or is now a reparse point/symbolic link.");
            }

            if (!File.Exists(scriptPath))
            {
                return Empty(
                    ProductFlavorDetectionStatus.AppBuildScriptUnavailable,
                    gradleDsl,
                    "The FBD-604 app build-script evidence is stale because the file is no longer available.");
            }

            if (IsReparsePoint(scriptPath))
            {
                return Empty(
                    ProductFlavorDetectionStatus.UnsafePath,
                    gradleDsl,
                    "The Android app build script is a reparse point/symbolic link and was not followed.");
            }

            if (new FileInfo(scriptPath).Length > MaxScriptBytes)
            {
                return Empty(
                    ProductFlavorDetectionStatus.FileTooLarge,
                    gradleDsl,
                    $"The Android app build script exceeds the {MaxScriptBytes} byte inspection limit.");
            }

            text = File.ReadAllText(scriptPath, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return Empty(
                ProductFlavorDetectionStatus.ReadFailed,
                gradleDsl,
                $"The Android app build script could not be read: {ex.Message}");
        }

        var mask = BuildStructuralMask(text);
        var document = new BlockRange(0, mask.Length);
        var androidBlocks = FindNamedBlocks(mask, document, "android");
        if (androidBlocks.Count == 0)
        {
            return new ProductFlavorDetectionResult(
                ProductFlavorDetectionStatus.NoFlavors,
                gradleDsl,
                Array.Empty<string>(),
                Array.Empty<AndroidProductFlavor>(),
                Array.Empty<ProductFlavorEvidence>(),
                0,
                false,
                "No top-level Android DSL block was found; no product flavors were enumerated.");
        }

        var declaredDimensions = new HashSet<string>(StringComparer.Ordinal);
        var hasUnresolvedDimensionDeclarations = false;
        var flavorBlocks = new List<FlavorBlock>();
        var unresolvedFlavorDeclarations = 0;
        var productFlavorsBlockCount = 0;

        foreach (var androidBlock in androidBlocks)
        {
            ParseDeclaredDimensions(
                text,
                mask,
                androidBlock,
                declaredDimensions,
                ref hasUnresolvedDimensionDeclarations);

            foreach (var productFlavorsBlock in FindNamedBlocks(mask, androidBlock, "productFlavors"))
            {
                productFlavorsBlockCount++;
                FindFlavorBlocks(
                    text,
                    mask,
                    productFlavorsBlock,
                    flavorBlocks,
                    ref unresolvedFlavorDeclarations);
            }
        }

        if (productFlavorsBlockCount == 0)
        {
            return new ProductFlavorDetectionResult(
                ProductFlavorDetectionStatus.NoFlavors,
                gradleDsl,
                declaredDimensions.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                Array.Empty<AndroidProductFlavor>(),
                Array.Empty<ProductFlavorEvidence>(),
                0,
                hasUnresolvedDimensionDeclarations,
                "No direct productFlavors block was found in the Android app build script.");
        }

        var duplicateNames = flavorBlocks
            .GroupBy(block => block.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var evidence = new List<ProductFlavorEvidence>();
        var unresolvedByFlavor = new Dictionary<string, HashSet<ProductFlavorField>>(StringComparer.Ordinal);

        foreach (var flavorBlock in flavorBlocks)
        {
            if (!unresolvedByFlavor.TryGetValue(flavorBlock.Name, out var unresolved))
            {
                unresolved = new HashSet<ProductFlavorField>();
                unresolvedByFlavor.Add(flavorBlock.Name, unresolved);
            }

            if (flavorBlock.Body.End <= flavorBlock.Body.Start)
                continue;

            ParseFlavorStringField(
                text,
                mask,
                flavorBlock,
                "dimension",
                ProductFlavorField.Dimension,
                allowEmpty: false,
                scriptPath,
                evidence,
                unresolved);
            ParseFlavorStringField(
                text,
                mask,
                flavorBlock,
                "applicationId",
                ProductFlavorField.ApplicationId,
                allowEmpty: false,
                scriptPath,
                evidence,
                unresolved);
            ParseFlavorStringField(
                text,
                mask,
                flavorBlock,
                "applicationIdSuffix",
                ProductFlavorField.ApplicationIdSuffix,
                allowEmpty: true,
                scriptPath,
                evidence,
                unresolved);
            ParseFlavorStringField(
                text,
                mask,
                flavorBlock,
                "versionNameSuffix",
                ProductFlavorField.VersionNameSuffix,
                allowEmpty: true,
                scriptPath,
                evidence,
                unresolved);
        }

        var hasAmbiguousField = false;
        var flavors = new List<AndroidProductFlavor>();
        var uniqueBlocks = flavorBlocks
            .GroupBy(block => block.Name, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(block => block.Name, StringComparer.Ordinal)
            .ToArray();

        foreach (var flavorBlock in uniqueBlocks)
        {
            var unresolved = unresolvedByFlavor.TryGetValue(flavorBlock.Name, out var set)
                ? set
                : new HashSet<ProductFlavorField>();

            var dimension = SelectField(
                flavorBlock.Name,
                ProductFlavorField.Dimension,
                evidence,
                scriptPath,
                out var dimensionAmbiguous);
            var applicationId = SelectField(
                flavorBlock.Name,
                ProductFlavorField.ApplicationId,
                evidence,
                scriptPath,
                out var applicationIdAmbiguous);
            var applicationIdSuffix = SelectField(
                flavorBlock.Name,
                ProductFlavorField.ApplicationIdSuffix,
                evidence,
                scriptPath,
                out var applicationIdSuffixAmbiguous);
            var versionNameSuffix = SelectField(
                flavorBlock.Name,
                ProductFlavorField.VersionNameSuffix,
                evidence,
                scriptPath,
                out var versionNameSuffixAmbiguous);

            hasAmbiguousField |= dimensionAmbiguous ||
                                 applicationIdAmbiguous ||
                                 applicationIdSuffixAmbiguous ||
                                 versionNameSuffixAmbiguous;

            if (unresolved.Contains(ProductFlavorField.Dimension)) dimension = null;
            if (unresolved.Contains(ProductFlavorField.ApplicationId)) applicationId = null;
            if (unresolved.Contains(ProductFlavorField.ApplicationIdSuffix)) applicationIdSuffix = null;
            if (unresolved.Contains(ProductFlavorField.VersionNameSuffix)) versionNameSuffix = null;

            if (dimension is null &&
                !unresolved.Contains(ProductFlavorField.Dimension) &&
                declaredDimensions.Count == 1 &&
                !hasUnresolvedDimensionDeclarations)
            {
                var inferred = declaredDimensions.Single();
                dimension = new ProductFlavorValue(
                    ProductFlavorField.Dimension,
                    ProductFlavorValueSourceKind.InferredSingleDimension,
                    inferred,
                    scriptPath);
                evidence.Add(new ProductFlavorEvidence(
                    flavorBlock.Name,
                    ProductFlavorField.Dimension,
                    ProductFlavorValueSourceKind.InferredSingleDimension,
                    inferred,
                    scriptPath));
            }

            flavors.Add(new AndroidProductFlavor(
                flavorBlock.Name,
                dimension,
                applicationId,
                applicationIdSuffix,
                versionNameSuffix,
                unresolved.OrderBy(field => field).ToArray(),
                scriptPath));
        }

        if (duplicateNames.Length > 0 || hasAmbiguousField)
        {
            var details = duplicateNames.Length > 0
                ? $" Duplicate flavor names: {string.Join(", ", duplicateNames)}."
                : string.Empty;

            return new ProductFlavorDetectionResult(
                ProductFlavorDetectionStatus.Ambiguous,
                gradleDsl,
                declaredDimensions.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                flavors,
                evidence.ToArray(),
                unresolvedFlavorDeclarations,
                hasUnresolvedDimensionDeclarations,
                "Conflicting or duplicate static product-flavor declarations were found; ambiguous values were not selected." + details);
        }

        if (flavors.Count == 0 && unresolvedFlavorDeclarations == 0)
        {
            return new ProductFlavorDetectionResult(
                ProductFlavorDetectionStatus.NoFlavors,
                gradleDsl,
                declaredDimensions.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                Array.Empty<AndroidProductFlavor>(),
                Array.Empty<ProductFlavorEvidence>(),
                0,
                hasUnresolvedDimensionDeclarations,
                "The productFlavors container is present but contains no supported flavor declarations.");
        }

        var hasUnresolvedFlavorFields = flavors.Any(flavor => flavor.UnresolvedFields.Count > 0);
        var status = unresolvedFlavorDeclarations > 0 ||
                     hasUnresolvedDimensionDeclarations ||
                     hasUnresolvedFlavorFields
            ? ProductFlavorDetectionStatus.Partial
            : ProductFlavorDetectionStatus.Succeeded;

        return new ProductFlavorDetectionResult(
            status,
            gradleDsl,
            declaredDimensions.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            flavors,
            evidence.ToArray(),
            unresolvedFlavorDeclarations,
            hasUnresolvedDimensionDeclarations,
            status == ProductFlavorDetectionStatus.Succeeded
                ? $"Enumerated {flavors.Count} statically configured Android product flavor(s) without executing Gradle."
                : $"Enumerated {flavors.Count} Android product flavor(s) with unresolved dynamic declarations preserved explicitly.");
    }

    private static void ParseDeclaredDimensions(
        string text,
        string mask,
        BlockRange androidBlock,
        ISet<string> dimensions,
        ref bool hasUnresolvedDeclarations)
    {
        foreach (var token in FindDirectTokens(mask, androidBlock, "flavorDimensions"))
        {
            if (!TryReadStatementExpression(text, token.End, androidBlock.End, out var expression) ||
                !TryParseStaticStringList(expression, out var values))
            {
                hasUnresolvedDeclarations = true;
                continue;
            }

            foreach (var value in values)
                dimensions.Add(value);
        }
    }

    private static void FindFlavorBlocks(
        string text,
        string mask,
        BlockRange container,
        ICollection<FlavorBlock> blocks,
        ref int unresolvedDeclarations)
    {
        var depth = 0;
        var index = container.Start;

        while (index < container.End)
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

            if (depth != 0 || !IsIdentifierStart(current))
            {
                index++;
                continue;
            }

            var tokenStart = index;
            index++;
            while (index < container.End && IsIdentifierPart(mask[index]))
                index++;

            var token = mask[tokenStart..index];
            if (!IsStatementLeadingToken(mask, container.Start, tokenStart))
                continue;

            if (token is "create" or "register")
            {
                var cursor = index;
                SkipWhitespace(mask, ref cursor, container.End);
                if (cursor >= container.End || mask[cursor] != '(')
                {
                    unresolvedDeclarations++;
                    continue;
                }

                var closeParen = FindMatchingDelimiter(mask, cursor, container.End, '(', ')');
                if (closeParen < 0)
                {
                    unresolvedDeclarations++;
                    continue;
                }

                var nameExpression = text[(cursor + 1)..closeParen];
                var hasStaticName = TryParseStaticString(nameExpression, allowEmpty: false, out var name) &&
                                    IsValidFlavorName(name);

                var afterCall = closeParen + 1;
                SkipWhitespace(mask, ref afterCall, container.End);

                if (afterCall < container.End && mask[afterCall] == '{')
                {
                    var closeBrace = FindMatchingBrace(mask, afterCall, container.End);
                    if (closeBrace < 0)
                    {
                        unresolvedDeclarations++;
                        index = closeParen + 1;
                        continue;
                    }

                    if (hasStaticName)
                        blocks.Add(new FlavorBlock(name, new BlockRange(afterCall + 1, closeBrace)));
                    else
                        unresolvedDeclarations++;

                    index = closeBrace + 1;
                    continue;
                }

                if (hasStaticName)
                {
                    blocks.Add(new FlavorBlock(name, new BlockRange(closeParen + 1, closeParen + 1)));
                    if (!IsStatementTail(mask, afterCall, container.End))
                        unresolvedDeclarations++;
                }
                else
                {
                    unresolvedDeclarations++;
                }

                index = closeParen + 1;
                continue;
            }

            if (ReservedContainerBlocks.Contains(token) || !IsValidFlavorName(token))
                continue;

            var blockCursor = index;
            SkipWhitespace(mask, ref blockCursor, container.End);
            if (blockCursor >= container.End || mask[blockCursor] != '{')
                continue;

            var blockClose = FindMatchingBrace(mask, blockCursor, container.End);
            if (blockClose < 0)
            {
                unresolvedDeclarations++;
                continue;
            }

            blocks.Add(new FlavorBlock(token, new BlockRange(blockCursor + 1, blockClose)));
            index = blockClose + 1;
        }
    }

    private static void ParseFlavorStringField(
        string text,
        string mask,
        FlavorBlock flavor,
        string keyword,
        ProductFlavorField field,
        bool allowEmpty,
        string scriptPath,
        ICollection<ProductFlavorEvidence> evidence,
        ISet<ProductFlavorField> unresolved)
    {
        foreach (var token in FindDirectTokens(mask, flavor.Body, keyword))
        {
            if (!TryReadStatementExpression(text, token.End, flavor.Body.End, out var expression) ||
                !TryParseStaticString(expression, allowEmpty, out var value))
            {
                unresolved.Add(field);
                continue;
            }

            evidence.Add(new ProductFlavorEvidence(
                flavor.Name,
                field,
                ProductFlavorValueSourceKind.StaticGradle,
                value,
                scriptPath));
        }
    }

    private static ProductFlavorValue? SelectField(
        string flavorName,
        ProductFlavorField field,
        IReadOnlyList<ProductFlavorEvidence> evidence,
        string scriptPath,
        out bool ambiguous)
    {
        var values = evidence
            .Where(item => item.FlavorName == flavorName && item.Field == field)
            .Select(item => item.Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        ambiguous = values.Length > 1;
        if (ambiguous || values.Length == 0)
            return null;

        return new ProductFlavorValue(
            field,
            ProductFlavorValueSourceKind.StaticGradle,
            values[0],
            scriptPath);
    }

    private static bool TryParseStaticStringList(string expression, out IReadOnlyList<string> values)
    {
        values = Array.Empty<string>();
        var candidate = expression.Trim();

        if (candidate.StartsWith("+=", StringComparison.Ordinal))
            candidate = candidate[2..].Trim();

        if (candidate.StartsWith(".add(", StringComparison.Ordinal) && candidate.EndsWith(')'))
            candidate = candidate[5..^1].Trim();
        else if (candidate.StartsWith("listOf(", StringComparison.Ordinal) && candidate.EndsWith(')'))
            candidate = candidate[7..^1].Trim();
        else if (candidate.Length >= 2 && candidate[0] == '[' && candidate[^1] == ']')
            candidate = candidate[1..^1].Trim();
        else if (candidate.Length >= 2 && candidate[0] == '(' && candidate[^1] == ')' &&
                 IsSingleOuterParenthesizedExpression(candidate))
            candidate = candidate[1..^1].Trim();

        if (candidate.Length == 0)
            return false;

        var parsed = new List<string>();
        var cursor = 0;

        while (cursor < candidate.Length)
        {
            SkipHorizontalWhitespace(candidate, ref cursor, candidate.Length);
            if (!TryReadQuotedLiteral(candidate, ref cursor, allowEmpty: false, out var value))
                return false;

            parsed.Add(value);
            SkipHorizontalWhitespace(candidate, ref cursor, candidate.Length);

            if (cursor == candidate.Length)
                break;

            if (candidate[cursor] != ',')
                return false;

            cursor++;
            SkipHorizontalWhitespace(candidate, ref cursor, candidate.Length);
            if (cursor == candidate.Length)
                return false;
        }

        values = parsed;
        return parsed.Count > 0;
    }

    private static bool TryParseStaticString(string expression, bool allowEmpty, out string value)
    {
        value = string.Empty;
        var candidate = TrimSingleOuterParentheses(expression);
        var cursor = 0;
        if (!TryReadQuotedLiteral(candidate, ref cursor, allowEmpty, out value))
            return false;

        SkipHorizontalWhitespace(candidate, ref cursor, candidate.Length);
        return cursor == candidate.Length;
    }

    private static bool TryReadQuotedLiteral(
        string text,
        ref int cursor,
        bool allowEmpty,
        out string value)
    {
        value = string.Empty;
        if (cursor >= text.Length || text[cursor] is not ('\'' or '"'))
            return false;

        var quote = text[cursor++];
        var start = cursor;
        var escaped = false;
        var interpolated = false;

        while (cursor < text.Length)
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
                interpolated = true;

            if (current == quote)
                break;

            cursor++;
        }

        if (cursor >= text.Length || text[cursor] != quote)
            return false;

        var literal = text[start..cursor];
        cursor++;

        if (escaped || interpolated || literal.Length > MaxStaticValueLength || literal.Any(char.IsControl))
            return false;
        if (!allowEmpty && string.IsNullOrWhiteSpace(literal))
            return false;

        value = literal;
        return true;
    }

    private static bool IsValidFlavorName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxFlavorNameLength)
            return false;
        if (!IsIdentifierStart(value[0]))
            return false;

        for (var index = 1; index < value.Length; index++)
        {
            var current = value[index];
            if (!IsIdentifierPart(current) && current != '-')
                return false;
        }

        return true;
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
        var bracketDepth = 0;

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

            if (current == '[')
            {
                bracketDepth++;
                output.Append(current);
                cursor++;
                continue;
            }

            if (current == ']')
            {
                if (bracketDepth == 0)
                    return false;
                bracketDepth--;
                output.Append(current);
                cursor++;
                continue;
            }

            if (parenthesisDepth == 0 && bracketDepth == 0 && current is '\r' or '\n' or ';' or '}')
                break;

            output.Append(current);
            cursor++;
        }

        if (quote != '\0' || parenthesisDepth != 0 || bracketDepth != 0)
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

    private static bool IsStatementTail(string mask, int cursor, int end)
    {
        while (cursor < end)
        {
            var current = mask[cursor];
            if (current is ' ' or '\t' or '\f')
            {
                cursor++;
                continue;
            }

            return current is '\r' or '\n' or ';' or '}';
        }

        return true;
    }

    private static int FindMatchingBrace(string mask, int openBrace, int end)
        => FindMatchingDelimiter(mask, openBrace, end, '{', '}');

    private static int FindMatchingDelimiter(string mask, int open, int end, char openToken, char closeToken)
    {
        var depth = 1;
        for (var index = open + 1; index < end; index++)
        {
            if (mask[index] == openToken) depth++;
            else if (mask[index] == closeToken) depth--;

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

    private static ProductFlavorDetectionResult Empty(
        ProductFlavorDetectionStatus status,
        GradleDslDetectionResult gradleDsl,
        string message)
        => new(
            status,
            gradleDsl,
            Array.Empty<string>(),
            Array.Empty<AndroidProductFlavor>(),
            Array.Empty<ProductFlavorEvidence>(),
            0,
            false,
            message);

    private readonly record struct BlockRange(int Start, int End);
    private readonly record struct TokenRange(int Start, int End);
    private readonly record struct FlavorBlock(string Name, BlockRange Body);
}
