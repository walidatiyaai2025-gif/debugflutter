using FlutterBuildDoctor.Android.Detection;
using FlutterBuildDoctor.Infrastructure.Environment;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class AndroidSdkRootDetectorIntegrationTests
{
    [Fact]
    public void Detect_FromProductionEnvironmentSnapshot_DoesNotMutateAndroidVariables()
    {
        var beforeSdkRoot = System.Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT", EnvironmentVariableTarget.Process);
        var beforeAndroidHome = System.Environment.GetEnvironmentVariable("ANDROID_HOME", EnvironmentVariableTarget.Process);
        var reader = new EnvironmentVariableReader(new SystemVariableValueSource());
        var detector = new AndroidSdkRootDetector();

        var snapshot = reader.Read();
        var result = detector.Detect(snapshot);

        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Equal(beforeSdkRoot, System.Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT", EnvironmentVariableTarget.Process));
        Assert.Equal(beforeAndroidHome, System.Environment.GetEnvironmentVariable("ANDROID_HOME", EnvironmentVariableTarget.Process));

        var expectedEffective = !string.IsNullOrWhiteSpace(snapshot.AndroidSdkRoot.EffectiveValue)
            ? snapshot.AndroidSdkRoot.EffectiveValue
            : snapshot.AndroidHome.EffectiveValue;

        if (string.IsNullOrWhiteSpace(expectedEffective))
        {
            Assert.Null(result.EffectiveCandidate);
            Assert.Equal(AndroidSdkRootDetectionStatus.MissingEffectiveRoot, result.Status);
        }
        else
        {
            Assert.NotNull(result.EffectiveCandidate);
            Assert.True(result.EffectiveCandidate!.IsEffective);
            Assert.Contains(result.EffectiveCandidate.Sources, source => source.Scope == FlutterBuildDoctor.Application.Environment.VariableScope.Process);
        }
    }
}
