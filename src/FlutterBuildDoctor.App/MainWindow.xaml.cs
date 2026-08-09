using System.Windows;
using FlutterBuildDoctor.App.ViewModels;

namespace FlutterBuildDoctor.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.EnvironmentDoctor.InitializeAsync();
        }
    }
}
