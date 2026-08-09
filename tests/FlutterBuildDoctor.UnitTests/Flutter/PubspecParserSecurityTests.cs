using FlutterBuildDoctor.Flutter.ProjectAnalysis;

namespace FlutterBuildDoctor.UnitTests.Flutter;

public sealed class PubspecParserSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fbd-pubspec-security-" + Guid.NewGuid().ToString("N"));

    public PubspecParserSecurityTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Parse_RedactsCredentialsAndQuerySecretsFromStructuredUrlEvidence()
    {
        var pubspecPath = Path.Combine(_root, "pubspec.yaml");
        File.WriteAllText(
            pubspecPath,
            """
            name: secure_sample
            homepage: https://site-user:site-secret@example.com/project?token=homepage-secret#fragment
            dependencies:
              private_git:
                git:
                  url: https://git-user:git-secret@github.com/example/private_repo.git?access_token=query-secret#ref
                  ref: main
              private_hosted:
                hosted:
                  name: private_hosted
                  url: https://registry-user:registry-secret@packages.example.com/api?token=hosted-secret
                version: ^1.0.0
            """);

        var result = new PubspecParser().Parse(SuccessfulRoot(pubspecPath));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Metadata);

        Assert.DoesNotContain("site-secret", result.Metadata.Homepage, StringComparison.Ordinal);
        Assert.DoesNotContain("homepage-secret", result.Metadata.Homepage, StringComparison.Ordinal);
        Assert.Contains("example.com", result.Metadata.Homepage, StringComparison.OrdinalIgnoreCase);

        var git = Assert.Single(result.Metadata.Dependencies, dependency => dependency.Name == "private_git");
        Assert.Equal(PubspecDependencyKind.Git, git.Kind);
        Assert.DoesNotContain("git-secret", git.GitUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("query-secret", git.GitUrl, StringComparison.Ordinal);
        Assert.Contains("github.com", git.GitUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("main", git.GitRef);

        var hosted = Assert.Single(result.Metadata.Dependencies, dependency => dependency.Name == "private_hosted");
        Assert.Equal(PubspecDependencyKind.Hosted, hosted.Kind);
        Assert.DoesNotContain("registry-secret", hosted.HostedUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("hosted-secret", hosted.HostedUrl, StringComparison.Ordinal);
        Assert.Contains("packages.example.com", hosted.HostedUrl, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Cleanup must not hide assertion failures.
        }
    }

    private FlutterProjectRootResult SuccessfulRoot(string pubspecPath)
        => new(
            FlutterProjectRootStatus.Succeeded,
            _root,
            _root,
            pubspecPath,
            Array.Empty<FlutterProjectCandidate>(),
            new[] { pubspecPath },
            "Test project root.");
}
