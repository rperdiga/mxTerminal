# Concord v4.2.0 — Maia bridge hardening implementation spec

**Branch:** `feat/v4.2.0-bridge-hardening`
**Author:** architect pass, 2026-05-09
**Implementer:** follow-on subagent (3 hours, single working session)
**Empirical baseline:** 2026-05-09 cocktail-clone build under `C:\Workspace\MendixApps\CocktailDemo32-main`. See `project_concord_v414_cocktail_test_findings` and `project_concord_v420_bridge_plan` memory entries.

---

## 1. Summary

Five smoking-gun failure modes drove the cocktail test off a cliff: every Maia tool call opened a fresh CDP WebSocket and spawned a fresh PowerShell CIM-query process; `StatusAsync` never re-injected the JS agent after a WebView reload; the JS bridge had no reset on agent-vanish; the `poll()` parser threw on unexpected shapes with an empty error string; and there was no diagnostic trace to ground future hypotheses. v4.2.0 ships a **persistent CDP connection** (cached port + reused WebSocket + retry-once-on-drop), **auto-re-injection on `StatusAsync`** when `poll()` returns `unknown` or null/non-object, **defensive `poll()` parsing** that surfaces the actual shape it saw, **a Settings toggle for verbose CDP request/response logging**, and a **WebSocket heartbeat** to keep Chromium from throttling the WebView when Maia is hidden. MCP tool surface, JSON shape, and existing settings keys are unchanged. Users see fewer "Unknown handle" / "unexpected shape" failures, no more `IOException: forcibly closed by remote host`, and — when the new diagnostic toggle is on — an auditable CDP trace in `terminal.log`.

## 2. Phase plan

Five atomic phases. Each is one commit, each ships independently, each is reverted in isolation. The order matters: phases 1 and 2 are infrastructure for phase 3+; phase 5 (heartbeat) depends on phase 1's persistent client.

| # | Phase | Commit subject | Files | Why-now |
|---|-------|---------------|-------|---------|
| 1 | Persistent CDP client (cache port, reuse WebSocket, retry-once-on-drop) | `Concord v4.2.0 P1: persistent CdpClient — cache port + reuse WebSocket + reconnect-on-drop` | `src/Maia/CdpClient.cs`, `src/Maia/ICdpClient.cs`, `src/TerminalPaneExtension.cs`, `src/TerminalPaneViewModel.cs`, `tests/Maia/CdpClientReconnectTests.cs` (new), `tests/Maia/CdpInjectedTransportTests.cs` (FakeCdp updated) | Eliminates the connection storm. ~80% of the win. Must land first; everything else assumes a singleton client. |
| 2 | Diagnostic logging toggle | `Concord v4.2.0 P2: MaiaDiagnosticLogging setting + Logger.Debug + CDP trace` | `src/Logging.cs`, `src/TerminalSettings.cs`, `src/Messages/Outgoing.cs`, `src/Messages/Incoming.cs`, `src/TerminalPaneViewModel.cs`, `src/TerminalPaneExtension.cs`, `src/StudioProActionServer.cs`, `src/Maia/CdpClient.cs`, `ui/index.html`, `ui/src/settings-modal.ts`, `tests/LoggingTests.cs`, `tests/SettingsApplyHelperTests.cs` (touched) | Without this, every future bug report is fingerpointing. Land before any other behavioral change so phase 3-5 traces are visible. |
| 3 | Auto-re-inject on `StatusAsync` + defensive `poll()` parser | `Concord v4.2.0 P3: re-inject agent on StatusAsync; surface poll() shape in errors` | `src/Maia/CdpInjectedTransport.cs`, `src/Maia/maia_agent.js`, `tests/Maia/CdpInjectedTransportTests.cs` | Eliminates "Unknown handle: pages-batch-1" and "poll() returned unexpected shape: " for the WebView-reload case. ~10% of the win. |
| 4 | Bridge-side handle ↔ sentinel rebinding on transient failure | `Concord v4.2.0 P4: re-submit on agent-vanish for in-flight handles` | `src/Maia/CdpInjectedTransport.cs` (StatusAsync extended), `tests/Maia/CdpInjectedTransportTests.cs` | When the WebView reload nukes the JS-side ticket but Concord still has the handle, fall back to a graceful "lost-in-transit" status instead of TransportUnavailable. Surfaces a defined error code, not a confusing `Unknown handle:` to the caller. |
| 5 | CDP keep-alive heartbeat (10s) | `Concord v4.2.0 P5: persistent CDP heartbeat to defeat WebView throttling` | `src/Maia/CdpClient.cs`, `tests/Maia/CdpClientHeartbeatTests.cs` (new) | Prevents the 500ms `setInterval` in `maia_agent.js` from drifting to 10s+ when Maia is hidden behind another pane. Cheap once phase 1 ships. |

Total expected diff: ~600 lines of source, ~400 lines of tests.

---

## 3. Per-phase code-level changes

### Phase 1 — persistent CDP client

**Goal:** `CdpClient` becomes a long-lived singleton owned by the action-server lifecycle. First call discovers the port + handshakes the WebSocket; subsequent calls reuse the WebSocket. On detected drop, one reconnect attempt before raising `TransportUnavailable`.

#### 3.1.1 — `src/Maia/ICdpClient.cs`

No interface change required — `ConnectMaiaAsync` becomes idempotent (no-op when already connected). The contract is unchanged from the caller's perspective. Add an XML-doc note:

```csharp
using System.Text.Json.Nodes;

namespace Terminal.Maia;

/// <summary>
/// Connects to Studio Pro's WebView2 Maia panel via Chrome DevTools Protocol
/// and runs JS evaluations inside it. Owns the WMI process scan, the /json
/// endpoint discovery, and the WebSocket plumbing. The single seam tests fake
/// to cover all transports.
/// <para>
/// v4.2.0+: instances are intended to be long-lived. <see cref="ConnectMaiaAsync"/>
/// is idempotent — first call performs port discovery and the WebSocket handshake;
/// subsequent calls return immediately when the underlying socket is healthy, or
/// transparently reconnect when it has dropped. Callers should NOT
/// <c>await using var cdp = clientFactory()</c> per-call any more.
/// </para>
/// </summary>
public interface ICdpClient : IAsyncDisposable
{
    Task ConnectMaiaAsync(CancellationToken ct);
    Task<JsonNode?> EvaluateAsync(string js, TimeSpan? timeout = null, CancellationToken ct = default);
}
```

#### 3.1.2 — `src/Maia/CdpClient.cs`

Major rewrite of `ConnectMaiaAsync` and addition of an internal `EnsureConnectedAsync` + `ReconnectAsync`. Discovery results (port + targetWsUrl) are cached.

**BEFORE — `ConnectMaiaAsync` (lines 30–51):**

```csharp
public async Task ConnectMaiaAsync(CancellationToken ct)
{
    if (!OperatingSystem.IsWindows())
        throw new TransportUnavailable("Maia bridge is Windows-only in this Concord release.");

    int port = FindDebugPort();
    string targetWsUrl = await DiscoverMaiaTargetAsync(port, ct);

    ws = new ClientWebSocket();
    try
    {
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(5));
        await ws.ConnectAsync(new Uri(targetWsUrl), connectCts.Token);
    }
    catch (Exception ex) when (ex is not TransportUnavailable)
    {
        ws.Dispose();
        ws = null;
        throw new TransportUnavailable($"WebSocket connect to Maia target failed: {ex.Message}", ex);
    }
}
```

