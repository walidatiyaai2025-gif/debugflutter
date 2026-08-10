using FlutterBuildDoctor.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace FlutterBuildDoctor.Infrastructure.Persistence;

public sealed class SqliteApplicationPersistenceStore : IApplicationPersistenceStore
{
    public const int CurrentSchemaVersion = 1;
    private readonly string _connectionString;

    public SqliteApplicationPersistenceStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA foreign_keys = ON;", cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, "PRAGMA journal_mode = WAL;", cancellationToken).ConfigureAwait(false);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS schema_info (
                singleton_id INTEGER NOT NULL PRIMARY KEY CHECK (singleton_id = 1),
                version INTEGER NOT NULL,
                applied_at TEXT NOT NULL
            );
            """, cancellationToken, transaction).ConfigureAwait(false);

        var version = await ReadSchemaVersionAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        if (version == 0)
        {
            await ApplyVersion1Async(connection, transaction, cancellationToken).ConfigureAwait(false);
            await using var versionCommand = connection.CreateCommand();
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = "INSERT INTO schema_info(singleton_id, version, applied_at) VALUES(1, $version, $appliedAt);";
            versionCommand.Parameters.AddWithValue("$version", CurrentSchemaVersion);
            versionCommand.Parameters.AddWithValue("$appliedAt", ToDb(DateTimeOffset.UtcNow));
            await versionCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (version > CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Database schema version {version} is newer than supported version {CurrentSchemaVersion}.");
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadSchemaVersionAsync(connection, transaction: null, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
        {
            return 0;
        }
    }

    public async Task UpsertRepositoryAsync(RepositoryHistoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await WithInitializedConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO repositories(id, local_path, remote_url, last_opened_at)
                VALUES($id, $path, $remote, $opened)
                ON CONFLICT(id) DO UPDATE SET
                    local_path=excluded.local_path,
                    remote_url=excluded.remote_url,
                    last_opened_at=excluded.last_opened_at;
                """;
            Add(command, "$id", record.RepositoryId.ToString("D"));
            Add(command, "$path", RequireText(record.LocalPath, nameof(record.LocalPath)));
            AddNullable(command, "$remote", record.RemoteUrl);
            Add(command, "$opened", ToDb(record.LastOpenedAt));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task AddCommandAsync(CommandHistoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return InsertHistoryAsync(
            """
            INSERT INTO command_history(id, repository_id, command_name, sanitized_command, status, started_at, finished_at, duration_ms, exit_code)
            VALUES($id, $repo, $name, $command, $status, $started, $finished, $duration, $exitCode);
            """,
            command =>
            {
                Add(command, "$id", record.Id.ToString("D"));
                AddGuid(command, "$repo", record.RepositoryId);
                Add(command, "$name", RequireText(record.CommandName, nameof(record.CommandName)));
                Add(command, "$command", RequireText(record.SanitizedCommand, nameof(record.SanitizedCommand)));
                Add(command, "$status", RequireText(record.Status, nameof(record.Status)));
                Add(command, "$started", ToDb(record.StartedAt));
                Add(command, "$finished", ToDb(record.FinishedAt));
                Add(command, "$duration", Math.Max(0L, (long)(record.FinishedAt - record.StartedAt).TotalMilliseconds));
                AddNullable(command, "$exitCode", record.ExitCode);
            },
            cancellationToken);
    }

    public Task AddDiagnosticAsync(DiagnosticHistoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return InsertHistoryAsync(
            """
            INSERT INTO diagnostics_history(id, repository_id, captured_at, readiness_score, blocker_count, warning_count, summary)
            VALUES($id, $repo, $captured, $readiness, $blockers, $warnings, $summary);
            """,
            command =>
            {
                Add(command, "$id", record.Id.ToString("D"));
                AddGuid(command, "$repo", record.RepositoryId);
                Add(command, "$captured", ToDb(record.CapturedAt));
                Add(command, "$readiness", Math.Clamp(record.ReadinessScore, 0, 100));
                Add(command, "$blockers", Math.Max(0, record.BlockerCount));
                Add(command, "$warnings", Math.Max(0, record.WarningCount));
                Add(command, "$summary", RequireMetadata(record.Summary, nameof(record.Summary)));
            },
            cancellationToken);
    }

    public Task AddRepairAsync(RepairHistoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return InsertHistoryAsync(
            """
            INSERT INTO repair_history(id, repository_id, recipe_id, signature_key, risk, status, started_at, finished_at, verified)
            VALUES($id, $repo, $recipe, $signature, $risk, $status, $started, $finished, $verified);
            """,
            command =>
            {
                Add(command, "$id", record.Id.ToString("D"));
                AddGuid(command, "$repo", record.RepositoryId);
                Add(command, "$recipe", RequireText(record.RecipeId, nameof(record.RecipeId)));
                Add(command, "$signature", RequireText(record.SignatureKey, nameof(record.SignatureKey)));
                Add(command, "$risk", RequireText(record.Risk, nameof(record.Risk)));
                Add(command, "$status", RequireText(record.Status, nameof(record.Status)));
                Add(command, "$started", ToDb(record.StartedAt));
                Add(command, "$finished", ToDb(record.FinishedAt));
                Add(command, "$verified", record.Verified ? 1 : 0);
            },
            cancellationToken);
    }

    public Task AddBuildAsync(BuildHistoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return InsertHistoryAsync(
            """
            INSERT INTO build_history(id, repository_id, artifact_type, mode, status, started_at, finished_at, duration_ms, artifact_path, size_bytes, sha256)
            VALUES($id, $repo, $type, $mode, $status, $started, $finished, $duration, $path, $size, $sha);
            """,
            command =>
            {
                Add(command, "$id", record.Id.ToString("D"));
                AddGuid(command, "$repo", record.RepositoryId);
                Add(command, "$type", RequireText(record.ArtifactType, nameof(record.ArtifactType)));
                Add(command, "$mode", RequireText(record.Mode, nameof(record.Mode)));
                Add(command, "$status", RequireText(record.Status, nameof(record.Status)));
                Add(command, "$started", ToDb(record.StartedAt));
                Add(command, "$finished", ToDb(record.FinishedAt));
                Add(command, "$duration", Math.Max(0L, (long)(record.FinishedAt - record.StartedAt).TotalMilliseconds));
                AddNullable(command, "$path", record.ArtifactPath);
                AddNullable(command, "$size", record.SizeBytes);
                AddNullable(command, "$sha", record.Sha256);
            },
            cancellationToken);
    }

    public Task AddReleaseAsync(ReleaseHistoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        return InsertHistoryAsync(
            """
            INSERT INTO release_history(id, repository_id, artifact_type, status, created_at, artifact_path, size_bytes, sha256)
            VALUES($id, $repo, $type, $status, $created, $path, $size, $sha);
            """,
            command =>
            {
                Add(command, "$id", record.Id.ToString("D"));
                AddGuid(command, "$repo", record.RepositoryId);
                Add(command, "$type", RequireText(record.ArtifactType, nameof(record.ArtifactType)));
                Add(command, "$status", RequireText(record.Status, nameof(record.Status)));
                Add(command, "$created", ToDb(record.CreatedAt));
                AddNullable(command, "$path", record.ArtifactPath);
                AddNullable(command, "$size", record.SizeBytes);
                AddNullable(command, "$sha", record.Sha256);
            },
            cancellationToken);
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        key = RequireKey(key);
        ArgumentNullException.ThrowIfNull(value);
        await WithInitializedConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO app_settings(setting_key, setting_value, updated_at)
                VALUES($key, $value, $updated)
                ON CONFLICT(setting_key) DO UPDATE SET
                    setting_value=excluded.setting_value,
                    updated_at=excluded.updated_at;
                """;
            Add(command, "$key", key);
            Add(command, "$value", value);
            Add(command, "$updated", ToDb(DateTimeOffset.UtcNow));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        key = RequireKey(key);
        return await WithInitializedConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT setting_value FROM app_settings WHERE setting_key=$key;";
            Add(command, "$key", key);
            var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is null or DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetPreferredProfileAsync(PreferredProfileRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await WithInitializedConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO preferred_profiles(repository_id, preferred_jdk, preferred_device, preferred_build_profile, updated_at)
                VALUES($repo, $jdk, $device, $profile, $updated)
                ON CONFLICT(repository_id) DO UPDATE SET
                    preferred_jdk=excluded.preferred_jdk,
                    preferred_device=excluded.preferred_device,
                    preferred_build_profile=excluded.preferred_build_profile,
                    updated_at=excluded.updated_at;
                """;
            Add(command, "$repo", record.RepositoryId.ToString("D"));
            AddNullable(command, "$jdk", record.PreferredJdk);
            AddNullable(command, "$device", record.PreferredDevice);
            AddNullable(command, "$profile", record.PreferredBuildProfile);
            Add(command, "$updated", ToDb(record.UpdatedAt));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PreferredProfileRecord?> GetPreferredProfileAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        return await WithInitializedConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT preferred_jdk, preferred_device, preferred_build_profile, updated_at
                FROM preferred_profiles WHERE repository_id=$repo;
                """;
            Add(command, "$repo", repositoryId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
            return new PreferredProfileRecord(
                repositoryId,
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                ParseDb(reader.GetString(3)));
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<CommandHistoryRecord>> GetRecentCommandsAsync(int limit = 50, CancellationToken cancellationToken = default)
        => QueryRecentAsync(
            """
            SELECT id, repository_id, command_name, sanitized_command, status, started_at, finished_at, exit_code
            FROM command_history ORDER BY started_at DESC LIMIT $limit;
            """,
            limit,
            reader => new CommandHistoryRecord(
                Guid.Parse(reader.GetString(0)),
                ReadNullableGuid(reader, 1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ParseDb(reader.GetString(5)),
                ParseDb(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetInt32(7)),
            cancellationToken);

    public Task<IReadOnlyList<BuildHistoryRecord>> GetRecentBuildsAsync(int limit = 50, CancellationToken cancellationToken = default)
        => QueryRecentAsync(
            """
            SELECT id, repository_id, artifact_type, mode, status, started_at, finished_at, artifact_path, size_bytes, sha256
            FROM build_history ORDER BY started_at DESC LIMIT $limit;
            """,
            limit,
            reader => new BuildHistoryRecord(
                Guid.Parse(reader.GetString(0)),
                ReadNullableGuid(reader, 1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ParseDb(reader.GetString(5)),
                ParseDb(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt64(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)),
            cancellationToken);

    public Task<IReadOnlyList<ReleaseHistoryRecord>> GetRecentReleasesAsync(int limit = 50, CancellationToken cancellationToken = default)
        => QueryRecentAsync(
            """
            SELECT id, repository_id, artifact_type, status, created_at, artifact_path, size_bytes, sha256
            FROM release_history ORDER BY created_at DESC LIMIT $limit;
            """,
            limit,
            reader => new ReleaseHistoryRecord(
                Guid.Parse(reader.GetString(0)),
                ReadNullableGuid(reader, 1),
                reader.GetString(2),
                reader.GetString(3),
                ParseDb(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)),
            cancellationToken);

    public async Task PruneAsync(PersistenceRetentionPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        await WithInitializedConnectionAsync(async connection =>
        {
            var cutoff = ToDb(DateTimeOffset.UtcNow.AddDays(-policy.MaxAgeDays));
            foreach (var table in RetainedTables)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"""
                    DELETE FROM {table.Table} WHERE {table.TimestampColumn} < $cutoff;
                    DELETE FROM {table.Table}
                    WHERE id NOT IN (
                        SELECT id FROM {table.Table}
                        ORDER BY {table.TimestampColumn} DESC
                        LIMIT $limit
                    );
                    """;
                Add(command, "$cutoff", cutoff);
                Add(command, "$limit", policy.MaxRowsPerHistory);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    private static readonly (string Table, string TimestampColumn)[] RetainedTables =
    {
        ("command_history", "started_at"),
        ("diagnostics_history", "captured_at"),
        ("repair_history", "started_at"),
        ("build_history", "started_at"),
        ("release_history", "created_at")
    };

    private async Task InsertHistoryAsync(string sql, Action<SqliteCommand> bind, CancellationToken cancellationToken)
    {
        await WithInitializedConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            bind(command);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<T>> QueryRecentAsync<T>(
        string sql,
        int limit,
        Func<SqliteDataReader, T> projector,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        return await WithInitializedConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            Add(command, "$limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<T>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) results.Add(projector(reader));
            return (IReadOnlyList<T>)results;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task WithInitializedConnectionAsync(Func<SqliteConnection, Task> action, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await action(connection).ConfigureAwait(false);
    }

    private async Task<T> WithInitializedConnectionAsync<T>(Func<SqliteConnection, Task<T>> action, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await action(connection).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task ApplyVersion1Async(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE repositories (
                id TEXT PRIMARY KEY,
                local_path TEXT NOT NULL,
                remote_url TEXT NULL,
                last_opened_at TEXT NOT NULL
            );
            CREATE UNIQUE INDEX ix_repositories_path ON repositories(local_path);

            CREATE TABLE command_history (
                id TEXT PRIMARY KEY,
                repository_id TEXT NULL,
                command_name TEXT NOT NULL,
                sanitized_command TEXT NOT NULL,
                status TEXT NOT NULL,
                started_at TEXT NOT NULL,
                finished_at TEXT NOT NULL,
                duration_ms INTEGER NOT NULL,
                exit_code INTEGER NULL,
                FOREIGN KEY(repository_id) REFERENCES repositories(id) ON DELETE SET NULL
            );
            CREATE INDEX ix_command_history_repo_time ON command_history(repository_id, started_at DESC);

            CREATE TABLE diagnostics_history (
                id TEXT PRIMARY KEY,
                repository_id TEXT NULL,
                captured_at TEXT NOT NULL,
                readiness_score INTEGER NOT NULL,
                blocker_count INTEGER NOT NULL,
                warning_count INTEGER NOT NULL,
                summary TEXT NOT NULL,
                FOREIGN KEY(repository_id) REFERENCES repositories(id) ON DELETE SET NULL
            );
            CREATE INDEX ix_diagnostics_history_repo_time ON diagnostics_history(repository_id, captured_at DESC);

            CREATE TABLE repair_history (
                id TEXT PRIMARY KEY,
                repository_id TEXT NULL,
                recipe_id TEXT NOT NULL,
                signature_key TEXT NOT NULL,
                risk TEXT NOT NULL,
                status TEXT NOT NULL,
                started_at TEXT NOT NULL,
                finished_at TEXT NOT NULL,
                verified INTEGER NOT NULL,
                FOREIGN KEY(repository_id) REFERENCES repositories(id) ON DELETE SET NULL
            );
            CREATE INDEX ix_repair_history_repo_time ON repair_history(repository_id, started_at DESC);

            CREATE TABLE build_history (
                id TEXT PRIMARY KEY,
                repository_id TEXT NULL,
                artifact_type TEXT NOT NULL,
                mode TEXT NOT NULL,
                status TEXT NOT NULL,
                started_at TEXT NOT NULL,
                finished_at TEXT NOT NULL,
                duration_ms INTEGER NOT NULL,
                artifact_path TEXT NULL,
                size_bytes INTEGER NULL,
                sha256 TEXT NULL,
                FOREIGN KEY(repository_id) REFERENCES repositories(id) ON DELETE SET NULL
            );
            CREATE INDEX ix_build_history_repo_time ON build_history(repository_id, started_at DESC);

            CREATE TABLE release_history (
                id TEXT PRIMARY KEY,
                repository_id TEXT NULL,
                artifact_type TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL,
                artifact_path TEXT NULL,
                size_bytes INTEGER NULL,
                sha256 TEXT NULL,
                FOREIGN KEY(repository_id) REFERENCES repositories(id) ON DELETE SET NULL
            );
            CREATE INDEX ix_release_history_repo_time ON release_history(repository_id, created_at DESC);

            CREATE TABLE app_settings (
                setting_key TEXT PRIMARY KEY,
                setting_value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE preferred_profiles (
                repository_id TEXT PRIMARY KEY,
                preferred_jdk TEXT NULL,
                preferred_device TEXT NULL,
                preferred_build_profile TEXT NULL,
                updated_at TEXT NOT NULL,
                FOREIGN KEY(repository_id) REFERENCES repositories(id) ON DELETE CASCADE
            );
            """;
        await ExecuteAsync(connection, sql, cancellationToken, transaction).ConfigureAwait(false);
    }

    private static async Task<int> ReadSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT version FROM schema_info WHERE singleton_id=1;";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is null or DBNull ? 0 : Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Add(SqliteCommand command, string name, object value) => command.Parameters.AddWithValue(name, value);
    private static void AddNullable(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    private static void AddGuid(SqliteCommand command, string name, Guid? value) => AddNullable(command, name, value?.ToString("D"));
    private static Guid? ReadNullableGuid(SqliteDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));
    private static string ToDb(DateTimeOffset value) => value.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
    private static DateTimeOffset ParseDb(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind);

    private static string RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
        if (value.Any(char.IsControl) && value.Any(character => character is not '\r' and not '\n' and not '\t'))
            throw new ArgumentException("Unsupported control characters are not allowed.", parameterName);
        return value;
    }

    private static string RequireMetadata(string? value, string parameterName)
    {
        var text = RequireText(value, parameterName);
        if (text.Length > 4096) throw new ArgumentException("Persisted summary metadata is limited to 4096 characters.", parameterName);
        return text;
    }

    private static string RequireKey(string? key)
    {
        key = RequireText(key, nameof(key)).Trim();
        if (key.Length > 128) throw new ArgumentException("Setting key is too long.", nameof(key));
        return key;
    }
}
