using System.Windows;
using System.Windows.Controls;
using LaptopGuardian.Desktop.ViewModels;

namespace LaptopGuardian.Desktop.Views;

public partial class ActivityView : UserControl
{
    public ActivityView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ActivityViewModel vm)
            await vm.LoadEventsAsync();
    }
}
