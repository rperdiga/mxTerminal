# Changelog

## 5.0.0-alpha.2 — W2 SPMCP merge + Host10x UI port

**Feature merge.** MCPExtension's source-merged tool catalog ships as part of Concord. A standalone SPMCP install is no longer required — `concord-mcp` on port 7783 advertises the full SPMCP surface (on 10.x) or a curated subset (on 11.x where Studio Pro's built-in MCP covers the rest).

### Architecture

- **`Concord.Core` has zero references to `Mendix.StudioPro.ExtensionsAPI`.** All Studio Pro modeling routes through 7 new Interop interfaces (`IModelHost`, `IDomainModelHost`, `IPageGenerationHost`, `INavigationHost`, `IVersionControlHost`, `IUntypedModelHost`, `IMicroflowAuthoringHost`) registered on the existing `HostServices` registry. Each host DLL provides the version-specific implementation.
- **MCPExtension source-merged into `src/Concord.Core/Spmcp/`** (subtree from `github.com/mendix-community/mcp-extensions`). 87 tools across DomainModel, Microflows, Pages, Navigation, Security, Workflows, ConstantsEnums, DataSample, Diagnostics, ProjectSettings families. All refactored to read through `HostServices.*` accessors — no direct Mendix-type references in the merged source.
- **`ToolCatalog` is the single dispatch path.** `src/Concord.Core/Mcp/ToolCatalog.cs` + `ITool` + `ToolFamily` + `Studio11xAllowlist`. SPMCP tools register via `SpmcpToolBootstrap{10|11}x`; UI-action tools (`run_app`, `stop_app`, `save_all`, `refresh_project`, `get_active_run_configuration`, `get_app_status`) via `UiActionsBootstrap`; Maia tools (10 entries) via `MaiaToolsBootstrap`. The hardcoded UI/Maia switch in `StudioProActionServer.HandleToolsCallAsync` retires. `tools/list` iterates the catalog.
- **Family toggles drive visibility.** Settings changes call `catalog.SetFamilyEnabled(ToolFamily.UiActions, ...)` and `(ToolFamily.Maia, ...)` — registering happens once at MEF activation; settings-save flips visibility without re-registration.

### Studio Pro 10.x

- **Full UI tier ported from Host11x.** `TerminalPaneExtension`, `TerminalPaneViewModel`, `TerminalWebServer`, `RunStateProbe`, `StudioProUiAutomation`, `TerminalMenuExtension` against the 10.21.1 ExtensionsAPI. The v5.0.0-alpha.1 "Concord (10.x preview)" placeholder is gone; clicking Extensions → Concord → Open Pane opens the real terminal pane.
- **Empirical finding: zero Mendix API drift between 10.21.1 and 11.6.2 across every UI type Concord consumes.** `DockablePaneExtension`, `WebViewDockablePaneViewModel`, `IWebView`, `MessageReceivedEventArgs`, `WebServerExtension`, `IWebServer.AddRoute`, `MenuExtension`, `IDockingWindowService.OpenPane`, `IExtensionFileService.ResolvePath`, `ILocalRunConfigurationsService.GetActiveConfiguration`, `IModel`, `IProject`, `Subscribe<T>` — all identical. Pre-port speculation about base-class-vs-interface divergence didn't materialize.
- **Caveat: this is compile-time API-shape parity.** Runtime behavior parity on a real Studio Pro 10.24.13 install (MEF activation timing, WebView2 differences, run-state event ordering) is still subject to a manual smoke test before declaring full feature parity.

### Internal

- `StudioProActions` no longer takes `IRunStateProbe`, `IStudioProUiAutomation`, or two `Func<>` callbacks via its constructor. All reads route through `HostServices.{RunStateProbe, UiAutomation, RunConfigurations, App}`. The pane sets these via `HostServices.SetRunStateProbe` / `SetUiAutomation` / `SetMaiaActions` at startup and on settings save.
- `StudioProActionServer` constructor simplified to `(int port, Logger? log)`. The legacy `studioProActionsEnabled` / `maiaIntegrationEnabled` boolean flags retire; visibility is catalog-driven.
- JSON-RPC wire format preserved: `ActionResult.Fail` returns from any catalog-dispatched tool surface as `isError: true` (the catalog dispatch detects `ActionResult` and sets the field; non-ActionResult returns keep `isError: false`).
- 271 tests passing (244 Terminal.Tests including 3 skipped Maia-live + 27 Concord.Core.Tests). Up from 244 in v5.0.0-alpha.1.

### Known follow-ups (deferred to 5.0.0-alpha.3)

- `tools/list` shows a placeholder description (`"Concord SPMCP tool (Family). Schema TBD."`) for every tool. Rich per-tool descriptions + input schemas need an `ITool` extension; deferred.
- `Terminal.RunConfigurationSnapshot` (DTO, nullable fields) and `Terminal.Interop.RunConfigurationInfo` (record, non-nullable) still coexist; consolidation deferred (W2 Task 32).
- Dead-code audit of pre-W1 injection paths + `#pragma warning disable CS0649` → `CS0414` correction deferred (W2 Tasks 30, 31).
- Studio Pro 10.24.13 runtime smoke test deferred to next milestone.
- 11.x curated allowlist (`Studio11xAllowlist.cs`) reconciliation against newer Studio Pro builds remains an ongoing review.

## 5.0.0-alpha.1 — Cross-version foundation

**Architecture change.** Concord now ships as `Concord.Core.dll` + two version-specific host DLLs (`Concord.Host11x.dll` for Studio Pro 11.x, `Concord.Host10x.dll` for Studio Pro 10.24.13). Each host binds its version's `Mendix.StudioPro.ExtensionsAPI`; Studio Pro picks the matching host. Shared infrastructure (PTY, settings, MCP host, Maia bridge, skill installer) lives in Core and works on both versions.

This is an **alpha**: 11.x is feature-complete and matches v4.2.2's behavior; 10.x is currently a preview (single "Concord (10.x preview)" menu entry showing a message dialog) while we port the UI-tier classes against the 10.21.1 API surface.

### What works on each Studio Pro version

| Version | Extension folder | Status |
|---|---|---|
| Studio Pro 11.x | `extensions/Concord11x/` | Full feature surface — terminal pane, MCP server, Maia bridge, skill installer, all v4.2.2 functionality |
| Studio Pro 10.24.13 | `extensions/Concord10x/` | Preview only — menu entry confirming Concord is loaded; full functionality coming in subsequent 5.x releases |

### Highlights

- `Concord.Core.dll`: a single shared library, no Studio Pro dependency at the package-reference level. Contains PTY layer, terminal session manager, settings/skills/rules installers, MCP host (StudioProActions + StudioProActionServer), Maia bridge (CDP transports + injected JS agent), and the new Interop-interface boundary (`IStudioProAppHost`, `IRunConfigurationsHost`, `IRunStateHost`, `IModuleImportHost`) plus a `HostServices` registry.
- `Concord.Host11x.dll`: binds `Mendix.StudioPro.ExtensionsAPI 11.6.2`. Hosts the existing 11.x MEF surface (TerminalMenuExtension, TerminalPaneExtension, TerminalPaneViewModel, TerminalWebServer, RunStateProbe, StudioProUiAutomation). All exports import `Host11xEntry` to guarantee MEF activation order so `HostContext.Initialize` + `HostServices.Register` run before any other host-side construction.
- `Concord.Host10x.dll`: binds `Mendix.StudioPro.ExtensionsAPI 10.21.1` (the version MCPExtension uses for 10.24.13 support). Currently exposes one `MenuExtension`-based menu entry; the rest of the UI surface lands in a follow-up as we reconcile the 10.x `MenuExtension` (abstract base class) vs 11.x `IMenuExtension` (interface) API drift.
- `manifest.json` is per-host with exactly one DLL — Studio Pro 10.24.13's extension loader rejects multi-DLL manifests (calls `.Single()` during hashing, throws `InvalidOperationException`).
- New per-host MSBuild deploy targets: `MendixDeployTarget10x` and `MendixDeployTarget11x` in `Directory.Build.props`. Cross-version devs set both to different test projects so a Studio Pro instance never sees the wrong-version extension folder (which would crash on type resolution).

