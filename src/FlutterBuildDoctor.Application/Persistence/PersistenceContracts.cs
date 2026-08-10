namespace FlutterBuildDoctor.Application.Persistence;

public sealed record RepositoryHistoryRecord(
    Guid RepositoryId,
    string LocalPath,
    string? RemoteUrl,
    DateTimeOffset LastOpenedAt);

public sealed record CommandHistoryRecord(
    Guid Id,
    Guid? RepositoryId,
    string CommandName,
    string SanitizedCommand,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    int? ExitCode);

public sealed record DiagnosticHistoryRecord(
    Guid Id,
    Guid? RepositoryId,
    DateTimeOffset CapturedAt,
    int ReadinessScore,
    int BlockerCount,
    int WarningCount,
    string Summary);

public sealed record RepairHistoryRecord(
    Guid Id,
    Guid? RepositoryId,
    string RecipeId,
    string SignatureKey,
    string Risk,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    bool Verified);

public sealed record BuildHistoryRecord(
    Guid Id,
    Guid? RepositoryId,
    string ArtifactType,
    string Mode,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string? ArtifactPath,
    long? SizeBytes,
    string? Sha256);

public sealed record ReleaseHistoryRecord(
    Guid Id,
    Guid? RepositoryId,
    string ArtifactType,
    string Status,
    DateTimeOffset CreatedAt,
    string? ArtifactPath,
    long? SizeBytes,
    string? Sha256);

public sealed record PreferredProfileRecord(
    Guid RepositoryId,
    string? PreferredJdk,
    string? PreferredDevice,
    string? PreferredBuildProfile,
    DateTimeOffset UpdatedAt);

public sealed record PersistenceRetentionPolicy(
    int MaxRowsPerHistory = 1000,
    int MaxAgeDays = 90)
{
    public void Validate()
    {
        if (MaxRowsPerHistory is < 1 or > 100000)
            throw new ArgumentOutOfRangeException(nameof(MaxRowsPerHistory));
        if (MaxAgeDays is < 1 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(MaxAgeDays));
    }
}

public interface IApplicationPersistenceStore
{
    string DatabasePath { get; }
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken = default);

    Task UpsertRepositoryAsync(RepositoryHistoryRecord record, CancellationToken cancellationToken = default);
    Task AddCommandAsync(CommandHistoryRecord record, CancellationToken cancellationToken = default);
    Task AddDiagnosticAsync(DiagnosticHistoryRecord record, CancellationToken cancellationToken = default);
    Task AddRepairAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default);
    Task AddBuildAsync(BuildHistoryRecord record, CancellationToken cancellationToken = default);
    Task AddReleaseAsync(ReleaseHistoryRecord record, CancellationToken cancellationToken = default);

    Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default);
    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task SetPreferredProfileAsync(PreferredProfileRecord record, CancellationToken cancellationToken = default);
    Task<PreferredProfileRecord?> GetPreferredProfileAsync(Guid repositoryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommandHistoryRecord>> GetRecentCommandsAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BuildHistoryRecord>> GetRecentBuildsAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReleaseHistoryRecord>> GetRecentReleasesAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task PruneAsync(PersistenceRetentionPolicy policy, CancellationToken cancellationToken = default);
}
