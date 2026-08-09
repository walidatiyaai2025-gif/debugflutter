namespace FlutterBuildDoctor.Application.Environment;

public enum VariableScope
{
    Process = 0,
    User,
    Machine
}

public enum VariableReadStatus
{
    Present = 0,
    Missing,
    Unavailable
}

public sealed record VariableScopeValue(
    VariableScope Scope,
    VariableReadStatus Status,
    string? Value,
    string? Error = null);

public sealed record VariableRecord(
    string Name,
    VariableScopeValue Process,
    VariableScopeValue User,
    VariableScopeValue Machine)
{
    public string? EffectiveValue => Process.Value;
}

public sealed record EnvironmentVariableSnapshot(
    DateTimeOffset CapturedAt,
    VariableRecord Path,
    VariableRecord JavaHome,
    VariableRecord AndroidHome,
    VariableRecord AndroidSdkRoot)
{
    public IReadOnlyList<VariableRecord> Variables =>
        new[] { Path, JavaHome, AndroidHome, AndroidSdkRoot };
}

public interface IEnvironmentVariableReader
{
    EnvironmentVariableSnapshot Read();
}

public interface IVariableValueSource
{
    string? Get(string name, VariableScope scope);
}