### Migration from 4.2.2

- Delete the old `extensions/Concord/` folder from your Mendix projects before installing 5.0.0-alpha.1.
- Wipe `<project>/.mendix-cache/extensions-cache/` so Studio Pro rebuilds the snapshot from the new layout.
- See `DEPLOYING.md` for the full developer + consumer paths.

### Internal

- 4 new Interop interfaces in Core, plus a `HostServices` static registry. Currently registered-but-unused (the existing host code still injects Studio Pro services through MEF `[Import]` constructors directly). Consolidating to a single host-injection mechanism is W2 work.
- `Concord.Core.Tests` project added (5 unit tests covering `TargetMode` + `HostContext`).
- All 245 existing tests (4 in Concord.Core.Tests + 241 in Terminal.Tests) pass on the new layout.
- Legacy `Terminal.csproj` shim deleted.

## 4.2.2 — 2026-05-10

### Fixed

- **HARD-BLOCK: Codex 0.128+ refused to start after Concord v1.3.0 migration.** `McpTomlConfigurator.RemoveNamed` only stripped the parent `[mcp_servers.<name>]` section, leaving orphan `[mcp_servers.<name>.tools.<X>]` child sub-sections behind. Older Codex tolerated these orphans; Codex 0.128+ validates the structure and refuses to start with `Error loading config.toml: invalid transport in mcp_servers.<name>`. Surfaced 2026-05-10 on the first production Codex run using v4.2.1's defaults-on Codex feature — a pre-v1.3.0 Concord install + Codex enabled = guaranteed hard-block. v4.2.2's `RemoveNamed` now strips the parent AND all child sub-sections in a single pass, including orphans where the parent was already removed by an earlier failed migration. Same fix applies to `RemoveActions`'s `LegacyActionsServerName` cleanup. Affected users: anyone who upgraded Concord across the v1.3.0 rename AND had Codex ticked when the rename ran. **One-click recovery via Concord Save now resolves it automatically; no manual `~/.codex/config.toml` surgery needed.**

### Added

- **Codex migration-prompt suppression for Concord-managed projects.** Codex 0.128+ shows an *"External agent config detected"* prompt on first entry to any project — offering to migrate Claude Code MCP servers, subagents, plugins, and recent sessions across. For users who installed Concord, the prompt is noise — Codex is already configured. v4.2.2 writes a per-project stamp under `[notice.external_config_migration_prompts.project_last_prompted_at]` with a far-future unix epoch (4070908800, year 2099) when Concord wires Codex for a project. Surgical: only stamps the project Concord is currently applying to; does not touch the home-level prompt; does not flip the `[features] external_migration` master toggle. Idempotent: re-application no-ops when the stamp is already future-dated.

### Changed (rules)

- **§2 Maia recovery ladder — sharpened "what is NOT a failure".** Codex's first production run called `maia__reset` 51 times across two sessions despite zero actual bridge disconnects — defensive-pessimism after benign `maia__health` / `maia__busy` responses. v4.2.2's rule explicitly lists which diagnostic shapes are SUCCESS, not failure: `maia__health` returning `available: true` for any transport, `maia__busy` reporting `{busy: true}`, `maia__ping` with non-zero `latency_ms`, `maia__status` with `streaming: true` or `lost: true`. **`maia__reset` is for recovering FROM observed failure, not for prophylactic bridge hygiene.**
- **§12 Verification — time-based `save_all` fallback (15 minutes).** v4.2.1's "save_all + refresh_project after each batch" cadence works during build phases but fails during pure visual-polish phases (re-running `pg_write_page` against existing pages, tweaking theme variables) — no batch boundaries means `save_all` is never triggered. CocktailDemo34 (2026-05-10) drifted 54 minutes without a save before a machine crash; the build survived only because Codex happened to save 3 minutes before the crash. v4.2.2's rule adds an explicit time-based trigger: **call `save_all` + `refresh_project` at least every 15 minutes of continuous work, even when no logical batch has completed.** Closes the crash-safety gap on long polish loops.

### Tests

