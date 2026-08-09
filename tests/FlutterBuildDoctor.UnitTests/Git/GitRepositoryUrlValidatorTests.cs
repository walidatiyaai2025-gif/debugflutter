using FlutterBuildDoctor.Git.Validation;

namespace FlutterBuildDoctor.UnitTests.Git;

public sealed class GitRepositoryUrlValidatorTests
{
    private readonly GitRepositoryUrlValidator _validator = new();

    [Theory]
    [InlineData("https://github.com/openai/example.git", GitRepositoryTransport.Https)]
    [InlineData("http://git.example.test/team/project.git", GitRepositoryTransport.Http)]
    [InlineData("ssh://git@github.com/openai/example.git", GitRepositoryTransport.Ssh)]
    [InlineData("git://github.com/openai/example.git", GitRepositoryTransport.Git)]
    [InlineData("git@github.com:openai/example.git", GitRepositoryTransport.Ssh)]
    public void Validate_accepts_supported_remote_formats(string url, GitRepositoryTransport transport)
    {
        var result = _validator.Validate(url);

        Assert.True(result.IsValid, result.Message);
        Assert.Equal(transport, result.Transport);
        Assert.Equal(GitRepositoryUrlError.None, result.Error);
        Assert.Equal(url, result.NormalizedUrl);
    }

    [Fact]
    public void Validate_trims_outer_whitespace_and_trailing_slash()
    {
        var result = _validator.Validate("  https://github.com/openai/example.git/  ");

        Assert.True(result.IsValid, result.Message);
        Assert.Equal("https://github.com/openai/example.git", result.NormalizedUrl);
    }

    [Theory]
    [InlineData(null, GitRepositoryUrlError.Empty)]
    [InlineData("", GitRepositoryUrlError.Empty)]
    [InlineData("   ", GitRepositoryUrlError.Empty)]
    [InlineData("C:\\src\\repo", GitRepositoryUrlError.LocalPathNotAllowed)]
    [InlineData("\\\\server\\share\\repo", GitRepositoryUrlError.LocalPathNotAllowed)]
    [InlineData("../repo", GitRepositoryUrlError.LocalPathNotAllowed)]
    [InlineData("ftp://example.com/team/repo.git", GitRepositoryUrlError.UnsupportedScheme)]
    [InlineData("file:///C:/src/repo", GitRepositoryUrlError.UnsupportedScheme)]
    [InlineData("https://github.com/", GitRepositoryUrlError.MissingRepositoryPath)]
    [InlineData("https://token@github.com/openai/example.git", GitRepositoryUrlError.CredentialsNotAllowed)]
    [InlineData("ssh://git:secret@github.com/openai/example.git", GitRepositoryUrlError.CredentialsNotAllowed)]
    [InlineData("https://github.com/openai/example.git?ref=main", GitRepositoryUrlError.QueryOrFragmentNotAllowed)]
    [InlineData("https://github.com/openai/example.git#readme", GitRepositoryUrlError.QueryOrFragmentNotAllowed)]
    public void Validate_rejects_unsafe_or_unsupported_values(string? url, GitRepositoryUrlError error)
    {
        var result = _validator.Validate(url);

        Assert.False(result.IsValid);
        Assert.Equal(error, result.Error);
        Assert.Null(result.NormalizedUrl);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public void Validate_rejects_embedded_control_characters()
    {
        var result = _validator.Validate("https://github.com/openai/\nexample.git");

        Assert.False(result.IsValid);
        Assert.Equal(GitRepositoryUrlError.Malformed, result.Error);
    }

    [Fact]
    public void Validate_rejects_scp_style_query_or_fragment()
    {
        var result = _validator.Validate("git@github.com:openai/example.git?token=secret");

        Assert.False(result.IsValid);
        Assert.Equal(GitRepositoryUrlError.QueryOrFragmentNotAllowed, result.Error);
    }
}
