// Helpers ported from Concord.Host11x.Interop for use in the Host10x Pane layer.
// These are pure utility types (no Mendix API surface) that support the
// TerminalPaneViewModel's action-server lifecycle management.
// They live here rather than in Concord.Host10x/Interop/ so Task 23 (the VM
// port) is self-contained within Pane/. Task 24 (TerminalPaneExtension port)
// will reference them from this same namespace.

using System.Net.Sockets;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Terminal;
using Terminal.Interop;

namespace Concord.Host10x.Interop;

/// <summary>
/// Probes the running Mendix app's HTTP port to determine run state.
/// Mirrors Host11x RunStateProbe — no Mendix API coupling.
/// </summary>
internal sealed class RunStateProbe : IRunStateProbe
{
    private const int ConnectTimeoutMs = 250;

    private readonly Func<string?> getApplicationRootUrl;

    public RunStateProbe(Func<string?> getApplicationRootUrl)
    {
        this.getApplicationRootUrl = getApplicationRootUrl;
    }

    public string? GetActiveUrl() => getApplicationRootUrl();

    public int? GetActivePort()
    {
        var url = getApplicationRootUrl();
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return uri.Port;
    }

    public async Task<RunState> IsRunningAsync(CancellationToken ct = default)
    {
        var port = GetActivePort();
        if (port is null or <= 0) return RunState.Unknown;

        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectTimeoutMs);
            await client.ConnectAsync("127.0.0.1", port.Value, timeoutCts.Token);
            return RunState.Running;
        }
        catch (SocketException)
        {
            return RunState.Stopped;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return RunState.Stopped;
        }
        catch
        {
            return RunState.Unknown;
        }
    }
}

/// <summary>
/// Drives Studio Pro's own menu/hotkey handlers from the action bridge so
/// MCP tools like run_app / stop_app can fire F5 / Shift+F5 without needing
/// focus on the IDE document tab.
/// Mirrors Host11x StudioProUiAutomation — no Mendix API coupling.
/// </summary>
internal sealed class StudioProUiAutomation : IStudioProUiAutomation
{
    private const uint WM_KEYDOWN    = 0x0100;
    private const uint WM_KEYUP      = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP   = 0x0105;

    private const ushort VK_SHIFT   = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU    = 0x12;
    private const ushort VK_F1      = 0x70;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly string runHotkey;
    private readonly string stopHotkey;
    private readonly string refreshHotkey;
    private readonly Logger? log;
    private string? lastFailureReason;

    public StudioProUiAutomation(string runHotkey, string stopHotkey, string refreshHotkey, Logger? log = null)
    {
        this.runHotkey = runHotkey;
        this.stopHotkey = stopHotkey;
        this.refreshHotkey = refreshHotkey;
        this.log = log;
    }

    public bool TriggerRun()             => Send(runHotkey);
    public bool TriggerStop()            => Send(stopHotkey);
    public bool TriggerRefreshFromDisk() => Send(refreshHotkey);
    public bool TriggerSaveAll()         => Send("Ctrl+S");

    public string? LastFailureReason => lastFailureReason;

    private bool Send(string hotkeyText)
    {
        if (OperatingSystem.IsWindows()) return SendWindows(hotkeyText);
        if (OperatingSystem.IsMacOS())   return SendMac(hotkeyText);

        lastFailureReason = $"UI automation is not implemented on this platform ({RuntimeInformation.OSDescription}).";
        log?.Info($"[actions] {lastFailureReason} ignoring {hotkeyText}");
        return false;
    }

