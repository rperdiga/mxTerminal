using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class TerminalSessionManagerWriteLockTests
{
    [Fact]
    public async Task ConcurrentWrites_AreSerializedPerSession()
    {
        // Concurrent Write() calls from chunked paste must not interleave on
        // the PTY writer. The semaphore in TerminalSessionManager.Write
        // serializes per-session; this test proves it by spawning 10 parallel
        // writes against a fake PTY whose WriteAsync is artificially slow,
        // and asserts the bytes arrive in invocation order rather than
        // racing.
        var fake = new RecordingFakePtyFactory(writeDelayMs: 5);
        var mgr = new TerminalSessionManager(fake);
        var (id, _) = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\", 80, 24);

        const int writers = 10;
        var tasks = Enumerable.Range(0, writers)
            .Select(i => mgr.Write(id, new[] { (byte)i }))
            .ToArray();
        await Task.WhenAll(tasks);

        // We can't assert ORDER between racing Task.Run launches (the test
        // would be flaky), but we CAN assert no calls overlapped — every
        // recorded WriteAsync invocation completed before the next began.
        fake.LastSession.WriteSpans.Should().HaveCount(writers);
        for (int i = 1; i < fake.LastSession.WriteSpans.Count; i++)
        {
            var prevEnd = fake.LastSession.WriteSpans[i - 1].End;
            var thisStart = fake.LastSession.WriteSpans[i].Start;
            thisStart.Should().BeOnOrAfter(prevEnd,
                "writes serialized by the per-session lock must not overlap");
        }
    }

    [Fact]
    public async Task DifferentSessions_WriteInParallel_NotSerialized()
    {
        // The per-session lock isolates tabs — writes to tab A must not
        // block writes to tab B. This is critical for multi-tab UX.
        var fake = new RecordingFakePtyFactory(writeDelayMs: 50);
        var mgr = new TerminalSessionManager(fake);
        var (idA, _) = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\", 80, 24);
        var sessionA = fake.LastSession;
        var (idB, _) = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\", 80, 24);
        var sessionB = fake.LastSession;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(
            mgr.Write(idA, new byte[] { 1 }),
            mgr.Write(idB, new byte[] { 2 })
        );
        sw.Stop();

        // Two parallel 50ms writes on independent sessions should complete
        // in ~50ms (parallel), not ~100ms (serialized). Allow generous
        // overhead for CI/Mac variance — anything under the serialized
        // floor of ~100ms still proves the sessions ran in parallel.
        sw.ElapsedMilliseconds.Should().BeLessThan(95,
            "writes to different sessions must not share a lock");
        sessionA.WriteSpans.Should().HaveCount(1);
        sessionB.WriteSpans.Should().HaveCount(1);
    }
}

internal sealed class RecordingFakePtyFactory : IPtyFactory
{
    private readonly int writeDelayMs;
    public RecordingFakePtySession LastSession { get; private set; } = null!;

    public RecordingFakePtyFactory(int writeDelayMs) => this.writeDelayMs = writeDelayMs;

    public Task<IPtySession> SpawnAsync(
        string shellPath, string[] args, string cwd, int cols, int rows,
        IDictionary<string, string> environment, CancellationToken ct)
    {
        LastSession = new RecordingFakePtySession(writeDelayMs);
        return Task.FromResult<IPtySession>(LastSession);
    }
}

internal sealed class RecordingFakePtySession : IPtySession
{
    private readonly int writeDelayMs;
    private readonly object gate = new();
    public List<WriteSpan> WriteSpans { get; } = new();
    public int Pid => 9999;
    public int? ExitCode => null;
    public event EventHandler<int?>? Exited;

    public RecordingFakePtySession(int writeDelayMs) => this.writeDelayMs = writeDelayMs;

    public Task<int> ReadAsync(byte[] buffer, CancellationToken ct)
    {
        // Block forever — this fake never produces output for these tests.
        var tcs = new TaskCompletionSource<int>();
        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task;
    }

    public async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        await Task.Delay(writeDelayMs, ct);
        var end = DateTime.UtcNow;
        lock (gate) WriteSpans.Add(new WriteSpan(start, end, data));
    }

    public void Resize(int cols, int rows) { }
    public void Dispose() => Exited?.Invoke(this, 0);
}

internal readonly record struct WriteSpan(DateTime Start, DateTime End, byte[] Data);
