using System.Diagnostics;
using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class TerminalSessionManagerStreamingTests
{
    // Polling helper — replaces fixed Task.Delay() in tests that wait on the
    // PTY read loop. Fixed delays flake under parallel xUnit load when the
    // threadpool is contended; polling reaches the steady state as soon as
    // it's reached and only times out when the read loop is genuinely stuck.
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000, int pollMs = 10)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return;
            await Task.Delay(pollMs);
        }
    }

    [Fact]
    public async Task PtyOutput_FiresOutputEvent_AfterCoalesceWindow()
    {
        var fake = new StreamingFakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        var received = new List<byte[]>();
        mgr.Output += (id, data) => received.Add(data);

        var (id, _) = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\", 80, 24);
        fake.LastSession.PushOutput(new byte[] { 0x41, 0x42 });
        fake.LastSession.PushOutput(new byte[] { 0x43 });

        // Output is coalesced on a ~16ms timer; poll until the flush fires.
        await WaitUntilAsync(() => received.Count >= 1 && received[0].Length >= 3);

        received.Should().HaveCount(1, "two pushes within the coalesce window become one event");
        received[0].Should().Equal(0x41, 0x42, 0x43);
    }

    [Fact]
    public async Task SnapshotBuffer_ReturnsAllOutputSeen()
    {
        var fake = new StreamingFakePtyFactory();
        var mgr = new TerminalSessionManager(fake, ringBufferBytes: 64);
        var (id, _) = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\", 80, 24);
        fake.LastSession.PushOutput(new byte[] { 1, 2, 3 });
        fake.LastSession.PushOutput(new byte[] { 4, 5 });

        await WaitUntilAsync(() => mgr.SnapshotBuffer(id).Length >= 5);

        mgr.SnapshotBuffer(id).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task SnapshotBuffer_TruncatesAtRingCapacity()
    {
        var fake = new StreamingFakePtyFactory();
        var mgr = new TerminalSessionManager(fake, ringBufferBytes: 4);
        var (id, _) = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\", 80, 24);
        fake.LastSession.PushOutput(new byte[] { 1, 2, 3, 4, 5, 6 });

        // Buffer caps at 4; once it hits 4 bytes AND the tail byte is 6, all
        // 6 pushed bytes have been processed through the ring.
        await WaitUntilAsync(() =>
        {
            var buf = mgr.SnapshotBuffer(id);
            return buf.Length == 4 && buf[3] == 6;
        });

        mgr.SnapshotBuffer(id).Should().Equal(3, 4, 5, 6);
    }

    [Fact]
    public async Task PtyExits_FiresExitedEvent_AndRemovesSession()
    {
        var fake = new StreamingFakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        int? capturedCode = null;
        string? capturedId = null;
        mgr.Exited += (id, code) => { capturedId = id; capturedCode = code; };

        var (id, _) = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\", 80, 24);
        fake.LastSession.RaiseExited(7);

        await WaitUntilAsync(() => capturedId != null);

        capturedId.Should().Be(id);
        capturedCode.Should().Be(7);
        mgr.ListSessions().Should().BeEmpty();
    }
}

internal sealed class StreamingFakePtyFactory : IPtyFactory
{
    public StreamingFakePtySession LastSession { get; private set; } = null!;

    public Task<IPtySession> SpawnAsync(
        string shellPath, string[] args, string cwd, int cols, int rows,
        IDictionary<string,string> environment, CancellationToken ct)
    {
        LastSession = new StreamingFakePtySession();
        return Task.FromResult<IPtySession>(LastSession);
    }
}

internal sealed class StreamingFakePtySession : IPtySession
{
    private readonly System.Threading.Channels.Channel<byte[]> queue =
        System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
    public int Pid => 4321;
    public int? ExitCode { get; private set; }
    public event EventHandler<int?>? Exited;

    public void PushOutput(byte[] data) => queue.Writer.TryWrite(data);
    public void RaiseExited(int? code) { ExitCode = code; queue.Writer.Complete(); Exited?.Invoke(this, code); }

    public async Task<int> ReadAsync(byte[] buffer, CancellationToken ct)
    {
        try
        {
            var chunk = await queue.Reader.ReadAsync(ct);
            var n = Math.Min(chunk.Length, buffer.Length);
            Array.Copy(chunk, buffer, n);
            return n;
        }
        catch (System.Threading.Channels.ChannelClosedException) { return 0; }
    }

    public Task WriteAsync(byte[] data, CancellationToken ct) => Task.CompletedTask;
    public void Resize(int cols, int rows) { }
    public void Dispose() { try { queue.Writer.Complete(); } catch { } }
}
