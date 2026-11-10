using System.Windows;
using RealtimeTranslate.ViewModels;

namespace RealtimeTranslate.Views;

public partial class UsageWindow : Window
{
    public UsageWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
