using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B650;

public sealed class DependencyLockfilePolicyTests
{
    [Fact]
    public void Evaluate_NormalizesOrdersAndFingerprintsDependencies()
    {
        var checksum = new string('A', 64);
        var input = new[]
        {
            new LockedDependency(" Zeta_Pkg ", "2.1.0", checksum),
            new LockedDependency("alpha.pkg", "1.0.0", checksum.ToLowerInvariant())
        };

        var first = DependencyLockfilePolicy.Evaluate(" PUBSPEC-LOCK ", input, stableOnly: true, requireChecksums: true);
        var second = DependencyLockfilePolicy.Evaluate("pubspec-lock", input.Reverse(), stableOnly: true, requireChecksums: true);

        Assert.Equal("pubspec-lock", first.Identity);
        Assert.Equal(new[] { "alpha.pkg", "zeta_pkg" }, first.Dependencies.Select(item => item.Name));
        Assert.All(first.Dependencies, item => Assert.Equal(item.Sha256, item.Sha256!.ToLowerInvariant()));
        Assert.Equal("lockfile-valid", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Fact]
    public void Evaluate_RejectsDuplicateNamesCaseInsensitively()
        => Assert.Throws<ArgumentException>(() => DependencyLockfilePolicy.Evaluate("lock", new[]
        {
            new LockedDependency("flutter", "1.0.0"),
            new LockedDependency("FLUTTER", "1.0.1")
        }));

    [Fact]
    public void Evaluate_RejectsPrereleaseWhenStableOnly()
        => Assert.Throws<ArgumentException>(() => DependencyLockfilePolicy.Evaluate(
            "lock", new[] { new LockedDependency("flutter", "3.0.0-beta.1") }, stableOnly: true));

    [Fact]
    public void Evaluate_RequiresChecksumWhenConfigured()
        => Assert.Throws<ArgumentException>(() => DependencyLockfilePolicy.Evaluate(
            "lock", new[] { new LockedDependency("flutter", "3.0.0") }, requireChecksums: true));

    [Theory]
    [InlineData("")]
    [InlineData("bad name")]
    [InlineData("../lock")]
    public void NormalizeIdentity_RejectsUnsafeValues(string value)
        => Assert.ThrowsAny<ArgumentException>(() => DependencyLockfilePolicy.NormalizeIdentity(value));
}
