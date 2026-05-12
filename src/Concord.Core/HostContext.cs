namespace Terminal;

public static class HostContext
{
    private static TargetMode _targetMode = TargetMode.Uninitialized;
    private static bool _initialized;
    private static readonly object _gate = new();

    public static TargetMode TargetMode
    {
        get { lock (_gate) return _targetMode; }
    }

    public static void Initialize(TargetMode mode)
    {
        lock (_gate)
        {
            if (_initialized)
                throw new InvalidOperationException(
                    "HostContext.Initialize was called twice. Each host DLL must call it exactly once at MEF activation.");
            if (mode == TargetMode.Uninitialized)
                throw new ArgumentException("TargetMode.Uninitialized is not a valid initialization value.", nameof(mode));
            _targetMode = mode;
            _initialized = true;
        }
    }

    // Test-only; gated by InternalsVisibleTo.
    internal static void Reset()
    {
        lock (_gate)
        {
            _targetMode = TargetMode.Uninitialized;
            _initialized = false;
        }
    }
}
