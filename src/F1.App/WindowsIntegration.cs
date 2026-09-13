using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using F1.Core;
using Microsoft.Win32;

namespace F1.App;

internal sealed class WindowsIntegration : IDisposable
{
    private readonly HwndSource source = new(new HwndSourceParameters("F1 hotkey") { Width = 0, Height = 0, WindowStyle = 0 });
    public event Action? Invoked;
    public WindowsIntegration() => source.AddHook(Hook);
    public static (uint Modifiers, uint Key) ParseHotkey(string text)
    {
        uint modifiers = 0x4000;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new ArgumentException("Choose a keyboard shortcut.");
        foreach (var part in parts[..^1]) modifiers |= part.ToUpperInvariant() switch
        { "CTRL" => 2u, "ALT" => 1u, "SHIFT" => 4u, "WIN" => 8u, _ => throw new ArgumentException("Use a hotkey such as F1 or Ctrl+Alt+H.") };
        if (parts.Length == 0 || !Enum.TryParse<Key>(parts[^1], true, out var key) || key == Key.None)
            throw new ArgumentException("Use a hotkey such as F1 or Ctrl+Alt+H.");
        return (modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
    }
    public void Register(string text)
    {
        var gesture = ParseHotkey(text);
        UnregisterHotKey(source.Handle, 1);
        if (!RegisterHotKey(source.Handle, 1, gesture.Modifiers, gesture.Key))
            throw new InvalidOperationException("That shortcut is registered by another application. Choose another shortcut in Settings.");
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled)
    { if (message == 0x0312) { handled = true; Invoked?.Invoke(); } return IntPtr.Zero; }
    public static (IntPtr Handle, ForegroundContext Context) Capture()
    {
        var handle = GetForegroundWindow();
        var title = new StringBuilder(513); GetWindowText(handle, title, title.Capacity);
        GetWindowThreadProcessId(handle, out var pid);
        string name;
        try { using var process = Process.GetProcessById((int)pid); name = process.ProcessName; }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception) { name = "Unknown"; }
        return (handle, new(name, title.ToString()));
    }
    public static void Position(Window window, IntPtr foreground)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var screen = foreground != IntPtr.Zero ? System.Windows.Forms.Screen.FromHandle(foreground) : System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        // Move to the target monitor first so its DPI applies before converting DIPs.
        SetWindowPos(handle, IntPtr.Zero, screen.WorkingArea.Left, screen.WorkingArea.Top, 0, 0, 0x0015);
        var scale = GetDpiForWindow(handle) / 96.0;
        window.Width = Math.Min(720, screen.WorkingArea.Width / scale - 32);
        window.MaxHeight = screen.WorkingArea.Height / scale * 0.7;
        var x = screen.WorkingArea.Left + (screen.WorkingArea.Width - window.Width * scale) / 2;
        var y = screen.WorkingArea.Top + screen.WorkingArea.Height * 0.2;
        SetWindowPos(handle, IntPtr.Zero, (int)x, (int)y, 0, 0, 0x0015);
    }
    public static void RestoreFocus(IntPtr handle) { if (handle != IntPtr.Zero) SetForegroundWindow(handle); }
    public static void Focus(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        ShowWindow(handle, 5); SetForegroundWindow(handle); window.Activate();
    }
    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("MiralieF1", "\"" + Environment.ProcessPath + "\"");
        else key.DeleteValue("MiralieF1", false);
    }
    public void Dispose() { UnregisterHotKey(source.Handle, 1); source.Dispose(); }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maximum);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
