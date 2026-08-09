using System.Security;
using FlutterBuildDoctor.Application.Environment;
using FlutterBuildDoctor.Infrastructure.Environment;

namespace FlutterBuildDoctor.UnitTests.EnvironmentDiscovery;

public sealed class EnvironmentVariableReaderTests
{
    [Fact]
    public void Read_CapturesAllRequiredVariablesAcrossScopes()
    {
        var source = new StubVariableSource();
        source.Set("PATH", VariableScope.Process, @"C:\Tools;C:\Windows");
        source.Set("PATH", VariableScope.User, @"C:\UserTools");
        source.Set("PATH", VariableScope.Machine, @"C:\Windows");
        source.Set("JAVA_HOME", VariableScope.Process, @"C:\Java\jdk-21");
        source.Set("JAVA_HOME", VariableScope.User, @"C:\Java\jdk-17");
        source.Set("ANDROID_HOME", VariableScope.Process, @"C:\Android\Sdk");
        source.Set("ANDROID_SDK_ROOT", VariableScope.Machine, @"D:\Android\Sdk");
        var reader = new EnvironmentVariableReader(source);

        var snapshot = reader.Read();

        Assert.Equal(4, snapshot.Variables.Count);
        Assert.Equal(@"C:\Tools;C:\Windows", snapshot.Path.EffectiveValue);
        Assert.Equal(@"C:\Java\jdk-21", snapshot.JavaHome.EffectiveValue);
        Assert.Equal(@"C:\Java\jdk-17", snapshot.JavaHome.User.Value);
        Assert.Equal(@"C:\Android\Sdk", snapshot.AndroidHome.EffectiveValue);
        Assert.Equal(@"D:\Android\Sdk", snapshot.AndroidSdkRoot.Machine.Value);
        Assert.All(snapshot.Variables, record => Assert.Equal(record.Name, record.Name.ToUpperInvariant()));
    }

    [Fact]
    public void Read_MissingValues_AreRepresentedWithoutInventingFallbacks()
    {
        var source = new StubVariableSource();
        source.Set("PATH", VariableScope.Process, @"C:\Windows");
        var reader = new EnvironmentVariableReader(source);

        var snapshot = reader.Read();

        Assert.Equal(VariableReadStatus.Present, snapshot.Path.Process.Status);
        Assert.Equal(VariableReadStatus.Missing, snapshot.JavaHome.Process.Status);
        Assert.Equal(VariableReadStatus.Missing, snapshot.JavaHome.User.Status);
        Assert.Equal(VariableReadStatus.Missing, snapshot.JavaHome.Machine.Status);
        Assert.Null(snapshot.JavaHome.EffectiveValue);
        Assert.Equal(VariableReadStatus.Missing, snapshot.AndroidHome.Process.Status);
        Assert.Equal(VariableReadStatus.Missing, snapshot.AndroidSdkRoot.Process.Status);
    }

    [Fact]
    public void Read_UnavailableScope_PreservesErrorAndContinuesOtherScopes()
    {
        var source = new StubVariableSource();
        source.Set("PATH", VariableScope.Process, @"C:\Windows");
        source.Throw("JAVA_HOME", VariableScope.User, new SecurityException("denied"));
        source.Set("JAVA_HOME", VariableScope.Machine, @"C:\Java\machine");
        var reader = new EnvironmentVariableReader(source);

        var snapshot = reader.Read();

        Assert.Equal(VariableReadStatus.Unavailable, snapshot.JavaHome.User.Status);
        Assert.Null(snapshot.JavaHome.User.Value);
        Assert.Equal("denied", snapshot.JavaHome.User.Error);
        Assert.Equal(VariableReadStatus.Present, snapshot.JavaHome.Machine.Status);
        Assert.Equal(@"C:\Java\machine", snapshot.JavaHome.Machine.Value);
        Assert.Equal(@"C:\Windows", snapshot.Path.EffectiveValue);
    }

    [Fact]
    public void Read_UsesProcessScopeAsEffectiveValueWithoutPromotingPersistedScopes()
    {
        var source = new StubVariableSource();
        source.Set("JAVA_HOME", VariableScope.User, @"C:\Java\user");
        source.Set("JAVA_HOME", VariableScope.Machine, @"C:\Java\machine");
        var reader = new EnvironmentVariableReader(source);

        var snapshot = reader.Read();

        Assert.Null(snapshot.JavaHome.EffectiveValue);
        Assert.Equal(VariableReadStatus.Missing, snapshot.JavaHome.Process.Status);
        Assert.Equal(@"C:\Java\user", snapshot.JavaHome.User.Value);
        Assert.Equal(@"C:\Java\machine", snapshot.JavaHome.Machine.Value);
    }

    private sealed class StubVariableSource : IVariableValueSource
    {
        private readonly Dictionary<(string Name, VariableScope Scope), object?> _values = new();

        public void Set(string name, VariableScope scope, string value)
            => _values[(name, scope)] = value;

        public void Throw(string name, VariableScope scope, Exception exception)
            => _values[(name, scope)] = exception;

        public string? Get(string name, VariableScope scope)
        {
            if (!_values.TryGetValue((name, scope), out var value))
            {
                return null;
            }

            if (value is Exception exception)
            {
                throw exception;
            }

            return (string?)value;
        }
    }
}
