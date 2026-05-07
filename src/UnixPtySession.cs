// POSIX-backed PTY session for macOS (and Linux). Mirrors the structure of
// ConPtySession in PtySession.cs — same IPtySession surface, same dispose
// discipline, same FileStream-wrapping-a-SafeFileHandle pattern.
//
// Native surface (libSystem on macOS — libc, libutil, and libpthread are all
// unified into /usr/lib/libSystem.B.dylib on Darwin):
//
//   openpty(out master, out slave, NULL, NULL, &Winsize)        -- util.h
//   posix_spawn_file_actions_init / addclose / adddup2          -- spawn.h
//   posix_spawnattr_init / setflags(POSIX_SPAWN_SETSID)         -- spawn.h
//   posix_spawnp(out pid, file, file_actions, attrs, argv, envp)
//   ioctl(master, TIOCSWINSZ, &Winsize)                         -- sys/ioctl.h
//   waitpid(pid, &status, WNOHANG)                              -- sys/wait.h
//   kill(pid, SIGHUP / SIGKILL)                                 -- signal.h
//   close(fd)                                                   -- unistd.h
//
// Constants are macOS-specific where they differ from Linux:
//   TIOCSWINSZ        = 0x80087467  (macOS) — Linux is 0x5414
//   POSIX_SPAWN_SETSID = 0x0400     (macOS 10.15+)
//
// Exit notification is a 250 ms waitpid(WNOHANG) poll on a background Task —
// simpler than kqueue/EVFILT_PROC and adequate for terminal-spawn frequencies.
//
// EOF detection on the master fd is via IOException catch — when the slave
// side closes, macOS returns EIO on master reads instead of the 0-byte EOF
// that Linux would return. Treating EIO as EOF preserves the IPtySession
// contract ("ReadAsync returns 0 when the PTY has disconnected").

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Terminal;

internal sealed class UnixPtySession : IPtySession, IDisposable
{
    private const string LibSystem = "libSystem.dylib";

    // macOS values from <sys/ttycom.h>, <spawn.h>, <sys/wait.h>, <signal.h>.
    private const uint TIOCSWINSZ = 0x80087467;
    private const short POSIX_SPAWN_SETSID = 0x0400;
    private const int WNOHANG = 1;
    private const int SIGHUP = 1;
    private const int SIGKILL = 9;

    private int masterFd;
    private readonly int pid;
    private readonly SafeFileHandle masterHandle;

    private int? exitCode;
    private readonly CancellationTokenSource exitPollCts = new();
    private readonly Task exitPollTask;
    private int disposed;

    public int Pid => pid;
    public int? ExitCode => exitCode;
    public event EventHandler<int?>? Exited;

    private UnixPtySession(int masterFd, int pid)
    {
        this.masterFd = masterFd;
        this.pid = pid;
        this.masterHandle = new SafeFileHandle((IntPtr)masterFd, ownsHandle: true);
        this.exitPollTask = Task.Run(PollForExitAsync);
    }

