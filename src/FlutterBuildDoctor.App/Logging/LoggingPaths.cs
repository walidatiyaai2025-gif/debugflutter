namespace FlutterBuildDoctor.App.Logging;

public static class LoggingPaths
{
    public static string EnsureLogFilePath()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlutterBuildDoctor",
            "logs");

        Directory.CreateDirectory(logDirectory);
        return Path.Combine(logDirectory, "flutter-build-doctor-.log");
    }
}
