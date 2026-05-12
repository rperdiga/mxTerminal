namespace Terminal;

public interface IStudioProUiAutomation
{
    /// <summary>Send F5 (or whatever the configured run hotkey is) to the Studio Pro main window.</summary>
    /// <returns>true if the keystroke was dispatched; false otherwise. On false,
    /// <see cref="LastFailureReason"/> carries a user-actionable explanation.</returns>
    bool TriggerRun();

    /// <summary>Send Shift+F5 (or whatever the configured stop hotkey is).</summary>
    bool TriggerStop();

    /// <summary>Send the configured refresh-from-disk hotkey (default F4).</summary>
    bool TriggerRefreshFromDisk();

    /// <summary>Send Ctrl+S to save all unsaved changes in Studio Pro.</summary>
    bool TriggerSaveAll();

    /// <summary>
    /// User-actionable explanation of why the last <c>Trigger…</c> call failed,
    /// or <c>null</c> if the last call succeeded. Surfaced through the action
    /// server so MCP clients (Claude, Codex) can guide the user — e.g. "grant
    /// Accessibility permission" on macOS, "dismiss modal dialog" on Windows.
    /// </summary>
    string? LastFailureReason { get; }
}
