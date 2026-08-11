using System;
using System.Linq;
using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B950;

public sealed class ArtifactManifestIntegrityPolicyTests
{
    [Fact]
    public void Evaluate_NormalizesOrdersAndFingerprintsManifest()
    {
        var hashA = new string('A', 64);
        var hashB = new string('b', 64);
        var first = ArtifactManifestIntegrityPolicy.Evaluate(" Release-1 ", new[]
        {
            new ArtifactManifestEntry("bin\\app.apk", hashB, 20),
            new ArtifactManifestEntry("docs/readme.txt", hashA, 10)
        });
        var second = ArtifactManifestIntegrityPolicy.Evaluate("release-1", new[]
        {
            new ArtifactManifestEntry("docs/readme.txt", hashA.ToLowerInvariant(), 10),
            new ArtifactManifestEntry("bin/app.apk", hashB, 20)
        });

        Assert.Equal("release-1", first.ManifestIdentity);
        Assert.Equal(new[] { "bin/app.apk", "docs/readme.txt" }, first.Entries.Select(entry => entry.RelativePath));
        Assert.All(first.Entries, entry => Assert.Equal(entry.Sha256, entry.Sha256.ToLowerInvariant()));
        Assert.Equal("artifact-manifest-valid", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Contains("bin/app.apk", first.CanonicalPayload, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_RejectsDuplicateAndUnsafePaths()
    {
        var hash = new string('a', 64);
        Assert.Throws<ArgumentException>(() => ArtifactManifestIntegrityPolicy.Evaluate("manifest", new[]
        {
            new ArtifactManifestEntry("A/file.txt", hash, 1),
            new ArtifactManifestEntry("a/file.txt", hash, 1)
        }));
        Assert.Throws<ArgumentException>(() => ArtifactManifestIntegrityPolicy.Evaluate("manifest", new[]
        {
            new ArtifactManifestEntry("../file.txt", hash, 1)
        }));
    }

    [Fact]
    public void Evaluate_RejectsInvalidHashesNegativeSizesAndExcessCount()
    {
        var hash = new string('a', 64);
        Assert.Throws<ArgumentException>(() => ArtifactManifestIntegrityPolicy.Evaluate("manifest", new[]
        {
            new ArtifactManifestEntry("a.txt", "bad", 1)
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArtifactManifestIntegrityPolicy.Evaluate("manifest", new[]
        {
            new ArtifactManifestEntry("a.txt", hash, -1)
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArtifactManifestIntegrityPolicy.Evaluate("manifest", new[]
        {
            new ArtifactManifestEntry("a.txt", hash, 1),
            new ArtifactManifestEntry("b.txt", hash, 1)
        }, 1));
    }
}
