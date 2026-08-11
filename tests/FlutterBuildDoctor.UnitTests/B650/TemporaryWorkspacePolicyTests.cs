using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B650;

public sealed class TemporaryWorkspacePolicyTests
{
    [Fact]
    public void Evaluate_NormalizesBoundsExpiryAndFingerprint()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-b650-root");
        var workspace = Path.Combine(root, "run-001");
        var created = new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.FromHours(3));
        var request = new TemporaryWorkspaceRequest(" RUN-001 ", root, workspace, created, TimeSpan.FromSeconds(1));

        var active = TemporaryWorkspacePolicy.Evaluate(request, created.AddSeconds(30));
        var expired = TemporaryWorkspacePolicy.Evaluate(request, created.AddMinutes(2));

        Assert.Equal("run-001", active.Identity);
        Assert.Equal(TemporaryWorkspacePolicy.MinTtl, active.Ttl);
        Assert.False(active.Expired);
        Assert.False(active.CleanupAllowed);
        Assert.True(expired.Expired);
        Assert.True(expired.CleanupAllowed);
        Assert.Equal("workspace-expired", expired.ReasonCode);
        Assert.Equal(active.Fingerprint, TemporaryWorkspacePolicy.Evaluate(request, created.AddSeconds(40)).Fingerprint);
    }

    [Fact]
    public void Evaluate_RejectsWorkspaceOutsideApprovedRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-b650-root");
        var outside = Path.Combine(Path.GetTempPath(), "fbd-b650-other", "run");
        Assert.Throws<ArgumentException>(() => TemporaryWorkspacePolicy.Evaluate(
            new TemporaryWorkspaceRequest("run", root, outside, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)),
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Evaluate_RejectsWorkspaceEqualToRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-b650-root");
        Assert.Throws<ArgumentException>(() => TemporaryWorkspacePolicy.Evaluate(
            new TemporaryWorkspaceRequest("run", root, root, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)),
            DateTimeOffset.UtcNow));
    }

    [Fact]
    public void NormalizeRoot_RejectsRelativePaths()
        => Assert.Throws<ArgumentException>(() => TemporaryWorkspacePolicy.NormalizeRoot("relative/temp"));
}
