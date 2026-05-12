using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Terminal;
using Terminal.Interop;

namespace Concord.Host11x.Interop;

/// <summary>
/// Drives Studio Pro's own menu/hotkey handlers from the action bridge so
/// MCP tools like <c>run_app</c> / <c>stop_app</c> can fire F5 / Shift+F5
/// without needing focus on the IDE document tab.
/// <para>
/// Two backends:
/// <list type="bullet">
///   <item><b>Windows</b>: Win32 <c>PostMessage</c> to our own
///   <c>MainWindowHandle</c> — focus-independent.</item>
///   <item><b>macOS</b>: <c>osascript</c> driving System Events to
///   key-code our own process (identified by Unix PID, so the .app's
///   display name doesn't matter). Requires Accessibility permission for
///   Studio Pro the first time; a clear failure reason is surfaced when
///   that's missing.</item>
/// </list>
/// </para>
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
    private string? lastFailureReason;

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

        // Press modifiers (in canonical order: Ctrl, Shift, Alt) then the key, then release in reverse.
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

    /// <summary>
    /// Mac backend. Builds a one-shot AppleScript that targets our own
    /// process (so the .app display name doesn't matter — "Mendix Studio Pro
    /// 11.10.0 Beta.app" today, something else tomorrow) and runs it via
    /// <c>/usr/bin/osascript</c>. Failure modes mapped to user-actionable
    /// reasons:
    /// <list type="bullet">
    ///   <item>Accessibility permission missing → AppleEvent code -1719
    ///   ("not allowed assistive access")</item>
    ///   <item>osascript binary missing → caught Win32Exception</item>
    ///   <item>Process lookup failed → script error logged with stderr</item>
    /// </list>
    /// Hotkey is parsed via the existing <see cref="TryParse"/> (Win VK
    /// codes), then mapped to macOS HIToolbox key codes for the AppleScript
    /// "key code N" form.
    /// <para>
    /// Before invoking osascript, we clear the WebView's first-responder
    /// grip via AppKit's <c>[[NSApp keyWindow] makeFirstResponder:nil]</c>.
    /// Without this, the xterm.js terminal inside Concord's pane absorbs F5
    /// (and friends) before Studio Pro's accelerator handler sees them —
    /// observed as "<c>JS: onData len=5</c>" landing in the log a few ms
    /// after every "<c>sent F5</c>". Clearing first responder is a no-op for
    /// non-Concord WebViews, so it's safe to do unconditionally on every
    /// keystroke send. AppKit must be touched on the main thread; we marshal
    /// via Eto.Forms's <c>Application.Instance.Invoke</c> when available.
    /// </para>
    /// </summary>
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

        // Defocus the WebView so xterm.js doesn't swallow the keystroke.
        try
        {
            bool cleared = ClearWebViewFirstResponder();
            log?.Info($"[actions] (mac) cleared first responder before {hotkeyText}: {cleared}");
        }
        catch (Exception ex)
        {
            // Best-effort — fall through and try the keystroke anyway.
            log?.Warn($"UI automation (Mac): clear-first-responder failed: {ex.GetType().Name}: {ex.Message}");
        }

        // Build "using {control down, shift down, option down}" clause.
        var modParts = new List<string>();
        if (modifiers.HasFlag(Modifiers.Ctrl))  modParts.Add("control down");
        if (modifiers.HasFlag(Modifiers.Shift)) modParts.Add("shift down");
        if (modifiers.HasFlag(Modifiers.Alt))   modParts.Add("option down");
        var usingClause = modParts.Count > 0 ? $" using {{{string.Join(", ", modParts)}}}" : string.Empty;

        // Identify Studio Pro by our own Unix PID. System Events sends the
        // keystroke to whichever application is frontmost at the time the
        // statement runs; setting `frontmost` on our own process inside the
        // same `tell application "System Events"` block makes the subsequent
        // `key code` land on us.
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
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
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
                // -1719 = "not allowed assistive access" — the canonical signal
                //         that Accessibility permission has not been granted.
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
            lastFailureReason = "macOS keystroke automation requires /usr/bin/osascript, which was not found. This is a stock macOS binary — if it's missing your install is unusual.";
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

    /// <summary>
    /// Map a Windows virtual-key code (subset we accept in <see cref="TryParse"/>:
    /// F1-F12, A-Z) to the macOS HIToolbox key code used by AppleScript's
    /// <c>key code</c> primitive. Constants from
    /// <c>/System/Library/Frameworks/Carbon.framework/.../HIToolbox/Events.h</c>.
    /// Returns false for unmappable inputs (F13+, non-letter keys we don't
    /// support today).
    /// </summary>
    private static bool TryWinVkToMacKeyCode(ushort vk, out int mac)
    {
        // F1-F12
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
        // Letters A-Z (VK 0x41-0x5A) — macOS keycodes are NOT contiguous, so
        // explicit table.
        if (vk is >= 0x41 and <= 0x5A)
        {
            // Index by letter: A,B,C,D,E,F,G,H,I,J,K,L,M,N,O,P,Q,R,S,T,U,V,W,X,Y,Z
            int[] letterCodes = { 0, 11, 8, 2, 14, 3, 5, 4, 34, 38, 40, 37, 46, 45, 31, 35, 12, 15, 1, 17, 32, 9, 13, 7, 16, 6 };
            mac = letterCodes[vk - 0x41];
            return true;
        }
        mac = 0;
        return false;
    }

    // ---- AppKit first-responder reset (Mac only) --------------------------
    //
    // Concord's pane lives in a WKWebView, which becomes the NSWindow's first
    // responder when the user clicks into it. From that point on, any
    // keystroke routed through System Events lands in the WebView (and is
    // consumed by xterm.js) instead of reaching Studio Pro's main accelerator
    // handler. We fix this by calling `[[NSApp keyWindow] makeFirstResponder:nil]`
    // — equivalent to AppKit's "Stop Editing" — right before sending F5.
    //
    // Threading: AppKit insists on the main thread. The action HTTP server
    // runs on the thread pool, so we marshal through Eto.Forms's main-thread
    // dispatcher (Concord already depends on Eto). If Eto isn't initialized
    // for some reason, fall through and call directly — undefined behavior in
    // theory, but tends to work for these read-only NSApp accessors in
    // practice and the alternative is to skip the defocus entirely.

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
        // libobjc is part of /usr/lib on every Mac; no path needed for the
        // dlopen behavior P/Invoke uses internally.
        private const string LibObjc = "/usr/lib/libobjc.A.dylib";

        [DllImport(LibObjc)]
        private static extern IntPtr objc_getClass([MarshalAs(UnmanagedType.LPStr)] string name);

        [DllImport(LibObjc)]
        private static extern IntPtr sel_registerName([MarshalAs(UnmanagedType.LPStr)] string name);

        // [obj selector]  → returns id, no extra args
        [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_id(IntPtr receiver, IntPtr selector);

        // [obj selector:nilOrId]  → returns BOOL (1 byte on Darwin), one id arg
        [DllImport(LibObjc, EntryPoint = "objc_msgSend")]
        private static extern byte objc_msgSend_bool_arg(IntPtr receiver, IntPtr selector, IntPtr arg);

        /// <summary>
        /// Sends <c>[[NSApp keyWindow] makeFirstResponder:nil]</c>. Returns
        /// <c>true</c> if the call succeeded; <c>false</c> if any of the
        /// objc lookups failed or no key window exists. Safe to call on
        /// non-macOS — the DllImport will throw, caller catches.
        /// </summary>
        public static bool ClearFirstResponderOnKeyWindow()
        {
            var nsAppCls = objc_getClass("NSApplication");
            if (nsAppCls == IntPtr.Zero) return false;

            var nsApp = objc_msgSend_id(nsAppCls, sel_registerName("sharedApplication"));
            if (nsApp == IntPtr.Zero) return false;

            var win = objc_msgSend_id(nsApp, sel_registerName("keyWindow"));
            // keyWindow can be nil if the app isn't in front or no window has
            // key status; fall back to mainWindow (the document-level window).
            if (win == IntPtr.Zero)
                win = objc_msgSend_id(nsApp, sel_registerName("mainWindow"));
            if (win == IntPtr.Zero) return false;

            return objc_msgSend_bool_arg(win, sel_registerName("makeFirstResponder:"), IntPtr.Zero) != 0;
        }
    }
}
