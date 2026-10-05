namespace LaptopGuardian.Agent.Models;

public static class EventType
{
    public const string AgentStarted = "agent_started";
    public const string AgentStopped = "agent_stopped";
    public const string SystemStartup = "system_startup";
    public const string SystemShutdown = "system_shutdown";
    public const string SessionLogin = "session_login";
    public const string SessionLogout = "session_logout";
    public const string SessionLock = "session_lock";
    public const string SessionUnlock = "session_unlock";
    public const string LoginFailed = "login_failed";
    public const string ProcessStart = "process_start";
    public const string ProcessStop = "process_stop";
    public const string UsbConnected = "usb_connected";
    public const string UsbDisconnected = "usb_disconnected";
    public const string NetworkConnected = "network_connected";
    public const string NetworkDisconnected = "network_disconnected";
    public const string NetworkChanged = "network_changed";
    public const string EventLogEntry = "eventlog_entry";
    public const string FileAccess = "file_access";
    public const string SystemMetrics = "system_metrics";
    public const string SystemSleep = "system_sleep";
    public const string SystemWake = "system_wake";
}
