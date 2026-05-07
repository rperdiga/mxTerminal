using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Terminal;

/// <summary>
/// Posts Win32 keyboard messages to Studio Pro's own main window so the
/// existing menu/hotkey handlers fire. PostMessage doesn't require focus,
/// so this works while the user types in the Terminal pane.
/// </summary>
public sealed class StudioProUiAutomation : IStudioProUiAutomation
{
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP   = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP   = 0x0105;

    // Virtual-key codes we use.
    private const ushort VK_SHIFT   = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU    = 0x12; // Alt
    private const ushort VK_F1      = 0x70;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly string runHotkey;
    private readonly string stopHotkey;
    private readonly string refreshHotkey;
    private readonly Logger? log;

    public StudioProUiAutomation(string runHotkey, string stopHotkey, string refreshHotkey, Logger? log = null)
    {
        this.runHotkey = runHotkey;
        this.stopHotkey = stopHotkey;
        this.refreshHotkey = refreshHotkey;
        this.log = log;
    }

    public bool TriggerRun()              => Send(runHotkey);
    public bool TriggerStop()             => Send(stopHotkey);
    public bool TriggerRefreshFromDisk()  => Send(refreshHotkey);
    public bool TriggerSaveAll()          => Send("Ctrl+S");

    private bool Send(string hotkeyText)
    {
        if (!OperatingSystem.IsWindows())
        {
            log?.Info($"[actions] UI automation not supported on this platform; ignoring {hotkeyText}");
            return false;
        }
        var hwnd = Process.GetCurrentProcess().MainWindowHandle;
        if (hwnd == IntPtr.Zero)
        {
            log?.Warn($"UI automation: Studio Pro MainWindowHandle is zero; cannot send {hotkeyText}");
            return false;
        }
        if (!TryParse(hotkeyText, out var modifiers, out var vk))
        {
            log?.Warn($"UI automation: cannot parse hotkey '{hotkeyText}'");
            return false;
        }

        // Press modifiers (in canonical order: Ctrl, Shift, Alt) then the key, then release in reverse.
        if (modifiers.HasFlag(Modifiers.Ctrl))  PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_CONTROL, IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Shift)) PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_SHIFT,   IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Alt))   PostMessage(hwnd, WM_SYSKEYDOWN, (IntPtr)VK_MENU, IntPtr.Zero);
        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
        PostMessage(hwnd, WM_KEYUP,   (IntPtr)vk, IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Alt))   PostMessage(hwnd, WM_SYSKEYUP, (IntPtr)VK_MENU, IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Shift)) PostMessage(hwnd, WM_KEYUP,   (IntPtr)VK_SHIFT,   IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Ctrl))  PostMessage(hwnd, WM_KEYUP,   (IntPtr)VK_CONTROL, IntPtr.Zero);

        log?.Info($"[actions] sent {hotkeyText} to Studio Pro main window");
        return true;
    }

    [Flags]
    private enum Modifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

    /// <summary>Parses "F5", "Shift+F5", "Ctrl+F4", "Ctrl+Shift+F12" into modifiers + VK.</summary>
    private static bool TryParse(string text, out Modifiers modifiers, out ushort vk)
    {
        modifiers = Modifiers.None;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":  case "control": modifiers |= Modifiers.Ctrl;  break;
                case "shift": modifiers |= Modifiers.Shift; break;
                case "alt":   modifiers |= Modifiers.Alt;   break;
                default: return false;
            }
        }

        var key = parts[^1];
        // Function keys: F1-F24
        if (key.Length >= 2 && (key[0] == 'F' || key[0] == 'f') &&
            int.TryParse(key.AsSpan(1), out var n) && n is >= 1 and <= 24)
        {
            vk = (ushort)(VK_F1 + (n - 1));
            return true;
        }
        // Letter keys: A-Z (VK codes 0x41-0x5A); needed for Ctrl+S etc.
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c >= 'A' && c <= 'Z') { vk = (ushort)c; return true; }
        }
        return false;
    }
}
