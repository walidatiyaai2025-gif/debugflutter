using System.Globalization;

namespace FlutterBuildDoctor.Application.Compatibility;

public readonly record struct SemanticVersion(
    int Major,
    int Minor = 0,
    int Patch = 0,
    int Revision = 0,
    string? PreRelease = null) : IComparable<SemanticVersion>
{
    public static SemanticVersion Parse(string value)
        => TryParse(value, out var version)
            ? version
            : throw new FormatException($"'{value}' is not a supported semantic version.");

    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var buildIndex = text.IndexOf('+');
        if (buildIndex >= 0)
        {
            text = text[..buildIndex];
        }

        string? preRelease = null;
        var preReleaseIndex = text.IndexOf('-');
        if (preReleaseIndex >= 0)
        {
            preRelease = text[(preReleaseIndex + 1)..];
            text = text[..preReleaseIndex];
            if (preRelease.Length == 0)
            {
                return false;
            }
        }

        var components = text.Split('.');
        if (components.Length is < 1 or > 4)
        {
            return false;
        }

        Span<int> numbers = stackalloc int[4];
        for (var index = 0; index < components.Length; index++)
        {
            if (!int.TryParse(components[index], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]) ||
                numbers[index] < 0)
            {
                return false;
            }
        }

        version = new SemanticVersion(numbers[0], numbers[1], numbers[2], numbers[3], preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var numeric = Major.CompareTo(other.Major);
        if (numeric != 0) return numeric;
        numeric = Minor.CompareTo(other.Minor);
        if (numeric != 0) return numeric;
        numeric = Patch.CompareTo(other.Patch);
        if (numeric != 0) return numeric;
        numeric = Revision.CompareTo(other.Revision);
        if (numeric != 0) return numeric;

        if (PreRelease is null && other.PreRelease is null) return 0;
        if (PreRelease is null) return 1;
        if (other.PreRelease is null) return -1;
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    public override string ToString()
    {
        var numeric = Revision == 0
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}.{Revision}";
        return PreRelease is null ? numeric : $"{numeric}-{PreRelease}";
    }

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    private static int ComparePreRelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var length = Math.Max(leftParts.Length, rightParts.Length);
        for (var index = 0; index < length; index++)
        {
            if (index >= leftParts.Length) return -1;
            if (index >= rightParts.Length) return 1;

            var leftNumeric = int.TryParse(leftParts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = int.TryParse(rightParts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);

            int comparison;
            if (leftNumeric && rightNumeric)
            {
                comparison = leftNumber.CompareTo(rightNumber);
            }
            else if (leftNumeric)
            {
                comparison = -1;
            }
            else if (rightNumeric)
            {
                comparison = 1;
            }
            else
            {
                comparison = string.Compare(leftParts[index], rightParts[index], StringComparison.OrdinalIgnoreCase);
            }

            if (comparison != 0) return comparison;
        }

        return 0;
    }
}
