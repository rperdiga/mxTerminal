namespace Terminal;

public interface IStudioProUiAutomation
{
    /// <summary>Send F5 (or whatever the configured run hotkey is) to the Studio Pro main window.</summary>
    /// <returns>true if a window handle was found and the message was posted; false otherwise.</returns>
    bool TriggerRun();

    /// <summary>Send Shift+F5 (or whatever the configured stop hotkey is).</summary>
    bool TriggerStop();

    /// <summary>Send the configured refresh-from-disk hotkey (default F4).</summary>
    bool TriggerRefreshFromDisk();
}