    public static UnixPtySession Spawn(
        string shell, string[] args, string cwd, int cols, int rows,
        IDictionary<string, string> environment)
    {
        var ws = new Winsize { ws_row = (ushort)rows, ws_col = (ushort)cols };

        if (openpty(out int master, out int slave, IntPtr.Zero, IntPtr.Zero, ref ws) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "openpty");

        IntPtr fileActions = IntPtr.Zero;
        IntPtr spawnAttrs = IntPtr.Zero;
        IntPtr argvBlock = IntPtr.Zero;
        IntPtr envpBlock = IntPtr.Zero;
        int argvCount = 0, envpCount = 0;
        bool slaveStillOpen = true;

        try
        {
            int rc;

            if ((rc = posix_spawn_file_actions_init(ref fileActions)) != 0)
                throw new Win32Exception(rc, "posix_spawn_file_actions_init");

            // Wire the slave fd to the child's stdin/stdout/stderr, then close
            // both the original slave handle and our master handle inside the
            // child. Order matters — file_actions are applied in the order added.
            if ((rc = posix_spawn_file_actions_adddup2(ref fileActions, slave, 0)) != 0)
                throw new Win32Exception(rc, "posix_spawn_file_actions_adddup2(stdin)");
            if ((rc = posix_spawn_file_actions_adddup2(ref fileActions, slave, 1)) != 0)
                throw new Win32Exception(rc, "posix_spawn_file_actions_adddup2(stdout)");
            if ((rc = posix_spawn_file_actions_adddup2(ref fileActions, slave, 2)) != 0)
                throw new Win32Exception(rc, "posix_spawn_file_actions_adddup2(stderr)");
            if ((rc = posix_spawn_file_actions_addclose(ref fileActions, slave)) != 0)
                throw new Win32Exception(rc, "posix_spawn_file_actions_addclose(slave)");
            if ((rc = posix_spawn_file_actions_addclose(ref fileActions, master)) != 0)
                throw new Win32Exception(rc, "posix_spawn_file_actions_addclose(master)");

            if ((rc = posix_spawnattr_init(ref spawnAttrs)) != 0)
                throw new Win32Exception(rc, "posix_spawnattr_init");
            // POSIX_SPAWN_SETSID gives the child its own session so it owns the
            // tty as a controlling terminal. Required for proper signal delivery
            // (Ctrl+C, SIGHUP on disconnect, etc.).
            if ((rc = posix_spawnattr_setflags(ref spawnAttrs, POSIX_SPAWN_SETSID)) != 0)
                throw new Win32Exception(rc, "posix_spawnattr_setflags");

            // argv: shell as argv[0], then args, then NULL.
            var argvManaged = new string?[1 + args.Length + 1];
            argvManaged[0] = shell;
            for (int i = 0; i < args.Length; i++) argvManaged[i + 1] = args[i];
            argvManaged[^1] = null;
            argvBlock = AllocStringArray(argvManaged);
            argvCount = argvManaged.Length;

            // envp: KEY=VALUE strings, then NULL. posix_spawnp does NOT inherit
            // our environment when envp is provided, so we must pass everything
            // we want the child to see.
            var envpManaged = new string?[environment.Count + 1];
            int j = 0;
            foreach (var kv in environment)
                envpManaged[j++] = $"{kv.Key}={kv.Value}";
            envpManaged[^1] = null;
            envpBlock = AllocStringArray(envpManaged);
            envpCount = envpManaged.Length;

            // chdir into the requested cwd before exec. Without this the child
            // inherits Studio Pro's cwd (the .app bundle on Mac), which makes
            // pwd, ls, and Claude's project-root detection all wrong. Best-
            // effort: if the cwd doesn't exist or addchdir_np fails for any
            // reason, fall through and let the child start in the inherited
            // cwd (legacy behavior — surprising, but better than failing the
            // spawn outright).
            if (!string.IsNullOrEmpty(cwd))
            {
                try
                {
                    var rcChdir = posix_spawn_file_actions_addchdir_np(ref fileActions, cwd);
                    if (rcChdir != 0)
                        throw new Win32Exception(rcChdir, $"posix_spawn_file_actions_addchdir_np({cwd})");
                }
                catch (DllNotFoundException) { /* macOS pre-10.15 — best-effort */ }
                catch (EntryPointNotFoundException) { /* same */ }
            }

            // posix_spawnp does the equivalent of fork+exec atomically. PATH
            // search happens because we use the `p` variant.

            if ((rc = posix_spawnp(out int childPid, shell, ref fileActions, ref spawnAttrs, argvBlock, envpBlock)) != 0)
                throw new Win32Exception(rc, $"posix_spawnp({shell})");

            // Parent must close its copy of the slave fd. Otherwise reads on
            // master never see EOF — even after the child exits, the kernel
            // still sees an open writer (us) on the slave side.
            if (close(slave) == 0) slaveStillOpen = false;

            return new UnixPtySession(master, childPid);
        }
        catch
        {
            if (slaveStillOpen) try { close(slave); } catch { /* best-effort */ }
            try { close(master); } catch { /* best-effort */ }
            throw;
        }
        finally
        {
            if (envpBlock != IntPtr.Zero) FreeStringArray(envpBlock, envpCount);
            if (argvBlock != IntPtr.Zero) FreeStringArray(argvBlock, argvCount);
            if (spawnAttrs != IntPtr.Zero) posix_spawnattr_destroy(ref spawnAttrs);
            if (fileActions != IntPtr.Zero) posix_spawn_file_actions_destroy(ref fileActions);
        }
    }

