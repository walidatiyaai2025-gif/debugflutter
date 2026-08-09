namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum AndroidSdkRequirementsStatus
{
    Succeeded = 0,
    Partial,
    GradleDslUnavailable,
    AppBuildScriptUnavailable,
    UnsafePath,
    FileTooLarge,
    ReadFailed,
    RequirementsNotFound,
    Ambiguous
}

public enum AndroidSdkLevelField
{
    CompileSdk = 0,
    MinSdk,
    TargetSdk
}

public enum AndroidSdkLevelValueKind
{
    StaticApiLevel = 0,
    FlutterReference
}

public sealed record AndroidSdkLevelValue(
    AndroidSdkLevelField Field,
    AndroidSdkLevelValueKind Kind,
    int? ApiLevel,
    string? FlutterReference,
    string ScriptPath);

public sealed record AndroidSdkLevelEvidence(
    AndroidSdkLevelField Field,
    AndroidSdkLevelValueKind Kind,
    int? ApiLevel,
    string? FlutterReference,
    string ScriptPath);

public sealed record AndroidSdkRequirementsResult(
    AndroidSdkRequirementsStatus Status,
    GradleDslDetectionResult GradleDsl,
    AndroidSdkLevelValue? CompileSdk,
    AndroidSdkLevelValue? MinSdk,
    AndroidSdkLevelValue? TargetSdk,
    IReadOnlyList<AndroidSdkLevelEvidence> Evidence,
    IReadOnlyList<AndroidSdkLevelField> UnresolvedFields,
    string Message)
{
    public bool IsSuccess => Status is AndroidSdkRequirementsStatus.Succeeded or AndroidSdkRequirementsStatus.Partial;
}

public interface IAndroidSdkRequirementsParser
{
    AndroidSdkRequirementsResult Parse(GradleDslDetectionResult gradleDsl);
}
