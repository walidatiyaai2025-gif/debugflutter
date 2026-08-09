using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Infrastructure.Environment;

public sealed class SystemVariableValueSource : IVariableValueSource
{
    public string? Get(string name, VariableScope scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var target = scope switch
        {
            VariableScope.Process => EnvironmentVariableTarget.Process,
            VariableScope.User => EnvironmentVariableTarget.User,
            VariableScope.Machine => EnvironmentVariableTarget.Machine,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, null)
        };

        return System.Environment.GetEnvironmentVariable(name, target);
    }
}