    public Task WriteAsync(byte[] data, CancellationToken ct) =>
        // Direct write() syscall — bypasses FileStream's buffer entirely.
        // We tried FileStream(bufferSize:4096) with explicit FlushAsync and
        // writes still didn't reach the slave; rather than fight the wrapper,
        // call the kernel directly. write(2) on a PTY master is non-blocking
        // for terminal-sized chunks (kernel TTY buffer is large), so doing
        // it inline (no Task.Run) is fine.
        Task.Run(() =>
        {
            int total = 0;
            while (total < data.Length)
            {
                int n;
                unsafe
                {
                    fixed (byte* p = &data[total])
                        n = (int)write(masterFd, (IntPtr)p, (UIntPtr)(data.Length - total));
                }
                if (n < 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "write(pty master)");
                if (n == 0) break;
                total += n;
            }
        }, ct);

    public Task<int> ReadAsync(byte[] buffer, CancellationToken ct) =>
        // Direct read() syscall on a thread pool thread — blocks until data
        // arrives or the slave-side closes (EIO on Darwin → treat as EOF).
        Task.Run(() =>
        {
            int n;
            unsafe
            {
                fixed (byte* p = &buffer[0])
                    n = (int)read(masterFd, (IntPtr)p, (UIntPtr)buffer.Length);
            }
            if (n < 0)
            {
                int err = Marshal.GetLastPInvokeError();
                // EIO (5) on a PTY master after the slave side closes means
                // "no more data" — surface as EOF per the IPtySession contract.
                if (err == 5 /* EIO */) return 0;
                throw new Win32Exception(err, "read(pty master)");
            }
            return n;
        }, ct);

    public void Resize(int cols, int rows)
    {
        if (masterFd == 0) return;
        var ws = new Winsize { ws_row = (ushort)rows, ws_col = (ushort)cols };
        if (ioctl(masterFd, TIOCSWINSZ, ref ws) != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "ioctl(TIOCSWINSZ)");
    }

