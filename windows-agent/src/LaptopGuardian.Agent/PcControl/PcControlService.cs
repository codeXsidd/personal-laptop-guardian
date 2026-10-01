using System.Runtime.InteropServices;

namespace LaptopGuardian.Agent.PcControl;

public interface IPcControlService
{
    bool LockWorkstation();
    bool Sleep();
    bool Restart(bool force = false);
    bool Shutdown(bool force = false);
}

public sealed partial class PcControlService : IPcControlService
{
    private readonly ILogger<PcControlService> _logger;

    public PcControlService(ILogger<PcControlService> logger)
    {
        _logger = logger;
    }

    public bool LockWorkstation()
    {
        _logger.LogInformation("PC Control: Locking workstation");
        return NativeMethods.LockWorkStation();
    }

    public bool Sleep()
    {
        _logger.LogInformation("PC Control: Putting PC to sleep");
        return NativeMethods.SetSuspendState(bHibernate: false, bForce: false, bWakeupEventsDisabled: false);
    }

    public bool Restart(bool force = false)
    {
        _logger.LogInformation("PC Control: Restarting PC (force={Force})", force);
        return InitiateShutdown(restart: true, force: force);
    }

    public bool Shutdown(bool force = false)
    {
        _logger.LogInformation("PC Control: Shutting down PC (force={Force})", force);
        return InitiateShutdown(restart: false, force: force);
    }

    private bool InitiateShutdown(bool restart, bool force)
    {
        EnableShutdownPrivilege();

        uint flags = NativeMethods.EWX_SHUTDOWN;
        if (restart) flags = NativeMethods.EWX_REBOOT;
        if (force) flags |= NativeMethods.EWX_FORCE;

        return NativeMethods.ExitWindowsEx(flags, NativeMethods.SHTDN_REASON_MAJOR_OTHER);
    }

    private static void EnableShutdownPrivilege()
    {
        if (!NativeMethods.OpenProcessToken(
                NativeMethods.GetCurrentProcess(),
                NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY,
                out var tokenHandle))
            return;

        try
        {
            NativeMethods.LookupPrivilegeValue(null, NativeMethods.SE_SHUTDOWN_NAME, out var luid);
            var tp = new NativeMethods.TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = NativeMethods.SE_PRIVILEGE_ENABLED,
            };
            NativeMethods.AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            NativeMethods.CloseHandle(tokenHandle);
        }
    }

    private static partial class NativeMethods
    {
        public const uint EWX_SHUTDOWN = 0x00000001;
        public const uint EWX_REBOOT = 0x00000002;
        public const uint EWX_FORCE = 0x00000004;
        public const uint SHTDN_REASON_MAJOR_OTHER = 0x00000000;
        public const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        public const uint TOKEN_QUERY = 0x0008;
        public const uint SE_PRIVILEGE_ENABLED = 0x00000002;
        public const string SE_SHUTDOWN_NAME = "SeShutdownPrivilege";

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool LockWorkStation();

        [LibraryImport("powrprof.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool SetSuspendState(
            [MarshalAs(UnmanagedType.Bool)] bool bHibernate,
            [MarshalAs(UnmanagedType.Bool)] bool bForce,
            [MarshalAs(UnmanagedType.Bool)] bool bWakeupEventsDisabled);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool ExitWindowsEx(uint uFlags, uint dwReason);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool OpenProcessToken(
            IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool LookupPrivilegeValue(
            string? lpSystemName, string lpName, out long lpLuid);

        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool AdjustTokenPrivileges(
            IntPtr TokenHandle,
            [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
            ref TOKEN_PRIVILEGES NewState,
            uint BufferLength,
            IntPtr PreviousState,
            IntPtr ReturnLength);

        [LibraryImport("kernel32.dll")]
        public static partial IntPtr GetCurrentProcess();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        public struct TOKEN_PRIVILEGES
        {
            public uint PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }
    }
}
