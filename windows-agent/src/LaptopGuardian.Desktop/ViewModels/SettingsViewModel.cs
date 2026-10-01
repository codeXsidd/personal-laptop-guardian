using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace LaptopGuardian.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LaptopGuardian", "desktop-settings.json");

    private const string ServiceName = "LaptopGuardian";

    [ObservableProperty] private bool _minimizeToTray = true;
    [ObservableProperty] private bool _launchOnStartup;
    [ObservableProperty] private bool _showNotifications = true;
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private string _serviceStatus = "Unknown";
    [ObservableProperty] private bool _isServiceActionRunning;

    public PinSetupViewModel PinSetup { get; }

    public SettingsViewModel(PinSetupViewModel pinSetup)
    {
        PinSetup = pinSetup;
        Load();
        RefreshServiceStatus();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<DesktopSettings>(json);
            if (settings is null) return;
            MinimizeToTray = settings.MinimizeToTray;
            LaunchOnStartup = settings.LaunchOnStartup;
            ShowNotifications = settings.ShowNotifications;
        }
        catch { }
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var settings = new DesktopSettings
            {
                MinimizeToTray = MinimizeToTray,
                LaunchOnStartup = LaunchOnStartup,
                ShowNotifications = ShowNotifications,
            };

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);

            ApplyStartupRegistration(LaunchOnStartup);
            StatusMessage = "Settings saved.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RefreshServiceStatus()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query {ServiceName}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null) { ServiceStatus = "Unknown"; return; }
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                ServiceStatus = "Not Installed";
                return;
            }

            var match = Regex.Match(output, @"STATE\s+:\s+\d+\s+(\w+)");
            ServiceStatus = match.Success ? match.Groups[1].Value switch
            {
                "RUNNING" => "Running",
                "STOPPED" => "Stopped",
                "START_PENDING" => "Starting...",
                "STOP_PENDING" => "Stopping...",
                "PAUSED" => "Paused",
                var s => s,
            } : "Unknown";
        }
        catch
        {
            ServiceStatus = "Unknown";
        }
    }

    [RelayCommand]
    private async Task StartServiceAsync()
    {
        await RunServiceCommandAsync("start");
    }

    [RelayCommand]
    private async Task StopServiceAsync()
    {
        await RunServiceCommandAsync("stop");
    }

    [RelayCommand]
    private async Task RestartServiceAsync()
    {
        IsServiceActionRunning = true;
        StatusMessage = "Restarting service...";

        try
        {
            var stopResult = await RunScCommandAsync("stop");
            if (stopResult)
                await Task.Delay(2000);
            await RunScCommandAsync("start");
            await Task.Delay(1000);
            RefreshServiceStatus();
            StatusMessage = "Service restarted.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Restart failed: {ex.Message}";
        }
        finally
        {
            IsServiceActionRunning = false;
        }
    }

    [RelayCommand]
    private void OpenServicesPanel()
    {
        try
        {
            Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true });
        }
        catch { }
    }

    private async Task RunServiceCommandAsync(string action)
    {
        IsServiceActionRunning = true;
        StatusMessage = $"{char.ToUpper(action[0])}{action[1..]}ing service...";

        try
        {
            var success = await RunScCommandAsync(action);
            await Task.Delay(1500);
            RefreshServiceStatus();
            StatusMessage = success ? $"Service {action} command sent." : $"Failed to {action} service.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to {action} service: {ex.Message}";
        }
        finally
        {
            IsServiceActionRunning = false;
        }
    }

    private static async Task<bool> RunScCommandAsync(string action)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            Arguments = $"{action} {ServiceName}",
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return false;
        await process.WaitForExitAsync();
        return process.ExitCode == 0;
    }

    private static void ApplyStartupRegistration(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return;

            const string valueName = "LaptopGuardianDesktop";
            if (enable)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (exePath is not null)
                    key.SetValue(valueName, $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }
        catch { }
    }

    private sealed class DesktopSettings
    {
        public bool MinimizeToTray { get; set; } = true;
        public bool LaunchOnStartup { get; set; }
        public bool ShowNotifications { get; set; } = true;
    }
}