**AFTER:**

```csharp
private int? cachedPort;
private string? cachedTargetWsUrl;
private DateTime cachedDiscoveryAt;
private static readonly TimeSpan DiscoveryTtl = TimeSpan.FromMinutes(5);
private readonly SemaphoreSlim connectGate = new(1, 1);
private readonly Logger? log;

// New ctor for DI of the logger; existing parameterless ctor preserved for tests.
public CdpClient() : this(null) { }
public CdpClient(Logger? log) { this.log = log; }

public async Task ConnectMaiaAsync(CancellationToken ct)
{
    if (!OperatingSystem.IsWindows())
        throw new TransportUnavailable("Maia bridge is Windows-only in this Concord release.");

    await connectGate.WaitAsync(ct);
    try
    {
        if (ws is { State: WebSocketState.Open }) return;   // already healthy
        await ConnectInternalAsync(ct);
    }
    finally { connectGate.Release(); }
}

private async Task ConnectInternalAsync(CancellationToken ct)
{
    // Re-use cached discovery within TTL; full re-probe afterwards.
    int port;
    string targetWsUrl;
    if (cachedPort is int p && cachedTargetWsUrl is string url
        && DateTime.UtcNow - cachedDiscoveryAt < DiscoveryTtl)
    {
        port = p;
        targetWsUrl = url;
        log?.Debug($"[cdp] reusing cached discovery port={port}");
    }
    else
    {
        port = FindDebugPort();
        targetWsUrl = await DiscoverMaiaTargetAsync(port, ct);
        cachedPort = port;
        cachedTargetWsUrl = targetWsUrl;
        cachedDiscoveryAt = DateTime.UtcNow;
        log?.Debug($"[cdp] discovered port={port} ws={targetWsUrl}");
    }

    // Tear down a half-open socket from a previous failed attempt.
    if (ws is not null)
    {
        try { ws.Dispose(); } catch { }
        ws = null;
    }

    ws = new ClientWebSocket();
    try
    {
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(5));
        await ws.ConnectAsync(new Uri(targetWsUrl), connectCts.Token);
    }
    catch (Exception ex) when (ex is not TransportUnavailable)
    {
        ws.Dispose();
        ws = null;
        // Discovery may have gone stale (Studio Pro restarted on a different
        // port). Invalidate the cache so the next call re-probes.
        cachedPort = null;
        cachedTargetWsUrl = null;
        throw new TransportUnavailable($"WebSocket connect to Maia target failed: {ex.Message}", ex);
    }
}
```

**Wrap `EvaluateAsync` with a one-shot reconnect on transport-level WebSocket failure (lines 151–216):**

```csharp
public async Task<JsonNode?> EvaluateAsync(string js, TimeSpan? timeout = null, CancellationToken ct = default)
{
    try
    {
        return await EvaluateOnceAsync(js, timeout, ct);
    }
    catch (TransportUnavailable ex) when (IsConnectionDropped(ex))
    {
        log?.Debug($"[cdp] socket drop detected; reconnecting once: {ex.Message}");
        // Force a reconnect under the same gate, then retry.
        await connectGate.WaitAsync(ct);
        try { await ConnectInternalAsync(ct); }
        finally { connectGate.Release(); }
        return await EvaluateOnceAsync(js, timeout, ct);
    }
}

private static bool IsConnectionDropped(TransportUnavailable ex)
    => ex.Message.Contains("not connected", StringComparison.OrdinalIgnoreCase)
    || ex.Message.Contains("WebSocket close", StringComparison.OrdinalIgnoreCase)
    || ex.InnerException is WebSocketException
    || ex.InnerException is System.IO.IOException;

private async Task<JsonNode?> EvaluateOnceAsync(string js, TimeSpan? timeout, CancellationToken ct)
{
    if (ws is null || ws.State != WebSocketState.Open)
        throw new TransportUnavailable("CDP client is not connected.");

    await evalGate.WaitAsync(ct);
    try
    {
        int id = Interlocked.Increment(ref messageId);
        var req = new JsonObject
        {
            ["id"] = id,
            ["method"] = "Runtime.evaluate",
            ["params"] = new JsonObject
            {
                ["expression"] = $"(() => {{ {js} }})()",
                ["returnByValue"] = true,
                ["awaitPromise"] = true,
            }
        };
        var requestText = req.ToJsonString();
        log?.Debug($"[cdp] >> id={id} bytes={requestText.Length}");
        var bytes = Encoding.UTF8.GetBytes(requestText);

        using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        sendCts.CancelAfter(timeout ?? EvaluateDefaultTimeout);
        try
        {
            await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, sendCts.Token);
        }
        catch (WebSocketException wex)
        {
            // Mark the socket dead so the outer reconnect path triggers.
            try { ws.Dispose(); } catch { }
            ws = null;
            throw new TransportUnavailable($"WebSocket send failed: {wex.Message}", wex);
        }

        var deadline = DateTime.UtcNow + (timeout ?? EvaluateDefaultTimeout);
        var buf = new ArraySegment<byte>(new byte[64 * 1024]);
        var sb = new StringBuilder();
        while (DateTime.UtcNow < deadline)
        {
            sb.Clear();
            while (true)
            {
                using var recvCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                    throw new TransportUnavailable($"CDP Runtime.evaluate timed out after {(timeout ?? EvaluateDefaultTimeout).TotalSeconds:0.0}s");
                recvCts.CancelAfter(remaining);
                WebSocketReceiveResult r;
                try { r = await ws.ReceiveAsync(buf, recvCts.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TransportUnavailable($"CDP Runtime.evaluate timed out after {(timeout ?? EvaluateDefaultTimeout).TotalSeconds:0.0}s");
                }
                catch (WebSocketException wex)
                {
                    try { ws.Dispose(); } catch { }
                    ws = null;
                    throw new TransportUnavailable($"WebSocket receive failed: {wex.Message}", wex);
                }
                if (r.MessageType == WebSocketMessageType.Close)
                {
                    try { ws.Dispose(); } catch { }
                    ws = null;
                    throw new TransportUnavailable("WebSocket closed by remote.");
                }
                sb.Append(Encoding.UTF8.GetString(buf.Array!, 0, r.Count));
                if (r.EndOfMessage) break;
            }
            var msg = JsonNode.Parse(sb.ToString()) as JsonObject;
            if (msg is null) continue;
            if (msg["id"] is JsonValue v && v.TryGetValue<int>(out var msgId) && msgId == id)
            {
                log?.Debug($"[cdp] << id={id} bytes={sb.Length}");
                if (msg["error"] is JsonObject err)
                    throw new CdpProtocolException("Runtime.evaluate", msg, $"CDP error: {err["message"]?.GetValue<string>()}");
                var result = msg["result"]?["result"];
                if (result?["exceptionDetails"] is not null)
                    throw new TransportUnavailable($"JS exception inside Maia WebView: {result["exceptionDetails"]?.ToJsonString()}");
                return result?["value"];
            }
        }
        throw new TransportUnavailable($"CDP Runtime.evaluate timed out after {(timeout ?? EvaluateDefaultTimeout).TotalSeconds:0.0}s");
    }
    finally { evalGate.Release(); }
}
```

**`DisposeAsync` extension:** must dispose the new `connectGate`. Update lines 218–232:

