namespace FlutterBuildDoctor.App.ViewModels;

public enum EnvironmentComponentState
{
    Ready = 0,
    Attention,
    Unavailable
}

public sealed record EnvironmentComponentViewModel(
    string Name,
    EnvironmentComponentState State,
    string StateText,
    string StatusDetail,
    string Path,
    string Version,
    string Action)
{
    public bool IsReady => State == EnvironmentComponentState.Ready;

    public bool NeedsAttention => State != EnvironmentComponentState.Ready;
}
