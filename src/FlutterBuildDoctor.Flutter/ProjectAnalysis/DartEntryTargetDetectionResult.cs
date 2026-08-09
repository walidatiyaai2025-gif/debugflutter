namespace FlutterBuildDoctor.Flutter.ProjectAnalysis;

public enum DartEntryTargetDetectionStatus
{
    Succeeded = 0,
    NoTargets,
    Partial,
    ProjectRootUnavailable,
    LibDirectoryUnavailable,
    UnsafePath,
    ScanLimitExceeded,
    InspectionFailed
}

public enum DartEntryTargetKind
{
    CanonicalMain = 0,
    ConventionalFlavorMain,
    NestedMain
}

public enum DartEntryTargetInspectionStatus
{
    Runnable = 0,
    MainDeclarationMissing,
    FileTooLarge,
    ReadFailed,
    UnsafePath
}

public enum DartEntryScanIssueKind
{
    ReparsePointSkipped = 0,
    EnumerationFailed,
    CandidateLimitReached,
    DirectoryLimitReached,
    DepthLimitReached
}

public sealed record DartEntryTarget(
    string AbsolutePath,
    string RelativeTargetPath,
    DartEntryTargetKind Kind,
    string? FlavorHint,
    DartEntryTargetInspectionStatus InspectionStatus,
    long? FileSizeBytes,
    string Message)
{
    public bool IsRunnable => InspectionStatus == DartEntryTargetInspectionStatus.Runnable;
}

public sealed record DartEntryScanIssue(
    DartEntryScanIssueKind Kind,
    string RelativePath,
    string Message);

public sealed record DartEntryTargetDetectionResult(
    DartEntryTargetDetectionStatus Status,
    FlutterProjectRootResult ProjectRoot,
    string? LibDirectory,
    IReadOnlyList<DartEntryTarget> Targets,
    IReadOnlyList<DartEntryScanIssue> Issues,
    int VisitedDirectories,
    int CandidateCount,
    string Message)
{
    public bool IsSuccess => Status is
        DartEntryTargetDetectionStatus.Succeeded or
        DartEntryTargetDetectionStatus.NoTargets or
        DartEntryTargetDetectionStatus.Partial;

    public IReadOnlyList<DartEntryTarget> RunnableTargets =>
        Targets.Where(target => target.IsRunnable).ToArray();
}

public interface IDartEntryTargetDetector
{
    DartEntryTargetDetectionResult Detect(FlutterProjectRootResult projectRoot);
}