    private async Task PollForExitAsync()
    {
        var ct = exitPollCts.Token;
        while (!ct.IsCancellationRequested)
        {
            int wpid = waitpid(pid, out int status, WNOHANG);
            if (wpid == pid)
            {
                // Child exited. Decode status the same way the WIFEXITED /
                // WEXITSTATUS macros do.
                if ((status & 0x7f) == 0)
                    exitCode = (status >> 8) & 0xff;
                else
                    exitCode = 128 + (status & 0x7f); // signaled — match shell convention
                Exited?.Invoke(this, exitCode);
                return;
            }
            if (wpid < 0) return; // ECHILD or similar — stop polling

            try { await Task.Delay(250, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;

        // Order matters on macOS:
        //   1. SIGHUP the child so it tears down its end of the PTY.
        //   2. Brief wait for the child to exit; SIGKILL if it doesn't.
        //   3. Close the master fd — only AFTER the child is gone, so the
        //      ReadAsync syscall has already returned EIO. Closing earlier
        //      hangs close(2) forever (verified 2026-05-07: UI thread stuck
        //      in __close_nocancel, Studio Pro frozen until force-killed).
        //   4. As a backstop, run the close on a background task with a
        //      timeout — even with the right order, kernel cleanup of a
        //      pending read on the master fd can occasionally race.
        //   5. Cancel waitpid polling.
        //
        // Every step is wrapped — Dispose can run during ALC teardown when
        // P/Invokes may throw BadImageFormatException because the native shim
        // has already been unloaded. We must never let that escape into
        // Mendix's host (it surfaces as an error popup).

        try { kill(pid, SIGHUP); } catch { /* best-effort */ }

        try
        {
            var deadline = Environment.TickCount + 200;
            while (Environment.TickCount < deadline)
            {
                int wpid;
                try { wpid = waitpid(pid, out _, WNOHANG); }
                catch { wpid = -1; }
                if (wpid != 0) break;
                Thread.Sleep(20);
            }
            int finalWpid;
            try { finalWpid = waitpid(pid, out _, WNOHANG); }
            catch { finalWpid = -1; }
            if (finalWpid == 0)
                try { kill(pid, SIGKILL); } catch { /* best-effort */ }
        }
        catch { /* best-effort */ }

        // Bound the close() syscall — if the kernel races on cleanup we'd
        // rather leak the fd than freeze the UI thread for minutes.
        try
        {
            var closeTask = Task.Run(() =>
            {
                try { masterHandle.Dispose(); } catch { /* best-effort */ }
            });
            closeTask.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch { /* best-effort */ }
        masterFd = 0;

        try { exitPollCts.Cancel(); } catch { /* best-effort */ }
        try { exitPollCts.Dispose(); } catch { /* best-effort */ }
    }

    // ---- libc string-array marshaling --------------------------------------
    //
    // posix_spawnp expects char** for argv and envp — null-terminated arrays of
    // null-terminated UTF-8 strings. Marshal.StringToHGlobalAnsi is locale-
    // dependent; doing UTF-8 explicitly is correct on macOS regardless of
    // LANG/LC_ALL.

    private static IntPtr AllocStringArray(string?[] strings)
    {
        var ptr = Marshal.AllocHGlobal(IntPtr.Size * strings.Length);
        for (int i = 0; i < strings.Length; i++)
        {
            IntPtr s = strings[i] is null ? IntPtr.Zero : AllocUtf8(strings[i]!);
            Marshal.WriteIntPtr(ptr, i * IntPtr.Size, s);
        }
        return ptr;
    }

    private static void FreeStringArray(IntPtr ptr, int count)
    {
        for (int i = 0; i < count; i++)
        {
            IntPtr s = Marshal.ReadIntPtr(ptr, i * IntPtr.Size);
            if (s != IntPtr.Zero) Marshal.FreeHGlobal(s);
        }
        Marshal.FreeHGlobal(ptr);
    }

    private static IntPtr AllocUtf8(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, ptr, bytes.Length);
        Marshal.WriteByte(ptr, bytes.Length, 0);
        return ptr;
    }

    // ---- libSystem P/Invoke ------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct Winsize
    {
        public ushort ws_row;
        public ushort ws_col;
        public ushort ws_xpixel;
        public ushort ws_ypixel;
    }

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int openpty(out int amaster, out int aslave, IntPtr name, IntPtr termp, ref Winsize ws);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int close(int fd);

    [DllImport(LibSystem, SetLastError = true, EntryPoint = "read")]
    private static extern nint read(int fd, IntPtr buf, UIntPtr count);

    [DllImport(LibSystem, SetLastError = true, EntryPoint = "write")]
    private static extern nint write(int fd, IntPtr buf, UIntPtr count);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int ioctl(int fd, uint request, ref Winsize ws);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int kill(int pid, int sig);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    // posix_spawn_file_actions_t and posix_spawnattr_t are typedef'd to void*
    // on Apple — so a single IntPtr by-ref handle is the correct ABI.
    // (On Linux they're opaque struct types; this file only targets Darwin.)
    [DllImport(LibSystem)]
    private static extern int posix_spawn_file_actions_init(ref IntPtr fileActions);

    [DllImport(LibSystem)]
    private static extern int posix_spawn_file_actions_destroy(ref IntPtr fileActions);

    [DllImport(LibSystem)]
    private static extern int posix_spawn_file_actions_addclose(ref IntPtr fileActions, int fildes);

    [DllImport(LibSystem)]
    private static extern int posix_spawn_file_actions_adddup2(ref IntPtr fileActions, int fildes, int newfildes);

    // macOS 10.15+: chdir into the spawned child before exec. Without this
    // the child inherits the parent process's cwd, which on Studio Pro is the
    // .app bundle (/Applications/Mendix Studio Pro X.Y.Z.app) — causing
    // shells to start in the wrong directory and Claude/Codex to register
    // the .app bundle as their "project root".
    [DllImport(LibSystem, EntryPoint = "posix_spawn_file_actions_addchdir_np")]
    private static extern int posix_spawn_file_actions_addchdir_np(ref IntPtr fileActions, [MarshalAs(UnmanagedType.LPUTF8Str)] string path);

    [DllImport(LibSystem)]
    private static extern int posix_spawnattr_init(ref IntPtr attrs);

    [DllImport(LibSystem)]
    private static extern int posix_spawnattr_destroy(ref IntPtr attrs);

    [DllImport(LibSystem)]
    private static extern int posix_spawnattr_setflags(ref IntPtr attrs, short flags);

    [DllImport(LibSystem)]
    private static extern int posix_spawnp(
        out int pid,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string file,
        ref IntPtr fileActions, ref IntPtr attrs,
        IntPtr argv, IntPtr envp);
}
