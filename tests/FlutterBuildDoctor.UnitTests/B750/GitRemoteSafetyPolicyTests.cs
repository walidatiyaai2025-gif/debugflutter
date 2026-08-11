using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B750;

public sealed class GitRemoteSafetyPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesApprovedHttpsRemoteAndFingerprintsDeterministically()
    {
        var request = new GitRemoteRequest(
            " Origin ",
            new Uri("https://GitHub.com/Owner/Repo.git"),
            new[] { "GITHUB.COM" });

        var first = GitRemoteSafetyPolicy.Evaluate(request);
        var second = GitRemoteSafetyPolicy.Evaluate(request);

        Assert.True(first.Allowed);
        Assert.Equal("origin", first.Identity);
        Assert.Equal("github.com", first.Host);
        Assert.Equal("Owner/Repo.git", first.RepositoryPath);
        Assert.Equal("https://github.com/Owner/Repo.git", first.SafeDisplayUri);
        Assert.Equal("remote-approved", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Theory]
    [InlineData("http://github.com/owner/repo.git")]
    [InlineData("ftp://github.com/owner/repo.git")]
    [InlineData("https://user:secret@github.com/owner/repo.git")]
    public void Evaluate_RejectsUnsafeRemoteUris(string uri)
        => Assert.Throws<ArgumentException>(() => GitRemoteSafetyPolicy.Evaluate(
            new GitRemoteRequest("origin", new Uri(uri))));

    [Fact]
    public void Evaluate_RejectsHostOutsideAllowListWithoutLeakingUriCredentials()
    {
        var result = GitRemoteSafetyPolicy.Evaluate(new GitRemoteRequest(
            "origin",
            new Uri("https://mirror.example.com/team/repo.git"),
            new[] { "github.com" }));

        Assert.False(result.Allowed);
        Assert.Equal("remote-host-not-approved", result.ReasonCode);
        Assert.Equal(string.Empty, result.SafeDisplayUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad identity")]
    [InlineData("../origin")]
    public void NormalizeIdentity_RejectsUnsafeValues(string value)
        => Assert.ThrowsAny<ArgumentException>(() => GitRemoteSafetyPolicy.NormalizeIdentity(value));
}
