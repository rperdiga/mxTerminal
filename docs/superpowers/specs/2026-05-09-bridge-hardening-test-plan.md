# Concord v4.2.0 — Maia Bridge Hardening: Test & Verification Plan

**Date:** 2026-05-09
**Status:** Plan — to be executed between v4.2.0 code-complete and marketplace publish
**Companion spec:** `2026-05-09-bridge-hardening-implementation.md` (architect, parallel)
**Owner:** Neo (executes); Alex (drafted plan)

---

## 1. Summary

The 2026-05-09 cocktail-clone smoke test exposed five concrete bridge failure modes (connection storm, missing re-injection, no reconnect, background-tab throttling, brittle poll parser). v4.2.0 fixes them. This plan verifies — across 4 layers (xUnit unit, JS-agent unit, gated live, manual smoke) — that each fix actually holds under load, recovery, and the same end-to-end workload that broke v4.1.4. The cocktail-clone regression is the highest-confidence end-to-end signal and is the gate before publishing to the Mendix Marketplace.

The plan is calibrated to a senior engineer. It pins WHAT must be true and WHAT counts as proof, not every keystroke.

---

## 2. Coverage map: fixes to tests

For each fix in the architect's spec, the test layer(s) that prove it. If the architect's final spec lands a fix not on this list, add a row before executing the plan.

| Fix (per implementation spec) | L1 unit | L2 JS | L3 live | L4 smoke | Cocktail regression |
|---|---|---|---|---|---|
| Persistent CDP connection (one WS reused) | yes | — | yes | — | yes |
| Cached debug-port (no PowerShell-per-call) | yes | — | yes | — | yes |
| Auto-reconnect on WS failure | yes | — | yes | — | yes |
| Auto-re-inject on `StatusAsync` after WebView reload | yes | yes | yes | yes | indirect |
| Heartbeat keep-alive (10s) | yes | — | yes | yes | indirect |
| Defensive `poll()` parser (never throws "unexpected shape") | yes | yes | yes | yes | yes |
| Diagnostic logging toggle (`MaiaDiagnosticsEnabled`) | yes | — | — | yes | — |
| Background-throttle resilience (timer-based scan + heartbeat) | — | yes | yes | yes | — |

If the architect's spec adds e.g. a renamed setting, a per-WebView agent-version probe, or a different reconnect policy, edit row labels here before writing the tests so they match the actual class/property names.

---

## 3. Layer 1 — xUnit unit tests

All paths under `tests/Maia/`. Match the existing naming convention (`<ClassUnderTest>Tests.cs`, `[Fact]` per behavior, `FluentAssertions` for asserts, fakes injected via constructor). No new test framework. No file scoping changes.

### 3.1 `tests/Maia/CdpInjectedTransportTests.cs` — additions

Already exists. Add to the same class:

| Test method | Mocks | Asserts |
|---|---|---|
| `SendAsync_ReusesPersistentClient_AcrossCalls` | `FakeCdp` with a `ConnectCount` counter | After 5 sequential `SendAsync`, `ConnectCount == 1`. Implies `clientFactory` is now wired through a singleton/shared accessor in production, or the transport caches its own client. |
| `StatusAsync_AutoReinjectsAgent_WhenAgentMissing` | `FakeCdp` returning `{"unknown":true}` from poll AND a queued response of `"installed"` from the next eval | `StatusAsync` must re-evaluate the agent install JS before re-trying the poll. Assert eval-order: agent-install JS, then poll JS. |
| `StatusAsync_AutoReinjectsAgent_OnlyOnce_PerStatusCall` | same | If re-injected agent ALSO returns `unknown:true` (true ghost handle), method MUST raise `TransportUnavailable("Unknown handle: ...")` rather than spin re-injecting. |
| `StatusAsync_DefensivePoll_OnUnexpectedShape_LogsAndRaisesTransportError` | `FakeCdp` returning `JsonValue.Create(42)` (a number, not an object) from poll | Method does NOT throw the literal string `"poll() returned unexpected shape"`. It either retries once and (if still bad) raises a `TransportUnavailable` whose message names the shape received. The verbatim string `"poll() returned unexpected shape"` MUST NOT appear in any thrown exception. |
| `StatusAsync_DefensivePoll_RetriesOnceOnEmptyPayload` | `FakeCdp` returning `null` first call, then a valid `{"status":"done","response":"x"}` | Asserts exactly 2 poll evals; result `Done == true, Response == "x"`. |
| `SendAsync_OnConnectionFailure_AttemptsReconnect` | `FakeCdp` whose first `EvaluateAsync` throws `IOException("connection forcibly closed")`, second succeeds | Asserts `SendAsync` returns success; `ConnectCount` increments by 1 (the reconnect). |
| `Heartbeat_FiresEveryInterval_WhilePersistentClientOpen` | A `FakeCdp` that records eval JS and a fake clock injected via `IClock` | After advancing 25s on the clock and pumping the heartbeat task, assert at least 2 evals matching `1+1` (or whatever literal the architect picks) were sent. If architect picks "WebSocket ping frame" instead of an eval, assert via the fake's `PingCount`. |

