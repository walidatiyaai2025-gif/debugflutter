namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum LocalPropertiesDetectionStatus
{
    Succeeded = 0,
    Partial,
    FileMissing,
    ProjectRootUnavailable,
    AndroidDirectoryUnavailable,
    UnsafePath,
    FileTooLarge,
    ReadFailed,
    Ambiguous
}

public enum LocalPropertiesPathKind
{
    AndroidSdk = 0,
    FlutterSdk
}

public enum LocalPropertiesPathStatus
{
    Valid = 0,
    MissingKey,
    EmptyValue,
    InvalidPath,
    DirectoryMissing,
    UnrecognizedLayout,
    Ambiguous
}

public sealed record LocalPropertiesPathEvidence(
    LocalPropertiesPathKind Kind,
    string PropertyKey,
    string DecodedValue,
    string? NormalizedPath,
    int Occurrence);

public sealed record LocalPropertiesPathResult(
    LocalPropertiesPathKind Kind,
    string PropertyKey,
    LocalPropertiesPathStatus Status,
    string? ConfiguredValue,
    string? NormalizedPath,
    bool Exists,
    bool HasExpectedLayout,
    int OccurrenceCount,
    IReadOnlyList<LocalPropertiesPathEvidence> Evidence,
    string Message)
{
    public bool IsValid => Status == LocalPropertiesPathStatus.Valid;
}

public sealed record LocalPropertiesDetectionResult(
    LocalPropertiesDetectionStatus Status,
    FlutterProjectRootResult ProjectRoot,
    string? AndroidDirectory,
    string? LocalPropertiesPath,
    LocalPropertiesPathResult AndroidSdk,
    LocalPropertiesPathResult FlutterSdk,
    string Message)
{
    public bool IsSuccess => Status is
        LocalPropertiesDetectionStatus.Succeeded or
        LocalPropertiesDetectionStatus.Partial or
        LocalPropertiesDetectionStatus.FileMissing;
}

public interface ILocalPropertiesDetector
{
    LocalPropertiesDetectionResult Detect(FlutterProjectRootResult projectRoot);
}
