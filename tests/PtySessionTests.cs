using FluentAssertions;
using MxStudioProTerminal;
using System.Text;
using Xunit;

namespace MxStudioProTerminal.Tests;

public class PtySessionTests
{
    [Fact]
    public async Task Spawn_CmdEcho_ProducesExpectedOutput()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var factory = new PtyNetFactory();
        await using var ctx = TestContext.Create();
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            env[(string)e.Key] = (string)(e.Value ?? "");

        var session = await factory.SpawnAsync(
            shellPath: "cmd.exe",
            args: new[] { "/c", "echo hello-from-pty" },
            cwd: Environment.CurrentDirectory,
            cols: 80, rows: 24,
            environment: env,
            ct: ctx.Token);

        var output = await ReadAllAsync(session, ctx.Token);
        Encoding.UTF8.GetString(output).Should().Contain("hello-from-pty");
    }

    [Fact]
    public async Task Spawn_InvalidExecutable_Throws()
    {
        if (!OperatingSystem.IsWindows()) return;

        var factory = new PtyNetFactory();
        await using var ctx = TestContext.Create();

        Func<Task> act = async () => await factory.SpawnAsync(
            shellPath: "definitely-not-a-real-program-xyz.exe",
            args: Array.Empty<string>(),
            cwd: Environment.CurrentDirectory,
            cols: 80, rows: 24,
            environment: new Dictionary<string, string>(),
            ct: ctx.Token);

        await act.Should().ThrowAsync<Exception>();
    }

    private static async Task<byte[]> ReadAllAsync(IPtySession session, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var buf = new byte[4096];
        var readsWithoutData = 0;
        while (readsWithoutData < 5 && !ct.IsCancellationRequested)
        {
            using var readCt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCt.CancelAfter(TimeSpan.FromMilliseconds(500));
            try
            {
                var n = await session.ReadAsync(buf, readCt.Token);
                if (n <= 0) break;
                ms.Write(buf, 0, n);
                readsWithoutData = 0;
            }
            catch (OperationCanceledException) { readsWithoutData++; }
        }
        return ms.ToArray();
    }

    private sealed class TestContext : IAsyncDisposable
    {
        public CancellationToken Token { get; }
        private readonly CancellationTokenSource cts;
        private TestContext(CancellationTokenSource cts) { this.cts = cts; Token = cts.Token; }
        public static TestContext Create() { var c = new CancellationTokenSource(TimeSpan.FromSeconds(10)); return new(c); }
        public ValueTask DisposeAsync() { cts.Cancel(); cts.Dispose(); return ValueTask.CompletedTask; }
    }
}
