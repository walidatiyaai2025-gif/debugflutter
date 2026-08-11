using FlutterBuildDoctor.Application.Artifacts;

namespace FlutterBuildDoctor.UnitTests.B350;

public sealed class ArtifactPublicationPolicyTests
{
    [Fact]
    public void Evaluate_AllowsVerifiedReleaseArtifactAndBuildsStablePublicationIdentity()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-publish");
        var artifact = Path.Combine(root, "build", "app-release.aab");
        var request = new ArtifactPublicationRequest(
            root,
            artifact,
            PublicationArtifactKind.Aab,
            PublicationBuildMode.Release,
            " GitHub-Actions ",
            IsVerified: true,
            RetentionDays: 999);

        var first = ArtifactPublicationPolicy.Evaluate(request);
        var second = ArtifactPublicationPolicy.Evaluate(request);

        Assert.True(first.Allowed);
        Assert.Equal("github", first.Channel);
        Assert.Equal("app-release-release.aab", first.PublicationName);
        Assert.Equal(ArtifactPublicationPolicy.MaxRetentionDays, first.RetentionDays);
        Assert.Equal("publish-ready", first.ReasonCode);
        Assert.Equal(first.PublicationKey, second.PublicationKey);
        Assert.Equal(64, first.PublicationKey.Length);
    }

    [Fact]
    public void Evaluate_RequiresVerifiedArtifactEvidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-publish");
        var decision = ArtifactPublicationPolicy.Evaluate(new ArtifactPublicationRequest(
            root,
            Path.Combine(root, "app.apk"),
            PublicationArtifactKind.Apk,
            PublicationBuildMode.Release,
            "internal",
            IsVerified: false));

        Assert.False(decision.Allowed);
        Assert.Equal("artifact-unverified", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_RequiresReleaseModeForAab()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-publish");
        var decision = ArtifactPublicationPolicy.Evaluate(new ArtifactPublicationRequest(
            root,
            Path.Combine(root, "app.aab"),
            PublicationArtifactKind.Aab,
            PublicationBuildMode.Profile,
            "local",
            IsVerified: true));

        Assert.False(decision.Allowed);
        Assert.Equal("aab-requires-release", decision.ReasonCode);
    }

    [Fact]
    public void Evaluate_AllowsDebugApkOnlyForLocalPublication()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-publish");
        var artifact = Path.Combine(root, "app.apk");
        var local = ArtifactPublicationPolicy.Evaluate(new ArtifactPublicationRequest(
            root, artifact, PublicationArtifactKind.Apk, PublicationBuildMode.Debug, "local", true));
        var remote = ArtifactPublicationPolicy.Evaluate(new ArtifactPublicationRequest(
            root, artifact, PublicationArtifactKind.Apk, PublicationBuildMode.Debug, "github", true));

        Assert.True(local.Allowed);
        Assert.False(remote.Allowed);
        Assert.Equal("debug-apk-local-only", remote.ReasonCode);
    }

    [Fact]
    public void NormalizeArtifactPath_RejectsWorkspaceEscape()
    {
        var root = Path.Combine(Path.GetTempPath(), "fbd-root", "workspace");
        var outside = Path.Combine(root, "..", "outside.apk");

        Assert.Throws<InvalidOperationException>(() => ArtifactPublicationPolicy.NormalizeArtifactPath(root, outside));
    }

    [Theory]
    [InlineData("local", "local")]
    [InlineData("GitHub-Actions", "github")]
    [InlineData(" INTERNAL ", "internal")]
    public void NormalizeChannel_UsesStableChannelNames(string input, string expected)
    {
        Assert.Equal(expected, ArtifactPublicationPolicy.NormalizeChannel(input));
    }
}