If the architect introduces a new class like `PersistentCdpClient` that wraps `ICdpClient`, add a separate file `tests/Maia/PersistentCdpClientTests.cs` with the connection-reuse, reconnect, and heartbeat tests instead of stuffing them into transport tests. The boundary should match the architect's class boundary.

### 3.2 `tests/Maia/MaiaDiagnosticsTests.cs` — new file

Covers the verbose-logging toggle. If the architect implements diagnostics as a static `MaiaDiagnostics.Enabled` flag plus `MaiaDiagnostics.LogPayload(...)`:

| Test method | Asserts |
|---|---|
| `LogPayload_NoOps_WhenDisabled` | With `Enabled = false`, calling `LogPayload("send", "{...}")` produces zero entries on the captured `Logger`. |
| `LogPayload_WritesStructuredLine_WhenEnabled` | With `Enabled = true`, log line contains `[maia-diag]` prefix, the call name (`send`), and the payload. |
| `LogPayload_TruncatesAt8KB` | Payload >8KB is truncated; suffix `…(truncated)` appears. (Picks a sane cap; adjust to architect's number.) |

### 3.3 `tests/Maia/CdpClientTests.cs` — new file (or extension to existing `CdpInjectedTransportTests`)

Covers the persistent-connection layer if the architect places it in `CdpClient` itself (i.e. `CdpClient` becomes a long-lived singleton):

| Test method | Asserts |
|---|---|
| `ConnectMaiaAsync_IsIdempotent_WhenAlreadyConnected` | Calling twice produces one underlying `ClientWebSocket` (state `Open`). |
| `EvaluateAsync_ThrowsOnConnectionClosed_TriggersReconnectFlag` | If WS state is `CloseReceived` mid-eval, raises a typed `CdpDisconnected` exception that the upper layer interprets as "reconnect, then retry". |
| `Dispose_DoesNotKillSingleton` | If made a singleton, `DisposeAsync` on a borrowed ref must NOT close the shared WS. (Or: architect uses a different ownership model — assert that ownership model.) |

This bucket depends on architect's choice between (a) singleton `CdpClient`, (b) pooled `CdpClient`, (c) per-transport persistent ref. Match the actual choice. Don't over-spec.

### 3.4 Existing tests — regression

Re-run all of:
- `MaiaActionsTests`, `MaiaRouterTests`, `MaiaJsonRpcTests`, `MaiaTypesTests`, `EmbeddedResourceTests`, `CdpChatTransportTests`.
- All non-Maia `tests/` (the existing v4.1.4 suite).

None of the v4.2.0 changes should require modifying these; if any break, the change broke a contract — investigate before adapting the test.

### 3.5 Run command

```powershell
dotnet test --filter "Category!=MaiaLive" --logger "console;verbosity=normal"
```

**Pass gate:** all green; zero skipped (other than MaiaLive); zero new warnings introduced by test files.

---

## 4. Layer 2 — JS agent tests (Node subprocess)

`maia_agent.js` lives at `src/Maia/maia_agent.js`. Per existing README this layer is "TODO: port the prototype's 12 tests." v4.2.0 adds at least the agent-side fixes. If the agent itself is not modified for v4.2.0, this layer can stay deferred. If the architect added e.g. a `__maiaBridge.version === 2` check or moved the timer to `requestAnimationFrame` + `setInterval` belt-and-suspenders, add tests.

Proposed location: `tests/Maia/js/` (new), with a `package.json` pinning Node 20 and `vitest` (matches existing UI testing). One xUnit `[Fact]` per JS test that shells out via `Process.Start("node")`, reads JSON results, asserts.

Tests to add IF the agent changed for v4.2.0:

| Test | Asserts |
|---|---|
| `Agent_VersionBumpForcesReinjection` | After `__maiaBridge.version = 1` is installed, re-running the install script sees `version === 2` is required and re-installs cleanly (returns `installed`, not `already-installed`). |
| `Agent_PollNeverReturnsNonObject` | Synthetic call to `__maiaBridge.poll(badInput)` always returns an object — either `{unknown: true}` or `{status, response, elapsed_ms}`. Never `null`, never a string, never `undefined`. |
| `Agent_TimerKeepsRunning_WhenDocumentHidden` | If architect moved the `setInterval` to use `Worker` or another non-throttled timer, a synthetic `document.hidden = true` does NOT slow the scan callback by >2x. (Skip if architect chose a different fix path — e.g. heartbeat-from-host.) |

If `maia_agent.js` is unchanged for v4.2.0, document that here and skip Layer 2 additions.

---

## 5. Layer 3 — Live tests (gated, `CONCORD_MAIA_LIVE=1`)

These exercise the actual WebView2 + CDP + Maia stack. They are the meat of v4.2.0 because every failure mode v4.2.0 fixes is a CDP/WebView interaction that mocks cannot reproduce.

### 5.1 Environment setup

- Studio Pro 11.10+ launched on a test Mendix project (any project; `CocktailDemo32-main` recommended for parity with the regression run).
- Single Studio Pro instance only.
- Maia tab visible in the right pane (initially focused — focus changes are part of specific tests below).
- Concord v4.2.0 .mxmodule built and installed (so the C# code under test is the production-built artifact, not just a `dotnet test` build).
- `CONCORD_MAIA_LIVE=1` set in the shell.

### 5.2 Test additions to `tests/Maia/MaiaLiveTests.cs`

All decorated `[SkippableFact, Trait("Category","MaiaLive")]`. All gated by the existing `LiveEnabled` check.

#### 5.2.1 `LongSession_HundredAsks_NoConnectionStorm`

```
[SkippableFact]
public async Task LongSession_HundredAsks_StaysOnOneConnection_NoIOExceptions()
```

**What it does:**
1. Spawn a router with both transports (mirroring `NewRouter()`).
2. Loop 100 times: `actions.AskAsync("Reply with the single word: pong", 30, ct)`.
3. Track: count of distinct CDP WebSocket sessions opened (instrument via a counter on `CdpClient` or a probe in `Logger`), count of `IOException` raised, count of `Unknown handle` raised, total wallclock.

**Success:**
- Zero `IOException` thrown.
- Zero `Unknown handle: <name>` errors returned.
- WebSocket-open count == 1 (or ≤2 if architect allows one preventive reconnect; match the architect's stated tolerance).
- Total wallclock < 6 minutes (sanity check against per-call PowerShell scan regression: previously each call cost ~1s in the WMI lookup; should now be amortized).

**Failure signal to capture:** if the test fails mid-loop, log the iteration index, the wallclock time of last success, the contents of the next 3 polls. That dump goes into the v4.2.0 release log.

#### 5.2.2 `BackgroundThrottle_AskCompletes_WhenMaiaTabUnfocused`

```
[SkippableFact]
public async Task BackgroundThrottle_AskCompletes_WhenMaiaTabIsBackgrounded()
```

**Manual prep (test runner does this between assertions, not the test itself — use `Console.WriteLine` to instruct the operator):**
1. Confirm Maia tab is visible.
2. Issue `actions.AskAsync("Reply with: foo", 30, ct)` — assert success (`response` contains `foo`).
3. Print: "Operator: in Studio Pro, click the App Explorer tab in the right pane (anywhere that takes Maia out of view). Press Enter when done."
4. Read a line from `Console.In`.
5. Issue `actions.AskAsync("Reply with: bar", 30, ct)` — assert success within timeout.
6. Print: "Operator: click the Maia tab again. Press Enter when done."
7. Issue `actions.AskAsync("Reply with: baz", 30, ct)` — assert success.

**Success:** all three calls return `Done = true` within 30s each. Step 5 is the load-bearing one — pre-v4.2.0 it would have hung past 30s due to Chromium timer throttling.

**This is intentionally interactive.** Live tests are manually triggered; the operator-pause is fine. If preferred, gate behind a second env var `CONCORD_MAIA_LIVE_INTERACTIVE=1` so the non-interactive subset can run unattended.

#### 5.2.3 `LayoutEditPrompt_DoesNotKillBridge`

```
[SkippableFact]
public async Task LayoutEditPrompt_BridgeRecovers_DoesNotDie()
```

**What it does:**
1. Issue a prompt designed to provoke Maia's structured-response path (the path that returned the unexpected shape on 2026-05-09): `actions.AskAsync("Duplicate the page Cocktails_Overview into a new page Cocktails_Overview2 and update navigation. Reply DONE when finished.", 90, ct)`.
2. Regardless of whether Maia answers correctly or returns a weird shape, assert: the bridge is still alive afterward — issue `actions.AskAsync("Reply with: alive", 30, ct)` and assert success.
3. If diagnostic logging is enabled (`MaiaDiagnosticsEnabled = true`), assert `resources/terminal.log` contains the verbose dump of step 1's poll responses.

**Success:** step 2's `alive` answer returns. Step 1's success is irrelevant — what matters is that v4.2.0 doesn't kill the bridge when Maia returns something the parser doesn't recognize.

#### 5.2.4 `WebViewReload_AutoReinjectAndRecover`

```
[SkippableFact]
public async Task WebViewReload_StatusAutoReinjects_AndOldHandleDegradesCleanly()
```

**What it does:**
1. Issue `actions.SendAsync("Reply with: setup", null, ct)`. Capture `handle1`.
2. Print: "Operator: in Studio Pro, close the Maia panel (X on the tab) and reopen it (right-pane tab strip). Press Enter when done."
3. Issue `actions.StatusAsync(handle1, ct)`. Assert: result is either `Done = false` with a sensible response indicating the handle is from a previous WebView session, OR a clean `ActionResult.Fail` whose message clearly says "handle no longer valid" or similar — NOT a raw `IOException`, NOT `Unknown handle: <name>` without the auto-reinject having tried.
4. Issue `actions.AskAsync("Reply with: alive", 30, ct)`. Assert success — bridge fully recovered.

**Success:** step 3 produces a defined error shape; step 4 succeeds within timeout. The auto-reinject ran (verifiable in diagnostic log).

#### 5.2.5 `ReconnectOnIOException_TransparentToCaller`

```
[SkippableFact]
public async Task ReconnectOnIOException_NextCallSucceeds()
```

**What it does:**
1. Issue `actions.AskAsync("Reply with: one", 30, ct)`. Assert success.
2. Force-kill the underlying CDP WebSocket via reflection on `CdpClient` (or a test-only helper if the architect added one — `CdpClient.ForceCloseForTest()`). If no test helper exists, this test stays as a `[Skip]` fact with a comment recording why.
3. Issue `actions.AskAsync("Reply with: two", 30, ct)`. Assert success — the reconnect logic kicked in transparently.

**Success:** step 3 succeeds; the diagnostic log shows a reconnect event between steps 1 and 3.

### 5.3 Run command

```powershell
$env:CONCORD_MAIA_LIVE = "1"
$env:CONCORD_MAIA_LIVE_INTERACTIVE = "1"   # only if Sec 5.2.2 runs in this pass
dotnet test --filter "Category=MaiaLive" --logger "console;verbosity=detailed"
```

**Pass gate:** all five new live tests pass. The interactive ones can pass in two batches (interactive and non-interactive) on separate runs.

---

## 6. Layer 4 — UI manual smoke (operator checklist)

After building the v4.2.0 .mxmodule (`dotnet publish` per `DEPLOYING.md`) and installing it in Studio Pro on a clean test project. Operator checks:

### 6.1 Settings UI

- [ ] Open Concord settings panel. The "Concord MCP" section renders without layout drift vs. v4.1.4.
- [ ] New "Diagnostic logging" sub-toggle (or whatever the architect named it) is present, defaulted **off**.
- [ ] Toggling Diagnostic logging on persists across Studio Pro restart.

### 6.2 Diagnostic-logging toggle

- [ ] With the toggle ON, fire 3 `maia__ask` calls from a Claude Code session attached to `concord-mcp`.
- [ ] Open `<project>/resources/terminal.log` (or wherever Concord writes its log — if path differs, follow architect's spec).
- [ ] Verify each call's CDP request (eval JS) and response (poll result) appears in the log with a `[maia-diag]` (or architect's) prefix.
- [ ] Verify payloads >8KB show truncation suffix (synthetic test: send a long prompt with a long expected answer).
- [ ] Toggle OFF. Fire 3 more `maia__ask` calls. Verify NO new `[maia-diag]` lines appear.

### 6.3 Heartbeat keep-alive

- [ ] With Maia panel idle (no new ask calls), wait 60 seconds.
- [ ] Then fire one `maia__ask`. Assert it returns within timeout — pre-v4.2.0 the WebSocket would have been killed by Chromium idle eviction, requiring a reconnect cost on first call after idle.
- [ ] Diagnostic log should show heartbeat eval/ping every ~10s during the idle wait.

### 6.4 PR description checklist

Paste this checklist into the v4.2.0 PR description, signed off by the operator with a screenshot of:
- Settings panel showing the new toggle.
- A snippet of `terminal.log` showing diagnostic output.
- A snippet showing heartbeat lines.

---

## 7. Cocktail-clone regression run

This is the highest-confidence end-to-end signal. v4.1.4 failed this exact test on 2026-05-09; v4.2.0 must let it complete.

### 7.1 Setup

1. Close Studio Pro fully (`/close-studio-pro` or kill all `studiopro.exe` + child processes).
2. Delete `C:\Workspace\MendixApps\CocktailDemo32-main` if it exists (start clean).
3. Re-clone or re-extract the CocktailDemo32 source (whichever the operator originally used on 2026-05-09 — exact source documented in `feedback_concord_v414_cocktail_test_findings.md`).
4. Open the project in Studio Pro 11.10+. Wait for first-load to complete (entity loading, dependency resolution).
5. Verify Concord v4.2.0 is installed (Settings → Concord MCP → version readout).
6. Open Maia panel. Confirm visible.
7. From a separate Claude Code shell attached to `concord-mcp`:

### 7.2 The exact prompt (verbatim from 2026-05-09)

Use the same Claude Code prompt that was issued during the failing v4.1.4 run. If the prompt is not preserved verbatim, reconstruct the closest equivalent and document the diff in the regression log. The semantic intent is "have Maia clone the Cocktails_Overview page into a new variant, then update navigation to include it" — a 30+ minute multi-step build that exercises `maia__send`, `maia__wait`, `maia__status`, and `maia__ask` repeatedly under realistic load.

### 7.3 Success criteria

The build completes without ANY of the v4.1.4 failure signatures appearing in the Claude Code transcript or `terminal.log`:

- [ ] Zero `Unknown handle: <name>` errors. (v4.1.4: 3 occurrences.)
- [ ] Zero `poll() returned unexpected shape:` errors. (v4.1.4: 2 occurrences. Note: in v4.2.0 the verbatim string MUST NOT appear at all — defensive parser logs but does not throw.)
- [ ] Zero `IOException: connection forcibly closed` errors. (v4.1.4: 8+ occurrences.)
- [ ] Total wallclock comparable to v4.1.4's pre-failure runtime (i.e. it's not 10x slower; persistent connection should make it FASTER, not slower).

### 7.4 Capture

Save these as artifacts attached to the v4.2.0 release notes:
- Full Claude Code transcript (markdown export).
- `terminal.log` from the Mendix project's resources folder.
- Screen recording (optional but recommended — proves the multi-step build actually finished).
- A one-page summary: time started, time finished, prompt issued, count of each error type seen, final state of the Cocktails_Overview2 page.

**This is the gate.** If the cocktail-clone regression run does not complete cleanly, do NOT publish to marketplace. Investigate, fix, re-run.

---

## 8. Acceptance gate (pre-marketplace publish)

Publish to marketplace ONLY when ALL of the following are in hand:

| # | Evidence | Where it lives |
|---|---|---|
| 1 | All Layer 1 + Layer 2 tests pass on Windows + macOS CI | GitHub Actions run, linked from PR |
| 2 | All Layer 3 live tests pass (operator-attested) | Run log + screenshot in PR |
| 3 | Layer 4 manual smoke checklist signed off | PR description |
| 4 | Cocktail-clone regression: clean transcript, no v4.1.4 error signatures | Artifact attached to PR |
| 5 | `CHANGELOG.md` updated for v4.2.0 with the bridge-hardening entry | Repo |
| 6 | `manifest.json` version bumped to 4.2.0 | Repo |
| 7 | `.mxmodule` built from the merged-to-main commit (not a branch build) | Build artifact path documented in PR |
| 8 | Marketplace listing copy-format follows Neo's preferred paste-format | Per `feedback_concord_copy_style.md` |

If any of #1–#4 are red, the gate is closed.

---

## 9. Test execution order

| Phase | When | What runs | Gate |
|---|---|---|---|
| Pre-implementation | Before any v4.2.0 code lands | Write the L1 unit tests for the planned fix, watch them fail (TDD-lite — recommended but not mandated) | None |
| Per-commit | After each fix commit on the v4.2.0 branch | Subset L1 tests touching that fix; full L1 if commit touches >1 area | All green |
| Pre-PR-merge | Before merging v4.2.0 → main | Full L1 suite + L2 (if added) + manual L3 (operator on a dev box) | All green |
| Pre-marketplace-publish | After main merge, before publish | Full L1 + L2 + L3 + L4 + cocktail regression | Section 8 acceptance gate |

Per-commit and pre-PR runs should NOT run live tests in CI (they require Studio Pro + Maia). Live tests run on the operator's machine on demand.

---

## 10. NOT covered in v4.2.0 (decisions, not omissions)

- **macOS Maia transport.** Bridge stays Windows-only. Architect spec confirms scope. No mac-specific tests needed beyond the existing "platform == darwin → tools omitted" L1 coverage in `MaiaJsonRpcTests`.
- **Tier 0 native MCP transport** (Mendix 11.12+ native MCP-server-as-tool). Not yet shipped by Mendix; transport interface is the swap-out seam. No tests; no shape to test against.
- **Marketplace dark-mode rendering bug.** Tracked separately in `project_concord_dark_mode_marketplace_bug` memory file. Independent of bridge hardening; do not block v4.2.0 on it.
- **Multi-instance Studio Pro disambiguation.** Multi-instance still raises `TransportUnavailable` with the documented message. Not in scope for v4.2.0.
- **Tier 2 (`cdp_chat`) hardening beyond Tier 1.** `CdpChatTransport` benefits passively from the persistent-CDP and reconnect work in `CdpClient` if architect chose to put fixes there; no Tier-2-specific tests added unless the architect's spec calls for them.
- **Long-prompt streaming UX.** The defensive poll parser merely guarantees the bridge survives a weird shape; it does not improve the streaming UX. Streaming polish is a v4.3.0 concern.
- **Telemetry / metrics export.** Diagnostic logging writes to `terminal.log` only. No metrics endpoint, no aggregation. Future iteration if needed.

---

## 11. Open questions (resolve before live testing)

1. **Cached debug-port lifetime.** If the architect caches the WMI-derived port for the lifetime of the Concord process: what happens when the operator restarts Studio Pro (different child WebView2 PID, possibly different port)? Is there a port-invalidation path on connect failure? L3 test 5.2.5 implicitly covers this if the reconnect logic re-runs the WMI scan; explicitly assert if not.
2. **Heartbeat content.** `1+1` eval vs. WebSocket-level ping frame? Eval is observable in CDP traffic logs (good for debugging) but costs more (full Runtime.evaluate round-trip). Ping is cheap but invisible. Architect's choice — adjust 3.1's heartbeat test to match.
3. **Diagnostic-logging output destination.** `resources/terminal.log` is the assumed path. Confirm against architect's spec — if it's a different file (e.g. `%LOCALAPPDATA%\Concord\maia-diag.log`), update Section 6.2 and the cocktail regression capture in 7.4.
4. **Re-injection idempotency under race.** If two `StatusAsync` calls hit a missing-agent state concurrently, do both attempt re-inject? The architect's spec needs to either serialize re-inject (semaphore) or document the race as benign. L1 test missing here — add if the architect chooses serialization.
5. **Cocktail-clone exact-prompt preservation.** Was the verbatim Claude Code prompt from 2026-05-09 saved? If not, reconstruct best-effort and note the divergence in 7.2 before running.
6. **Test-only hooks.** Tests 3.1 (heartbeat-with-fake-clock) and 5.2.5 (force-close WS) need test helpers in production code (`IClock` injection; `ForceCloseForTest()` method). Acceptable to ship if guarded by `#if DEBUG` or `internal` + `InternalsVisibleTo`. Architect to confirm.
