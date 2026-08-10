namespace FlutterBuildDoctor.Domain.Compatibility;

public readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? PreRelease = null) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim().TrimStart('v', 'V');
        var metadataIndex = normalized.IndexOf('+');
        if (metadataIndex >= 0) normalized = normalized[..metadataIndex];

        string? preRelease = null;
        var dashIndex = normalized.IndexOf('-');
        if (dashIndex >= 0)
        {
            preRelease = normalized[(dashIndex + 1)..];
            normalized = normalized[..dashIndex];
        }

        var parts = normalized.Split('.', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 3) return false;
        if (!int.TryParse(parts[0], out var major) || major < 0) return false;
        var minor = 0;
        var patch = 0;
        if (parts.Length > 1 && (!int.TryParse(parts[1], out minor) || minor < 0)) return false;
        if (parts.Length > 2 && (!int.TryParse(parts[2], out patch) || patch < 0)) return false;

        version = new SemanticVersion(major, minor, patch, string.IsNullOrWhiteSpace(preRelease) ? null : preRelease);
        return true;
    }

    public static SemanticVersion Parse(string value)
        => TryParse(value, out var version)
            ? version
            : throw new FormatException($"'{value}' is not a valid semantic version.");

    public int CompareTo(SemanticVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;

        if (PreRelease is null && other.PreRelease is null) return 0;
        if (PreRelease is null) return 1;
        if (other.PreRelease is null) return -1;
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    private static int ComparePreRelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var length = Math.Max(leftParts.Length, rightParts.Length);
        for (var i = 0; i < length; i++)
        {
            if (i >= leftParts.Length) return -1;
            if (i >= rightParts.Length) return 1;
            var l = leftParts[i];
            var r = rightParts[i];
            var lNumeric = int.TryParse(l, out var li);
            var rNumeric = int.TryParse(r, out var ri);
            int comparison;
            if (lNumeric && rNumeric) comparison = li.CompareTo(ri);
            else if (lNumeric) comparison = -1;
            else if (rNumeric) comparison = 1;
            else comparison = string.CompareOrdinal(l, r);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    public override string ToString()
        => $"{Major}.{Minor}.{Patch}{(PreRelease is null ? string.Empty : $"-{PreRelease}")}";

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
}

public enum VersionConstraintKind
{
    Any = 0,
    Exact,
    Minimum,
    Range
}

public sealed record VersionConstraint(
    VersionConstraintKind Kind,
    SemanticVersion? Minimum = null,
    SemanticVersion? Maximum = null,
    bool IncludeMaximum = true)
{
    public bool IsSatisfiedBy(SemanticVersion version) => Kind switch
    {
        VersionConstraintKind.Any => true,
        VersionConstraintKind.Exact => Minimum is { } exact && version.CompareTo(exact) == 0,
        VersionConstraintKind.Minimum => Minimum is { } minimum && version >= minimum,
        VersionConstraintKind.Range => Minimum is { } min && Maximum is { } max && version >= min && (IncludeMaximum ? version <= max : version < max),
        _ => false
    };

    public static bool TryParse(string? value, out VersionConstraint constraint)
    {
        constraint = new VersionConstraint(VersionConstraintKind.Any);
        if (string.IsNullOrWhiteSpace(value) || value.Trim() == "*") return true;

        var text = value.Trim();
        if (text.StartsWith(">=", StringComparison.Ordinal))
        {
            if (!SemanticVersion.TryParse(text[2..].Trim(), out var minimum)) return false;
            constraint = new VersionConstraint(VersionConstraintKind.Minimum, minimum);
            return true;
        }

        if (text.Contains("..", StringComparison.Ordinal))
        {
            var parts = text.Split("..", 2, StringSplitOptions.TrimEntries);
            if (!SemanticVersion.TryParse(parts[0], out var minimum) || !SemanticVersion.TryParse(parts[1], out var maximum)) return false;
            if (maximum < minimum) return false;
            constraint = new VersionConstraint(VersionConstraintKind.Range, minimum, maximum);
            return true;
        }

        if (!SemanticVersion.TryParse(text.TrimStart('=').Trim(), out var exact)) return false;
        constraint = new VersionConstraint(VersionConstraintKind.Exact, exact, exact);
        return true;
    }

    public static VersionConstraint Parse(string value)
        => TryParse(value, out var constraint)
            ? constraint
            : throw new FormatException($"'{value}' is not a supported version constraint.");

    public override string ToString() => Kind switch
    {
        VersionConstraintKind.Any => "*",
        VersionConstraintKind.Exact => $"={Minimum}",
        VersionConstraintKind.Minimum => $">={Minimum}",
        VersionConstraintKind.Range => $"{Minimum}..{Maximum}",
        _ => "unknown"
    };
}
