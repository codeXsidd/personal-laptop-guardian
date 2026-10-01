using System.Windows;
using System.Windows.Media.Imaging;

namespace LaptopGuardian.Desktop.Views;

public partial class QrCodeDialog : Window
{
    public QrCodeDialog(BitmapSource qrImage, string pairingCode)
    {
        InitializeComponent();
        QrImage.Source = qrImage;
        CodeText.Text = pairingCode;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
