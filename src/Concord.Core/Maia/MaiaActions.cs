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

    // ---- v4.2.1 introspection tools --------------------------------------

    /// <summary>
    /// "Is Maia generating?" — read-only DOM probe via the introspection-
    /// capable transport (currently <see cref="CdpInjectedTransport"/>).
    /// No traffic to Maia. See <see cref="BusyResult"/> for the discriminator.
    /// </summary>
    public async Task<ActionResult> BusyAsync(CancellationToken ct)
    {
        var introspection = router.GetIntrospection();
        if (introspection is null)
            return ActionResult.Fail("No introspection-capable transport available.");
        try
        {
            var r = await introspection.BusyAsync(ct);
            return ActionResult.OkWith(r.Busy ? "busy" : "idle", new
            {
                busy = r.Busy,
                reason = r.Reason,
                idle_for_ms = r.IdleForMs,
                spinner = r.Spinner,
            });
        }
        catch (TransportError ex) { return ActionResult.Fail(ex.Message); }
    }

    /// <summary>
    /// Programmatic click of Maia's "New chat" button — wipes Maia's
    /// in-panel context. Used in the §2 recovery ladder when
    /// <c>maia__reset</c> + re-probe didn't restore working state.
    /// </summary>
    public async Task<ActionResult> NewChatAsync(CancellationToken ct)
    {
        var introspection = router.GetIntrospection();
        if (introspection is null)
            return ActionResult.Fail("No introspection-capable transport available.");
        try
        {
            var r = await introspection.NewChatAsync(ct);
            if (!r.Ok)
                return ActionResult.Fail($"new_chat failed: {r.Error ?? "unknown"}");
            // Clearing Maia also invalidates any router-side handle bindings —
            // the v1/v2 reset path covers this server-side too. Calling reset
            // here keeps the router in sync without any extra round-trip.
            await router.ResetAsync(ct);
            return ActionResult.OkWith("new_chat_started", new
            {
                started_at = r.StartedAt?.ToString("o"),
            });
        }
        catch (TransportError ex) { return ActionResult.Fail(ex.Message); }
    }

    /// <summary>
    /// Cheap liveness probe — sends "ping" to Maia, waits up to <paramref name="timeoutSec"/>
    /// for any response, returns latency. Use BEFORE expensive ask calls when
    /// bridge health is uncertain; if the ping fails fast, run the §2 ladder
    /// instead of burning a 60s timeout on a real ask.
    /// </summary>
    public async Task<ActionResult> PingAsync(double timeoutSec, CancellationToken ct)
    {
        var deadline = Stopwatch.StartNew();
        var budget = timeoutSec <= 0 ? 5.0 : timeoutSec;
        try
        {
            var sentinel = AutoSentinel();
            await router.SendAsync("ping", sentinel, ct);
            // Reuse the existing wait machinery — it already understands the
            // lost-handle discriminator and timeouts.
            var inner = await WaitAsync(sentinel, budget, ct);
            // Inner OkWith carries done/response/elapsed; flatten to a ping-
            // shaped payload.
            var alive = inner.Status == "done";
            return ActionResult.OkWith(alive ? "alive" : "no_response", new
            {
                alive,
                latency_ms = (long)deadline.Elapsed.TotalMilliseconds,
                response = inner.Status == "done" ? ExtractResponse(inner) : null,
                timed_out = inner.Status == "timed_out",
            });
        }
        catch (TransportError ex) { return ActionResult.Fail(ex.Message); }
    }

    /// <summary>
    /// Bridge-state introspection without traffic to Maia. Returns
    /// router availability, in-flight handle bindings, last probe time,
    /// + an embedded busy() snapshot when the introspection transport is
    /// reachable. Use as a one-call diagnostic before deciding how to
    /// recover from a suspected bridge issue.
    /// </summary>
    public async Task<ActionResult> HealthAsync(CancellationToken ct)
    {
        var snap = router.GetHealthSnapshot();
        BusyResult? busy = null;
        string? busyError = null;
        var introspection = router.GetIntrospection();
        if (introspection is not null)
        {
            try { busy = await introspection.BusyAsync(ct); }
            catch (TransportError ex) { busyError = ex.Message; }
        }

        return ActionResult.OkWith("health", new
        {
            last_probe_at = snap.LastProbeAt?.ToString("o"),
            forced_tier = snap.ForcedTier,
            active_bindings = snap.ActiveBindings,
            transports = snap.Transports.Select(t => new
            {
                name = t.Name,
                tier = t.Tier,
                available = t.Available,
                last_latency_ms = t.LastLatencyMs,
                reason = t.Reason,
            }).ToArray(),
            maia_busy = busy is null ? null : new
            {
                busy = busy.Busy,
                reason = busy.Reason,
                idle_for_ms = busy.IdleForMs,
            },
            maia_busy_error = busyError,
        });
    }

    private static string? ExtractResponse(ActionResult r)
    {
        if (r.Data is null) return null;
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(r.Data);
            var node = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
            return node?["response"]?.GetValue<string>();
        }
        catch { return null; }
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
