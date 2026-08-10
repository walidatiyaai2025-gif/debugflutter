using FlutterBuildDoctor.Application.Persistence;
using FlutterBuildDoctor.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace FlutterBuildDoctor.UnitTests.Persistence;

public sealed class SqliteApplicationPersistenceStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fbd-sqlite-{Guid.NewGuid():N}");
    private readonly string _databasePath;

    public SqliteApplicationPersistenceStoreTests()
    {
        Directory.CreateDirectory(_root);
        _databasePath = Path.Combine(_root, "history.db");
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotentAndCreatesCurrentSchema()
    {
        var store = Store();

        await store.InitializeAsync();
        await store.InitializeAsync();

        Assert.True(File.Exists(_databasePath));
        Assert.Equal(SqliteApplicationPersistenceStore.CurrentSchemaVersion, await store.GetSchemaVersionAsync());
    }

    [Fact]
    public async Task RepositorySettingsAndPreferredProfile_RoundTrip()
    {
        var store = Store();
        var repositoryId = Guid.NewGuid();
        await store.UpsertRepositoryAsync(new RepositoryHistoryRecord(repositoryId, @"C:\work\app", "https://example.invalid/repo.git", DateTimeOffset.UtcNow));
        await store.SetSettingAsync("theme", "dark");
        await store.SetPreferredProfileAsync(new PreferredProfileRecord(repositoryId, @"C:\Java\jdk-21", "emulator-5554", "release", DateTimeOffset.UtcNow));

        Assert.Equal("dark", await store.GetSettingAsync("theme"));
        var profile = await store.GetPreferredProfileAsync(repositoryId);
        Assert.NotNull(profile);
        Assert.Equal(@"C:\Java\jdk-21", profile!.PreferredJdk);
        Assert.Equal("emulator-5554", profile.PreferredDevice);
        Assert.Equal("release", profile.PreferredBuildProfile);
    }

    [Fact]
    public async Task CommandBuildAndReleaseHistory_RoundTripNewestFirst()
    {
        var store = Store();
        var now = DateTimeOffset.UtcNow;
        await store.AddCommandAsync(new CommandHistoryRecord(Guid.NewGuid(), null, "flutter analyze", "flutter analyze", "Succeeded", now.AddMinutes(-2), now.AddMinutes(-1), 0));
        await store.AddCommandAsync(new CommandHistoryRecord(Guid.NewGuid(), null, "flutter test", "flutter test", "Succeeded", now, now.AddSeconds(1), 0));
        await store.AddBuildAsync(new BuildHistoryRecord(Guid.NewGuid(), null, "Apk", "Release", "Succeeded", now, now.AddSeconds(3), "app-release.apk", 123, new string('a', 64)));
        await store.AddReleaseAsync(new ReleaseHistoryRecord(Guid.NewGuid(), null, "Apk", "Succeeded", now, "app-release.apk", 123, new string('a', 64)));

        var commands = await store.GetRecentCommandsAsync();
        var builds = await store.GetRecentBuildsAsync();
        var releases = await store.GetRecentReleasesAsync();

        Assert.Equal("flutter test", commands[0].CommandName);
        Assert.Single(builds);
        Assert.Equal("Release", builds[0].Mode);
        Assert.Single(releases);
        Assert.Equal(64, releases[0].Sha256!.Length);
    }

    [Fact]
    public async Task DiagnosticsAndRepairSchemas_AcceptSanitizedMetadata()
    {
        var store = Store();
        var now = DateTimeOffset.UtcNow;

        await store.AddDiagnosticAsync(new DiagnosticHistoryRecord(Guid.NewGuid(), null, now, 85, 1, 2, "1 blocker, 2 warnings"));
        await store.AddRepairAsync(new RepairHistoryRecord(Guid.NewGuid(), null, "repair.flutter-clean", new string('b', 64), "Safe", "Succeeded", now, now.AddSeconds(1), true));

        Assert.Equal(SqliteApplicationPersistenceStore.CurrentSchemaVersion, await store.GetSchemaVersionAsync());
    }

    [Fact]
    public async Task PruneAsync_RemovesOldRowsAndCapsRecentHistory()
    {
        var store = Store();
        var now = DateTimeOffset.UtcNow;
        await store.AddCommandAsync(new CommandHistoryRecord(Guid.NewGuid(), null, "old", "flutter old", "Failed", now.AddDays(-30), now.AddDays(-30).AddSeconds(1), 1));
        for (var index = 0; index < 5; index++)
        {
            await store.AddCommandAsync(new CommandHistoryRecord(Guid.NewGuid(), null, $"new-{index}", $"flutter new-{index}", "Succeeded", now.AddMinutes(index), now.AddMinutes(index).AddSeconds(1), 0));
        }

        await store.PruneAsync(new PersistenceRetentionPolicy(MaxRowsPerHistory: 3, MaxAgeDays: 7));

        var commands = await store.GetRecentCommandsAsync(20);
        Assert.Equal(3, commands.Count);
        Assert.DoesNotContain(commands, command => command.CommandName == "old");
        Assert.Equal("new-4", commands[0].CommandName);
    }

    [Fact]
    public async Task NewerSchema_IsRejectedInsteadOfSilentlyDowngraded()
    {
        var store = Store();
        await store.InitializeAsync();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false
        }.ToString();
        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE schema_info SET version=999 WHERE singleton_id=1;";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InitializeAsync());
    }

    private SqliteApplicationPersistenceStore Store() => new(_databasePath);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
