// ConPTY-backed PTY session. Replaces the prior WinPTY backend (Quick.PtyNet
// + Quick.PtyNet.WinPty), which screen-scraped a hidden conhost and didn't
// faithfully proxy DECSET/DECRST sequences (notably bracketed-paste mode
// `\x1b[?2004h`). ConPTY (Win10 1809+) is part of kernel32 — no sidecar
// binary to deploy, no AssemblyLoadContext resolver hack needed.
//
// Adapted from microsoft/vs-pty.net (MIT) — handle-leak fixes inline:
//   - bInheritHandles MUST be false; inheritance happens via the
//     ProcThreadAttributeList PSEUDOCONSOLE attribute, not the handle table
//   - Dispose order: ClosePseudoConsole first (signals child EOF), THEN
//     process/thread handles, THEN our pipe ends
//   - DeleteProcThreadAttributeList AND FreeHGlobal — vs-pty.net's code
//     does only the second and leaks the attribute list internals
//
// Cross-reference: docs/PASTE.md (paste pipeline rationale).

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Terminal;

public sealed class PtyNetFactory : IPtyFactory
{
    public Task<IPtySession> SpawnAsync(
        string shellPath, string[] args, string cwd, int cols, int rows,
        IDictionary<string, string> environment, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!ConPty.IsSupported)
                throw new PlatformNotSupportedException(
                    "ConPTY requires Windows 10 1809 (build 17763) or newer.");
            return Task.FromResult<IPtySession>(
                ConPtySession.Spawn(shellPath, args, cwd, cols, rows, environment));
        }
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            return Task.FromResult<IPtySession>(
                UnixPtySession.Spawn(shellPath, args, cwd, cols, rows, environment));
        throw new PlatformNotSupportedException(
            $"Unsupported OS: {RuntimeInformation.OSDescription}");
    }
}

internal static class ConPty
{
    // Guard the static initializer — on non-Windows, LoadLibraryW would throw
    // DllNotFoundException at type-load time, blowing up before any of our
    // OS-dispatch logic can kick in.
    public static readonly bool IsSupported = OperatingSystem.IsWindows() && Probe();

    private static bool Probe()
    {
        var k = LoadLibraryW("kernel32.dll");
        return k != IntPtr.Zero && GetProcAddress(k, "CreatePseudoConsole") != IntPtr.Zero;
    }

    internal const int STARTF_USESTDHANDLES = 0x00000100;
    internal const int CREATE_UNICODE_ENV = 0x00000400;
    internal const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    internal const uint INFINITE = 0xFFFFFFFF;

    // PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = ProcThreadAttributeValue(22, FALSE, TRUE, FALSE)
    //   = 22 | (1 << 17) = 22 | 0x20000.
    internal static readonly IntPtr ATTR_PSEUDOCONSOLE = (IntPtr)(22 | 0x20000);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Coord
    {
        public ushort X, Y;
        public Coord(int x, int y) { X = (ushort)x; Y = (ushort)y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 8, CharSet = CharSet.Unicode)]
    internal struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr LoadLibraryW([MarshalAs(UnmanagedType.LPWStr)] string name);
    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetProcAddress(IntPtr hModule, [MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int CreatePseudoConsole(Coord size, IntPtr hInput, IntPtr hOutput, uint flags, out IntPtr hPC);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern int ResizePseudoConsole(IntPtr hPC, Coord size);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern void ClosePseudoConsole(IntPtr hPC);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern bool CreateProcess(
        string? lpApplicationName, string lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
        bool bInheritHandles, int dwCreationFlags,
        IntPtr lpEnvironment, string lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);
}

internal sealed class ConPtySession : IPtySession, IDisposable
{
    private IntPtr hPC;
    private IntPtr hProcess, hThread;

    // Our ends of the duplex pipes. Wrapped in FileStream for async I/O.
    // Kept so Dispose can release them after ClosePseudoConsole.
    private readonly SafeFileHandle ourWriteToChild;   // we WRITE here -> child stdin
    private readonly SafeFileHandle ourReadFromChild;  // child stdout -> we READ here
    // The CON-side ends. ClosePseudoConsole consumes them; Dispose closes the
    // wrappers (no-op once consumed) but they MUST stay alive until then.
    private readonly SafeFileHandle conInRead;
    private readonly SafeFileHandle conOutWrite;

    private readonly FileStream writer;
    private readonly FileStream reader;

    private int? exitCode;
    private RegisteredWaitHandle? exitWait;
    private readonly EventWaitHandle exitEvent;

    public int Pid { get; }
    public int? ExitCode => exitCode;
    public event EventHandler<int?>? Exited;

    private ConPtySession(
        IntPtr hPC, IntPtr hProcess, IntPtr hThread, int pid,
        SafeFileHandle ourWriteToChild, SafeFileHandle ourReadFromChild,
        SafeFileHandle conInRead, SafeFileHandle conOutWrite)
    {
        this.hPC = hPC;
        this.hProcess = hProcess;
        this.hThread = hThread;
        this.Pid = pid;
        this.ourWriteToChild = ourWriteToChild;
        this.ourReadFromChild = ourReadFromChild;
        this.conInRead = conInRead;
        this.conOutWrite = conOutWrite;

        // CreatePipe returns synchronous (non-overlapped) handles, so FileStream
        // must be constructed with isAsync: false. ReadAsync/WriteAsync still
        // work — they just run on a thread-pool thread instead of true overlapped
        // I/O. PTY data volume is low enough that this is fine. Switching to
        // CreateNamedPipe would give us real async at the cost of more complex
        // setup code.
        writer = new FileStream(ourWriteToChild, FileAccess.Write, bufferSize: 4096, isAsync: false);
        reader = new FileStream(ourReadFromChild, FileAccess.Read, bufferSize: 4096, isAsync: false);

        // Wait on the process handle for exit; fire Exited once.
        exitEvent = new ManualResetEvent(false);
        // Wrap the process handle (don't own it — Dispose closes hProcess separately).
        exitEvent.SafeWaitHandle = new SafeWaitHandle(hProcess, ownsHandle: false);
        exitWait = ThreadPool.RegisterWaitForSingleObject(
            exitEvent, OnProcessExited, state: null,
            millisecondsTimeOutInterval: Timeout.Infinite, executeOnlyOnce: true);
    }

    public static ConPtySession Spawn(
        string shell, string[] args, string cwd, int cols, int rows,
        IDictionary<string, string> environment)
    {
        // Two anonymous pipes:
        //   pipe A: we WRITE to ourWriteToChild  -> child reads from conInRead
        //   pipe B: child writes to conOutWrite  -> we READ from ourReadFromChild
        if (!ConPty.CreatePipe(out var conInRead, out var ourWriteToChild, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (in)");
        if (!ConPty.CreatePipe(out var ourReadFromChild, out var conOutWrite, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (out)");

        int hr = ConPty.CreatePseudoConsole(
            new ConPty.Coord(cols, rows),
            conInRead.DangerousGetHandle(),
            conOutWrite.DangerousGetHandle(),
            flags: 0,
            out IntPtr hPC);
        if (hr != 0)
        {
            conInRead.Dispose();
            conOutWrite.Dispose();
            ourWriteToChild.Dispose();
            ourReadFromChild.Dispose();
            Marshal.ThrowExceptionForHR(hr);
        }

        // Build STARTUPINFOEX with the PSEUDOCONSOLE attribute pointing at hPC.
        var si = default(ConPty.STARTUPINFOEX);
        si.StartupInfo.cb = Marshal.SizeOf<ConPty.STARTUPINFOEX>();
        si.StartupInfo.dwFlags = ConPty.STARTF_USESTDHANDLES;

        IntPtr attrSize = IntPtr.Zero;
        // First call queries size (returns FALSE; that's expected).
        ConPty.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
        si.lpAttributeList = Marshal.AllocHGlobal(attrSize);

        try
        {
            if (!ConPty.InitializeProcThreadAttributeList(si.lpAttributeList, 1, 0, ref attrSize))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList");

            if (!ConPty.UpdateProcThreadAttribute(
                    si.lpAttributeList, 0,
                    ConPty.ATTR_PSEUDOCONSOLE, hPC,
                    (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute");

            var cmdLine = BuildCommandLine(shell, args);
            // Per CreateProcess docs, lpCommandLine is mutated by the OS; pass a writable copy.
            // Marshal.StringToHGlobalUni gives us a copy, but we use the string overload which
            // already round-trips through a managed copy — fine for our use.

            var envBlock = BuildEnvironmentBlock(environment);
            IntPtr lpEnv = Marshal.StringToHGlobalUni(envBlock);
            try
            {
                if (!ConPty.CreateProcess(
                        lpApplicationName: null,
                        lpCommandLine: cmdLine,
                        lpProcessAttributes: IntPtr.Zero,
                        lpThreadAttributes: IntPtr.Zero,
                        bInheritHandles: false, // pseudoconsole inheritance is via the attribute list
                        dwCreationFlags: ConPty.EXTENDED_STARTUPINFO_PRESENT | ConPty.CREATE_UNICODE_ENV,
                        lpEnvironment: lpEnv,
                        lpCurrentDirectory: cwd,
                        lpStartupInfo: ref si,
                        lpProcessInformation: out var pi))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcess");
                }

                return new ConPtySession(
                    hPC, pi.hProcess, pi.hThread, pi.dwProcessId,
                    ourWriteToChild, ourReadFromChild, conInRead, conOutWrite);
            }
            finally
            {
                Marshal.FreeHGlobal(lpEnv);
            }
        }
        finally
        {
            if (si.lpAttributeList != IntPtr.Zero)
            {
                ConPty.DeleteProcThreadAttributeList(si.lpAttributeList);
                Marshal.FreeHGlobal(si.lpAttributeList);
            }
        }
    }

    private static string BuildCommandLine(string app, string[] args)
    {
        var sb = new StringBuilder();
        AppendQuoted(sb, app);
        foreach (var a in args)
        {
            sb.Append(' ');
            AppendQuoted(sb, a);
        }
        return sb.ToString();

        static void AppendQuoted(StringBuilder sb, string s)
        {
            if (s.Length > 0 && !s.Contains(' ') && !s.Contains('\t') && !s.Contains('"'))
            {
                sb.Append(s);
                return;
            }
            sb.Append('"');
            sb.Append(s.Replace("\"", "\\\""));
            sb.Append('"');
        }
    }

    private static string BuildEnvironmentBlock(IDictionary<string, string> env)
    {
        // Sorted, NUL-separated, NUL-terminated KEY=VALUE pairs. Required by CreateProcess.
        var keys = env.Keys.ToArray();
        Array.Sort(keys, StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        foreach (var k in keys)
        {
            sb.Append(k);
            sb.Append('=');
            sb.Append(env[k]);
            sb.Append('\0');
        }
        sb.Append('\0');
        return sb.ToString();
    }

    public Task<int> ReadAsync(byte[] buffer, CancellationToken ct) =>
        reader.ReadAsync(buffer, 0, buffer.Length, ct);

    public Task WriteAsync(byte[] data, CancellationToken ct) =>
        writer.WriteAsync(data, 0, data.Length, ct);

    public void Resize(int cols, int rows)
    {
        if (hPC == IntPtr.Zero) return;
        int hr = ConPty.ResizePseudoConsole(hPC, new ConPty.Coord(cols, rows));
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
    }

    private void OnProcessExited(object? state, bool timedOut)
    {
        if (hProcess != IntPtr.Zero && ConPty.GetExitCodeProcess(hProcess, out var code))
        {
            exitCode = unchecked((int)code);
        }
        Exited?.Invoke(this, exitCode);
    }

    public void Dispose()
    {
        // Order matters per Microsoft's ConPTY docs:
        //   1. Tear down our managed streams (FileStream owns the SafeFileHandle).
        //   2. ClosePseudoConsole — signals EOF to child.
        //   3. Close process / thread handles.
        //   4. Dispose CON-side pipe ends (ClosePseudoConsole already consumed
        //      them; calling Dispose is safe — the SafeFileHandle becomes a no-op).
        try { writer.Dispose(); } catch { /* best-effort */ }
        try { reader.Dispose(); } catch { /* best-effort */ }

        if (hPC != IntPtr.Zero)
        {
            try { ConPty.ClosePseudoConsole(hPC); } catch { /* best-effort */ }
            hPC = IntPtr.Zero;
        }
        if (hThread != IntPtr.Zero)
        {
            ConPty.CloseHandle(hThread);
            hThread = IntPtr.Zero;
        }
        if (hProcess != IntPtr.Zero)
        {
            ConPty.CloseHandle(hProcess);
            hProcess = IntPtr.Zero;
        }

        try { conInRead.Dispose(); } catch { /* best-effort */ }
        try { conOutWrite.Dispose(); } catch { /* best-effort */ }

        if (exitWait is not null)
        {
            exitWait.Unregister(null);
            exitWait = null;
        }
        try { exitEvent.Dispose(); } catch { /* best-effort */ }
    }
}
