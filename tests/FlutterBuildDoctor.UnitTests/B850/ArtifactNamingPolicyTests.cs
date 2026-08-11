using FlutterBuildDoctor.Application.Governance;

namespace FlutterBuildDoctor.UnitTests.B850;

public sealed class ArtifactNamingPolicyTests
{
    [Fact]
    public void Create_NormalizesSemanticArtifactNameAndFingerprint()
    {
        var first = ArtifactNamingPolicy.Create(" Release APK ", " Flutter Build Doctor ", "1.2.3-RC.1", "RC", "APK");
        var second = ArtifactNamingPolicy.Create("release apk", "Flutter   Build   Doctor", "1.2.3-rc.1", "rc", ".apk");

        Assert.Equal("flutter-build-doctor-1.2.3-rc.1-rc.apk", first.FileName);
        Assert.Equal("artifact-name-valid", first.ReasonCode);
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(64, first.Fingerprint.Length);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.x")]
    public void NormalizeVersion_RejectsNonSemanticVersions(string value)
        => Assert.Throws<ArgumentException>(() => ArtifactNamingPolicy.NormalizeVersion(value));

    [Theory]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    public void NormalizeBaseName_RejectsUnsafeFileCharacters(string value)
        => Assert.Throws<ArgumentException>(() => ArtifactNamingPolicy.NormalizeBaseName(value));

    [Fact]
    public void Create_RejectsUnsupportedExtension()
        => Assert.Throws<ArgumentException>(() => ArtifactNamingPolicy.Create("id", "artifact", "1.0.0", "release", ".bat"));
}
