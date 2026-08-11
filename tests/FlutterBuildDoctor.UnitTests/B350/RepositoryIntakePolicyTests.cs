using FlutterBuildDoctor.Application.Repositories;

namespace FlutterBuildDoctor.UnitTests.B350;

public sealed class RepositoryIntakePolicyTests
{
    [Fact]
    public void Prepare_NormalizesHttpsAndSshToSameCanonicalRepository()
    {
        var https = RepositoryIntakePolicy.Prepare(new RepositoryIntakeRequest(
            " https://github.com/Walid/DebugFlutter ",
            Branch: " feature/test ",
            CloneDepth: 25));
        var ssh = RepositoryIntakePolicy.Prepare(new RepositoryIntakeRequest(
            "git@github.com:Walid/DebugFlutter.git",
            Branch: "feature/test",
            CloneDepth: 25));

        Assert.Equal("https://github.com/Walid/DebugFlutter.git", https.NormalizedRepositoryUrl);
        Assert.Equal(https.NormalizedRepositoryUrl, ssh.NormalizedRepositoryUrl);
        Assert.Equal("feature/test", https.Branch);
        Assert.Equal("walid-debugflutter", https.WorkspaceSlug);
        Assert.Equal(https.Fingerprint, ssh.Fingerprint);
        Assert.Equal(64, https.Fingerprint.Length);
    }

    [Theory]
    [InlineData("http://github.com/a/b")]
    [InlineData("file:///c:/repo")]
    [InlineData("https://example.com/a/b")]
    [InlineData("https://github.com/a/b/extra")]
    public void NormalizeRepositoryUrl_RejectsUnsupportedLocations(string repositoryUrl)
    {
        Assert.Throws<ArgumentException>(() => RepositoryIntakePolicy.NormalizeRepositoryUrl(repositoryUrl));
    }

    [Theory]
    [InlineData("../main")]
    [InlineData("feature\\escape")]
    [InlineData("refs/heads/main@{1}")]
    [InlineData("/main")]
    [InlineData("main/")]
    public void NormalizeBranch_RejectsUnsafeRefSyntax(string branch)
    {
        Assert.Throws<ArgumentException>(() => RepositoryIntakePolicy.NormalizeBranch(branch));
    }

    [Fact]
    public void Prepare_BoundsCloneDepthAndDefaultsBranch()
    {
        var shallow = RepositoryIntakePolicy.Prepare(new RepositoryIntakeRequest("https://github.com/a/b", CloneDepth: 0));
        var deep = RepositoryIntakePolicy.Prepare(new RepositoryIntakeRequest("https://github.com/a/b", CloneDepth: int.MaxValue));

        Assert.Equal("main", shallow.Branch);
        Assert.Equal(1, shallow.CloneDepth);
        Assert.Equal(RepositoryIntakePolicy.MaxCloneDepth, deep.CloneDepth);
    }

    [Fact]
    public void Prepare_DetectsAndNormalizesDetachedCommitRef()
    {
        var decision = RepositoryIntakePolicy.Prepare(new RepositoryIntakeRequest(
            "https://github.com/a/b",
            CommitSha: " ABCDEF1 "));

        Assert.True(decision.IsDetachedRef);
        Assert.Equal("abcdef1", decision.CommitSha);
    }

    [Fact]
    public void Prepare_RejectsMalformedCommitRef()
    {
        Assert.Throws<ArgumentException>(() => RepositoryIntakePolicy.Prepare(new RepositoryIntakeRequest(
            "https://github.com/a/b",
            CommitSha: "xyz")));
    }
}
