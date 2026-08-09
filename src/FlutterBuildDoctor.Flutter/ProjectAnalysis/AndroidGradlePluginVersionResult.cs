namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum AndroidGradlePluginVersionStatus
{
    Succeeded = 0,
    GradleDslUnavailable,
    ScriptUnavailable,
    VersionNotFound,
    Ambiguous,
    UnsafePath,
    FileTooLarge,
    ReadFailed
}

public enum AndroidGradlePluginDeclarationKind
{
    ModernPluginDsl = 0,
    LegacyBuildscriptClasspath
}

public sealed record AndroidGradlePluginVersionEvidence(
    string Version,
    AndroidGradlePluginDeclarationKind DeclarationKind,
    GradleScriptRole ScriptRole,
    string ScriptPath,
    string? PluginId);

public sealed record AndroidGradlePluginVersionResult(
    AndroidGradlePluginVersionStatus Status,
    GradleDslDetectionResult GradleDsl,
    string? Version,
    IReadOnlyList<AndroidGradlePluginVersionEvidence> Evidence,
    string Message)
{
    public bool IsSuccess => Status == AndroidGradlePluginVersionStatus.Succeeded;
}

public interface IAndroidGradlePluginVersionParser
{
    AndroidGradlePluginVersionResult Parse(GradleDslDetectionResult gradleDsl);
}
