namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum KotlinPluginVersionStatus
{
    Succeeded = 0,
    GradleDslUnavailable,
    ScriptUnavailable,
    UnsafePath,
    FileTooLarge,
    ReadFailed,
    VersionNotFound,
    Ambiguous
}

public enum KotlinPluginDeclarationKind
{
    ModernPluginDsl = 0,
    KotlinDslShorthand,
    LegacyBuildscriptClasspath,
    LegacyVersionProperty
}

public sealed record KotlinPluginVersionEvidence(
    string Version,
    KotlinPluginDeclarationKind DeclarationKind,
    GradleScriptRole ScriptRole,
    string ScriptPath,
    string? PluginId);

public sealed record KotlinPluginVersionResult(
    KotlinPluginVersionStatus Status,
    GradleDslDetectionResult GradleDsl,
    string? Version,
    IReadOnlyList<KotlinPluginVersionEvidence> Evidence,
    string Message)
{
    public bool IsSuccess => Status == KotlinPluginVersionStatus.Succeeded;
}

public interface IKotlinPluginVersionParser
{
    KotlinPluginVersionResult Parse(GradleDslDetectionResult gradleDsl);
}
