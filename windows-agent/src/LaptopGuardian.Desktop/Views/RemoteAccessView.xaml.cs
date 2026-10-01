using System.Windows.Controls;
using LaptopGuardian.Desktop.ViewModels;

namespace LaptopGuardian.Desktop.Views;

public partial class RemoteAccessView : UserControl
{
    private bool _initialized;

    public RemoteAccessView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        if (DataContext is RemoteAccessViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }
}
