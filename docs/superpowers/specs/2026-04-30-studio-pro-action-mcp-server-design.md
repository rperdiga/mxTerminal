# Studio Pro Action MCP Server — Design

**Date:** 2026-04-30
**Status:** Design — pending implementation plan
**Touches:** `Terminal.csproj` (new files), `TerminalPaneExtension`, `TerminalSettings`, `McpJsonConfigurator`, `McpTomlConfigurator`, settings modal UI

## 1. Goal & context

Claude Code (and Codex / Copilot CLI) running inside the Terminal pane can today talk to Studio Pro's built-in MCP server, but that server only exposes model-introspection and document tools (16 tools — none for runtime control). When Claude edits a microflow or a page on disk, the developer still has to alt-tab to Studio Pro and press F5 themselves to see the change. Worse, Claude has no way to verify the change works without asking the user to start the runtime.

This design adds a **second MCP server hosted in-process by the Terminal extension** that exposes three Studio Pro lifecycle actions:

- `run_app` — start the local Mendix runtime
- `stop_app` — stop it
- `refresh_project` — reload the model from disk (so external edits to model files are picked up)

It does **not** add the actions to Studio Pro's existing MCP server (we don't own that server) — instead Claude Code is configured with both servers and discovers our actions automatically.

## 2. Out of scope

- `build_app` / `deploy_app` as separate tools (covered by `run_app` since Studio Pro does an incremental build before run)
- Introspection tools like `get_run_state` / `get_errors` — Studio Pro's built-in MCP already provides `ped_check_errors`; run-state is implicit in each action's structured return
- Notifications stream (MCP `notifications/message`) — over-engineered for v1
- Auto-tail of runtime log into a Terminal tab — keeps coupling low
- Per-action user confirmation prompts — explicitly chosen against; toasts only
- Hot-reload-without-restart variants of refresh
- Confirmation/auth on the HTTP endpoint — localhost-only bind matches Studio Pro's own MCP server posture

## 3. Architecture

```
                                Studio Pro process (one DLL load)
┌──────────────────────────────────────────────────────────────────────────────┐
│                                                                              │
│  Existing pieces (unchanged):                                                │
│   • TerminalSessionManager   — owns PTYs                                     │
│   • TerminalPaneViewModel    — WebView ↔ session bridge                      │
│   • McpJsonConfigurator      — writes .mcp.json (Claude/Copilot)             │
│   • McpTomlConfigurator      — writes ~/.codex/config.toml                   │
│                                                                              │
│  New pieces:                                                                 │
│   ┌───────────────────────────┐                                              │
│   │ StudioProActionServer     │  HTTP listener on configurable port          │
│   │ (in-process, async)       │  (default 7783). Implements MCP              │
│   │                           │  streamable-HTTP per the same protocol       │
│   │  Tools:                   │  Studio Pro's own server speaks.             │
│   │   - run_app               │  127.0.0.1-only bind.                        │
│   │   - stop_app              │                                              │
│   │   - refresh_project       │                                              │
│   └────────────┬──────────────┘                                              │
│                │ invokes                                                     │
│                ▼                                                             │
│   ┌───────────────────────────┐                                              │
│   │ StudioProUiAutomation     │  Win32 PostMessage of WM_KEYDOWN/WM_KEYUP    │
│   │                           │  to Studio Pro main HWND. Reuses Studio      │
│   │                           │  Pro's own F5/Shift+F5/Refresh handlers.     │
│   └───────────────────────────┘                                              │
│                                                                              │
│   ┌───────────────────────────┐                                              │
│   │ RunStateProbe             │  TCP-connect probe of the active local-run   │
│   │                           │  configuration's port (read via              │
│   │                           │  ILocalRunConfigurationsService) to detect   │
│   │                           │  "running" vs "stopped" pre/post action.     │
│   └───────────────────────────┘                                              │
│                                                                              │
│  Settings (extended):                                                        │
│   • Existing settings UI grows a "MCP action server" section: enable toggle, │
│     port. Off by default. When enabled, the existing MCP configurators       │
│     register a second server entry named "mendix-studio-pro-actions" in      │
│     .mcp.json / config.toml so Claude Code automatically discovers it.       │
└──────────────────────────────────────────────────────────────────────────────┘
```

