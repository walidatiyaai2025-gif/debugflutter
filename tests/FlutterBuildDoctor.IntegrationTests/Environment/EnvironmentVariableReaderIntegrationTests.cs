using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Infrastructure.Environment;

namespace FlutterBuildDoctor.IntegrationTests.Environment;

public sealed class EnvironmentVariableReaderIntegrationTests
{
    [Fact]
    public void Read_OnWindowsRunner_CapturesEffectivePathWithoutMutation()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var before = System.Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
        var reader = new EnvironmentVariableReader(new SystemVariableValueSource());

        var snapshot = reader.Read();
        var after = System.Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);

        Assert.Equal(before, after);
        Assert.Equal(VariableReadStatus.Present, snapshot.Path.Process.Status);
        Assert.Equal(before, snapshot.Path.EffectiveValue);
        Assert.Equal(4, snapshot.Variables.Count);
        Assert.Contains(snapshot.Variables, variable => variable.Name == "JAVA_HOME");
        Assert.Contains(snapshot.Variables, variable => variable.Name == "ANDROID_HOME");
        Assert.Contains(snapshot.Variables, variable => variable.Name == "ANDROID_SDK_ROOT");
    }
}
