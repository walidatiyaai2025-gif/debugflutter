namespace FlutterBuildDoctor.Infrastructure.Tools;

public sealed class ExecutableProbe
{
    public string? FindOnPath(string executable)
    {
        var paths = System.Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            ?? Array.Empty<string>();

        foreach (var path in paths)
        {
            var candidate = Path.Combine(path, executable);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