**Key choices:**

- **Same process** as the Terminal extension. Dies with Studio Pro. No external launcher. Can call `Application.Instance.Invoke(...)` to marshal UI-touching work onto Studio Pro's UI thread.
- **127.0.0.1-only**, no auth. Matches the existing Studio Pro MCP server posture; sufficient for dev environments.
- **Off by default**, opt-in via Settings — same posture as the existing MCP integration.
- **Re-uses** `McpJsonConfigurator` / `McpTomlConfigurator`. From Claude's perspective it's just a second MCP server.

## 4. Components

### 4.1 `StudioProActionServer` (new, ~150 LOC)

Hand-rolled JSON-RPC-over-HTTP listener. Reuses the wire format we already understand from [`McpProbe`](../../src/McpProbe.cs). Implements three MCP methods:

- `initialize` → returns `serverInfo: { name: "mendix-studio-pro-actions", version: "1.0.0" }`, `capabilities.tools.listChanged = false`
- `tools/list` → returns the three tool definitions with input schemas (see §5)
- `tools/call` → dispatches by `name`

Hand-rolled rather than the official `ModelContextProtocol` NuGet because (a) the codebase already does JSON-RPC by hand in `McpProbe`, (b) 3 tools doesn't justify the dependency, (c) the official SDK can replace it later without consumers noticing.

Listens on `HttpListener` at `http://127.0.0.1:<port>/mcp`. Owned by the singleton [`TerminalSessionManager`](../../src/TerminalSessionManager.cs) (same lifecycle as the PTY map: starts when settings flip the `ActionsServerEnabled` toggle on, stops when flipped off, and is disposed at extension unload alongside the existing PTY cleanup).

### 4.2 `StudioProUiAutomation` (new, ~80 LOC)

Three methods:
- `TriggerRun()`
- `TriggerStop()`
- `TriggerRefreshFromDisk()`

Implementation: `[DllImport("user32.dll")] PostMessage(hwnd, WM_KEYDOWN, vk, lparam)` followed by `WM_KEYUP`, with the `hwnd` resolved as `Process.GetCurrentProcess().MainWindowHandle`. PostMessage doesn't require focus, so this works while the user is typing in the Terminal pane.

The exact Studio Pro key bindings (F5 / Shift+F5 / Ctrl+F5 / something else) are looked up at implementation time from Studio Pro's own menu definitions; the spec leaves a TODO for that lookup. If a binding can't be resolved, `TriggerXxx` falls back to no-op + warn.

### 4.3 `RunStateProbe` (new, ~40 LOC)

Returns `bool IsRunning()` by:
1. Resolving the active local-run configuration's port via `ILocalRunConfigurationsService.GetActiveConfiguration(model)` (this is a public extensibility API — confirmed in `Mendix.StudioPro.ExtensionsAPI.xml`).
2. TCP `Connect` to `127.0.0.1:<port>` with a short timeout (e.g. 250 ms).
3. Connect succeeds → running; refused → not running; timeout → unknown (treat as not running for action purposes).

If `GetActiveConfiguration` returns no configuration or doesn't expose a port, `IsRunning` returns `unknown` and the caller skips the pre/post status logic (returns `{ status: "command_sent" }` instead of `started/already_running`).

### 4.4 `TerminalSettings` extension

Add two fields to the settings record at [`TerminalSettings`](../../src/TerminalSettings.cs):

```csharp
public bool ActionsServerEnabled { get; init; } = false;
public int  ActionsServerPort    { get; init; } = 7783;
```

JSON keys `actionsServerEnabled` and `actionsServerPort` in `terminal-settings.json`. Existing settings stay backward-compatible (missing fields → defaults).

### 4.5 Configurator extension

[`McpJsonConfigurator`](../../src/McpJsonConfigurator.cs) and [`McpTomlConfigurator`](../../src/McpTomlConfigurator.cs) each get a parallel pair of methods for the second server name:

```csharp
public const string ActionsServerName = "mendix-studio-pro-actions";

public void UpsertActions(string url) { ... }
public void RemoveActions() { ... }
```

These mirror the existing `Upsert(url)` / `Remove()` for the primary `mendix-studio-pro` server name. Implementation refactor:

