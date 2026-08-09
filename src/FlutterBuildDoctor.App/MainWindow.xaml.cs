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
    }
}
