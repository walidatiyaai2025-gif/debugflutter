using System.Security;
using FlutterBuildDoctor.Application.Environment;

namespace FlutterBuildDoctor.Infrastructure.Environment;

public sealed class EnvironmentVariableReader : IEnvironmentVariableReader
{
    private static readonly string[] VariableNames =
    {
        "PATH",
        "JAVA_HOME",
        "ANDROID_HOME",
        "ANDROID_SDK_ROOT"
    };

    private readonly IVariableValueSource _source;

    public EnvironmentVariableReader(IVariableValueSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    public EnvironmentVariableSnapshot Read()
    {
        var records = VariableNames.ToDictionary(
            name => name,
            ReadVariable,
            StringComparer.OrdinalIgnoreCase);

        return new EnvironmentVariableSnapshot(
            DateTimeOffset.UtcNow,
            records["PATH"],
            records["JAVA_HOME"],
            records["ANDROID_HOME"],
            records["ANDROID_SDK_ROOT"]);
    }

    private VariableRecord ReadVariable(string name)
        => new(
            name,
            ReadScope(name, VariableScope.Process),
            ReadScope(name, VariableScope.User),
            ReadScope(name, VariableScope.Machine));

    private VariableScopeValue ReadScope(string name, VariableScope scope)
    {
        try
        {
            var value = _source.Get(name, scope);
            return string.IsNullOrEmpty(value)
                ? new VariableScopeValue(scope, VariableReadStatus.Missing, value)
                : new VariableScopeValue(scope, VariableReadStatus.Present, value);
        }
        catch (Exception ex) when (ex is SecurityException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            return new VariableScopeValue(
                scope,
                VariableReadStatus.Unavailable,
                Value: null,
                Error: ex.Message);
        }
    }
}