- `McpJsonConfigurator`: trivial — already keys by `ServerName`; extract a private `Upsert(string serverName, string url)` / `Remove(string serverName)` and have the existing public `Upsert`/`Remove` plus the new `UpsertActions`/`RemoveActions` delegate to them.
- `McpTomlConfigurator`: `SectionHeader` is currently a `private static readonly` field derived from `ServerName`. Refactor `FindSection(List<string> lines)` to `FindSection(List<string> lines, string sectionHeader)`; have the existing `Upsert`/`Remove` and the new `UpsertActions`/`RemoveActions` build the appropriate header string and pass it through.

Both configurators preserve backward compatibility — existing public method signatures are unchanged.

### 4.6 Settings UI

Add a "MCP Actions Server" section to the existing settings modal:
- Enable toggle
- Port input

On save:
- If toggle is on and the existing toggle is also on, both `Upsert`s run (primary server entry + actions server entry).
- If the actions toggle was just flipped on, probe our own server URL after starting it (same probe-before-save pattern as the existing MCP setup) and roll back if it doesn't answer.
- If the actions toggle was flipped off, run `RemoveActions()` on both configurators and stop the server.

## 5. Tool specifications

### 5.1 `run_app`

```json
{
  "name": "run_app",
  "description": "Start the local Mendix runtime for the currently open Studio Pro app. If already running, returns 'already_running' without disturbing it.",
  "inputSchema": {
    "type": "object",
    "properties": {},
    "required": []
  }
}
```

**Behavior:** probe state-before → trigger run via UI automation → wait for runtime port to open (with timeout, e.g. 30 s) → probe state-after → return.

**Returns (one of):**
- `{ "status": "started", "url": "http://localhost:8080" }` (port from active local-run config)
- `{ "status": "already_running", "url": "http://localhost:8080" }`
- `{ "status": "command_sent" }` (state could not be probed; user should check Studio Pro)
- `{ "error": "<message>" }` (e.g. main window unavailable)

### 5.2 `stop_app`

```json
{
  "name": "stop_app",
  "description": "Stop the local Mendix runtime. No-op if it isn't running.",
  "inputSchema": { "type": "object", "properties": {}, "required": [] }
}
```

**Behavior:** probe state-before → trigger stop via UI automation → wait for port to close (timeout, e.g. 10 s) → return.

**Returns (one of):**
- `{ "status": "stopped" }`
- `{ "status": "wasnt_running" }`
- `{ "status": "command_sent" }`
- `{ "error": "<message>" }`

### 5.3 `refresh_project`

```json
{
  "name": "refresh_project",
  "description": "Reload the project model from disk. Use after editing model files (e.g. microflow XML) outside Studio Pro to make the IDE pick up the changes.",
  "inputSchema": { "type": "object", "properties": {}, "required": [] }
}
```

**Behavior:** trigger refresh-from-disk via UI automation → return.

**Returns (one of):**
- `{ "status": "reloaded" }`
- `{ "error": "<message>" }`

If Claude wants to know whether the refreshed model has errors, it calls Studio Pro's existing `ped_check_errors` MCP tool — we don't duplicate that here.

## 6. Data flow — single `run_app` call

1. Claude Code (in the Terminal pane) → POST `http://127.0.0.1:7783/mcp` with `{ "jsonrpc": "2.0", "method": "tools/call", "params": { "name": "run_app", "arguments": {} } }`
2. `StudioProActionServer` parses → dispatches to `RunAppHandler`
3. Handler acquires the in-flight lock (one action at a time across all three tools)
4. `RunStateProbe.IsRunning()` → state-before
5. `Application.Instance.Invoke(() => uiAutomation.TriggerRun())` to marshal onto Studio Pro's UI thread
6. Wait up to 30 s for `IsRunning()` to flip to true (poll every 500 ms)
7. Show toast via `INotificationPopupService.ShowNotification("Claude Code", "Run triggered")`
8. Release lock
9. Return JSON: `{ "status": "started", "url": "http://localhost:8080" }` (or appropriate alternative — see §5.1)

`stop_app` and `refresh_project` follow the same shape with their own UI-automation calls and timeouts.

