namespace FlutterBuildDoctor.Android.Devices;

public sealed class AvdListParser : IAvdListParser
{
    public IReadOnlyList<AndroidVirtualDevice> Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<AndroidVirtualDevice>();

        return output
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith("INFO", StringComparison.OrdinalIgnoreCase) &&
                           !line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase) &&
                           !line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => new AndroidVirtualDevice(name))
            .ToArray();
    }
}