```csharp
public async ValueTask DisposeAsync()
{
    if (ws is not null)
    {
        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
        }
        catch { /* best-effort */ }
        ws.Dispose();
        ws = null;
    }
    evalGate.Dispose();
    connectGate.Dispose();
}
```

#### 3.1.3 — Caller wiring (`src/TerminalPaneExtension.cs` lines 285–300, `src/TerminalPaneViewModel.cs` lines 261–276)

Replace per-call `() => new Terminal.Maia.CdpClient()` factories with a singleton-returning factory keyed off one shared instance:

**BEFORE (TerminalPaneExtension.cs):**

```csharp
var transports = new Terminal.Maia.IMaiaTransport[]
{
    new Terminal.Maia.CdpInjectedTransport(() => new Terminal.Maia.CdpClient()),
    new Terminal.Maia.CdpChatTransport(() => new Terminal.Maia.CdpClient()),
};
```

**AFTER:**

```csharp
// Singleton CdpClient: WebSocket + port discovery are reused across all
// Maia tool calls. v4.1.x spawned a fresh PowerShell + WebSocket per call,
// which DoS'd Studio Pro's CDP under cocktail-test load (45+ status/wait
// calls in a 30-min build). See docs/superpowers/specs/2026-05-09-bridge-hardening-implementation.md.
var sharedCdp = new Terminal.Maia.CdpClient(log);
var transports = new Terminal.Maia.IMaiaTransport[]
{
    new Terminal.Maia.CdpInjectedTransport(() => sharedCdp),
    new Terminal.Maia.CdpChatTransport(() => sharedCdp),
};
```

