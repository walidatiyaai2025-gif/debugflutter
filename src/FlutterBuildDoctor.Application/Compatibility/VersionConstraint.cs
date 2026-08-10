namespace FlutterBuildDoctor.Application.Compatibility;

public sealed record VersionConstraint(
    SemanticVersion? Minimum,
    bool MinimumInclusive,
    SemanticVersion? Maximum,
    bool MaximumInclusive)
{
    public bool Contains(SemanticVersion version)
    {
        if (Minimum is { } minimum)
        {
            var comparison = version.CompareTo(minimum);
            if (comparison < 0 || (!MinimumInclusive && comparison == 0))
            {
                return false;
            }
        }

        if (Maximum is { } maximum)
        {
            var comparison = version.CompareTo(maximum);
            if (comparison > 0 || (!MaximumInclusive && comparison == 0))
            {
                return false;
            }
        }

        return true;
    }

    public static VersionConstraint Parse(string value)
        => TryParse(value, out var constraint)
            ? constraint!
            : throw new FormatException($"'{value}' is not a supported version constraint.");

    public static bool TryParse(string? value, out VersionConstraint? constraint)
    {
        constraint = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith('^'))
        {
            if (!SemanticVersion.TryParse(text[1..], out var minimum))
            {
                return false;
            }

            var maximum = minimum.Major > 0
                ? new SemanticVersion(minimum.Major + 1)
                : minimum.Minor > 0
                    ? new SemanticVersion(0, minimum.Minor + 1)
                    : new SemanticVersion(0, 0, minimum.Patch + 1);
            constraint = new VersionConstraint(minimum, true, maximum, false);
            return true;
        }

        SemanticVersion? lower = null;
        var lowerInclusive = false;
        SemanticVersion? upper = null;
        var upperInclusive = false;
        var tokens = text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var token in tokens)
        {
            if (!TryParseComparator(token, out var comparator, out var version))
            {
                return false;
            }

            switch (comparator)
            {
                case Comparator.Exact:
                    MergeLower(ref lower, ref lowerInclusive, version, true);
                    MergeUpper(ref upper, ref upperInclusive, version, true);
                    break;
                case Comparator.GreaterThan:
                    MergeLower(ref lower, ref lowerInclusive, version, false);
                    break;
                case Comparator.GreaterThanOrEqual:
                    MergeLower(ref lower, ref lowerInclusive, version, true);
                    break;
                case Comparator.LessThan:
                    MergeUpper(ref upper, ref upperInclusive, version, false);
                    break;
                case Comparator.LessThanOrEqual:
                    MergeUpper(ref upper, ref upperInclusive, version, true);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported comparator {comparator}.");
            }
        }

        if (lower is null && upper is null)
        {
            return false;
        }

        if (lower is { } minimumValue && upper is { } maximumValue)
        {
            var comparison = minimumValue.CompareTo(maximumValue);
            if (comparison > 0 || (comparison == 0 && (!lowerInclusive || !upperInclusive)))
            {
                return false;
            }
        }

        constraint = new VersionConstraint(lower, lowerInclusive, upper, upperInclusive);
        return true;
    }

    private static void MergeLower(
        ref SemanticVersion? current,
        ref bool currentInclusive,
        SemanticVersion candidate,
        bool candidateInclusive)
    {
        if (current is null || candidate > current.Value)
        {
            current = candidate;
            currentInclusive = candidateInclusive;
            return;
        }

        if (candidate == current.Value)
        {
            currentInclusive = currentInclusive && candidateInclusive;
        }
    }

    private static void MergeUpper(
        ref SemanticVersion? current,
        ref bool currentInclusive,
        SemanticVersion candidate,
        bool candidateInclusive)
    {
        if (current is null || candidate < current.Value)
        {
            current = candidate;
            currentInclusive = candidateInclusive;
            return;
        }

        if (candidate == current.Value)
        {
            currentInclusive = currentInclusive && candidateInclusive;
        }
    }

    private static bool TryParseComparator(string token, out Comparator comparator, out SemanticVersion version)
    {
        comparator = Comparator.Exact;
        version = default;
        var value = token;

        if (token.StartsWith(">=", StringComparison.Ordinal))
        {
            comparator = Comparator.GreaterThanOrEqual;
            value = token[2..];
        }
        else if (token.StartsWith("<=", StringComparison.Ordinal))
        {
            comparator = Comparator.LessThanOrEqual;
            value = token[2..];
        }
        else if (token.StartsWith('>'))
        {
            comparator = Comparator.GreaterThan;
            value = token[1..];
        }
        else if (token.StartsWith('<'))
        {
            comparator = Comparator.LessThan;
            value = token[1..];
        }
        else if (token.StartsWith('='))
        {
            comparator = Comparator.Exact;
            value = token[1..];
        }

        return SemanticVersion.TryParse(value, out version);
    }

    private enum Comparator
    {
        Exact = 0,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual
    }
}
