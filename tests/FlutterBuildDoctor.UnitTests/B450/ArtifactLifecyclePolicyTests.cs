using FlutterBuildDoctor.Application.Artifacts;

namespace FlutterBuildDoctor.UnitTests.B450;

public sealed class ArtifactLifecyclePolicyTests
{
    [Fact]
    public void Evaluate_NormalizesOrdersExpiresAndFingerprintsDeterministically()
    {
        var now = new DateTimeOffset(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);
        var artifacts = new[]
        {
            new ArtifactLifecycleInput("old", " old build.APK ", now.AddDays(-10), 5),
            new ArtifactLifecycleInput("new", "new build.AAB", now.AddHours(-1), 999)
        };

        var first = ArtifactLifecyclePolicy.Evaluate(artifacts, now);
        var second = ArtifactLifecyclePolicy.Evaluate(artifacts.Reverse(), now);

        Assert.Equal("new", first.Artifacts[0].Identity);
        Assert.Equal("new-build.aab", first.Artifacts[0].FileName);
        Assert.Equal(ArtifactLifecyclePolicy.MaxRetentionDays, first.Artifacts[0].RetentionDays);
        Assert.False(first.Artifacts[0].Expired);
        Assert.Equal("active", first.Artifacts[0].ReasonCode);
        Assert.True(first.Artifacts[1].Expired);
        Assert.Equal("expired", first.Artifacts[1].ReasonCode);
        Assert.Equal(TimeSpan.Zero, first.Artifacts[0].CreatedAtUtc.Offset);
        Assert.Equal(first.Artifacts[0].CreatedAtUtc.AddDays(first.Artifacts[0].RetentionDays), first.Artifacts[0].ExpiresAtUtc);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Theory]
    [InlineData("artifact.exe")]
    [InlineData("../artifact.apk")]
    [InlineData("folder/artifact.apk")]
    public void NormalizeFileName_RejectsUnsafeNames(string value)
        => Assert.Throws<ArgumentException>(() => ArtifactLifecyclePolicy.NormalizeFileName(value));

    [Fact]
    public void Normalize_ClampsMinimumRetention()
    {
        var now = DateTimeOffset.UtcNow;
        var item = ArtifactLifecyclePolicy.Normalize(new ArtifactLifecycleInput("artifact", "app.apk", now, -10), now);
        Assert.Equal(ArtifactLifecyclePolicy.MinRetentionDays, item.RetentionDays);
    }

    [Fact]
    public void Evaluate_BoundsArtifactCount()
    {
        var now = DateTimeOffset.UtcNow;
        var values = Enumerable.Range(0, ArtifactLifecyclePolicy.MaxArtifacts + 1)
            .Select(index => new ArtifactLifecycleInput($"artifact-{index}", $"app-{index}.apk", now, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArtifactLifecyclePolicy.Evaluate(values, now));
    }
}
