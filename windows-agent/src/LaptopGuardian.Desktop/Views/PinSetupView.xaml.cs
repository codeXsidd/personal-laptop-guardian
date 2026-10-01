using LaptopGuardian.Desktop.ViewModels;

namespace LaptopGuardian.Desktop.Views;

public partial class PinSetupView : UserControl
{
    public PinSetupView()
    {
        InitializeComponent();
    }

    private void OnCurrentPinChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is PinSetupViewModel vm && sender is System.Windows.Controls.PasswordBox pb)
            vm.CurrentPin = pb.Password;
    }

    private void OnNewPinChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is PinSetupViewModel vm && sender is System.Windows.Controls.PasswordBox pb)
            vm.NewPin = pb.Password;
    }

    private void OnConfirmPinChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is PinSetupViewModel vm && sender is System.Windows.Controls.PasswordBox pb)
            vm.ConfirmPin = pb.Password;
    }
}
