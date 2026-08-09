namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum ReleaseVersionStatus
{
    Succeeded = 0,
    Partial,
    PubspecUnavailable,
    GradleDslUnavailable,
    AppBuildScriptUnavailable,
    UnsafePath,
    FileTooLarge,
    ReadFailed,
    VersionNotFound,
    Ambiguous
}

public enum ReleaseVersionField
{
    VersionName = 0,
    VersionCode
}

public enum ReleaseVersionSourceKind
{
    StaticGradle = 0,
    FlutterPubspecReference
}

public sealed record ReleaseVersionValue(
    ReleaseVersionField Field,
    ReleaseVersionSourceKind SourceKind,
    string Value,
    int? NumericValue,
    string ScriptPath,
    string? PubspecPath);

public sealed record ReleaseVersionEvidence(
    ReleaseVersionField Field,
    ReleaseVersionSourceKind SourceKind,
    string Value,
    int? NumericValue,
    string ScriptPath,
    string? PubspecPath);

public sealed record ReleaseVersionResult(
    ReleaseVersionStatus Status,
    GradleDslDetectionResult GradleDsl,
    string? PubspecPath,
    string? PubspecVersion,
    ReleaseVersionValue? VersionName,
    ReleaseVersionValue? VersionCode,
    IReadOnlyList<ReleaseVersionEvidence> Evidence,
    IReadOnlyList<ReleaseVersionField> UnresolvedFields,
    string Message)
{
    public bool IsSuccess => Status is ReleaseVersionStatus.Succeeded or ReleaseVersionStatus.Partial;
}

public interface IReleaseVersionParser
{
    ReleaseVersionResult Parse(
        PubspecParseResult pubspec,
        GradleDslDetectionResult gradleDsl);
}
