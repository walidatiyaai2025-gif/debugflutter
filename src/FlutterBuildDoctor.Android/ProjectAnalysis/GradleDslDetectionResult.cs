namespace FlutterBuildDoctor.Android.ProjectAnalysis;

public enum GradleDslDetectionStatus
{
    Succeeded = 0,
    MixedDsl,
    InvalidRequest,
    ProjectRootNotFound,
    AndroidDirectoryNotFound,
    BuildScriptsNotFound,
    ConflictingScripts,
    InspectionFailed
}

public enum GradleDslKind
{
    Unknown = 0,
    Groovy,
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
    string? ProjectRoot,
    string? AndroidRoot,
    GradleDslKind EffectiveDsl,
    IReadOnlyList<GradleScriptEvidence> Scripts,
    string Message)
{
    public bool IsSuccess
        => Status is GradleDslDetectionStatus.Succeeded or GradleDslDetectionStatus.MixedDsl;

    public GradleScriptEvidence? SettingsScript
        => Scripts.FirstOrDefault(script => script.Role == GradleScriptRole.Settings);

    public GradleScriptEvidence? ProjectBuildScript
        => Scripts.FirstOrDefault(script => script.Role == GradleScriptRole.ProjectBuild);

    public GradleScriptEvidence? AppBuildScript
        => Scripts.FirstOrDefault(script => script.Role == GradleScriptRole.AppBuild);
}

public interface IGradleDslDetector
{
    GradleDslDetectionResult Detect(string projectRoot);
}
