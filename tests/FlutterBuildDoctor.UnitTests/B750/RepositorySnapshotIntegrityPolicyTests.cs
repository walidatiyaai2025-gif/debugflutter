using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B750;

public sealed class RepositorySnapshotIntegrityPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesOrdersAndFingerprintsSnapshotDeterministically()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-b750-repo");
        var timestamp = new DateTimeOffset(2026, 8, 11, 21, 0, 0, TimeSpan.FromHours(3));
        var files = new[]
        {
            new RepositorySnapshotFile("src\\B.cs", new string('B', 64)),
            new RepositorySnapshotFile("src/A.cs", new string('A', 64))
        };

        var first = RepositorySnapshotIntegrityPolicy.Evaluate(" SNAP-001 ", root, timestamp, files);
        var second = RepositorySnapshotIntegrityPolicy.Evaluate("snap-001", root, timestamp, files.AsEnumerable().Reverse());

        Assert.Equal("snap-001", first.Identity);
        Assert.Equal(new[] { "src/A.cs", "src/B.cs" }, first.Files.Select(file => file.RelativePath));
        Assert.All(first.Files, file => Assert.Equal(file.Sha256, file.Sha256.ToLowerInvariant()));
        Assert.Equal(TimeSpan.Zero, first.SnapshotAtUtc.Offset);
        Assert.Equal("repository-snapshot-valid", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsDuplicateTrackedPathsCaseInsensitively()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-b750-repo");
        Assert.Throws<ArgumentException>(() => RepositorySnapshotIntegrityPolicy.Evaluate("snap", root, DateTimeOffset.UtcNow, new[]
        {
            new RepositorySnapshotFile("src/App.cs", new string('a', 64)),
            new RepositorySnapshotFile("SRC/app.cs", new string('b', 64))
        }));
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("/absolute/file.txt")]
    [InlineData("C:/absolute/file.txt")]
    public void NormalizeRelativePath_RejectsUnsafePaths(string value)
        => Assert.Throws<ArgumentException>(() => RepositorySnapshotIntegrityPolicy.NormalizeRelativePath(value));

    [Fact]
    public void Evaluate_RejectsMalformedTrackedFileHash()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-b750-repo");
        Assert.Throws<ArgumentException>(() => RepositorySnapshotIntegrityPolicy.Evaluate("snap", root, DateTimeOffset.UtcNow, new[]
        {
            new RepositorySnapshotFile("src/app.cs", "bad")
        }));
    }
}