## 7. Error handling

| Failure | Where | Response |
|---|---|---|
| Studio Pro main `HWND` is `IntPtr.Zero` (extension loaded before the IDE finished initializing) | `StudioProUiAutomation.TriggerXxx` | Return MCP `tools/call` error: `"Studio Pro main window unavailable; try again after the IDE finishes loading"`. Don't crash. |
| Port already in use when starting the server | `StudioProActionServer.Start` | Log via `Logger`, surface in Settings UI as a probe failure (matches existing `McpProbe` rollback pattern), settings save fails cleanly with the toggle reverting. |
| `ILocalRunConfigurationsService` returns null / no active config | `RunStateProbe` | Treat as "unknown state"; skip pre/post probe and return `{ status: "command_sent" }` |
| `Application.Instance.Invoke` throws (UI thread dead during shutdown) | `tools/call` handler | Catch + return MCP error. Don't propagate as HTTP 500. |
| Claude calls `run_app` twice within 100 ms | Server | Single in-flight lock around UI-automation triggers. Second call waits, sees state-after the first, returns `"already_running"`. |
| `INotificationPopupService` not registered (older Studio Pro version) | Toast | Log `Warn` and continue — toast is informational, not load-bearing. |
| `refresh_project` runs while there are unsaved changes in Studio Pro | UI automation | Studio Pro's own refresh-from-disk handler decides; we don't second-guess (consistent with what F5-equivalent does for the user manually). If it shows a "save first?" dialog, that surfaces in Studio Pro UI as it normally would. |
| HTTP request body is malformed JSON | Server | Return JSON-RPC `-32700` parse error per spec. |
| Unknown tool name in `tools/call` | Server | Return JSON-RPC `-32601` method-not-found-style error with helpful message. |

## 8. Testing strategy

Mirrors what already exists in [`tests/`](../../tests/):

**Unit-testable in xUnit + FluentAssertions:**
- `StudioProActionServer` JSON-RPC routing — `HttpClient` against the listener bound to `127.0.0.1:0` (ephemeral port). Inject fakes for the action layer (`IStudioProUiAutomation`, `IRunStateProbe`).
- `RunStateProbe` — point at a `TcpListener` that opens/closes at controlled times.
- `McpJsonConfigurator` extension — straight extension of `McpJsonConfiguratorTests` for the second server name.
- `McpTomlConfigurator` extension — same.
- `TerminalSettings` round-trip with the two new fields.

**Not unit-testable, manual smoke** (matches existing `TerminalPaneExtension` / `TerminalSessionManager` posture in the original design):
- `StudioProUiAutomation` — sending Win32 messages requires Studio Pro. Manual smoke step with a sample `run_app` → expected runtime starts, `stop_app` → expected runtime stops, `refresh_project` after editing a microflow XML on disk → expected IDE shows the new state.
- End-to-end Claude Code → MCP → UI → toast flow — manual, with the test recipe listed in the implementation plan.

## 9. Settings & migration

`terminal-settings.json` migration: missing `actionsServerEnabled` / `actionsServerPort` fields default to `false` / `7783`. No migration logic needed — backward-compatible deserialization is already how the existing settings record works.

`.mcp.json` / `~/.codex/config.toml` migration: existing entries for `mendix-studio-pro` are preserved untouched. The new `mendix-studio-pro-actions` entry is only added when the user enables the actions toggle.

## 10. Open questions / TODOs for implementation

- **Studio Pro's actual hotkey bindings for Run / Stop / Refresh-from-disk.** The spec assumes F5 / Shift+F5 / Ctrl+F5 but the implementation step must confirm by inspecting Studio Pro's menu bindings (or the user can paste them in). If a binding doesn't exist as a hotkey, fall back to UIA invoke of the menu item by name.
- **Mendix runtime port discovery.** `ILocalRunConfigurationsService.GetActiveConfiguration` returns a configuration object; the implementation step must confirm which property holds the runtime port (or whether it's separately exposed).
- **Probe timeout values** (30 s for run, 10 s for stop) are starting points; tune during smoke testing.
- **Toast wording.** Suggestion: "Run triggered" / "Stop triggered" / "Refreshed from disk". Final wording trivial, deferred to implementation.
