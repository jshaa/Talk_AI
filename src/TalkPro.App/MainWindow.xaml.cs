using System.Windows;
using TalkPro.App.ViewModels;

namespace TalkPro.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
