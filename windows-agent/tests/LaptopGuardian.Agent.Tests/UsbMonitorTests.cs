using LaptopGuardian.Agent.Monitors;

namespace LaptopGuardian.Agent.Tests;

public sealed class UsbMonitorTests
{
    [Theory]
    [InlineData("USB\\VID_1234&PID_5678\\serial", true)]
    [InlineData("USBSTOR\\Disk&Ven_Kingston&Prod_DataTraveler\\123", true)]
    [InlineData("USBPRINT\\Printer&Ven_HP\\001", true)]
    [InlineData("HID\\VID_046D&PID_C52B\\6&1B&0", true)]
    [InlineData("PCI\\VEN_8086&DEV_1E3A\\3&1&B0", false)]
    [InlineData("ACPI\\PNP0C0A\\1", false)]
    [InlineData("SWD\\PRINTENUM\\{UUID}", false)]
    [InlineData("", false)]
    public void IsUsbDevice_ClassifiesCorrectly(string pnpDeviceId, bool expected)
    {
        Assert.Equal(expected, UsbMonitor.IsUsbDevice(pnpDeviceId));
    }

    [Fact]
    public void IsUsbDevice_IsCaseInsensitive()
    {
        Assert.True(UsbMonitor.IsUsbDevice("usb\\vid_1234&pid_5678\\serial"));
        Assert.True(UsbMonitor.IsUsbDevice("USB\\VID_1234&PID_5678\\serial"));
        Assert.True(UsbMonitor.IsUsbDevice("UsbStor\\Disk\\123"));
    }
}
