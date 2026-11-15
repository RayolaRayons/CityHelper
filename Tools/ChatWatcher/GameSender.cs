using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace CityHelper.Tools.ChatWatcher;

public sealed record SendDelays(int SwitchMs, int PasteMs, int SendMs);

/// <summary>
/// Sends a command to the City of Heroes window the way the old invite-queue script did: switch to the game,
/// Enter (opens the chat bar), Ctrl+V (the command is already on the clipboard), Enter, then back to the
/// previous window. Keys go in as scan codes because the game ignores virtual-key input.
/// </summary>
public static class GameSender
{
    public const string ProcessName = "CityOfHeroes";

    private const ushort ScEnter = 0x1C;
    private const ushort ScCtrl = 0x1D;
    private const ushort ScV = 0x2F;

    public static async Task SetClipboardAsync(string text)
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (window?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    /// <summary>The command must already be on the clipboard. Returns (success, message for the status line).</summary>
    public static async Task<(bool Ok, string Message)> SendClipboardAsync(SendDelays delays)
    {
        if (!OperatingSystem.IsWindows())
            return (false, "Direct send only works on Windows; the command was copied to the clipboard.");

        var game = FindGameWindow();
        if (game == IntPtr.Zero)
            return (false, $"Could not find a visible {ProcessName}.exe window; the command was copied to the clipboard.");

        var previous = NativeMethods.GetForegroundWindow();
        if (previous != game)
        {
            if (NativeMethods.IsIconic(game))
                NativeMethods.ShowWindow(game, 9);   // SW_RESTORE
            ForceForeground(game);
            if (!await WaitForForeground(game, 1500))
                return (false, "Windows would not switch focus to City of Heroes; the command was copied to the clipboard.");
            await Task.Delay(delays.SwitchMs);
        }

        await Tap(ScEnter);                         // opens the chat bar
        await Task.Delay(delays.PasteMs);

        NativeMethods.Key(ScCtrl, down: true);      // Ctrl+V
        await Task.Delay(25);
        await Tap(ScV);
        NativeMethods.Key(ScCtrl, down: false);
        await Task.Delay(delays.SendMs);

        await Tap(ScEnter);                         // sends it
        await Task.Delay(250);

        if (previous != IntPtr.Zero && previous != game)
        {
            await Task.Delay(150);
            ForceForeground(previous);
        }
        return (true, "");
    }

    private static async Task Tap(ushort scanCode)
    {
        NativeMethods.Key(scanCode, down: true);
        await Task.Delay(25);
        NativeMethods.Key(scanCode, down: false);
    }

    private static async Task<bool> WaitForForeground(IntPtr hwnd, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (NativeMethods.GetForegroundWindow() == hwnd)
                return true;
            await Task.Delay(20);
        }
        return NativeMethods.GetForegroundWindow() == hwnd;
    }

    private static void ForceForeground(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows() || !NativeMethods.IsWindow(hwnd))
            return;
        if (NativeMethods.SetForegroundWindow(hwnd))
            return;

        // Windows refuses focus changes from background threads; attaching to the foreground thread's input works around it
        var foreground = NativeMethods.GetForegroundWindow();
        uint foregroundThread = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint thisThread = NativeMethods.GetCurrentThreadId();
        if (foregroundThread != thisThread)
            NativeMethods.AttachThreadInput(thisThread, foregroundThread, true);
        try { NativeMethods.SetForegroundWindow(hwnd); }
        finally
        {
            if (foregroundThread != thisThread)
                NativeMethods.AttachThreadInput(thisThread, foregroundThread, false);
        }
    }

    private static IntPtr FindGameWindow()
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.GetWindowTextLength(hwnd) == 0)
                    return true;
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                using var process = Process.GetProcessById((int)pid);
                if (process.ProcessName.Equals(ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    found = hwnd;
                    return false;
                }
            }
            catch (Exception)
            {
                // Protected / exited processes: skip and keep looking
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>A short beep for a new request.</summary>
    public static void Beep()
    {
        if (OperatingSystem.IsWindows())
            NativeMethods.MessageBeep(0x40);   // MB_ICONASTERISK
    }

    private static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")] public static extern bool MessageBeep(uint type);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        private const uint InputKeyboard = 1;
        private const uint KeyEventScanCode = 0x0008;
        private const uint KeyEventKeyUp = 0x0002;

        public static void Key(ushort scanCode, bool down)
        {
            var input = new INPUT
            {
                type = InputKeyboard,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = scanCode,
                        dwFlags = KeyEventScanCode | (down ? 0 : KeyEventKeyUp),
                    },
                },
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        // The union must include the largest member (mouse) so INPUT has the size Windows expects
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }
    }
}
