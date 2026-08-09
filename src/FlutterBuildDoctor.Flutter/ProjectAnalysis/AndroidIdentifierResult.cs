namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum AndroidIdentifierStatus
{
    Succeeded = 0,
    Partial,
    GradleDslUnavailable,
    AppBuildScriptUnavailable,
    UnsafePath,
    FileTooLarge,
    ReadFailed,
    IdentifiersNotFound,
    Ambiguous
}

public enum AndroidIdentifierField
{
    Namespace = 0,
    ApplicationId
}

public sealed record AndroidIdentifierValue(
    AndroidIdentifierField Field,
    string Value,
    string ScriptPath);

public sealed record AndroidIdentifierEvidence(
    AndroidIdentifierField Field,
    string Value,
    string ScriptPath);

public sealed record AndroidIdentifierResult(
    AndroidIdentifierStatus Status,
    GradleDslDetectionResult GradleDsl,
    AndroidIdentifierValue? Namespace,
    AndroidIdentifierValue? ApplicationId,
    IReadOnlyList<AndroidIdentifierEvidence> Evidence,
    IReadOnlyList<AndroidIdentifierField> UnresolvedFields,
    string Message)
{
    public bool IsSuccess => Status is AndroidIdentifierStatus.Succeeded or AndroidIdentifierStatus.Partial;
}

public interface IAndroidIdentifierParser
{
    AndroidIdentifierResult Parse(GradleDslDetectionResult gradleDsl);
}
