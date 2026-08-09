namespace FlutterBuildDoctor.Infrastructure.Diagnostics;

public static class ProcessVersionParser
{
    public static string Normalize(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return string.Empty;

        return output.Trim();
    }

    public static string FindVersionLine(string? output)
    {
        var text = Normalize(output);
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(x => x.Contains("version", StringComparison.OrdinalIgnoreCase))
            ?? text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? string.Empty;
    }
}