    private bool SendWindows(string hotkeyText)
    {
        var hwnd = Process.GetCurrentProcess().MainWindowHandle;
        if (hwnd == IntPtr.Zero)
        {
            lastFailureReason = "Studio Pro main window unavailable; the IDE may still be loading or a modal dialog has focus. Bring Studio Pro to the foreground and dismiss any open dialogs, then retry.";
            log?.Warn($"UI automation: Studio Pro MainWindowHandle is zero; cannot send {hotkeyText}");
            return false;
        }
        if (!TryParse(hotkeyText, out var modifiers, out var vk))
        {
            lastFailureReason = $"UI automation: cannot parse hotkey '{hotkeyText}'.";
            log?.Warn(lastFailureReason);
            return false;
        }

        if (modifiers.HasFlag(Modifiers.Ctrl))  PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_CONTROL, IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Shift)) PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_SHIFT,   IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Alt))   PostMessage(hwnd, WM_SYSKEYDOWN, (IntPtr)VK_MENU, IntPtr.Zero);
        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
        PostMessage(hwnd, WM_KEYUP,   (IntPtr)vk, IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Alt))   PostMessage(hwnd, WM_SYSKEYUP, (IntPtr)VK_MENU, IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Shift)) PostMessage(hwnd, WM_KEYUP,   (IntPtr)VK_SHIFT,   IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Ctrl))  PostMessage(hwnd, WM_KEYUP,   (IntPtr)VK_CONTROL, IntPtr.Zero);

        lastFailureReason = null;
        log?.Info($"[actions] sent {hotkeyText} to Studio Pro main window");
        return true;
    }

    private bool SendMac(string hotkeyText)
    {
        if (!TryParse(hotkeyText, out var modifiers, out var vk))
        {
            lastFailureReason = $"UI automation: cannot parse hotkey '{hotkeyText}'.";
            log?.Warn(lastFailureReason);
            return false;
        }
        if (!TryWinVkToMacKeyCode(vk, out int macKeyCode))
        {
            lastFailureReason = $"UI automation: no macOS key-code mapping for hotkey '{hotkeyText}' (Win VK 0x{vk:X}).";
            log?.Warn(lastFailureReason);
            return false;
        }

        try
        {
            bool cleared = ClearWebViewFirstResponder();
            log?.Info($"[actions] (mac) cleared first responder before {hotkeyText}: {cleared}");
        }
        catch (Exception ex)
        {
            log?.Warn($"UI automation (Mac): clear-first-responder failed: {ex.GetType().Name}: {ex.Message}");
        }

        var modParts = new List<string>();
        if (modifiers.HasFlag(Modifiers.Ctrl))  modParts.Add("control down");
        if (modifiers.HasFlag(Modifiers.Shift)) modParts.Add("shift down");
        if (modifiers.HasFlag(Modifiers.Alt))   modParts.Add("option down");
        var usingClause = modParts.Count > 0 ? $" using {{{string.Join(", ", modParts)}}}" : string.Empty;

        int pid = Environment.ProcessId;
        var script = new StringBuilder()
            .AppendLine("tell application \"System Events\"")
            .AppendLine($"    set sp to first application process whose unix id is {pid}")
            .AppendLine("    set frontmost of sp to true")
            .AppendLine($"    key code {macKeyCode}{usingClause}")
            .Append("end tell")
            .ToString();

        try
        {
            var psi = new ProcessStartInfo("/usr/bin/osascript")
            {
                RedirectStandardError  = true,
                RedirectStandardOutput = true,
                UseShellExecute        = false,
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add(script);

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Process.Start returned null");

            if (!proc.WaitForExit(2000))
            {
                try { proc.Kill(); } catch { /* best-effort */ }
                lastFailureReason = "macOS keystroke automation timed out (osascript hung). Try again; if this persists, report it.";
                log?.Warn($"UI automation (Mac): osascript timed out for {hotkeyText}");
                return false;
            }

            string stderr = proc.StandardError.ReadToEnd().Trim();
            if (proc.ExitCode != 0)
            {
                if (stderr.Contains("-1719", StringComparison.Ordinal) ||
                    stderr.Contains("not allowed assistive access", StringComparison.OrdinalIgnoreCase))
                {
                    lastFailureReason =
                        "macOS Accessibility permission not granted to Studio Pro. " +
                        "Open System Settings → Privacy & Security → Accessibility, " +
                        "enable Studio Pro (add it with the + button if it isn't listed), " +
                        "then restart Studio Pro and retry. " +
                        "This is a one-time setup needed for the action bridge to drive the IDE.";
                }
                else
                {
                    lastFailureReason =
                        $"macOS keystroke automation failed (osascript exit={proc.ExitCode}): {stderr}";
                }
                log?.Warn($"UI automation (Mac): {lastFailureReason}");
                return false;
            }

            lastFailureReason = null;
            log?.Info($"[actions] sent {hotkeyText} to Studio Pro (macOS osascript, key code {macKeyCode})");
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 2 /* ENOENT */)
        {
            lastFailureReason = "macOS keystroke automation requires /usr/bin/osascript, which was not found.";
            log?.Warn($"UI automation (Mac): {lastFailureReason}");
            return false;
        }
        catch (Exception ex)
        {
            lastFailureReason = $"macOS keystroke automation failed: {ex.GetType().Name}: {ex.Message}";
            log?.Warn($"UI automation (Mac): {lastFailureReason}");
            return false;
        }
    }

    [Flags]
    private enum Modifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

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
        if (key.Length >= 2 && (key[0] == 'F' || key[0] == 'f') &&
            int.TryParse(key.AsSpan(1), out var n) && n is >= 1 and <= 24)
        {
            vk = (ushort)(VK_F1 + (n - 1));
            return true;
        }
        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c >= 'A' && c <= 'Z') { vk = (ushort)c; return true; }
        }
        return false;
    }

    private static bool TryWinVkToMacKeyCode(ushort vk, out int mac)
    {
        switch (vk)
        {
            case 0x70: mac = 122; return true; // F1
            case 0x71: mac = 120; return true; // F2
            case 0x72: mac = 99;  return true; // F3
            case 0x73: mac = 118; return true; // F4
            case 0x74: mac = 96;  return true; // F5
            case 0x75: mac = 97;  return true; // F6
            case 0x76: mac = 98;  return true; // F7
            case 0x77: mac = 100; return true; // F8
            case 0x78: mac = 101; return true; // F9
            case 0x79: mac = 109; return true; // F10
            case 0x7A: mac = 103; return true; // F11
            case 0x7B: mac = 111; return true; // F12
        }
        if (vk is >= 0x41 and <= 0x5A)
        {
            int[] letterCodes = { 0, 11, 8, 2, 14, 3, 5, 4, 34, 38, 40, 37, 46, 45, 31, 35, 12, 15, 1, 17, 32, 9, 13, 7, 16, 6 };
            mac = letterCodes[vk - 0x41];
            return true;
        }
        mac = 0;
        return false;
    }

    private static bool ClearWebViewFirstResponder()
    {
        var instance = Eto.Forms.Application.Instance;
        if (instance is null)
            return MacAppKit.ClearFirstResponderOnKeyWindow();

        bool result = false;
        instance.Invoke(() => { result = MacAppKit.ClearFirstResponderOnKeyWindow(); });
        return result;
    }

    private static class MacAppKit
    {
        private const string LibObjc = "/usr/lib/libobjc.A.dylib";

        [DllImport(LibObjc)]
        private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(LibObjc)]
        private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_id(IntPtr receiver, IntPtr selector);

        [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
        private static extern byte objc_msgSend_bool_arg(IntPtr receiver, IntPtr selector, IntPtr arg);

        public static bool ClearFirstResponderOnKeyWindow()
        {
            var nsAppCls = objc_getClass("NSApplication");
            if (nsAppCls == IntPtr.Zero) return false;

            var nsApp = objc_msgSend_id(nsAppCls, sel_registerName("sharedApplication"));
            if (nsApp == IntPtr.Zero) return false;

            var win = objc_msgSend_id(nsApp, sel_registerName("keyWindow"));
            if (win == IntPtr.Zero)
                win = objc_msgSend_id(nsApp, sel_registerName("mainWindow"));
            if (win == IntPtr.Zero) return false;

            return objc_msgSend_bool_arg(win, sel_registerName("makeFirstResponder:"), IntPtr.Zero) != 0;
        }
    }
}
