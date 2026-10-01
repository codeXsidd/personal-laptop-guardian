using System.Runtime.InteropServices;

namespace LaptopGuardian.Desktop.Services;

internal static partial class InputHelper
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const int WHEEL_DELTA = 120;

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint Type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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
    private static partial bool ExitWindowsEx(uint uFlags, uint dwReason);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LookupPrivilegeValue(string? lpSystemName, string lpName, out long lpLuid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public long Luid;
        public uint Attributes;
    }

    public static INPUT CreateMouseMoveInput(int absX, int absY)
    {
        return new INPUT
        {
            Type = INPUT_MOUSE,
            U = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE,
                }
            }
        };
    }

    public static void SendInput(INPUT input)
    {
        var inputs = new[] { input };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void SendMouseClick(bool isRight)
    {
        var down = new INPUT
        {
            Type = INPUT_MOUSE,
            U = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = isRight ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN,
                }
            }
        };
        var up = new INPUT
        {
            Type = INPUT_MOUSE,
            U = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = isRight ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP,
                }
            }
        };
        SendInput(2, new[] { down, up }, Marshal.SizeOf<INPUT>());
    }

    public static void SendMouseScroll(int deltaY)
    {
        var input = new INPUT
        {
            Type = INPUT_MOUSE,
            U = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    mouseData = deltaY * WHEEL_DELTA,
                    dwFlags = MOUSEEVENTF_WHEEL,
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void SendKeyPress(string? key, bool down)
    {
        if (string.IsNullOrEmpty(key)) return;
        var vk = MapKeyToVk(key);
        if (vk == 0) return;

        uint flags = down ? 0 : KEYEVENTF_KEYUP;
        if (IsExtendedKey(vk)) flags |= KEYEVENTF_EXTENDEDKEY;

        var input = new INPUT
        {
            Type = INPUT_KEYBOARD,
            U = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    dwFlags = flags,
                }
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public static void InitiateShutdown(bool restart)
    {
        EnableShutdownPrivilege();
        uint flags = restart ? 0x00000002u : 0x00000001u; // EWX_REBOOT / EWX_SHUTDOWN
        ExitWindowsEx(flags, 0);
    }

    private static void EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008, out var token))
            return;
        try
        {
            LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid);
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = 0x00000002 };
            AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(token); }
    }

    private static ushort MapKeyToVk(string key)
    {
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z') return (ushort)c;
            if (c is >= '0' and <= '9') return (ushort)c;
            return c switch
            {
                ' ' => 0x20,  // VK_SPACE
                _ => 0
            };
        }

        return key.ToLowerInvariant() switch
        {
            "enter" or "return" => 0x0D,
            "backspace" => 0x08,
            "tab" => 0x09,
            "escape" or "esc" => 0x1B,
            "shift" or "shift_left" => 0xA0,
            "shift_right" => 0xA1,
            "control" or "ctrl" or "control_left" => 0xA2,
            "control_right" => 0xA3,
            "alt" or "alt_left" => 0xA4,
            "alt_right" => 0xA5,
            "delete" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "page_up" => 0x21,
            "page_down" => 0x22,
            "arrow_up" or "up" => 0x26,
            "arrow_down" or "down" => 0x28,
            "arrow_left" or "left" => 0x25,
            "arrow_right" or "right" => 0x27,
            "f1" => 0x70, "f2" => 0x71, "f3" => 0x72, "f4" => 0x73,
            "f5" => 0x74, "f6" => 0x75, "f7" => 0x76, "f8" => 0x77,
            "f9" => 0x78, "f10" => 0x79, "f11" => 0x7A, "f12" => 0x7B,
            "caps_lock" => 0x14,
            "insert" => 0x2D,
            "windows" or "meta" => 0x5B,
            _ => 0
        };
    }

    private static bool IsExtendedKey(ushort vk) =>
        vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
            or 0x2D or 0x2E or 0x5B or 0xA3 or 0xA5;
}
