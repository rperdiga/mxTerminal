# Concord MCP — Maia Bridge embedded in C#

**Date:** 2026-05-08
**Status:** Design — approved sections, pending implementation plan
**Audience:** Concord maintainers
**Scope:** v1.3.0 of the Concord extension

## Context

Concord ships an in-process MCP server today ([src/StudioProActionServer.cs](../../../src/StudioProActionServer.cs)) that exposes Studio Pro UI actions (run/stop/refresh/save_all/get_app_status) as MCP tools on `127.0.0.1:7783`. It currently identifies on the wire as `mendix-studio-pro-actions` v1.0.0.

A separate Python prototype at [C:\Extensions\mxTerminal\AppBuildAuto1\concord-maia-bridge](file:///C:/Extensions/mxTerminal/AppBuildAuto1/concord-maia-bridge) proves that programmatic Claude ↔ Maia communication is feasible by attaching to Studio Pro's WebView2 panel via Chrome DevTools Protocol (CDP) on the localhost `--remote-debugging-port`. The prototype is ~1100 lines of Python with a 2-tier transport router (injected JS agent + DOM-scrape fallback) and 48 automated tests.

This design ports that bridge into Concord as a fully native C# subsystem — no Python, no subprocess, no second port. It also renames the Concord MCP server's wire identity to `concord-mcp` so the new combined surface (Studio Pro UI actions + Maia integration) reads coherently.

This is **internal CoE tooling**, not a Marketplace-bound build. The Mendix R&D coordination preconditions described in the prototype's `INTEGRATION.md` do not block this work.

## Goals

1. Maia bridge functionality available as MCP tools from Concord with zero external processes.
2. Existing `mendix-studio-pro-actions` server renamed to `concord-mcp` (v1.3.0), housing both tool families in one HTTP MCP endpoint on the existing port.
3. Settings UI surfaces Maia integration as a first-class toggle inside a renamed "Concord MCP" sidebar section.
4. Transport layer is factored so a future tier-0 transport (Mendix 11.12 native MCP-server-as-tool) can replace CDP without touching the rest of the codebase.
5. macOS users see a coherent, gracefully-disabled Maia toggle — no broken behavior, no half-shipped tools.

## Non-goals

- Marketplace distribution of CDP-driven Maia integration (requires Mendix R&D conversation; out of scope here).
- macOS Maia transport implementation. Architecture supports adding one later; this iteration ships zero tiers on mac.
- Replacing the existing port-binding lifecycle in `StudioProActionServer`. Master toggle still controls whether the listener binds at all.
- Exposing a `maia__health` tool to MCP callers. Internal probing happens for tier selection; the user-visible surface stays at the proven failure-message pattern.

## Approach

**Adjacent module, minimal touch to existing server.** A new `src/Maia/` folder holds the entire bridge; `StudioProActionServer.cs` gains six `maia__*` tool registrations alongside its existing tools, plus the `ServerName` rename. Considered alternatives:

- **Tool-provider refactor first** — clean but adds a refactor PR before the feature PR; tool-level abstraction not yet load-bearing.
- **Separate MCP server on a second port** — worst UX (clients configure two endpoints); contradicts the "one process, one server" goal.

## Architecture

```
Claude / Codex / Copilot CLI
        │  HTTP MCP (streamable, JSON-RPC)
        ▼
StudioProActionServer  (127.0.0.1:7783)
   ServerName = "concord-mcp", Version = 1.3.0
        │
        ├── existing actions ── StudioProActions ── (probe / UIA)
        │
        └── maia tools ──────── MaiaActions ────── MaiaRouter
                                                    │
                                            picks lowest available IMaiaTransport
                                            ┌─────────────┬───────────────┐
                                            ▼             ▼               ▼
                                    CdpInjectedTransport  CdpChatTransport  (future:
                                    (Tier 1, JS agent)   (Tier 2, scrape)   NativeMcpTransport,
                                                                            Tier 0)
                                            │             │
                                            └─────────────┴───────────► CdpClient
                                                                          (System.Net.WebSockets,
                                                                           process scan via WMI)
                                                                                │
                                                                                ▼
                                                              msedgewebview2.exe (Maia panel),
                                                              --remote-debugging-port=N
```

Three boundaries that matter:

1. **`IMaiaTransport`** — the seam future Mendix-native MCP transport rides through. Properties: `Name`, `Tier`. Methods: `HealthCheckAsync`, `SendAsync`, `StatusAsync`, `ResetAsync`. Signals demotion via custom `TransportUnavailable` exception.
2. **`ICdpClient`** — single-purpose wrapper around process discovery + `/json` endpoint + WebSocket I/O. One implementation, used by both CDP-based transports. Only place fakes live in unit tests.
3. **`MaiaActions`** — verb layer mapping `maia__send/status/wait/ask/reset/force_tier` onto `MaiaRouter`. Returns the existing `ActionResult` record so JSON shapes stay uniform across all Concord MCP tools.

### Settings UX

The existing settings sidebar item `"Action bridge"` is renamed to `"Concord MCP"` and gains a master + two symmetric sub-toggles:

```
Concord MCP
  Concord runs an in-process MCP server (concord-mcp) that exposes Studio Pro
  capabilities to the CLIs you use inside this terminal.

  [x] Enable Concord MCP server
      Concord MCP listening on localhost:7783

      Tool families:
        [x] Studio Pro UI actions      run, stop, refresh, save_all, get_app_status
        [x] Maia integration           send, status, wait, ask, reset, force_tier
            (Windows only — disabled on macOS)
```

Tri-state behavior:

| Master | Studio Pro UI actions | Maia | Platform | Result |
|---|---|---|---|---|
| off | — | — | any | no MCP server, no tools |
| on | on | off | any | server up, only Studio Pro UI action tools listed |
| on | off | on | Windows | server up, only maia tools listed |
| on | on | on | Windows | server up, both families listed |
| on | * | on | macOS | server up, action tools listed; maia tools omitted |
| on | off | off | any | server up, empty `tools/list` (allowed but unusual) |

The `Studio Pro UI actions` sub-toggle is real (not pinned-on). Letting users opt into a Maia-only server is a legitimate config — and uniform UI without hidden behavior is easier to reason about.

## Components

Files added under `src/Maia/`:

```
src/Maia/
├── IMaiaTransport.cs       interface — name/tier/health_check/send/status/reset
├── ICdpClient.cs           interface — connect/evaluate/close
├── CdpClient.cs            WMI process scan + WebSocket plumbing
├── CdpInjectedTransport.cs Tier 1 — injects maia_agent.js, polls via _evaluate
├── CdpChatTransport.cs     Tier 2 — input + Enter + bubble-scrape fallback
├── MaiaRouter.cs           tier selection + 60s lazy re-probe + per-call demotion
├── MaiaActions.cs          verb layer (send/status/wait/ask/reset/force_tier)
├── MaiaTicket.cs           handle, sentinel, created_at, response, done flag
└── maia_agent.js           embedded resource (verbatim port from Python pkg)
```

### `IMaiaTransport`

```csharp
public interface IMaiaTransport
{
    string Name { get; }           // "cdp_injected", "cdp_chat", future "native_mcp"
    int Tier { get; }              // 1, 2, future 0
    Task<HealthStatus> HealthCheckAsync(CancellationToken ct);
    Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct);
    Task<StatusResult> StatusAsync(string handle, CancellationToken ct);
    Task ResetAsync(CancellationToken ct);
}
```

Router reads `Tier` (lowest wins), `Name` (logging/force_tier), `HealthCheckAsync` (probe). `TransportUnavailable` (custom exception) signals "demote me".

### `ICdpClient`

```csharp
public interface ICdpClient : IAsyncDisposable
{
    Task ConnectMaiaAsync(CancellationToken ct);
    Task<JsonNode?> EvaluateAsync(string js, TimeSpan? timeout = null, CancellationToken ct = default);
}
```

Hides WMI scan, multi-instance guard, `/json` GET, WebSocket protocol. Implementation owns a `SemaphoreSlim` to serialize CDP `Runtime.evaluate` round-trips on a single WebSocket (the protocol requires correlated request/response).

### `MaiaRouter`

Owns the ordered transport list, a cached `Dictionary<string, HealthStatus>`, and a `_lastProbeAt` timestamp. Per-method contract: snapshot `_active`, try, catch `TransportUnavailable`, demote, retry; refresh probe lazily when `now - _lastProbeAt >= 60s`. Reprobe runs `Task.WhenAll` over all transports — total reprobe latency = max(probe latencies), not sum.

`_active` reads happen under a lock; CDP I/O happens outside the lock. A reprobe demoting `_active` mid-Send doesn't perturb the in-flight call (finishes against old transport); next call sees the new active.

The router additionally tracks `handle → transport_name` so `Status`/`Wait` route to whichever transport currently holds a ticket, even if `_active` has changed since `Send`.

### `MaiaActions`

```csharp
public sealed class MaiaActions
{
    public Task<ActionResult> SendAsync(string prompt, string? sentinel, CancellationToken ct);
    public Task<ActionResult> StatusAsync(string handle, CancellationToken ct);
    public Task<ActionResult> WaitAsync(string handle, double timeoutSec, CancellationToken ct);
    public Task<ActionResult> AskAsync(string prompt, double timeoutSec, CancellationToken ct);
    public Task<ActionResult> ResetAsync(CancellationToken ct);
    public Task<ActionResult> ForceTierAsync(string name, CancellationToken ct);
}
```

Returns existing `ActionResult` record so the JSON-RPC `result.content` shape matches existing tools. Auto-generates a unique short sentinel when caller omits one (exact format pinned during implementation against the prototype).

### Settings plumbing

- [src/TerminalSettings.cs](../../../src/TerminalSettings.cs): rename `ActionsServerEnabled` → `McpServerEnabled` (master). Add `StudioProActionsEnabled` and `MaiaIntegrationEnabled` (both default `true`). `Load` reads old key if new key absent (one-time migration).
- [src/Messages/Outgoing.cs](../../../src/Messages/Outgoing.cs) `SettingsPayload`: expose all three flags + a `platform` discriminator the modal uses to render the disabled-on-mac state.
- [src/Messages/Incoming.cs](../../../src/Messages/Incoming.cs) `SaveSettingsPayload`: accept all three flags.
- [ui/index.html](../../../ui/index.html): rename the section header and nav item; add the two sub-checkboxes; wrap with `(Windows only)` hint copy on Maia.
- [ui/src/settings-modal.ts](../../../ui/src/settings-modal.ts): render disabled-with-tooltip when `platform === "darwin"`; wire the two sub-toggles into the save payload.

The master checkbox continues to control whether the listener binds at all (existing behavior). Sub-toggles purely filter `tools/list` when the server is up.

## Data flow

### `maia__send` — non-blocking submit

```
Claude Code ──POST /mcp tools/call name=maia__send args={prompt, sentinel?}
              │
              ▼
StudioProActionServer.HandleToolsCallAsync
              │  dispatches → MaiaActions.SendAsync
              ▼
MaiaActions.SendAsync(prompt, sentinel ?? AutoSentinel())
              ▼
MaiaRouter.SendAsync
              │  _active = lowest-tier available
              │  try active.SendAsync; on TransportUnavailable → demote, retry
              ▼
CdpInjectedTransport.SendAsync (Tier 1)
              │  ensure agent injected (idempotent)
              │  evaluate window.__maiaBridge.submit(prompt, sentinel)
              │  agent: types into #MX_CHAT_INPUT, dispatches Enter,
              │          stores ticket keyed by sentinel,
              │          MutationObserver watches the chat list
              ▼
returns { handle, sentinel, transport, sent_at }
```

Ticket lifecycle (in-WebView): TTL 1h, cap 100 entries — same bounds as the Python agent. The JS agent owns ticket truth; Concord retains only `handle ↔ sentinel ↔ transport` correlation.

### `maia__status` — non-blocking peek

`status(handle) → MaiaRouter.StatusAsync → active.StatusAsync → Tier 1 evaluates `window.__maiaBridge.peek(sentinel)` → returns `{ done, response, streaming, elapsed_sec }`.

### `maia__wait` — server-side polling

Polls at a fixed 250ms cadence; bails on caller-supplied `timeout_sec` (default 60s). Returns the `status` shape plus a `timed_out` flag. Ticket stays alive on timeout; caller can re-issue `wait`.

### `maia__ask` — send + wait composition

Single response carrying `{ response, elapsed_sec, transport }`. Timeout propagates from the caller.

### `maia__reset` — clear injected-agent state

Routes to *every* transport's `ResetAsync` rather than just `_active`, since a non-active transport may still hold stale tickets from before a demotion. ≤2 CDP round-trips today.

### `maia__force_tier` — manual override

Mutates `_active` until next reprobe. Doesn't bypass the availability check — forcing an unavailable tier returns an error rather than re-demoting.

### Concurrency model

Maia tools do **not** acquire `StudioProActions.gate`. The JS agent's keyed-by-sentinel ticket Map is multi-ticket-aware; the per-WebSocket `SemaphoreSlim` in `CdpClient` is the only serialization point. Many in-flight `send`/`status` calls multiplex through one CDP WebSocket.

## Error handling

Three failure classes with defined response shapes:

| Class | Cause | Caught | Caller sees |
|---|---|---|---|
| **Transport-recoverable** | One transport fails (CDP eval errors, JS exception, target gone) | `MaiaRouter` catches `TransportUnavailable`, demotes, retries | Success if a lower tier handles; structured error only after all tiers exhausted |
| **Environment** | Maia panel closed, no `--remote-debugging-port`, multi-instance Studio Pro, msedgewebview2.exe gone | `CdpClient` raises `TransportUnavailable` | All tiers report unavailable → `{ "error": "<message>" }` |
| **Programmer/usage** | Unknown handle, bogus `force_tier` name, malformed args | `MaiaActions` validates | `ActionResult.Fail`, no demotion |

Custom exceptions (internal only — never cross JSON-RPC boundary):

- `TransportUnavailable(string reason)` — drives demotion. Reason becomes user-visible when all tiers exhaust.
- `CdpProtocolException(string method, JsonNode? response)` — CDP returned malformed response; treated as `TransportUnavailable` for routing.

User-visible error messages ported verbatim from the Python prototype:

- `"Maia panel not visible. In Studio Pro click the Maia tab (right pane) and retry."`
- `"Studio Pro WebView2 has no --remote-debugging-port (Studio Pro not running, or running with debug port disabled)."`
- `"Multiple Studio Pro instances detected (ports {…}). Close all but one Studio Pro instance and retry."`
- `"Injected agent could not locate the chat-list container. Maia panel may not be fully rendered."`
- `"CDP endpoint :{port}/json unreachable: {…}"`

JSON-RPC framing: tool calls return through the existing `tools/call` shape `result.content = [{ type: "text", text: "<json>" }]`. Errors land as `{ "error": "..." }` in that JSON. Top-level `error` envelope reserved for parse errors / unknown method (existing behavior).

Logging via Concord's existing `Logger`:

- `Info` — startup probe results, tier selection, force_tier overrides.
- `Warn` — every transport demotion with `from_tier`, `to_tier`, `reason`.
- `Error` — all-tiers-exhausted; CDP responses that don't match expected shape.
- `Debug` — gated by `CONCORD_MAIA_DEBUG=1` env var.

Timeout budgets (each layer bounded so a stuck Maia/CDP can't wedge a tool call):

- `EVALUATE_DEFAULT_TIMEOUT_SEC` = 10s per CDP `Runtime.evaluate`.
- WebSocket connect = 5s; `/json` GET = 3s.
- `WaitAsync`/`AskAsync` honor caller `timeout_sec`; default 60s.

Failure during settings save: if `MaiaIntegrationEnabled` flips on and the Maia subsystem fails to initialize (rare, since init is lazy at first call), the master MCP server stays up with just the Studio Pro UI action tools. `SaveSettingsResult.Warning` carries the message.

## Testing

### Layer 1 — Pure unit tests (xUnit, CI)

Located under `tests/Maia/`. Coverage:

- `MaiaRouterTests` (using `FakeTransport(name, tier, behavior)`):
  - Lowest-tier-available picked at startup.
  - Per-call `TransportUnavailable` demotes and retries.
  - All tiers exhausted → structured error, no throw past public surface.
  - 60s lazy reprobe: doesn't reprobe within window; does after.
  - Reprobe runs probes concurrently (timing or call-order observer).
  - `force_tier` mutates `_active`; rejects non-existent / unavailable names.
  - `_active` change mid-call: in-flight finishes against old transport.
- `MaiaActionsTests` (mocking `IMaiaTransport`/`MaiaRouter`):
  - Auto-sentinel format and uniqueness.
  - `Wait` polls at cadence, honors timeout, returns `timed_out=true` cleanly.
  - `Ask` composition with timeout propagation.
  - `Reset` calls every transport's `ResetAsync`.
  - Unknown handle / bogus `force_tier` → `ActionResult.Fail`.
- `MaiaJsonRpcTests` (test server bound to ephemeral port):
  - `tools/list` includes maia tools when master + Maia toggle + Windows.
  - Conditional registration: each toggle off (master, sub, OS) drops them.
  - Each `maia__*` `tools/call` returns the correct shape.
  - `ServerName == "concord-mcp"`, `Version == "1.3.0"`.
- `CdpClientTests` (fakes `/json` endpoint and WebSocket via in-memory loopback):
  - Multi-instance studiopro.exe → documented message.
  - No `--remote-debugging-port` → documented message.
  - `/json` unreachable → documented message.
  - `Runtime.evaluate` timeout → `TransportUnavailable`.
  - CDP error response → `CdpProtocolException`.
- `MaiaSettingsMigrationTests`:
  - Old schema (`actionsServerEnabled` only) loads with `McpServerEnabled` from old key, sub-toggles default `true`.
  - New schema round-trips identically.
  - Defaults on fresh install, Win and mac.

### Layer 2 — JS agent unit tests (Node subprocess)

Port the prototype's 12 JS tests for `maia_agent.js`. Driven via `Process.Start("node")` from xUnit. Node 20 already an assumed dev dep (Concord's `BuildUi` target runs `npm install --prefix ui`). Asserts structural-walk to find chat-list, `submit`/`peek` round-trip with mock DOM, sentinel matching, stability fallback, TTL/cap eviction.

### Layer 3 — Live tests (gated, manual-trigger)

Test trait `[Trait("Category","MaiaLive")]`, skipped unless `CONCORD_MAIA_LIVE=1`. xUnit `Skip` mechanism gates each fact at evaluation. Coverage mirrors prototype:

- Health probe finds Tier 1 active when Maia panel is open.
- `ask("In ten words, what file extension do Mendix project files use?")` returns `.mpr` text within 30s.
- Demotion path: artificially break Tier 1, verify Tier 2 takes over.
- Multi-instance guard fires on two Studio Pro processes.
- `force_tier("cdp_chat")` answers correctly; returns to Tier 1 on next reprobe.

### Layer 4 — UI manual smoke

Documented as a checklist on the PR:

- Master off → no port readout, sub-toggles greyed.
- Master on → readout shows bound port; sub-toggles editable on Windows; Maia disabled-with-tooltip on mac.
- Toggling Maia off mid-session → next `tools/list` from a CLI client omits maia tools.
- Settings persist across Studio Pro restart.

### CI

- Layers 1 + 2 run on every PR (existing matrix: Windows + macOS).
- Layer 3 documented in `tests/Maia/README.md`; not run in CI.
- Layer 4 in PR description.

## Risks and open questions

- **Mendix may disable `--remote-debugging-port`** in a future Studio Pro release. The bridge would silently break. Mitigation: the transport interface is the swap-out seam; if/when Mendix 11.12 ships native MCP-server-as-tool, register a tier-0 transport and the bridge keeps working without code changes elsewhere.
- **CSS class regeneration breaks Tier 2.** Tier 2 scrapes `p.sc-bPkUNa`, a styled-components-generated class that regenerates per Mendix build. This is a known weakness. Tier 1's structural walk survives; Tier 2 needs revisiting per Mendix release.
- **macOS Maia integration is shipped-as-disabled.** A real mac transport (e.g. via Studio Pro's mac WebView debug surface, if any) is a future iteration.
- **Multi-instance Studio Pro disambiguation by PID/project** is not implemented. Multi-instance currently raises `TransportUnavailable` with the documented message — same as the prototype.

## Out of scope (for this iteration)

- Tool-provider abstraction in `StudioProActionServer` (cleanup opportunity, not load-bearing yet).
- Mendix R&D conversation per `INTEGRATION.md` (preconditional only for Marketplace distribution; this is internal CoE tooling).
- macOS Maia transport.
- `maia__health` exposed as a user-facing MCP tool (internal probing only).