- `McpTomlConfiguratorTests` +9: orphan-child-strip on legacy server with no parent (Neo's exact repro); parent+children stripped together for both legacy and current sections; primary-server child strip; **name-disambiguation regression** (removing "mendix-studio-pro" must NOT strip "mendix-studio-pro-actions" children); migration-prompt creation, existing-table append, existing-entry update, already-future no-op, null-empty no-op.
- Full suite: 241/241 pass (+3 live-Maia skips when Studio Pro isn't running). v4.2.1 was 232; v4.2.2 adds 9 net new passing tests.

### Notes

- **Empirical baseline.** Every change in v4.2.2 traces to a specific observation from the 2026-05-10 CocktailDemo34 Codex production run — the first end-to-end build using v4.2.1's three-CLI rules paths. The Codex run validated v4.2.1's headline claim (Codex consumes `.codex/rules/concord-*.md` via `AGENTS.md` `@`-imports exactly as Claude Code consumes its parallel set) AND surfaced four follow-up gaps captured into v4.2.2.
- **`McpTomlConfigurator` is now safer.** Two regression hazards explicitly tested: removing a server with similar-named neighbors (`mendix-studio-pro` vs. `mendix-studio-pro-actions`) does not cross-strip; orphan child sub-sections without a parent are still cleaned up.
- **Deferred to v4.2.3 / later:** `.codex/skills` vs. `.agents/skills` path investigation (Codex's migrator wants the latter; needs empirical verification before changing Concord's install path); Playwright MCP design decision (3 options on the table, no consensus yet); optional Codex autonomous-mode Settings checkbox (`approval_policy = "never"` + `sandbox_mode = "workspace-write"` — requires UI work).

## 4.2.1 — 2026-05-10

### Added

- **Four new Maia introspection MCP tools.** All read- or-DOM-bound through the existing CDP-injected agent; no new transport plumbing.
  - **`maia__busy`** — read-only DOM probe: "is Maia generating?". Returns `{busy, reason: 'spinner-visible'|'recent-dom-mutation'|'idle', idle_for_ms, spinner}`. No traffic to Maia. The spinner heuristic uses documented selectors (`[role="progressbar"]`, `[aria-busy="true"]`, `.spinner`, etc.) plus a 1000ms recent-mutation threshold tracked via the existing `MutationObserver`.
  - **`maia__ping`** — cheap liveness probe. Sends `"ping"` with auto-sentinel, waits up to `timeout_sec` (default 5s), returns `{alive, latency_ms, response, timed_out}`. Reuses the existing send/wait/sentinel machinery including v4.2.0's lost-handle discriminator. Use before expensive `maia__ask` calls when bridge health is uncertain.
  - **`maia__health`** — bridge-state introspection without traffic to Maia. Returns per-transport availability + last latency + reason, last probe time, in-flight handle bindings, forced tier, plus an embedded `busy()` snapshot. One-call diagnostic before recovery decisions.
  - **`maia__new_chat`** — programmatic click of Maia's "New chat" button. Wipes Maia's panel context. Used in §2 ladder step 3.5 between `maia__reset` and user-handoff. Always preceded by `maia__busy` per the rule.
- **Codex + Copilot rules paths lit up.** Each CLI now ships Concord rules + a managed-block import file with identical content; only the destination path varies. `claude → .claude/rules/ + CLAUDE.md`, `codex → .codex/rules/ + AGENTS.md`, `copilot → .github/rules/ + .github/copilot-instructions.md`. Implementation: `ClaudeMdManager` unsealed; `AgentsMdManager` and `CopilotInstructionsManager` subclass it with one-line overrides. Block-edit logic, orphan preservation, atomic writes, and `.github/` directory auto-creation are shared via the base.
- **Codex defaults-on with first-run banner.** `TerminalSettings.Defaults()` flipped: `McpClients = ["claude","copilot","codex"]`, `SkillClients = ["claude","copilot","codex"]`. First-run banner explains the user-global `~/.codex/config.toml` write so the user is informed, not surprised. The banner points to Settings if the user wants to revert.
- **§3 task-scoped failure cap (rules update).** Replaced the raw-count "3 consecutive `maia__*` failures = STOP" rule with a task-scoped variant: count failures on **the same logical operation** (same handle being polled, same prompt being re-tried after refinement, same `maia__reset` + re-probe cycle). CocktailDemo33 (2026-05-10) hit 3 transient errors across 3 unrelated tasks under v4.1.5 and stopped the build prematurely — v4.2.0's auto-reconnect would have absorbed each one cleanly.
- **§12 errors-before-`run_app` hard gate (rules update).** `run_app` is gated by zero errors. The same demo build called `run_app` 19× against 2 unresolved Studio Pro errors; the gate would have caught it on call 1 and routed the agent to fix the errors instead of looping on the runtime.
- **§3/§5 Maia-as-page-fixer tiebreaker (rules update).** Page errors that Maia just wrote get one Maia second-attempt before user homework — mirrors §2's "Maia owns pages" doctrine. Non-page docs (microflows, entities, view entities) stay on the §5 single-shot rule (Maia can't reliably edit those).
- **§8 seed-data self-service-button pattern (rules update).** Codified the production-validated pattern: `ProjectManage` singleton entity + `NeedProjectSeedData` Boolean + visibility-bound button on the home page + Playwright self-click after `run_app`. Eliminates the After-Startup soft-stop entirely. Cross-references from §10 (layout-first) and §11 (theme module) so the agent finds it during clone-build planning. Pattern landed in production as `SUB_Cocktail_SeedIfEmpty` during the 2026-05-10 CocktailDemo33 build.
- **§2 recovery ladder step 1.5 + 3.5 (rules update).** Step 1.5 inserts an agent-level `maia__health` liveness check (returns availability without traffic — saves a wait window when the WebSocket is dead). Step 3.5 wipes Maia's panel state via `maia__busy → wait → maia__new_chat` between `maia__reset` and the user-handoff escalation.
- **Optional task-boundary new-chat guidance (rules update).** Long sessions accumulate context inside Maia's chat panel; `maia__new_chat` (preceded by `maia__busy`) is a safe no-op for keeping Maia sharp between unrelated tasks. Heuristic: if the next prompt would not meaningfully reference the prior conversation, start fresh.

### Changed

- **`StartActionServer` accepts the singleton `CdpClient`.** Manager now owns its lifetime: each toggle replaces + disposes the old client (3s bounded await on `DisposeAsync`) before swapping in the new one. Stops the v4.2.0 per-toggle `ClientWebSocket` + `SemaphoreSlim` GC drift for power users who toggle Maia on/off repeatedly.
- **`CdpClient` heartbeat now triggers `__maiaBridge.scan()`.** Single-line addition to the existing 10s heartbeat eval. Defeats Chromium's WebView2 background-tab throttling that drifts the JS-side `setInterval(scanForCompletions, 500)` toward seconds when Maia is hidden behind another pane. Persistent CDP socket is unaffected by tab visibility, so detection latency stays bounded at the heartbeat interval regardless of pane state.
- **`__maiaBridge` JS bridge bumps to v2.** New methods: `busy()`, `newChat()`, `scan()`. New tracked state: `lastChatMutationAt`. v1 bridges are torn down + re-injected on session start so the new methods are present even on long-lived WebViews.
- **`§1` closed-set tool list (rules update).** Extended with the four new Maia introspection tools. Same gating discipline: forbidden paths and recovery ladders apply equally to the new tools.

### Fixed

- **`get_active_run_configuration` callback wired in VM save path.** `TerminalPaneViewModel.HandleSaveSettings` constructed `StudioProActions(probe, ui)` without the `getActiveRunConfig` + `getProjectInfo` callbacks. After any Settings save, the rebuilt action server returned `Active-run-configuration callback not wired` for both `get_active_run_configuration` AND the embedded config in `get_app_status`. Surfaced 13:12:56 during CocktailDemo33. Fix: pass both callbacks through the VM ctor (optional params, back-compat preserved); `TerminalPaneExtension.Open()` wires them with the same closure shape used in `TryAutoStartActionServer`.

### Tests

- **`CdpClientReconnectTests`** (4 cases) — IsDisconnect → reconnect-with-fresh-adapter → retry-succeeds path under a fake `IWebSocketAdapter`. Backed by a new `IWebSocketAdapter` interface + `ClientWebSocketAdapter` production wrapper; `CdpClient`'s internal test ctor accepts `webSocketFactory` + `discoveryOverride`.
- **`MaiaActionsTests`** +9 — busy/new_chat/ping/health happy + no-introspection paths against an `IntrospectableStubTransport`.
- **`AgentsMdManagerTests`** + **`CopilotInstructionsManagerTests`** (4 each) — block creation, user-content preservation, block strip, `.github/` dir auto-create.
- **`MaiaJsonRpcTests`** updated for the four new tool names in the tools/list set.
- Total: 232/232 pass (+3 live-Maia skipped when Studio Pro isn't running). v4.2.0 was 228; v4.2.1 adds 19 net new passing tests.

### Notes

- **Empirical baseline.** Every rule + tool addition in v4.2.1 traces back to a specific observation from the 2026-05-10 CocktailDemo33 monitoring run, where v4.2.0's bridge survived 4–5 distinct failure modes that killed v4.1.4 plus an unplanned Studio Pro hard crash. v4.2.0 made the bridge reliable; v4.2.1 makes the agent's verification discipline equally reliable + adds the introspection toolkit production exercise revealed was missing.
- **MCP tool surface.** Claude Code, Codex, and Copilot CLI all see ten Maia tools when Maia integration is on (Windows): `maia__send`, `maia__status`, `maia__wait`, `maia__ask`, `maia__reset`, `maia__force_tier`, `maia__busy`, `maia__ping`, `maia__health`, `maia__new_chat`. Old clients ignore unknown tools.
- **Carry-over remediation.** All three v4.2.0 explicitly-deferred risks (singleton dispose on toggle, WebSocket test seam + reconnect tests, JS throttle fix) ship in this release.
- **Out of scope, deferred to later releases.** Tier 2 ambient ping loop (a background `maia__ping` cadence with degradation-trend logging) is design-noted in the punchlist but not in this release. Marketplace dark-mode rendering bug remains an open separate workstream.

## 4.2.0 — 2026-05-09

### Added

- **Persistent CDP connection.** The `CdpClient` is now a long-lived singleton owned by the action-server lifecycle. Port discovery (PowerShell + WMI) and the WebSocket handshake happen ONCE per Studio Pro session instead of on every Maia tool call. The 2026-05-09 cocktail-clone smoke test of v4.1.4 surfaced the v4.1.x pattern's failure mode: a single `maia__wait` polling at 250ms × 60s opened ~240 fresh PowerShell processes and ~240 WebSocket handshakes, eventually saturating Studio Pro's CDP server and producing `IOException: connection forcibly closed by remote host`. v4.2.0 reuses the connection across all `maia__*` calls.
- **Auto-reconnect on WebSocket drop.** `EvaluateAsync` wraps `EvaluateOnceAsync` with a one-shot reconnect when the WebSocket dies mid-call. Drop detection is typed (`TransportUnavailable.IsDisconnect` flag set at every `WebSocketException` / `IOException` catch site) — no fragile string-matching the message. Discovery cache is invalidated on connect failure so a Studio Pro restart self-heals after one failed call.
- **Auto-re-injection on `StatusAsync`.** When the JS-side `window.__maiaBridge` is gone (WebView reload, panel close/reopen, V8 GC of detached scope), the JS-side wrapper returns `{__reinject: true}`; the transport reinstalls the agent and retries `poll()` once before declaring the handle lost. Eliminates the `Unknown handle: <name>` errors seen 3× in the cocktail test (different handles each time).
- **Defensive `poll()` parser.** Non-object responses surface the actual shape received (`<null>`, `value:42`, `array(len=N)`, `node:JsonValue`) instead of the empty-string interpolation that produced the cryptic `poll() returned unexpected shape: ` errors in v4.1.4 (2× during layout-edit attempts).
- **Lost-handle discriminator.** When the router has a binding for a handle but the JS-side ticket vanished after a WebView reload, `maia__status` returns `lost: true` instead of throwing. `maia__wait` exits the polling loop early with a `lost` discriminator so callers can re-ask cleanly. Genuine unknowns (typo, never sent) still surface as a structured `Unknown handle:` exception. The lost-vs-unknown decision lives in the router, which has the full view of `handleToTransport` bindings.
- **CDP keep-alive heartbeat (10s).** Every 10 seconds, the persistent `CdpClient` fires a no-op `1+1` `Runtime.evaluate` over the live socket. Defeats Chromium's WebView2 background-tab throttling that drifts `maia_agent.js`'s `setInterval(scan, 500)` from 500ms to seconds when Maia is hidden behind another pane. Heartbeat that times out because the eval-gate is held by a long `maia__wait` does NOT die — it skips and resumes (capped at 12 consecutive skips before yielding to the next user-driven Evaluate's reconnect path).
- **Diagnostic logging toggle.** New `Settings → Concord MCP → "Diagnostic logging"` checkbox (default off, Windows-only). When on, `Logger.Debug` writes CDP request/response traces to `terminal.log` in the form `[cdp] >> id=N bytes=…` / `[cdp] << id=N bytes=…`. Use to ground future bridge-failure diagnoses; off by default to keep the log lean for ordinary users.

### Changed

- **All 7 transport call sites** (4 in `CdpInjectedTransport`, 3 in `CdpChatTransport`) drop their `await using var cdp = clientFactory()` — the singleton's `DisposeAsync` is fired only by the action-server's shutdown path, never per call.
- **`StatusResult` record** gains `Lost` and `UnknownHandle` boolean fields (both default false). Existing positional constructors compile unchanged.
- **`MaiaActions` payloads** all carry the `lost` field consistently (`polled`, `done`, `lost`, `timed_out`). MCP clients that don't care can ignore it.
- **`TerminalSettings` record** gains `MaiaDiagnosticLogging` field (default false). Backwards-compat preserved.
- **Settings DTO migration.** New field is additive; old settings files load with `MaiaDiagnosticLogging: false`. A user who installs v4.2.0, flips the toggle, then downgrades to v4.1.4: the v4.1.4 DTO ignores the unknown field on read but does NOT preserve it on save. Acceptable for an opt-in diagnostic.

### Notes

- **Empirical baseline.** Bug surface measured against the 2026-05-09 cocktail-clone smoke test at `C:\Workspace\MendixApps\CocktailDemo32-main`: 3× `Unknown handle`, 2× `poll() returned unexpected shape`, 8+ `IOException`, bridge effectively dead from 16:21:34. v4.2.0 design + implementation grounded in `docs/superpowers/specs/2026-05-09-bridge-hardening-implementation.md` + adversarial review (5 blockers found, all applied).
- **MCP tool surface unchanged.** Same six `maia__*` tools, same input schemas. Output payloads gain the `lost` field; MCP clients ignore unknown fields.
- **Out of scope, deferred to v4.2.1:** explicit dispose of the previous singleton on settings toggle off→on (small leak: one ClientWebSocket + one SemaphoreSlim per cycle); WebSocket-test-double for full reconnect-on-drop unit coverage (manual smoke test covers the live path); JS-side `setInterval` throttle (CDP heartbeat keeps the WebSocket warm but the JS scan can still drift — real fix is `requestAnimationFrame` or a CDP-driven scan trigger).
- **Verification gate before marketplace publish.** `docs/superpowers/specs/2026-05-09-bridge-hardening-test-plan.md` specifies the four-layer test plan (xUnit unit + JS agent + gated live + manual smoke) plus the cocktail-clone regression run. The headline acceptance criteria: zero `Unknown handle`, zero `poll() returned unexpected shape: ` (empty-string), zero `IOException: forcibly closed`, fewer than 5 `powershell.exe` spawns observed in Task Manager during a 30-min cocktail-clone build.
- **v4.1.5 rules iteration ships in the same release.** The `concord-build-rules.md` updates that came out of the cocktail-test forensics (broader Maia recovery trigger, hard cap on bridge calls, layout-edit manual fallback default, mark-as-UI-resources soft-stop ordering, save_all + refresh_project cadence rule, read-loop anti-pattern) are independent of the bridge work but ride this release for batch-shipping convenience. See the v4.1.5 commit on this branch.
- **Rules file split into 3 files** to stay under Claude Code's 40k-char per-file performance threshold. The v4.1.5 additions pushed `concord-build-rules.md` from 39.8k → 42.1k chars, which triggered a `/memory` warning at session start. Content is unchanged (15 sections preserved, all cross-references intact); split is purely structural. Layout: `concord-build-rules.md` (core operational discipline — §1, §3, §4, §12, §13, §14, §15) + `concord-pages-and-themes.md` (UI construction — §2, §8, §10, §11) + `concord-model-discipline.md` (model rules — §5, §6, §7, §9). All three carry the `concord-` prefix, ship in the bundle, and are auto-imported into `<project>/CLAUDE.md` via the managed block (canonical first, siblings sorted by name). User-authored sibling `.md` files at the rules root (no `concord-` prefix) are still NOT auto-imported — user content belongs in `.claude/rules/project/` per §14. `ClaudeMdManager.BuildBlock` was extended to enumerate all `concord-*.md` siblings; orphan-cleanup logic in `RulesInstaller.InstallAll` already handled this since v4.1.4.

## 4.1.4 — 2026-05-09

### Added

- **Always-loaded build rules** (`concord-build-rules.md`). Concord now ships a project-level rules document that auto-loads into every Claude Code session running inside the Mendix project. The rules govern *how* the agent works (tool hierarchy, page-via-Maia doctrine, recovery ladders, layout-first for branded apps, sibling-theme-module pattern, verification gates, persisting learned conventions) — not *what* to build. Authored to prevent the named failure modes from forensic build sessions: orphan pages, shell microflows, ActionButton wiring trap, letter-not-spirit compliance, end-of-build "manual steps required" punt-lists. ~360 lines across 15 sections. Installed at `<project>/.claude/rules/concord-build-rules.md` on every Save. Refreshed on every Concord upgrade so future releases ship rule changes automatically.
- **Project-specific rules folder** (`<project>/.claude/rules/project/`). User-owned space for project-specific instructional content (domain glossary, design-system tokens, integration patterns, learnings the agent persists during a build per §14 of the rules). Pre-created with a `README.md` stub on first install only. **Concord never overwrites contents** — survives every upgrade. Every `.md` file dropped here is auto-imported into `CLAUDE.md` on the next Save.
- **`CLAUDE.md` auto-management** at the project root. Concord creates or refreshes a fenced `<!-- BEGIN CONCORD MANAGED -->` ... `<!-- END CONCORD MANAGED -->` block that `@`-imports the rules file plus every `.md` in `.claude/rules/project/`. User content outside the markers is preserved verbatim. Existing well-formed block is replaced **in place** — block does not migrate to the top of the file on every Save. Atomic write (write-temp-then-move) so an interrupted Save can never leave a half-written `CLAUDE.md`.
- **Orphan-bundled-file cleanup on upgrade.** Top-level rule files prefixed `concord-` are Concord-managed; if a future release stops shipping a particular rules file, it's removed from upgraded installs automatically. User-authored siblings (no `concord-` prefix) are never touched.
- **`RulesInstaller` and `ClaudeMdManager` classes** + **35 new unit tests** (13 RulesInstaller + 22 ClaudeMdManager). Full coverage: idempotency, byte-stable repeated saves, `project/`-folder preservation, orphan-BEGIN edge cases (no END at all + intervening BEGIN before END), block-position preservation (top / middle / bottom), corrupt-state recovery (multiple managed blocks collapse to one), reference-equality optimization for no-op writes, atomic-write semantics, orphan-cleanup on bundle shrinkage.
- **Rules ship in the build pipeline** (`<Content Include="rules/**/*">` in `Terminal.csproj`). Available in deploy output and the per-project extension cache on both Windows and macOS — same shape as `skills/`.

### Changed

- **`SettingsApplyHelper.ApplyAll` signature** gains a `bundledRulesRoot` parameter (mirrors `bundledSkillsRoot`). Both call sites in `TerminalPaneViewModel.cs` and the three call sites in `TerminalPaneExtension.cs` (Open + first-run + upgrade-apply) updated to resolve and pass it via `extensionFileService.ResolvePath("rules")`. `SettingsApplyHelperTests.cs` updated for the new parameter.
- **Rules track the same enable-toggle as skills** for v4.1.4 (no separate `RulesEnabled` field). When the user toggles Skills off in Settings, both skill packs AND the always-loaded rules are removed; the `CLAUDE.md` managed block is stripped. Phase 2 may split this if user feedback warrants.

### Notes

- **Existing customers materialize the rules on upgrade.** First open of any Mendix project after the 4.1.4 install fires the upgrade-apply path (`stamp '4.1.3' < 4.1.4`), which runs the apply chain including the new `ApplyRulesConfig` step. Rules land on disk + the `CLAUDE.md` block is created — no manual Save needed. Banner: `Updated to 4.1.4. Rewired: ... Open Settings to adjust.`
- **End-to-end loading verified.** Smoke test on TestOSApp3 confirmed Claude Code in a Concord pane lists `concord-build-rules.md` and `project/README.md` in its loaded set and quotes §2 verbatim. The `@`-import chain (project-root `CLAUDE.md` → `.claude/rules/*`) works end-to-end.
- **Phase 1 = Claude only.** Codex (`AGENTS.md`) and Copilot CLI (`.github/copilot-instructions.md`) are wired in `ApplyRulesConfig` as no-ops with explicit TODO markers. They light up in a follow-up phase once the Claude path is validated on more real builds.
- **Rules content is doctrine, grounded in evidence.** §2's "Pages always go through Maia, never `ped_*`" comes from the Studio Pro MCP system prompt itself. §7's five named failure modes (orphan pages, shell microflows, ActionButton wiring trap, letter-not-spirit compliance, end-of-build punt-lists) come from forensic analysis of session JSONLs from real Mendix-clone build attempts. §2's Maia warm-up ladder addresses the cold-panel false-positive observed in the field. The full file went through three rounds of fresh-context adversarial review before shipping.

## 4.1.3 — 2026-05-09

### Added

- **Mac variant of the `mendix-page-gen` skill pack.** The Windows version of this skill instructs the CLI agent to delegate page writes to Maia (via Concord MCP's `maia__ask`) because Studio Pro's MCP doesn't expose `pg_read_page` / `pg_write_page` tools. On macOS, Maia integration isn't available (WKWebView can't be inspected externally without host opt-in — see `docs/MAIA_MAC_FEASIBILITY.md`), so a new Mac-specific variant ships at `skills-mac/mendix-page-gen/SKILL.md`. Same widget catalog and rules; the head section is rewritten to print a copy-paste hand-off prompt for the user ("Open Maia in Studio Pro, paste this, send, reply `done`") and then stop and wait. After the user confirms, the CLI verifies with `ped_check_errors` directly. The other 6 skill packs (`mendix-microflow-*`, `mendix-view-entities`, `mendix-workflow-*`) don't reference Maia and remain platform-identical.
- **`SkillInstaller` overlay support.** Optional `overlaySkillsRoot` constructor parameter. After copying the primary bundled skill folders into the target subdir, the installer copies the overlay root on top — same-named files inside same-named skill folders win. Lets us swap one skill for a platform-specific variant without forking all 7.
- **`SettingsApplyHelper` auto-derives the Mac overlay.** When `OperatingSystem.IsMacOS()` is true and `<bundledSkillsRoot>/../skills-mac` exists, the helper passes it as the overlay to `SkillInstaller`. The diff log line now includes `overlay-root=...` so you can confirm at runtime by tailing `terminal.log`.
- **`docs/MAIA_MAC_FEASIBILITY.md`.** Research write-up explaining why a CDP-style Maia transport on Mac isn't feasible: WKWebView's `isInspectable` requires host opt-in (Mendix would have to flip it on the Maia WebView in Studio Pro itself), `_developerExtrasEnabled` is similarly host-side, and the Mendix Extensions API 11.6.2 has no Maia-related surface. Documents three forward paths: an AX/osascript Tier-3 transport (~1-2 day prototype, brittle), a feature request to Mendix for an `IMaiaService` extension API, or a feature request for a Mac `--remote-debugging-port`-equivalent debug flag. Also records the rejected SIP-bypass route (unshippable). Linked from the README's macOS callout.
- **Tests:** `InstallAll_OverlayReplacesPrimarySkill` and `InstallAll_OverlayMissingDoesNotThrow` in `SkillInstallerTests`.

### Changed

- **README's macOS callout** rewritten: `Maia integration is Windows-only in this release` → `Maia integration is Windows-only` + an explicit pointer to the feasibility doc + a new section under "Bundled skill packs" explaining the Mac variant of `mendix-page-gen`.

### Notes

- **No Windows-side change.** The Mac scoping is gated on `OperatingSystem.IsMacOS()`. The `skills-mac/` overlay directory is shipped with every build but only consumed on macOS. Windows users see no functional change.
- **Existing Mac customers materialize the Mac variant on upgrade.** `IsUpgradeApplyNeeded` fires on the strict-older `lastAppliedVersion` stamp (`4.1.2 < 4.1.3`), the apply chain re-runs `SkillInstaller.InstallAll` for ticked CLIs, the Mac overlay copies on top of the primary, and `<project>/.claude/skills/mendix-page-gen/SKILL.md` ends up with the Mac hand-off copy. No manual Save needed. Banner: `Updated to 4.1.3. Rewired: Skill packs installed: Claude Code, Copilot CLI. Open Settings to adjust.`
- **Maia gating itself was already in place in 4.1.2.** Both action-server construction sites (`TerminalPaneViewModel.cs` and `TerminalPaneExtension.cs`) already computed `maiaEnabled = OperatingSystem.IsWindows() && setting`, so the `maia__*` tool family was already excluded from the Concord MCP advertise list on Mac. 4.1.3 just documents the behavior + adds the matching Mac-side skill flow.

## 4.1.2 — 2026-05-08

### Fixed

- **Port-leak in `terminal-settings.json`.** The settings modal's outgoing payload was sending the LIVE bound port of the Concord MCP server to the JS side (so the readout could display "listening on `localhost:8099`" when 7783 was busy). The incoming Save handler then persisted that live port to disk as if it were configuration intent. Next launch the runtime ignored it (correct — the server always probes a free port) but the saved value stuck in the file forever, displayed as a phantom "configured port" the user never chose. **Fix:** `McpServerPort`, `McpPort`, and the legacy `ActionsServerPort` keys are removed from the settings schema entirely. Old keys in existing files deserialize as ignored fields and disappear on the next save. The Concord MCP listening port is now exposed only through a read-only display field (`liveActionServerPort`) that is never echoed back through Save.
- **Settings modal title.** "Concord Terminal Settings" → "Concord Settings" (the modal already lives inside the terminal pane).
- **Save vs. Cancel button affordance** in dark mode. Both rendered as the same gray surface, making the primary action invisible. Save now carries an accent-colored border without violating Studio Pro's no-filled-primary-button convention. Cancel relabeled to "Close" — settings UX never "cancels" pending intent the user already saw applied.

### Changed

- **Banner copy** rewritten to read like product, not like log lines:
  - First-run: `Concord wired up for first-time use: ...` → `Concord ready. Wired: ...`
  - Upgrade with changes: `Concord upgraded (X → Y). Refreshed wiring: ... Open Settings to customize.` → `Updated to Y. Rewired: ... Open Settings to adjust.`
  - Upgrade no changes: `Concord upgraded to Y. No wiring changes needed.` → `Updated to Y. No changes.`
  - SP-MCP advisory: `Studio Pro's MCP server appears disabled. Enable it in Edit → Preferences → Maia → MCP Server, then reopen this pane to make the wired CLI configs functional.` → `Studio Pro MCP is off. Enable it in Edit → Preferences → Maia → MCP Server, then reopen Concord.`
  - Maia advisory: `Maia tools require the Maia panel to be visible. Keep it open while Claude Code or Copilot CLI drives Maia.` → `Keep the Maia panel open while Maia tools are in use.`
- **Save-result strings** cleaned up: `MCP servers updated for ...` → `MCP wired for ...`; `skill packs installed for X skills` → `Skill packs installed: X` (no more triple "skills").
- **Concord MCP port readout** reduced from a 5-line wall of prose to a single status line: `Connected on localhost:7783.` (or `Concord MCP starting…` / `Concord MCP is off.`).
- **Modal open animation** trimmed from 180ms to 140ms (modal-in-modal context, the slower curve read sluggish).
- **Footer credit** reworded: `A Siemens CoE extension for Studio Pro.` → `Built by the Siemens CoE Team.`
- **README current-version** corrected (was stale at 4.0.0; this release is 4.1.2).

### Added

- **Atomic file writes** (`File.Replace`) in `McpJsonConfigurator`, `McpTomlConfigurator`, and `TerminalSettings.Save`. Previous delete-then-move pattern had a brief window where AV scanners or concurrent readers saw the file gone; if the move then failed, the user was left with no file at all. Journaled NTFS rename closes the window.
- **Corrupt-file backup** in `TerminalSettings.Load`. A malformed `terminal-settings.json` no longer silently defaults — it's renamed to `terminal-settings.json.broken-{timestamp}.bak` so the user can recover their custom shell/theme/etc., then defaults take over.
- **JSON serializer skips nulls** when writing settings. Removes residual `"actionsServerEnabled": null` (and any future legacy back-compat field that goes null) from the saved file.

### Notes

- **Existing customers automatically benefit.** The upgrade-apply path (introduced in 4.1.1) fires on first open after the 4.1.2 install, runs the apply chain, writes a fresh `terminal-settings.json` without the stale port keys, and stamps `lastAppliedVersion: 4.1.2`. No manual cleanup required.

## 4.1.1 — 2026-05-08

### Added

- **Upgrade auto-apply.** When Concord opens against a project that has a `terminal-settings.json` file from an older Concord version (compared via `lastAppliedVersion` semver stamp), the wiring keys (`McpEnabled`, `McpClients`, `McpServerEnabled`, sub-toggles, `SkillsEnabled`, `SkillClients`) are re-defaulted to current `Defaults()` values and re-applied to disk — so customers who upgrade from 1.x or 4.0.0 get the new MCP wiring + skill packs materialized without needing to open Settings and Save manually. Runtime preferences (shell, theme, ring buffer, scrollback, restore-tabs, refresh hotkey) are preserved verbatim. Banner: `Updated to {ver}. Rewired: ... Open Settings to adjust.`
- **Cross-machine safety.** `IsUpgradeApplyNeeded` only fires on a strict-older stamp (`prev < curr` in System.Version semantics). A colleague pulling a project from a machine running a more recent Concord version sees no apply (their wiring wins; no downgrade). Once stamped current, subsequent opens at the same version are no-ops.

## 4.1.0 — 2026-05-08

### Added

- **Default-on settings + first-run auto-apply.** `TerminalSettings.Defaults()` now returns all toggles on (Claude Code + Copilot CLI for both MCP families and skills). When Concord opens against a project that has no `resources/terminal-settings.json` yet, it writes `.mcp.json`, installs bundled skill packs into `.claude/skills/` and `.github/skills/`, and persists the settings file in one go — no modal clicks required. The Concord MCP bridge starts automatically through the existing `TryAutoStartActionServer` path now that the trigger condition is on by default.
- **First-run advisory banners.** On a fresh install, three banners appear via the existing `mcpResult` channel: a "Concord wired up" summary, a Studio Pro MCP-disabled advisory (if the SQLite probe shows it disabled in Preferences), and a Maia-pane-open advisory on Windows.

### Notes

- **Codex stays opt-in.** Auto-enabling Codex would write to user-global `~/.codex/config.toml` and `~/.codex/skills/` (a side effect outside the project tree). The customer can flip Codex on per-section in Settings.
- **Existing customers are not retroactively flipped.** The auto-apply only runs when no `resources/terminal-settings.json` file exists. Anyone with a saved settings file from 4.0.0 keeps their explicit choices.
- **Edge case (very old settings files).** If a 1.x or 2.x settings file is loaded that's missing `skillsEnabled`/`skillClients` keys entirely, `Load()` migration applies the new defaults in memory (skills appear enabled in the modal). The disk state isn't auto-applied — the next Save makes it consistent.

### Refactor

- The apply-on-save chain (MCP json/toml writers + skill installer) was extracted from `TerminalPaneViewModel` into a static `SettingsApplyHelper` so the extension's first-run path can call the same code without taking a ViewModel dependency. The orchestration layer now has unit-test coverage that didn't exist before.

## 4.0.0 — 2026-05-08

> **About the version jump (1.3.0 → 4.0.0).** The 4.0 major reflects
> the MCP wire-identity rename shipped in 1.3.0 (`mendix-studio-pro-actions`
> → `concord-mcp`) — a breaking change for any client config that
> referenced the old name — combined with the bundled-skills ship in
> 4.0.0. The 2.x and 3.x series are intentionally skipped to align the
> major version with the Concord product brand (a 4.0 launches feels
> right for what shipped together on 2026-05-08; renumbering historical
> commits would be a worse trade-off).

### Added

- **Bundled Mendix skill packs.** The Skills section of the settings modal is now a working installer: enable per-CLI to write the Concord-bundled skills into `<project>/.claude/skills/`, `<project>/.github/skills/`, and/or `<project>/.codex/skills/`. Disable a CLI to remove only the Concord-bundled folders — user-authored siblings under the same directory are left intact. Each Save refreshes the bundled content so a Concord upgrade ships new or updated skills automatically.
- **7 Mendix skills** ship in this release: `mendix-microflow-common`, `mendix-microflow-syntax`, `mendix-microflow-update`, `mendix-page-gen`, `mendix-view-entities`, `mendix-workflow-common`, `mendix-workflow-update`.

### Notes

- Skills are installed project-local only in this release (no `~/.claude/skills/` writes).
- If you have hand-edited a Concord-bundled skill folder, your edits will be overwritten on the next Save. Add custom skills as siblings (e.g. `<project>/.claude/skills/my-thing/`) to keep them safe across upgrades.

## 1.3.0 — 2026-05-08

### Breaking
- MCP server wire identity renamed from `mendix-studio-pro-actions` to `concord-mcp`. Update any MCP client config (Claude Code `.mcp.json`, Codex `~/.codex/config.toml`, Copilot CLI) that references the old name.

### Added
- **Maia integration** as a first-class tool family inside Concord MCP, embedded in C# (no Python, no subprocess). Tools: `maia__send`, `maia__status`, `maia__wait`, `maia__ask`, `maia__reset`, `maia__force_tier`. Two-tier transport: injected JS agent (Tier 1) + DOM-scrape fallback (Tier 2). Windows only.
- Settings sidebar item renamed: `Action bridge` → `Concord MCP`. Two sub-toggles: `Studio Pro UI actions` and `Maia integration`. Maia disabled-with-tooltip on macOS.
- New settings keys: `mcpServerEnabled`, `mcpServerPort`, `studioProActionsEnabled`, `maiaIntegrationEnabled`. Old keys (`actionsServerEnabled`, `actionsServerPort`) read for one minor-version migration.

### Note
- Maia integration is internal CoE tooling. The CDP-driven approach (Studio Pro's WebView2 `--remote-debugging-port`) is not Mendix-blessed and may break if Mendix changes that surface. The transport interface is the swap-out seam for future Mendix-native MCP-server-as-tool support.

## 1.2.2 — 2026-05-07

### Action bridge keystrokes now reach Studio Pro on Mac

In 1.2.1, `osascript` was successfully sending F5 / Shift+F5 to Studio
Pro — but the keystrokes landed in the xterm inside the Concord pane,
not in Studio Pro's main accelerator handler. Visible in the log as a
`JS: onData len=5` entry firing within milliseconds of every
`[actions] sent F5` entry: F5's VT escape sequence was being absorbed
by xterm.js because the WKWebView held first-responder status.

Fixed in `src/StudioProUiAutomation.cs` by clearing the WebView's
first-responder grip via AppKit P/Invoke before each Mac keystroke
send:

```objc
[[[NSApplication sharedApplication] keyWindow] makeFirstResponder:nil]
```

Marshalled via Eto.Forms's `Application.Instance.Invoke` so the AppKit
call lands on the main thread — the action HTTP server otherwise runs
on the thread pool. Falls back to `mainWindow` if `keyWindow` is nil,
and falls through silently if AppKit can't be reached (best-effort:
worst case is the previous broken behavior, not a crash).

After 1.2.2, the keystroke reaches Studio Pro's main UI and triggers
Run / Stop / Refresh as expected. Side-effect: the user needs to click
back into the Concord pane after the action fires if they want to type
more — first responder isn't restored.

### Files touched

- `src/StudioProUiAutomation.cs` — `ClearWebViewFirstResponder` helper
  + private `MacAppKit` static class with `objc_getClass` /
  `sel_registerName` / `objc_msgSend` P/Invoke. Called from `SendMac`
  before invoking osascript.

## 1.2.1 — 2026-05-07

### Action bridge now works on macOS

In 1.2.0 the four hotkey-based action tools (`run_app`, `stop_app`,
`refresh_project`, `save_all`) silently no-op'd on Mac — they used Win32
`PostMessage`, which has no equivalent on Mac. This release adds a real
Mac backend.

**Implementation (`src/StudioProUiAutomation.cs`).** New `SendMac` path
invokes `/usr/bin/osascript` with a one-shot AppleScript that:

1. Looks up our own process via Unix PID (`Environment.ProcessId`) — so
   the lookup is stable regardless of the `.app`'s display name
   ("Mendix Studio Pro 11.10.0 Beta.app" today, something else
   tomorrow).
2. Brings Studio Pro to the foreground via `set frontmost of sp to
   true`.
3. Sends `key code N [using {modifiers}]` to deliver the keystroke.

Key codes use Apple's HIToolbox values from `Events.h`. Modifier
mapping: Ctrl → control down, Shift → shift down, Alt → option down.

**Permission-aware error reporting.** When osascript fails with
AppleEvent `-1719` ("not allowed assistive access"), the bridge surfaces
a specific user-actionable message instead of the generic "main window
unavailable" string from 1.2.0:

> "macOS Accessibility permission not granted to Studio Pro. Open System
> Settings → Privacy & Security → Accessibility, enable Studio Pro (add
> it with the + button if it isn't listed), then restart Studio Pro and
> retry."

This message rides through `IStudioProUiAutomation.LastFailureReason`
into `ActionResult.Error`, so Claude / Codex see it directly and can
guide the user. New `RunApp_TriggerFails_PropagatesUiFailureReason` test
covers the propagation.

### Tests + tooling

- New `IStudioProUiAutomation.LastFailureReason` property (test mocks
  updated)
- 96 xunit tests passing on Mac (was 95 in 1.2.0)

### Files touched

- `src/IStudioProUiAutomation.cs` — `LastFailureReason` property
- `src/StudioProUiAutomation.cs` — Win/Mac dispatch, `SendMac`
  via osascript, AppleEvent error mapping, Win VK → macOS HIToolbox
  key code table
- `src/StudioProActions.cs` — surface `ui.LastFailureReason` in failure
  ActionResults
- `tests/StudioProActionServerTests.cs`, `tests/StudioProActionsTests.cs`
  — mock updates + propagation test

## 1.2.0 — 2026-05-07

### macOS support

Concord now runs on Studio Pro for Mac in addition to Windows. The C#
extension, the WebView UI, and the test suite all branch on
`OperatingSystem.IsMacOS()` / `IsWindows()` so a single build of
`Concord.dll` works on either host.

**POSIX PTY backend (`src/UnixPtySession.cs`).** Mirrors the
`IPtySession` surface of `ConPtySession` but built directly on
`libSystem.dylib` — `openpty(3)` to allocate the pty pair,
`posix_spawn_file_actions_*` + `posix_spawnp` to wire the slave fd to
stdin/stdout/stderr, `posix_spawn_file_actions_addchdir_np` (macOS
10.15+) to chdir before exec so shells start in the project root rather
than the Studio Pro `.app` bundle. EOF on the master fd is signaled by
`EIO` rather than a 0-byte read on Darwin — caught and surfaced as EOF
to preserve the cross-platform `IPtySession` contract. Dispose order is
SIGHUP → bounded waitpid → SIGKILL → close, with a watchdogged
`close()` call (a stuck close on a pending master read can otherwise
freeze the UI thread for minutes — verified during bring-up).

**WKWebView bridge (`ui/src/bridge.ts`).** Detects WebView family at
runtime and dispatches accordingly: `window.chrome.webview` for
WebView2 on Windows, `window.webkit.messageHandlers.studioPro` +
`window.WKPostMessage` for WKWebView on Mac. WKWebView requires JSON
string payloads; WebView2 accepts objects directly.

**WKWebView focus + keyboard fixes (`ui/src/xterm-tab.ts`).**
WKWebView refuses programmatic focus on the off-screen helper
`<textarea>` xterm.js relies on for input; we reposition the helper
on-screen with `opacity: 0` and walk the focus chain on mousedown.
A document-level keydown→VT100-bytes fallback fires when the textarea
doesn't receive first-responder, mapping arrows / function keys / Enter /
Backspace / Ctrl-letter combos to the byte sequences a TUI expects.

**Settings.sqlite probe — Mac path (`src/StudioProThemeProbe.cs`).**
Studio Pro on Mac persists its preferences at
`~/Library/Application Support/Mendix/Settings.sqlite`, not the XDG
`~/.local/share` path that .NET's `LocalApplicationData` resolves to on
Darwin. The probe branches explicitly. New
`tests/StudioProThemeProbeTests.cs` covers both Windows and Mac path
resolution.

**Shell handling.** `ShellDetector` returns `$SHELL` (typically zsh on
modern macOS), `/bin/zsh`, `/bin/bash`, `/bin/sh`, plus `pwsh` if on
PATH. `TerminalSettings.MigrateShellPathForPlatform` rewrites obviously
incompatible saved values (`cmd.exe` on Mac, `/bin/zsh` on Windows) to
the OS default at load time, so `terminal-settings.json` files survive
moving a project between hosts. `TerminalSessionManager` injects a
zsh `ZDOTDIR` override (Mac: `~/Library/Application Support/Concord/zsh`)
and a bash rcfile that prepends `/opt/homebrew/{bin,sbin}` and
`/usr/local/bin` to PATH so `claude`, `codex`, and `gh` resolve out of
the box without the user's `.zshrc` having loaded yet.

**Action bridge (`src/StudioProActionServer.cs`).** Switched from
`HttpListener` to a hand-rolled HTTP/1.1 dispatcher over `TcpListener`
(~150 LOC). HttpListener on macOS does not properly isolate prefixes
by port — probes to `localhost:55169` were being answered by Studio
Pro's own HttpListener on `7782` with a `Microsoft-NetCore/2.0`
404 — and HttpListener is also not officially supported on Mac by
.NET. TcpListener is cross-platform, well-supported, and our HTTP
needs are tiny (POST `/mcp`, JSON in/out). The four hotkey-based
tools (`run_app` / `stop_app` / `refresh_project` / `save_all`) silently
no-op on Mac via `OperatingSystem.IsWindows()` guards in
`StudioProUiAutomation.Send` — they require Win32 `PostMessage` with
no equivalent that works on Mac without prompting for accessibility
permissions. The two service-based tools
(`get_active_run_configuration`, `get_app_status`) work on both
platforms.

**WKWebView main-thread offload (`src/TerminalSessionManager.cs`).**
Studio Pro's WKScriptMessage handler delivers JS→C# messages on the
main UI thread on Mac. Synchronously taking `WriteLock` and writing to
the PTY would block the main thread — visible as the rainbow
beachball on every keystroke. `Write(tabId, data)` now offloads the
lock-acquire + PTY write to the thread pool. Per-tab order is still
preserved by the `SemaphoreSlim` semaphore. WebView2 on Windows
happens to dispatch off-thread, which masked the issue on the original
code path; the offload helps both platforms.

**Build target (`Terminal.csproj`).** `DeployToMendix` already had
cross-platform branches; this release adds an `extensions-cache`
overlay step so Mac builds also refresh the per-project Studio Pro
snapshot at `<project>/.mendix-cache/extensions-cache/<guid>/`.
Without this, Studio Pro on Mac kept serving the cached `wwwroot/`
across iterations.

### Tests + tooling

- `StudioProThemeProbeTests.cs` — verifies path resolution branches
  cleanly to Windows (`%LOCALAPPDATA%\Mendix\Settings.sqlite`) on Win
  and to `~/Library/Application Support/Mendix/Settings.sqlite` on Mac
- Cross-platform `Spawn_Echo_ProducesExpectedOutput_CrossPlatform`
  exercises the `PtyNetFactory` dispatch end-to-end on whichever OS
  the test runs on (cmd.exe / `/bin/echo`)
- `TerminalSettings` tests now use OS-aware shell paths so the
  platform-migration logic doesn't rewrite the test fixture under us
- 95 xunit tests passing on Mac + Windows (was 88 on 1.1.1)

### Known caveats on Mac

- The four hotkey-based Action Bridge tools (`run_app`, `stop_app`,
  `refresh_project`, `save_all`) are no-ops on Mac. Use Studio Pro's
  own keyboard shortcuts directly.
- Studio Pro's per-project `.mendix-cache/extensions-cache/` snapshot
  means a manual drop-in of a new `Concord/` folder requires a full
  Studio Pro restart (or a manual cache clear) before the new bits are
  served. The developer-path build handles this automatically.

## 1.1.1 — 2026-05-02

### MCP probe + save fixes (fresh-project regression)

Two bugs surfaced when opening Concord on a fresh Mendix project that had
no prior `terminal-settings.json`:

- **Wrong default MCP port (7782).** `TerminalSettings.Defaults()` used a
  legacy port, so a fresh project's "Enable Studio Pro MCP" toggle would
  probe `localhost:7782` and time out. Default is now `8100` (Studio Pro's
  standard MCP port). The runtime always re-probes Studio Pro's actual
  port from `%LOCALAPPDATA%\Mendix\Settings.sqlite` at save time; the
  default only applies when the SQLite probe fails entirely.
- **Settings didn't persist when MCP probe failed.** Toggling "Enable
  Studio Pro MCP" and clicking Save would silently revert if the probe
  timed out. The probe failure now surfaces a notice but the save
  proceeds — the user's intent (toggle ON) is preserved; they fix the
  Studio Pro Preferences and re-save to wire up the CLI configs.

These regressions existed in 1.1.0 but were latent because the testbed
project (TestOSApp3) had a pre-existing settings file with the right port.

## 1.1.0 — 2026-05-01

### Paste pipeline overhaul

**What this means in practice:** users can now paste multi-page content
(policy docs, code blocks, chat transcripts — anything up to 1 MB)
directly into Claude Code's prompt and have it land as one paste, not
as 50+ individual submissions. When the receiving CLI supports it,
big pastes collapse to the native `[Pasted text +N lines]` placeholder
in the prompt history. Pastes ≥ 4 KB show a quiet status notice;
≥ 50 KB show an estimated delivery time; ≥ 1 MB are refused with a
"save to a file" guidance.

Multi-line paste into a CLI prompt (notably Claude Code) used to truncate
above ~30 lines on Windows. Fixed end-to-end with a four-layer approach.

**PTY backend: WinPTY → ConPTY.** Replaced `Quick.PtyNet` +
`Quick.PtyNet.WinPty` with hand-rolled `kernel32!CreatePseudoConsole`
P/Invoke (~290 LOC, `src/PtySession.cs`). ConPTY proxies VT input mode
faithfully, so modern TUI prompts (Claude Code, vim, fzf) can negotiate
bracketed-paste mode (`\x1b[?2004h`) with our xterm.js. Verified: first
`bracket-mode SET` log line ever observed across all four investigation
rounds. Side wins: no more native sidecar `winpty.dll` to deploy, no
more `AssemblyLoadContext` resolver hack for MEF load paths.

**Paced chunking with per-tab write lock.** UI chunks input ≥ 1 KB into
256-byte slices with 25 ms gaps; C# `TerminalSessionManager.Write`
serializes per session via `SemaphoreSlim`. Defense in depth for
non-bracketed receivers and very large pastes. Numbers tuned via real
measurement against Claude Code on Windows.

**LF-bypass branch for non-bracketed receivers.** When bracketed-paste
mode is OFF and the paste contains newlines, bypass xterm's default
`\r?\n → \r` coercion (which causes line-aware prompts to treat each
newline as Enter/submit) and stream LFs directly via the keystroke
channel.

**Size-tiered UX.** Pastes ≥ 4 KB show a brief notice; ≥ 50 KB show a
duration estimate; ≥ 1 MB are refused with a "save to file" hint. New
shared `notice.ts` helper lets any UI component surface the chrome
banner (previously private to settings-modal).

### Tests + tooling

- New pure helpers in `ui/src/paste.ts` (line-ending normalization, size
  classifier, chunk range generator, duration estimator, line counter)
- 26 new vitest tests covering all five helpers (33 UI tests total)
- 2 new xunit tests in `tests/TerminalSessionManagerWriteLockTests.cs`
  proving per-session write serialization without cross-session
  blocking (88 C# tests total)
- Manual paste regression matrix added to `DEPLOYING.md`
- Architecture + diagnostic playbook in `docs/PASTE.md`

### Diagnostics

- Output stream scanner detects `\x1b[?2004h` / `?2004l` and logs
  `bracket-mode SET` / `bracket-mode RESET` per tab
- Paste handler logs `bracketed=`, MIME types, plain length (shape only,
  never content — clipboard secrets risk)
- Per-keystroke input log gated behind `bytes > 32` (no more typing
  flood in the log)
- Removed clipboard-content preview lines from the log

### Files added

- `src/PtySession.cs` (full rewrite for ConPTY)
- `ui/src/paste.ts`, `ui/src/paste.test.ts`
- `ui/src/notice.ts`
- `tests/TerminalSessionManagerWriteLockTests.cs`
- `docs/PASTE.md`
- `CHANGELOG.md` (this file)

### Files removed from deploy

- `winpty.dll`, `winpty-agent.exe` (no longer needed)
- 7 fewer files in the deployed extension folder

## 1.0.0 — 2026-04-30

Initial Concord release (renamed from "Terminal" / "mxTerminal"). See
git history before this changelog entry for the rename + visual identity
work.