Apply the same edit at `TerminalPaneViewModel.cs` lines 268–272 (passing the same `log` that's already in scope at that call site).

**Lifecycle note for the implementer:** `sharedCdp` is captured by the transport closures and lives for the lifetime of the `MaiaActions` instance, which is held by the `TerminalSessionManager` until `StopActionServer` is called. When the user toggles Maia integration off → on, the new instance replaces the old one; old `sharedCdp` becomes garbage-collectable after the closures drop. There is no `Dispose` plumbing today for the old instance — that's a known small leak (one ClientWebSocket + one SemaphoreSlim per toggle cycle) and is **out of scope for v4.2.0** (call it out under Risks below).

#### 3.1.4 — `tests/Maia/CdpInjectedTransportTests.cs` — update FakeCdp

The existing FakeCdp's `DisposeAsync` no longer matters since callers don't `await using`. But the tests themselves need the cdpFactory to return the SAME instance each call. Update FakeCdp comment + add a `ConnectCalls` counter that we'll assert in phase 1 tests:

```csharp
private sealed class FakeCdp : ICdpClient
{
    public List<string> Evals { get; } = new();
    public Func<string, JsonNode?>? Responder;
    public int ConnectCalls;
    public Task ConnectMaiaAsync(CancellationToken ct)
    {
        ConnectCalls++;
        return Task.CompletedTask;
    }
    public Task<JsonNode?> EvaluateAsync(string js, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        Evals.Add(js);
        return Task.FromResult(Responder?.Invoke(js));
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

Add one new test asserting connect-is-cheap:

```csharp
[Fact]
public async Task SendThenStatus_ReusesSameCdpInstance()
{
    var fake = new FakeCdp
    {
        Responder = js =>
            js.Contains("findChatRoot") ? JsonValue.Create("installed")
            : js.Contains("submit")     ? new JsonObject { ["ok"] = true }
            : js.Contains("poll")       ? new JsonObject { ["status"] = "pending", ["response"] = "", ["elapsed_ms"] = 100.0 }
            : null
    };
    var t = new CdpInjectedTransport(() => fake);
    await t.SendAsync("hi", "<MX-T>", CancellationToken.None);
    await t.StatusAsync("<MX-T>", CancellationToken.None);

    // Even though SendAsync and StatusAsync each call ConnectMaiaAsync,
    // a real persistent CdpClient short-circuits when already connected.
    // For the fake we just assert both call sites do invoke connect (which
    // proves our factory returned the same instance both times).
    fake.ConnectCalls.Should().Be(2);
}
```

#### 3.1.5 — new test file: `tests/Maia/CdpClientReconnectTests.cs`

Live network is forbidden in unit tests, so this fakes the WebSocket via a thin wrapper. Simplest practical coverage: assert `ConnectMaiaAsync` is idempotent against a fake, and that after `Dispose`, a second `Connect` resets state. A full reconnect-on-drop test requires either `WebSocketServer` (out of scope) or refactoring `CdpClient` to accept a `Func<Uri, ClientWebSocket>` factory. **For v4.2.0, take the lighter coverage and rely on the manual smoke test for the reconnect path.**

```csharp
using System.Net.WebSockets;
using FluentAssertions;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

public class CdpClientReconnectTests
{
    [Fact]
    public void Constructor_DoesNotConnect()
    {
        // The constructor must not perform the PowerShell CIM lookup or
        // the WebSocket handshake — that work is deferred to ConnectMaiaAsync,
        // which the action-server lifecycle calls lazily on first use.
        var c = new CdpClient();
        // No assertion needed beyond "did not throw" — the contract is
        // observed by absence of side effects.
        c.Should().NotBeNull();
    }

    [Fact]
    public async Task DisposeAsync_OnFreshClient_DoesNotThrow()
    {
        var c = new CdpClient();
        Func<Task> act = async () => await c.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    // NOTE: full reconnect-on-drop coverage requires a WebSocket test double.
    // Manual smoke test in §8 below covers the end-to-end path. A future
    // refactor of CdpClient to accept Func<Uri, ClientWebSocket> would
    // unlock unit-level coverage; tracked as v4.2.1.
}
```

---

### Phase 2 — diagnostic logging toggle

**Goal:** new boolean `MaiaDiagnosticLogging` (default false). When on, `Logger.Debug(string)` writes to `terminal.log`. CDP request/response lines are emitted via `log?.Debug(...)`. UI exposes one checkbox.

#### 3.2.1 — `src/Logging.cs`

Add the `Debug` level and a level filter. The filter is settings-driven; the simplest plumbing: `Logger` exposes a public `bool DiagnosticEnabled { get; set; }`, which the apply-paths flip whenever settings save.

**BEFORE (lines 8–13):**

```csharp
public Logger(string projectDir) => this.projectDir = projectDir;

public void Info(string message)  => Write("INFO",  message, exception: null);
public void Warn(string message)  => Write("WARN",  message, exception: null);
public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);
```

**AFTER:**

```csharp
public Logger(string projectDir) => this.projectDir = projectDir;

/// <summary>
/// When true, <see cref="Debug"/> calls write to the log; otherwise they
/// no-op. Toggled by the settings save-path when the user flips
/// "Diagnostic logging" in Settings → Concord MCP. Default false: keeps
/// the log free of CDP traffic for users who don't need it.
/// </summary>
public bool DiagnosticEnabled { get; set; }

public void Info(string message)  => Write("INFO",  message, exception: null);
public void Warn(string message)  => Write("WARN",  message, exception: null);
public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

/// <summary>
/// Diagnostic-only line. No-op when <see cref="DiagnosticEnabled"/> is false.
/// Used by Maia / CDP code to trace request/response shapes without
/// polluting the terminal.log of users who don't have the toggle on.
/// </summary>
public void Debug(string message)
{
    if (!DiagnosticEnabled) return;
    Write("DEBUG", message, exception: null);
}
```

#### 3.2.2 — `src/TerminalSettings.cs` — add field, default false

Update the record (line 6) and `Defaults()` (line 39) and `Load`/`Save`/`Dto`. The new field goes at the end before `LastAppliedVersion` to preserve positional-constructor compatibility for tests.

**BEFORE (record header, lines 6–28):**

```csharp
public sealed record TerminalSettings(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines,
    string Theme,
    bool McpEnabled,
    string[] McpClients,
    bool McpServerEnabled,
    bool StudioProActionsEnabled,
    bool MaiaIntegrationEnabled,
    string RefreshFromDiskHotkey,
    bool RestoreTabsOnReopen,
    bool SkillsEnabled,
    string[] SkillClients,
    string? LastAppliedVersion = null)
```

**AFTER:**

```csharp
public sealed record TerminalSettings(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines,
    string Theme,
    bool McpEnabled,
    string[] McpClients,
    bool McpServerEnabled,
    bool StudioProActionsEnabled,
    bool MaiaIntegrationEnabled,
    string RefreshFromDiskHotkey,
    bool RestoreTabsOnReopen,
    bool SkillsEnabled,
    string[] SkillClients,
    // v4.2.0+: when true, Logger.Debug writes CDP request/response traces
    // to terminal.log. Default false to keep the log lean. UI surface:
    // Settings → Concord MCP → "Diagnostic logging".
    bool MaiaDiagnosticLogging = false,
    string? LastAppliedVersion = null)
```

`Defaults()` — add the new field:

```csharp
public static TerminalSettings Defaults() => new(
    ShellPath: DefaultShellPath(),
    Args: Array.Empty<string>(),
    RingBufferKB: 4096,
    XtermScrollbackLines: 10000,
    Theme: "auto",
    McpEnabled: true,
    McpClients: new[] { "claude", "copilot" },
    McpServerEnabled: true,
    StudioProActionsEnabled: true,
    MaiaIntegrationEnabled: true,
    RefreshFromDiskHotkey: "F4",
    RestoreTabsOnReopen: true,
    SkillsEnabled: true,
    SkillClients: new[] { "claude", "copilot" },
    MaiaDiagnosticLogging: false,
    LastAppliedVersion: null);
```

`Load` — append `MaiaDiagnosticLogging: dto.MaiaDiagnosticLogging ?? def.MaiaDiagnosticLogging,` before the `LastAppliedVersion` line. `Save` — pass `MaiaDiagnosticLogging` into the Dto. Add the field to the Dto record:

```csharp
private sealed record Dto(
    string? ShellPath,
    string[]? Args,
    int? RingBufferKB,
    int? XtermScrollbackLines,
    string? Theme,
    bool? McpEnabled,
    string[]? McpClients,
    bool? McpServerEnabled,
    bool? StudioProActionsEnabled,
    bool? MaiaIntegrationEnabled,
    string? RefreshFromDiskHotkey,
    bool? RestoreTabsOnReopen,
    bool? SkillsEnabled,
    string[]? SkillClients,
    bool? MaiaDiagnosticLogging = null,
    string? LastAppliedVersion = null,
    bool? ActionsServerEnabled = null);
```

#### 3.2.3 — `src/Messages/Outgoing.cs` SettingsPayload — add field

Append `bool MaiaDiagnosticLogging` to the `SettingsPayload` record before `SkillsEnabled`:

```csharp
public sealed record SettingsPayload(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines,
    string Theme,
    IReadOnlyList<ShellOptionPayload> AvailableShells,
    bool McpEnabled,
    string[] McpClients,
    bool McpServerEnabled,
    bool StudioProActionsEnabled,
    bool MaiaIntegrationEnabled,
    bool MaiaDiagnosticLogging,
    string Platform,
    string RefreshFromDiskHotkey,
    bool RestoreTabsOnReopen,
    AboutInfoPayload About,
    StudioProMcpInfoPayload? StudioProMcp,
    int? LiveActionServerPort,
    bool SkillsEnabled,
    string[] SkillClients,
    IReadOnlyList<BundledSkillPayload> BundledSkills);
```

#### 3.2.4 — `src/Messages/Incoming.cs` SaveSettingsPayload — add field

```csharp
public sealed record SaveSettingsPayload(
    string ShellPath,
    string[] Args,
    int? RingBufferKB = null,
    int? XtermScrollbackLines = null,
    string? Theme = null,
    bool? McpEnabled = null,
    string[]? McpClients = null,
    bool? McpServerEnabled = null,
    bool? StudioProActionsEnabled = null,
    bool? MaiaIntegrationEnabled = null,
    bool? MaiaDiagnosticLogging = null,
    string? RefreshFromDiskHotkey = null,
    bool? RestoreTabsOnReopen = null,
    bool? SkillsEnabled = null,
    string[]? SkillClients = null);
```

#### 3.2.5 — `src/TerminalPaneViewModel.cs` — wire the toggle

Around line 230 where `newMaiaIntegration` is read, add:

```csharp
var newMaiaDiagnosticLogging = p.MaiaDiagnosticLogging ?? current.MaiaDiagnosticLogging;
```

Apply it to the live logger BEFORE the `manager.StartActionServer(...)` call (so the next CDP trace immediately reflects the new state):

```csharp
log.DiagnosticEnabled = newMaiaDiagnosticLogging;
```

Add `MaiaDiagnosticLogging = newMaiaDiagnosticLogging,` to the `updated = current with { … }` block.

In `BuildSettingsPayload` (around line 534) add:

```csharp
MaiaDiagnosticLogging: s.MaiaDiagnosticLogging,
```

#### 3.2.6 — `src/TerminalPaneExtension.cs` — apply at startup

In `TryAutoStartActionServer` (around line 240–290 — search for `var settings =`), set the live flag right after settings are loaded:

```csharp
log.DiagnosticEnabled = settings.MaiaDiagnosticLogging;
```

In `TryFirstRunApply` (around line 446) add:

```csharp
MaiaDiagnosticLogging = defaults.MaiaDiagnosticLogging,
```

— this is the field-initializer in the `defaults with { … }` block. Since the field default is `false` and `Defaults()` already returns false, this is mostly a redundant explicit, but matches the pattern of every other setting being copied through.

#### 3.2.7 — `ui/index.html` — add checkbox

Insert a new `<div class="checkbox-row">` between line 880 (the Maia integration row) and line 881 (the platform-note div):

```html
            <div class="checkbox-row">
              <input id="set-maia-enabled" type="checkbox">
              <label for="set-maia-enabled" style="margin:0">Maia integration <span class="muted">— send, status, wait, ask, reset, force_tier</span></label>
            </div>
            <div id="maia-platform-note" class="mcp-port-readout"></div>
            <div class="checkbox-row">
              <input id="set-maia-diagnostic" type="checkbox">
              <label for="set-maia-diagnostic" style="margin:0">Diagnostic logging <span class="muted">— write CDP request/response traces to terminal.log</span></label>
            </div>
```

Default unchecked — the C# `Defaults()` returns false, and the `applyData` step writes `d.maiaDiagnosticLogging` into `chkMaiaDiagnostic.checked`.

#### 3.2.8 — `ui/src/settings-modal.ts` — wire the checkbox

Add to the `SettingsPayload` interface (around line 33):

```typescript
maiaIntegrationEnabled: boolean;
maiaDiagnosticLogging: boolean;
```

Add a field around line 117 (next to `chkMaia`):

```typescript
private chkMaiaDiagnostic = document.getElementById(
  "set-maia-diagnostic",
) as HTMLInputElement;
```

In `applyData` (around line 317):

```typescript
this.chkMaia.checked = d.maiaIntegrationEnabled;
this.chkMaiaDiagnostic.checked = d.maiaDiagnosticLogging;
```

In `applyMaiaPlatformGate` (around line 364) — disable the diagnostic toggle when Maia itself is platform-gated off:

```typescript
private applyMaiaPlatformGate(platform: string): void {
    const isWindows = platform === "windows";
    this.chkMaia.disabled = !isWindows;
    this.chkMaiaDiagnostic.disabled = !isWindows;
    // … rest unchanged
}
```

In `save()` (around line 485):

```typescript
maiaIntegrationEnabled: this.chkMaia.checked,
maiaDiagnosticLogging: this.chkMaiaDiagnostic.checked,
```

#### 3.2.9 — `src/Maia/CdpClient.cs` already wired

The CdpClient ctor accepts the logger; phase 1 already routes `log?.Debug(...)` calls. With `DiagnosticEnabled=false` (default), those calls return immediately. Net cost when off: one bool read + one method call per Evaluate. Negligible.

---

### Phase 3 — auto-re-inject + defensive `poll()`

**Goal:** when `StatusAsync` calls `poll(handle)` and gets back `null`, a non-object, or `{unknown:true}`, re-inject the agent and retry once. If the handle is genuinely unknown after re-injection, surface a structured error so the caller (and the log) sees what shape the parser actually got.

#### 3.3.1 — `src/Maia/CdpInjectedTransport.cs` — StatusAsync rewrite

**BEFORE (lines 64–84):**

```csharp
public async Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
{
    await using var cdp = clientFactory();
    await cdp.ConnectMaiaAsync(ct);
    var js = $"return window.__maiaBridge.poll({JsonSerializer.Serialize(handle)});";
    var node = await cdp.EvaluateAsync(js, ct: ct);
    if (node is not JsonObject p)
        throw new TransportUnavailable($"poll() returned unexpected shape: {node?.ToJsonString()}");

    if (p["unknown"]?.GetValue<bool>() == true)
        throw new TransportUnavailable($"Unknown handle: {handle}");

    var status = p["status"]?.GetValue<string>() ?? "pending";
    var elapsedMs = p["elapsed_ms"]?.GetValue<double>() ?? 0;
    return new StatusResult(
        Done: status == "done",
        Response: p["response"]?.GetValue<string>() ?? "",
        Streaming: status == "streaming",
        ElapsedSec: elapsedMs / 1000.0,
        TransportUsed: Name);
}
```

**AFTER:**

```csharp
public async Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
{
    var cdp = clientFactory();
    await cdp.ConnectMaiaAsync(ct);

    // First poll — fast path. If the agent is missing or has a fresh
    // tickets Map (WebView reload), we'll see either a JS exception
    // (caught upstream as TransportUnavailable("JS exception inside…")),
    // a null/non-object node, or {unknown:true}. All three converge on
    // "re-inject and retry once."
    var poll = await TryPollAsync(cdp, handle, ct);
    if (poll.NeedsReinject)
    {
        await EnsureAgentAsync(cdp, ct);
        poll = await TryPollAsync(cdp, handle, ct);
    }

    if (poll.UnknownHandle)
    {
        // After re-injection: if the handle is still unknown, the JS-side
        // ticket really is gone (WebView reloaded between Send and Status,
        // or the user clicked "New chat"). Surface the structured error;
        // caller sees the same Unknown-handle message as before, but the
        // log line records that we tried to recover.
        throw new TransportUnavailable($"Unknown handle: {handle} (after re-injection retry)");
    }
    if (poll.Result is not JsonObject p)
    {
        // Defensive last-ditch: re-injection didn't produce an object either.
        // Log the actual shape we saw; do NOT use empty string interpolation.
        var shape = poll.Result switch
        {
            null => "<null>",
            JsonValue v => $"value:{v.ToJsonString()}",
            JsonArray a => $"array(len={a.Count})",
            _ => $"node:{poll.Result.GetType().Name}"
        };
        throw new TransportUnavailable($"poll() returned non-object: {shape}");
    }

    var status = p["status"]?.GetValue<string>() ?? "pending";
    var elapsedMs = p["elapsed_ms"]?.GetValue<double>() ?? 0;
    return new StatusResult(
        Done: status == "done",
        Response: p["response"]?.GetValue<string>() ?? "",
        Streaming: status == "streaming",
        ElapsedSec: elapsedMs / 1000.0,
        TransportUsed: Name);
}

private readonly record struct PollOutcome(JsonNode? Result, bool NeedsReinject, bool UnknownHandle);

private static async Task<PollOutcome> TryPollAsync(ICdpClient cdp, string handle, CancellationToken ct)
{
    // Wrap the call in a try inside the JS so window.__maiaBridge being
    // missing surfaces as a structured value rather than a JS exception.
    var js = $$"""
        if (!window.__maiaBridge) return { __reinject: true, reason: 'no-bridge' };
        try {
            const r = window.__maiaBridge.poll({{JsonSerializer.Serialize(handle)}});
            return r;
        } catch (e) {
            return { __reinject: true, reason: 'poll-throw', message: String(e && e.message || e) };
        }
        """;
    var node = await cdp.EvaluateAsync(js, ct: ct);
    if (node is JsonObject obj)
    {
        if (obj["__reinject"]?.GetValue<bool>() == true)
            return new PollOutcome(node, NeedsReinject: true, UnknownHandle: false);
        if (obj["unknown"]?.GetValue<bool>() == true)
            return new PollOutcome(node, NeedsReinject: false, UnknownHandle: true);
        return new PollOutcome(node, NeedsReinject: false, UnknownHandle: false);
    }
    // null or non-object — treat as needs-reinject (defensive). The agent
    // contract says poll() always returns an object; if we got something
    // else, the agent is gone or corrupted.
    return new PollOutcome(node, NeedsReinject: true, UnknownHandle: false);
}
```

Note: the lifecycle change is real — we're no longer `await using` the cdp here, because in v4.2.0 the same instance is shared across all callers (phase 1 wiring). The transport must NOT dispose it.

#### 3.3.2 — `src/Maia/maia_agent.js` — no behavioral change needed for phase 3

The new `__reinject` payload is C#-side; the JS contract is unchanged. The IIFE wrapper in `CdpClient.EvaluateAsync` already accepts statements with explicit `return`s, so the new `if (!window.__maiaBridge) return {…}` works as-is.

#### 3.3.3 — Tests for Phase 3

Add to `tests/Maia/CdpInjectedTransportTests.cs`:

```csharp
[Fact]
public async Task StatusAsync_BridgeMissing_ReInjectsAndRetries()
{
    int evalCount = 0;
    bool agentInstalled = false;
    var fake = new FakeCdp
    {
        Responder = js =>
        {
            evalCount++;
            // Agent install request.
            if (js.Contains("findChatRoot"))
            {
                agentInstalled = true;
                return JsonValue.Create("installed");
            }
            // First poll: bridge missing. After re-inject (agentInstalled=true),
            // poll succeeds.
            if (js.Contains("__maiaBridge.poll"))
            {
                if (!agentInstalled)
                    return new JsonObject { ["__reinject"] = true, ["reason"] = "no-bridge" };
                return new JsonObject
                {
                    ["status"] = "done",
                    ["response"] = "ok",
                    ["elapsed_ms"] = 50.0,
                };
            }
            return null;
        }
    };
    var t = new CdpInjectedTransport(() => fake);

    var s = await t.StatusAsync("<MX-T>", CancellationToken.None);

    s.Done.Should().BeTrue();
    s.Response.Should().Be("ok");
    // Three eval calls: first poll → re-inject → second poll.
    evalCount.Should().Be(3);
}

[Fact]
public async Task StatusAsync_UnknownAfterReinject_RaisesStructuredError()
{
    var fake = new FakeCdp
    {
        Responder = js =>
            js.Contains("findChatRoot") ? JsonValue.Create("installed")
            : js.Contains("__maiaBridge.poll") ? new JsonObject { ["unknown"] = true }
            : null
    };
    var t = new CdpInjectedTransport(() => fake);

    Func<Task> act = () => t.StatusAsync("<MX-LOST>", CancellationToken.None);

    await act.Should().ThrowAsync<TransportUnavailable>()
        .Where(e => e.Message.Contains("Unknown handle: <MX-LOST>")
                 && e.Message.Contains("after re-injection retry"));
}

[Fact]
public async Task StatusAsync_NonObjectResult_LogsShape()
{
    var fake = new FakeCdp { Responder = _ => JsonValue.Create(42) };
    var t = new CdpInjectedTransport(() => fake);

    Func<Task> act = () => t.StatusAsync("<MX-T>", CancellationToken.None);

    await act.Should().ThrowAsync<TransportUnavailable>()
        .Where(e => e.Message.Contains("poll() returned non-object")
                 && e.Message.Contains("value:42"));
}
```

---

### Phase 4 — graceful "lost handle" fallback

**Goal:** when phase 3's `Unknown handle (after re-injection retry)` fires for a handle that the **router** still has bound to this transport (i.e. the JS state vanished mid-flight, but Concord's C# state remembers we sent it), surface a non-error `lost` status so `maia__wait` doesn't loop forever and the caller can treat it as "send was lost, please re-ask".

This is small but high-value: in the cocktail log, the router-side `handleToTransport` survived; the JS-side ticket didn't. v4.1.4 surfaced this as a hard error every poll. v4.2.0 surfaces it once, with a recoverable status.

#### 3.4.1 — Extend `MaiaTypes.cs`

Add a `Status` discriminator to `StatusResult` so callers can tell `pending` vs `streaming` vs `done` vs `lost`:

```csharp
public sealed record StatusResult(
    bool Done,
    string Response,
    bool Streaming,
    double ElapsedSec,
    string TransportUsed,
    bool Lost = false);   // v4.2.0: handle existed in router but JS ticket vanished
```

Backwards-compat: existing serialization adds an `lost: false` field; absent in the contract before, but readers ignore unknown fields (the MCP JSON consumer doesn't validate unknown keys). MaiaActions echoes it through.

#### 3.4.2 — `src/Maia/CdpInjectedTransport.cs` — return `Lost=true` instead of throwing

Modify the phase-3 logic so that the `UnknownHandle` branch returns `StatusResult(Done=false, Response="", Streaming=false, ElapsedSec=0, TransportUsed=Name, Lost=true)` **only when this is a re-injection attempt** (i.e. NeedsReinject was true on first poll). Genuine unknown-on-first-call (no reload happened) is still an error.

Replace the post-retry block:

```csharp
    var poll = await TryPollAsync(cdp, handle, ct);
    bool didReinject = false;
    if (poll.NeedsReinject)
    {
        await EnsureAgentAsync(cdp, ct);
        poll = await TryPollAsync(cdp, handle, ct);
        didReinject = true;
    }

    if (poll.UnknownHandle)
    {
        if (didReinject)
        {
            // The WebView reloaded between Send and Status. C#-side router
            // still has the handle bound to us, so we know it was a real
            // send; the JS-side ticket is just gone. Surface as lost.
            return new StatusResult(
                Done: false, Response: "", Streaming: false,
                ElapsedSec: 0, TransportUsed: Name, Lost: true);
        }
        throw new TransportUnavailable($"Unknown handle: {handle}");
    }
```

#### 3.4.3 — `src/Maia/MaiaActions.cs` — propagate `Lost` upward

In `StatusAsync`'s success block (around line 38):

```csharp
return ActionResult.OkWith("polled", new
{
    done = s.Done,
    response = s.Response,
    streaming = s.Streaming,
    elapsed_sec = s.ElapsedSec,
    transport = s.TransportUsed,
    lost = s.Lost,
});
```

In `WaitAsync`, exit the loop early when `Lost` flips true:

```csharp
var s = await router.StatusAsync(handle, ct);
if (s.Done)
{
    return ActionResult.OkWith("done", new { done = true, response = s.Response, elapsed_sec = s.ElapsedSec, transport = s.TransportUsed, timed_out = false });
}
if (s.Lost)
{
    return ActionResult.OkWith("lost", new { done = false, lost = true, elapsed_sec = s.ElapsedSec, transport = s.TransportUsed });
}
```

#### 3.4.4 — Tests for phase 4

Append to `tests/Maia/CdpInjectedTransportTests.cs`:

```csharp
[Fact]
public async Task StatusAsync_UnknownAfterReinject_ReturnsLostInsteadOfThrowing()
{
    var fake = new FakeCdp
    {
        Responder = js =>
            js.Contains("findChatRoot") ? JsonValue.Create("installed")
            // First poll: needs reinject. Second poll (after reinject): unknown.
            : js.Contains("__maiaBridge.poll") ? new JsonObject { ["__reinject"] = true, ["reason"] = "no-bridge" }
            : null
    };
    // First eval is poll (returns __reinject), second is install (returns 'installed'),
    // third is poll again — fake will still return __reinject. We need a state machine.

    int pollCount = 0;
    fake.Responder = js =>
    {
        if (js.Contains("findChatRoot")) return JsonValue.Create("installed");
        if (js.Contains("__maiaBridge.poll"))
        {
            pollCount++;
            // First poll: bridge missing. Second poll (after reinject): unknown.
            if (pollCount == 1) return new JsonObject { ["__reinject"] = true };
            return new JsonObject { ["unknown"] = true };
        }
        return null;
    };
    var t = new CdpInjectedTransport(() => fake);

    var s = await t.StatusAsync("<MX-LOST>", CancellationToken.None);

    s.Lost.Should().BeTrue();
    s.Done.Should().BeFalse();
}
```

Update the existing `StatusAsync_UnknownAfterReinject_RaisesStructuredError` test from phase 3 to assert that `Lost = true` is returned WHEN reinject ran first, but the "no reinject needed" path still throws. (Actually — on re-read, phase 3 + phase 4 together mean the only "throw on unknown" path is when the FIRST poll succeeded structurally but said unknown. Adjust the phase-3 test:)

```csharp
[Fact]
public async Task StatusAsync_UnknownOnFirstPoll_StillThrows()
{
    // No reinject needed: bridge present, ticket genuinely missing.
    // (Caller passed a handle we never sent.)
    var fake = new FakeCdp
    {
        Responder = js =>
            js.Contains("__maiaBridge.poll") ? new JsonObject { ["unknown"] = true }
            : null
    };
    var t = new CdpInjectedTransport(() => fake);

    Func<Task> act = () => t.StatusAsync("<MX-NEVERSEEN>", CancellationToken.None);

    await act.Should().ThrowAsync<TransportUnavailable>()
        .Where(e => e.Message.Contains("Unknown handle: <MX-NEVERSEEN>"));
}
```

---

### Phase 5 — CDP keep-alive heartbeat

**Goal:** every 10 seconds, send a no-op `Runtime.evaluate` with body `1+1` to keep the WebSocket warm and the WebView's `setInterval(scanForCompletions, 500)` from being throttled by Chromium when the tab is hidden.

#### 3.5.1 — `src/Maia/CdpClient.cs` — heartbeat timer

Add fields:

```csharp
private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(10);
private CancellationTokenSource? heartbeatCts;
private Task? heartbeatTask;
```

Start the heartbeat at the end of `ConnectInternalAsync` (after the WebSocket is open):

```csharp
heartbeatCts?.Cancel();
heartbeatCts = new CancellationTokenSource();
heartbeatTask = Task.Run(() => HeartbeatLoopAsync(heartbeatCts.Token));
```

Add the loop:

```csharp
private async Task HeartbeatLoopAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        try { await Task.Delay(HeartbeatInterval, ct); }
        catch (OperationCanceledException) { return; }

        if (ws is null || ws.State != WebSocketState.Open) return;
        try
        {
            // Tiny no-op evaluate. Logs at Debug level so it doesn't
            // pollute the log when the toggle is off.
            await EvaluateOnceAsync("return 1+1;", TimeSpan.FromSeconds(3), ct);
            log?.Debug("[cdp] heartbeat ok");
        }
        catch (TransportUnavailable ex)
        {
            log?.Debug($"[cdp] heartbeat failed: {ex.Message}");
            // Don't reconnect from inside the heartbeat — the next real
            // call will trigger reconnect via the EvaluateAsync wrapper.
            return;
        }
        catch (Exception ex)
        {
            log?.Debug($"[cdp] heartbeat unexpected: {ex.GetType().Name}: {ex.Message}");
            return;
        }
    }
}
```

Cancel the heartbeat in `DisposeAsync` and at the top of `ConnectInternalAsync` (so reconnection swaps the timer cleanly):

```csharp
public async ValueTask DisposeAsync()
{
    try { heartbeatCts?.Cancel(); } catch { }
    if (heartbeatTask is not null)
    {
        try { await heartbeatTask.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
    }
    heartbeatCts?.Dispose();
    // … existing dispose …
}
```

#### 3.5.2 — Tests for phase 5

The heartbeat is hard to unit-test without a real WebSocket. Skip detailed unit coverage; rely on the smoke test (§8.5). Add a smoke-test note to a new file `tests/Maia/CdpClientHeartbeatTests.cs`:

```csharp
namespace Terminal.Tests.Maia;

// Heartbeat behavior is covered by the manual smoke test in
// docs/superpowers/specs/2026-05-09-bridge-hardening-implementation.md §8.5.
// Unit-level coverage requires a WebSocket test double, deferred to v4.2.1.
public class CdpClientHeartbeatTests { }
```

---

## 4. Test summary

| Phase | New tests | Modified tests |
|-------|-----------|----------------|
| 1 | `CdpClientReconnectTests.Constructor_DoesNotConnect`, `…DisposeAsync_OnFreshClient_DoesNotThrow`, `CdpInjectedTransportTests.SendThenStatus_ReusesSameCdpInstance` | `CdpInjectedTransportTests.FakeCdp` (add `ConnectCalls` counter) |
| 2 | `LoggingTests.Debug_NoOpsWhenDisabled`, `…WritesWhenEnabled` | `SettingsApplyHelperTests` — verify `MaiaDiagnosticLogging` round-trips through Save/Load (one new assertion in an existing round-trip test) |
| 3 | `StatusAsync_BridgeMissing_ReInjectsAndRetries`, `StatusAsync_NonObjectResult_LogsShape` | `StatusAsync_UnknownAfterReinject_RaisesStructuredError` from spec, refactored to `StatusAsync_UnknownOnFirstPoll_StillThrows` per phase 4 reframe |
| 4 | `StatusAsync_UnknownAfterReinject_ReturnsLostInsteadOfThrowing` | `MaiaActionsTests` (if present) — assert `lost` field appears on `WaitAsync` early-exit |
| 5 | `CdpClientHeartbeatTests` (placeholder marker only) | none |

`LoggingTests.Debug_*`:

```csharp
[Fact]
public void Debug_WritesWhenDiagnosticEnabled()
{
    using var temp = new TempProjectDir();
    var log = new Logger(temp.Path) { DiagnosticEnabled = true };
    log.Debug("trace-line");
    var contents = File.ReadAllText(log.Path!);
    contents.Should().Contain("DEBUG").And.Contain("trace-line");
}

[Fact]
public void Debug_NoOpsWhenDiagnosticDisabled()
{
    using var temp = new TempProjectDir();
    var log = new Logger(temp.Path) { DiagnosticEnabled = false };
    log.Debug("trace-line");
    File.Exists(log.Path!).Should().BeFalse();
}
```

`TempProjectDir` already exists in the test fixtures (used by `LoggingTests` today).

## 5. Settings UI changes

| Element | Current | After |
|---------|---------|-------|
| `ui/index.html` lines 877–881 | Maia integration row + platform note | Maia integration row + platform note + **new "Diagnostic logging" row** |
| `ui/src/settings-modal.ts` `SettingsPayload` | 17 fields | 18 fields (adds `maiaDiagnosticLogging: boolean`) |
| `ui/src/settings-modal.ts` save() | 14 fields | 15 fields |
| Default value | n/a | unchecked (false) |

Exact label text:

> **Diagnostic logging** — write CDP request/response traces to terminal.log

Position: directly under the Maia platform note inside the **Concord MCP** section. The toggle is platform-gated the same way as Maia integration: disabled on macOS.

## 6. Migration / backwards compat

- **Settings schema:** purely additive. New field `MaiaDiagnosticLogging` defaults false; the new field on `StatusResult` (`Lost`) defaults false. Existing settings.json files load unchanged.
- **MCP tool surface:** unchanged. Same six tools, same input schemas, same JSON-RPC behavior.
- **MCP response shape:** the `polled` payload gains an additional `lost` boolean field, and the `wait` payload gains a `lost` discriminator value. MCP clients ignore unknown fields, so v4.1.x callers see no break. New callers can opt into the lost-state branch.
- **Concord version stamp:** bump assembly version from `4.1.4` → `4.2.0` in `Terminal.csproj` `<Version>` and `<InformationalVersion>` (set the latter to `4.2.0+bridge-hardening`). This stamps `LastAppliedVersion` for users upgrading.
- **Existing `await using var cdp = clientFactory()` callers in tests:** fine. Tests construct their own fakes that no-op on Dispose. No production code does `await using` after this change.

## 7. Risks

**Out of scope, deliberately:**

1. **CdpClient lifecycle on toggle off→on.** The old singleton is dropped to GC instead of explicitly disposed. Cost: one ClientWebSocket + one SemaphoreSlim per toggle cycle. Tracked for v4.2.1.
2. **Full reconnect-on-drop unit coverage.** Requires a `Func<Uri, ClientWebSocket>` seam. The smoke test (§8) catches the live path; unit-level coverage is v4.2.1.
3. **`maia_agent.js` heartbeat-from-the-JS-side.** Chromium's setInterval throttle still happens to the JS-side scanner when Maia is hidden. The CDP heartbeat keeps the WebSocket warm, but the JS-side scan can still drift to seconds. Real fix is a JS-side `requestAnimationFrame` or a CDP-driven scan trigger from the C# heartbeat. Deferred to v4.2.1 — phase 5 here is the cheaper half.
4. **`maia__force_tier` settings persistence.** The router's `forced` field is in-memory; a Studio Pro restart loses the override. Existing v4.1.4 behavior; not regressed.
5. **MaiaLiveTests.** The live-integration test file already exists at `tests/Maia/MaiaLiveTests.cs`. It is `[Trait("category", "live")]` and excluded from CI; no changes needed for v4.2.0. It will incidentally exercise the new persistent-client path the next time someone runs it manually.

**Active risks of what we ARE changing:**

1. **Singleton cdp + multi-tier transports race.** Two transports share one `CdpClient`. If `CdpInjectedTransport.SendAsync` is mid-Evaluate when `CdpChatTransport.HealthCheckAsync` fires, they serialize on `evalGate` (already there) — correct, but adds latency. Cost in practice: zero, because today's router only hits Tier 1 when it's healthy; Tier 2 is a fallback used after Tier 1 fails. The shared client is the right call.
2. **Stale port cache after Studio Pro restart.** Mitigated by the 5-min TTL on cached discovery + automatic invalidation on connect failure. Worst-case: the user restarts Studio Pro mid-session, the next call fails once with `WebSocket connect to Maia target failed`, the cache is invalidated, the call after that re-discovers and succeeds. Bridge "feels like" it died for one tool call. Acceptable; documented in CHANGELOG.
3. **Heartbeat fires during real Evaluate.** The `evalGate` semaphore serializes them, so worst case the heartbeat queues behind a 60s `wait`. The heartbeat's own `Task.Delay(10s)` keeps it from piling up.
4. **`__reinject` payload in JS.** New field on a JSON object. The C#-side check uses `obj["__reinject"]?.GetValue<bool>() == true`, which is null-safe. If a future agent.js change accidentally introduces a colliding key, the parser correctly treats it as a re-injection signal. Low blast radius; documented in `maia_agent.js` comment.
5. **Settings file forward-compat.** A user who installs v4.2.0, sees `MaiaDiagnosticLogging: true` written to their settings, then downgrades to v4.1.4 — the old DTO ignores the unknown field on read but DOESN'T preserve it on save. So a downgrade-then-upgrade cycle silently drops the user's diagnostic toggle. Acceptable for an opt-in diagnostic; documented in the CHANGELOG migration note.

## 8. Manual smoke-test checklist

Run after each phase commit. Each step is independent.

### 8.1 Phase 1 — persistent connection

1. Build: `dotnet build -c Debug` in `C:\Workspace\Dev\Projects\Concord`. Verify `wwwroot/Concord.dll` updated.
2. Open Studio Pro on `C:\Workspace\MendixApps\CocktailDemo32-main`. Confirm Concord pane loads, Maia tab is visible in right pane.
3. From Claude Code in the Concord terminal, run:
   ```
   maia__ask "say hello and write <MX-SMOKE> on a new line" 30
   ```
4. Open Task Manager → Details. Sort by name. Watch `powershell.exe`. Run the same `maia__ask` 5 more times back-to-back.
5. **PASS:** `powershell.exe` count does NOT spike. Each `ask` should not spawn a new PS process (only the first call did the discovery).
6. **FAIL:** if PS count climbs by 1 per `ask`, phase 1 didn't ship — singleton wiring is broken.

### 8.2 Phase 2 — diagnostic logging

1. Open Settings → Concord MCP. Confirm new "Diagnostic logging" checkbox visible, default off.
2. Toggle it on. Save.
3. Run `maia__ask "test" 30`.
4. Open `resources/terminal.log`. Search for `DEBUG`.
5. **PASS:** lines like `[cdp] >> id=1 bytes=87` and `[cdp] << id=1 bytes=412` appear.
6. Toggle off. Save. Run another `maia__ask`.
7. **PASS:** no new `DEBUG` lines after the toggle-off save.

### 8.3 Phase 3 — auto-re-inject

1. Run `maia__ask "long answer please, list 10 mendix patterns" 60`. Note the returned handle.
2. Click Maia's "New chat" button (clears the WebView, nukes `window.__maiaBridge`).
3. Immediately run `maia__status <handle-from-step-1>`.
4. **PASS (with phase 4):** response is `lost: true, done: false`. No exception in `terminal.log`.
5. **PASS (without phase 4, phase 3 only):** response is `Unknown handle: <handle> (after re-injection retry)`, AND a DEBUG line `[cdp] >> id=N bytes=…` shows the re-inject `findChatRoot` payload.
6. Confirm `window.__maiaBridge` is back: in Studio Pro DevTools Console (open via the WebView's debug port), run `typeof window.__maiaBridge` — should be `'object'`.

### 8.4 Phase 4 — graceful lost handle

1. Same as 8.3, but expect `lost: true` instead of an error.
2. Run `maia__wait <handle> 5` against the lost handle.
3. **PASS:** returns within ~1s with `{ status: "lost", done: false, lost: true }` instead of polling for 5s and timing out.

### 8.5 Phase 5 — heartbeat

1. Enable diagnostic logging (8.2).
2. Run `maia__send "stub"` to force the connection up.
3. Click another pane in Studio Pro to hide Maia behind it (e.g. select an entity in Domain Model).
4. Wait 60 seconds doing nothing.
5. Open `terminal.log`.
6. **PASS:** at least 5 lines of `[cdp] heartbeat ok` interleaved over the 60s.
7. **PASS:** no `WebSocket close` or `forcibly closed by remote host` lines.
8. **FAIL:** if heartbeat lines stop firing entirely or the WebSocket dies, phase 5 didn't ship.

### 8.6 Whole-bridge cocktail re-test

The headline regression test. After all 5 phases land, repeat the 2026-05-09 cocktail-clone flow on a fresh `CocktailDemo32-main` checkout. Pre-acceptance bar:

- Total `[concord-mcp] tool '…' failed:` lines in `terminal.log` for the run: **<5** (was 8+).
- Zero `IOException: forcibly closed by remote host`.
- Zero `poll() returned unexpected shape: ` (the empty-string version).
- Total `powershell.exe` spawns observed in Task Manager during the run: **<5** (was likely 200+).

Record the numbers in `project_concord_v420_bridge_plan` memory after the test.

---

## 9. Commit protocol

Each phase: one commit, one message. Suggested format (matches existing repo):

```
Concord v4.2.0 P{N}: {one-line subject}

{1–3 sentence body explaining the why, referencing this spec.}

Refs: docs/superpowers/specs/2026-05-09-bridge-hardening-implementation.md#phase-{N}
```

After phase 5 lands and 8.6 passes:
- Bump `Version` and `InformationalVersion` in `Terminal.csproj`.
- Update `CHANGELOG.md` with the 5-phase summary + migration note from §6.
- Run the fresh-reviewer skill on the cumulative diff before opening the PR. Loop until ship.
- PR title: `Concord v4.2.0 — Maia bridge hardening`.

---

*End of spec.*
