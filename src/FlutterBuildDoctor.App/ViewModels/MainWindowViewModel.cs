using CommunityToolkit.Mvvm.ComponentModel;

namespace FlutterBuildDoctor.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    public string ApplicationName => "Flutter Build Doctor";

    public string StartupStatus => "Ready";
}
