using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace ShipmentTracker.Desktop;

internal sealed class WindowsIntegration : IDisposable
{
    private const int HotkeyId = 401;
    private readonly IntPtr handle;
    private readonly HwndSource source;
    private readonly Action toggle;
    private bool registered;

    public WindowsIntegration(Window window, Action toggle)
    {
        this.toggle = toggle;
        handle = new WindowInteropHelper(window).EnsureHandle();
        source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Window handle is unavailable.");
        source.AddHook(Receive);
    }

    public void SetHotkey(string hotkey)
    {
        if (hotkey is not ("None" or "Ctrl+Alt+D" or "Ctrl+Alt+T"))
            throw new InvalidOperationException("Unsupported shortcut. Select a shortcut from Settings.");
        if (registered)
        {
            if (!UnregisterHotKey(handle, HotkeyId)) throw new Win32Exception(Marshal.GetLastWin32Error());
            registered = false;
        }
        if (hotkey == "None") return;
        var key = hotkey.EndsWith('D') ? 0x44u : 0x54u;
        if (!RegisterHotKey(handle, HotkeyId, 0x4003, key))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "That shortcut could not be registered. Choose another in Settings.");
        registered = true;
    }

    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
            if (!executable.EndsWith("ShipmentTracker.Desktop.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Run the published ShipmentTracker.Desktop.exe before enabling sign-in startup.");
            key.SetValue("LocalShipmentTracker", "\"" + executable + "\" --tray");
        }
        else key.DeleteValue("LocalShipmentTracker", false);
    }

    private IntPtr Receive(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam.ToInt32() == HotkeyId)
        {
            toggle();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (registered && !UnregisterHotKey(handle, HotkeyId))
            System.Diagnostics.Trace.TraceError("Could not release the registered shortcut.");
        source.RemoveHook(Receive);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
