using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class TerminalSessionManagerTests
{
    [Fact]
    public async Task CreateSession_ReturnsTabIdAndTitle_AndSessionAppearsInList()
    {
        var fake = new FakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        var (id, title) = await mgr.CreateSessionAsync("powershell.exe", Array.Empty<string>(), "C:\\X", 80, 24);

        id.Should().NotBeNullOrEmpty();
        title.Should().Be("Pwsh - 1");
        var list = mgr.ListSessions();
        list.Should().HaveCount(1);
        list[0].TabId.Should().Be(id);
        list[0].Title.Should().Be("Pwsh - 1");
        list[0].ShellPath.Should().Be("powershell.exe");
        list[0].Cwd.Should().Be("C:\\X");
        list[0].Alive.Should().BeTrue();
    }

    [Fact]
    public async Task TabTitles_UseLowercaseShellLabel_AndIncrementOrdinal()
    {
        var fake = new FakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        var (_, t1) = await mgr.CreateSessionAsync("powershell.exe", Array.Empty<string>(), "C:\\X", 80, 24);
        var (_, t2) = await mgr.CreateSessionAsync("bash.exe", Array.Empty<string>(), "C:\\X", 80, 24);
        var (_, t3) = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\X", 80, 24);
        var (_, t4) = await mgr.CreateSessionAsync("pwsh.exe", Array.Empty<string>(), "C:\\X", 80, 24);

        t1.Should().Be("Pwsh - 1");
        t2.Should().Be("Bash - 2");
        t3.Should().Be("Cmd - 3");
        t4.Should().Be("Pwsh - 4");  // pwsh.exe and powershell.exe both canonicalize to "Pwsh"
    }

    [Fact]
    public async Task TabOrdinal_GapFills_AfterClose()
    {
        var fake = new FakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        var (id1, t1) = await mgr.CreateSessionAsync("powershell.exe", Array.Empty<string>(), "C:\\X", 80, 24);
        var (id2, t2) = await mgr.CreateSessionAsync("powershell.exe", Array.Empty<string>(), "C:\\X", 80, 24);
        var (_,   t3) = await mgr.CreateSessionAsync("powershell.exe", Array.Empty<string>(), "C:\\X", 80, 24);

        t1.Should().Be("Pwsh - 1");
        t2.Should().Be("Pwsh - 2");
        t3.Should().Be("Pwsh - 3");

        // Close #2 → next new tab should fill the #2 slot, not become #4.
        mgr.Close(id2);
        var (_, t4) = await mgr.CreateSessionAsync("powershell.exe", Array.Empty<string>(), "C:\\X", 80, 24);
        t4.Should().Be("Pwsh - 2");

        // Close all three → next tab is #1 again.
        mgr.Close(id1);
        mgr.Close(mgr.ListSessions()[0].TabId);
        mgr.Close(mgr.ListSessions()[0].TabId);
        var (_, t5) = await mgr.CreateSessionAsync("powershell.exe", Array.Empty<string>(), "C:\\X", 80, 24);
        t5.Should().Be("Pwsh - 1");
    }

    [Fact]
    public async Task Close_RemovesSessionFromList()
    {
        var fake = new FakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        var (id, _) = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\Y", 80, 24);

        mgr.Close(id);

        mgr.ListSessions().Should().BeEmpty();
        fake.LastSession.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Write_ForwardsBytesToPty()
    {
        var fake = new FakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        var (id, _) = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\X", 80, 24);

        mgr.Write(id, new byte[] { 0x68, 0x69 });  // "hi"

        fake.LastSession.WrittenBytes.Should().Equal(0x68, 0x69);
    }

    [Fact]
    public async Task Resize_ForwardsToPty()
    {
        var fake = new FakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        var (id, _) = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\X", 80, 24);

        mgr.Resize(id, 120, 40);

        fake.LastSession.Cols.Should().Be(120);
        fake.LastSession.Rows.Should().Be(40);
    }

    [Fact]
    public async Task DisposeAll_KillsAllPtys()
    {
        var fake = new FakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\X", 80, 24);
        await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\X", 80, 24);

        mgr.DisposeAll();

        mgr.ListSessions().Should().BeEmpty();
        fake.AllSessions.Should().AllSatisfy(s => s.Disposed.Should().BeTrue());
    }

    [Fact]
    public void Write_UnknownTabId_DoesNotThrow()
    {
        var mgr = new TerminalSessionManager(new FakePtyFactory());
        Action act = () => mgr.Write("nonexistent", new byte[] { 1 });
        act.Should().NotThrow();
    }
}

internal sealed class FakePtyFactory : IPtyFactory
{
    public List<FakePtySession> AllSessions { get; } = new();
    public FakePtySession LastSession => AllSessions[^1];

    public Task<IPtySession> SpawnAsync(
        string shellPath, string[] args, string cwd, int cols, int rows,
        IDictionary<string,string> environment, CancellationToken ct)
    {
        var s = new FakePtySession(cols, rows);
        AllSessions.Add(s);
        return Task.FromResult<IPtySession>(s);
    }
}

internal sealed class FakePtySession : IPtySession
{
    public int Pid => 1234;
    public int? ExitCode { get; private set; }
    public event EventHandler<int?>? Exited;
    public List<byte> WrittenBytes { get; } = new();
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public bool Disposed { get; private set; }

    public FakePtySession(int cols, int rows) { Cols = cols; Rows = rows; }

    public Task WriteAsync(byte[] data, CancellationToken ct)
    {
        WrittenBytes.AddRange(data);
        return Task.CompletedTask;
    }

    public async Task<int> ReadAsync(byte[] buffer, CancellationToken ct)
    {
        // Block forever (until disposed) — manager's read loop will exit on Dispose
        try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
        return 0;
    }

    public void Resize(int cols, int rows) { Cols = cols; Rows = rows; }
    public void Dispose() { Disposed = true; ExitCode = 0; Exited?.Invoke(this, 0); }
    public void RaiseExited(int? code) { ExitCode = code; Exited?.Invoke(this, code); }
}
