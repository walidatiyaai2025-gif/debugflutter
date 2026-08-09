namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum GradleDslDetectionStatus
{
    Succeeded = 0,
    ProjectRootUnavailable,
    AndroidDirectoryMissing,
    BuildScriptsMissing,
    Ambiguous,
    InspectionFailed
}

public enum GradleDslKind
{
    Groovy = 0,
    Kotlin,
    Mixed
}

public enum GradleScriptRole
{
    Settings = 0,
    ProjectBuild,
    AppBuild
}

public sealed record GradleScriptEvidence(
    GradleScriptRole Role,
    GradleDslKind Dsl,
    string Path);

public sealed record GradleDslDetectionResult(
    GradleDslDetectionStatus Status,
    FlutterProjectRootResult ProjectRoot,
    string? AndroidDirectory,
    GradleDslKind? EffectiveDsl,
    IReadOnlyList<GradleScriptEvidence> Scripts,
    string Message)
{
    public bool IsSuccess => Status == GradleDslDetectionStatus.Succeeded;

    public GradleScriptEvidence? ProjectBuildScript
        => Scripts.FirstOrDefault(script => script.Role == GradleScriptRole.ProjectBuild);

    public GradleScriptEvidence? AppBuildScript
        => Scripts.FirstOrDefault(script => script.Role == GradleScriptRole.AppBuild);

    public GradleScriptEvidence? SettingsScript
        => Scripts.FirstOrDefault(script => script.Role == GradleScriptRole.Settings);
}

public interface IGradleDslDetector
{
    GradleDslDetectionResult Detect(FlutterProjectRootResult projectRoot);
}
