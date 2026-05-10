using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Terminal.Maia;

public sealed class MaiaActions
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    private readonly MaiaRouter router;

    public MaiaActions(MaiaRouter router) { this.router = router; }

    public async Task<ActionResult> SendAsync(string prompt, string? sentinel, CancellationToken ct)
    {
        try
        {
            var s = string.IsNullOrEmpty(sentinel) ? AutoSentinel() : sentinel;
            var r = await router.SendAsync(prompt, s, ct);
            return ActionResult.OkWith("sent", new
            {
                handle = r.Handle,
                sentinel = r.Sentinel,
                transport = r.TransportUsed,
                sent_at = r.SentAt,
            });
        }
        catch (TransportError ex) { return ActionResult.Fail(ex.Message); }
    }

    public async Task<ActionResult> StatusAsync(string handle, CancellationToken ct)
    {
        try
        {
            var s = await router.StatusAsync(handle, ct);
            // v4.2.0: every status payload carries lost. Callers can switch on
            // it: false=normal, true=JS-side ticket vanished after a WebView
            // reload (re-ask). MCP clients that don't care can ignore the field.
            return ActionResult.OkWith("polled", new
            {
                done = s.Done,
                response = s.Response,
                streaming = s.Streaming,
                elapsed_sec = s.ElapsedSec,
                transport = s.TransportUsed,
                lost = s.Lost,
            });
        }
        catch (TransportError ex) { return ActionResult.Fail(ex.Message); }
    }

    public async Task<ActionResult> WaitAsync(string handle, double timeoutSec, CancellationToken ct)
    {
        var deadline = Stopwatch.StartNew();
        var budget = TimeSpan.FromSeconds(timeoutSec <= 0 ? DefaultTimeout.TotalSeconds : timeoutSec);
        while (deadline.Elapsed < budget)
        {
            try
            {
                var s = await router.StatusAsync(handle, ct);
                if (s.Done)
                {
                    return ActionResult.OkWith("done", new
                    {
                        done = true,
                        response = s.Response,
                        elapsed_sec = s.ElapsedSec,
                        transport = s.TransportUsed,
                        timed_out = false,
                        lost = false,
                    });
                }
                if (s.Lost)
                {
                    // v4.2.0: WebView reloaded mid-wait — JS ticket vanished.
                    // Exit the polling loop early with the lost discriminator
                    // so the caller can re-ask instead of polling for the
                    // remainder of the timeout against a doomed handle.
                    return ActionResult.OkWith("lost", new
                    {
                        done = false,
                        response = "",
                        elapsed_sec = deadline.Elapsed.TotalSeconds,
                        transport = s.TransportUsed,
                        timed_out = false,
                        lost = true,
                    });
                }
            }
            catch (TransportError ex) { return ActionResult.Fail(ex.Message); }
            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
        return ActionResult.OkWith("timed_out", new
        {
            done = false,
            response = "",
            elapsed_sec = deadline.Elapsed.TotalSeconds,
            timed_out = true,
            lost = false,
        });
    }

    public async Task<ActionResult> AskAsync(string prompt, double timeoutSec, CancellationToken ct)
    {
        try
        {
            var sentinel = AutoSentinel();
            var send = await router.SendAsync(prompt, sentinel, ct);
            return await WaitAsync(send.Handle, timeoutSec, ct);
        }
        catch (TransportError ex) { return ActionResult.Fail(ex.Message); }
    }

    public async Task<ActionResult> ResetAsync(CancellationToken ct)
    {
        try { await router.ResetAsync(ct); return ActionResult.Ok("reset"); }
        catch (TransportError ex) { return ActionResult.Fail(ex.Message); }
    }

    public Task<ActionResult> ForceTierAsync(string name, CancellationToken ct)
    {
        try
        {
            router.ForceTier(name);
            return Task.FromResult(ActionResult.Ok($"forced_{name}"));
        }
        catch (ArgumentException ex)  { return Task.FromResult(ActionResult.Fail(ex.Message)); }
        catch (TransportError ex)     { return Task.FromResult(ActionResult.Fail(ex.Message)); }
    }

    /// <summary>
    /// Format: &lt;MX-XXXXXX&gt; where X is base32-friendly ([2-7A-Z]). 6 chars from 32^6 ≈ 1B
    /// gives ample collision margin for a session at the bridge's 100-ticket cap.
    /// </summary>
    internal static string AutoSentinel()
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        Span<byte> buf = stackalloc byte[6];
        RandomNumberGenerator.Fill(buf);
        var sb = new StringBuilder("<MX-");
        for (int i = 0; i < buf.Length; i++) sb.Append(alphabet[buf[i] & 0x1F]);
        sb.Append('>');
        return sb.ToString();
    }
}
