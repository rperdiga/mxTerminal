# Deploying Concord

Two paths. Pick whichever matches your situation.

- **Developer path** — you're building from source and want to iterate. Skip to [§ Developer path](#developer-path-build-from-source).
- **Consumer path** — you just want Concord working in your Mendix project. Skip to [§ Consumer path](#consumer-path-drop-in-a-prebuilt-folder).

Both paths have the same Studio Pro one-time setup: see [§ Studio Pro setup](#studio-pro-setup) below.

Concord runs on **Windows** and **macOS**, supporting **Studio Pro 10.24.13 through 11.x** (10.x is a preview — menu entry only in 5.0.0-alpha.1; full functionality is 11.x). Path examples in this doc use Windows separators by default; Mac equivalents are called out inline.

> **Deploy folder changed in 5.x.** Studio Pro 11.x projects use `extensions/Concord11x/`; Studio Pro 10.x projects use `extensions/Concord10x/`. The old `extensions/Concord/` path (4.x) is retired. See [§ Migrating from 4.x](#migrating-from-4x-old-extensionsconcord-layout) if you are upgrading.

If you're upgrading from the older "**Terminal**" extension (the original `mxTerminal`), see [§ Migrating from Terminal](#migrating-from-terminal-the-old-name) at the bottom.

If you're upgrading from **Concord 4.x**, see [§ Migrating from 4.x](#migrating-from-4x-old-extensionsconcord-layout) at the bottom.

---

## Studio Pro setup (one-time, both paths)

1. Open Studio Pro **Edit → Preferences → Advanced** tab.
2. Check **Extension Development**.
3. **Restart Studio Pro.** This setting takes effect on next launch only.

This is what allows Studio Pro to load extensions from a project's `extensions/` folder. Without it, Concord won't appear in the Extensions menu no matter where the files live.

(Alternative: launch Studio Pro once with `--enable-extension-development` on the command line. Same effect, but applies only to that single launch — the preference checkbox is the durable answer.)

---

## Consumer path (drop in a prebuilt folder)

The fastest way to get Concord into a Mendix project. No build required.

### Which folder do I need?

| Your Studio Pro version | Folder to use |
|---|---|
| **11.x** (11.10+) | `Concord11x/` |
| **10.24.13** (preview) | `Concord10x/` |

**Never copy both folders into the same Mendix project.** Studio Pro will crash trying to load the wrong-version DLL.

### Get a prebuilt folder

Two ways:

- **From a colleague who built from source:** ask them to zip up `<their-project>\extensions\Concord11x\` (or `Concord10x\`) and send it to you. That folder contains `Concord.Host11x.dll` (or `Concord.Host10x.dll`), `Concord.Core.dll`, `manifest.json`, all the runtime DLLs, and the `wwwroot/` UI bundle.
- **From a release artifact:** if a prebuilt zip is published (e.g. as a GitHub release), download and unzip.

### Install into your Mendix project

1. Open the project folder (e.g. Windows: `C:\Workspace\MendixApps\YourProject`; macOS: `~/Mendix/YourProject`).
2. If an `extensions/` folder doesn't exist at the project root, create it.
3. Copy the `Concord11x` (or `Concord10x`) folder you received into `extensions/`. You should end up with:

**Studio Pro 11.x:**
```
YourProject/
   extensions/
      Concord11x/
         Concord.Host11x.dll
         Concord.Core.dll
         manifest.json
         wwwroot/
         skills/             (7 bundled Mendix skill packs)
         skills-mac/         (Mac overlay — mendix-page-gen variant for macOS)
         rules/              (always-loaded build rules — concord-build-rules.md)
         (...other DLLs and assets)
```

**Studio Pro 10.x (preview):**
```
YourProject/
   extensions/
      Concord10x/
         Concord.Host10x.dll
         Concord.Core.dll
         manifest.json
         (...other DLLs and assets)
```

4. Make sure **Studio Pro setup** above is done.
5. Start Studio Pro and open the project. Studio Pro will:
   - Scan `extensions/` and find Concord.
   - Show a one-time **"Trust this extension"** prompt (per Mendix's extension-trust flow). Approve it.
6. **Studio Pro 11.x:** Open the pane via **Extensions → Concord → Open Pane**. The pane appears in the right-side pane strip (next to Properties / Toolbox / Maia). Click the **Concord** tab in that strip to focus it. **Studio Pro 10.x (preview):** You'll see **Extensions → Concord (10.x preview)** — a placeholder menu entry; the full pane and MCP surface will be available in a later 5.x release.

**On first open of a fresh project, Concord wires itself up automatically:**

- Writes `<project>/.mcp.json` with `mendix-studio-pro` + `concord-mcp` entries (Claude Code + Copilot CLI by default; Codex is opt-in)
- Installs the 7 bundled Mendix skill packs into `<project>/.claude/skills/` and `<project>/.github/skills/`
- Persists Concord's own settings file at `<project>/resources/terminal-settings.json`
- Stamps the current Concord version into the settings file so subsequent opens know wiring is up-to-date

You'll see 1–3 notice banners at the top of the pane explaining what just happened. The banners include "Studio Pro MCP is off" if you haven't enabled it in Studio Pro Preferences yet, and a "keep the Maia panel open" reminder if Maia integration is enabled.

See [README § What Concord writes to disk](./README.md#what-concord-writes-to-disk) for the full inventory.

To install in additional projects, repeat steps 1–6 in each.

To remove: delete the `extensions/Concord11x/` (or `extensions/Concord10x/`) folder. Restart Studio Pro.

> **macOS note:** Studio Pro on Mac snapshots `extensions/` into `<project>/.mendix-cache/extensions-cache/<guid>/` at first load and serves `wwwroot/` from there. If you replace `extensions/Concord11x/` (or `extensions/Concord10x/`) with a newer build while Studio Pro is running, Studio Pro will keep serving the old cached copy until restart. Either fully quit Studio Pro before swapping the folder, or also delete the `<project>/.mendix-cache/extensions-cache/` directory so Studio Pro rebuilds the cache on next launch (the developer-path build does this automatically).

---

## Developer path (build from source)

### Prerequisites

| What | Version | Verify |
|---|---|---|
| Node.js | 18 or newer | `node --version` |
| .NET SDK | 8.x **or** 10.x with the `net8.0` reference pack present | `dotnet --version` and `dotnet --list-runtimes` should show `Microsoft.NETCore.App 8.0.x` |
| Git | any recent version | `git --version` |
| Studio Pro | 10.24.13 or newer (Windows or macOS); 11.10+ for full feature parity | check **Help → About** in Studio Pro |
| OS | Windows 10 1809 (build 17763)+ for ConPTY, **or** macOS 10.15+ for `posix_spawn_file_actions_addchdir_np` | — |

The .NET 10 SDK can target `net8.0` if the .NET 8 runtime + reference pack is installed (which it usually is on a Windows or Mac dev box that's seen any .NET work). If a build fails with "no reference pack for net8.0", install the .NET 8 SDK from https://dotnet.microsoft.com/.

### One-time setup

```sh
git clone https://github.com/rperdiga/mxTerminal.git
cd mxTerminal

# Per-developer deploy config (gitignored — your machine's paths)
# Windows:
copy Directory.Build.props.example Directory.Build.props
# macOS / Linux:
cp Directory.Build.props.example Directory.Build.props

# Edit Directory.Build.props and set the per-host deploy properties.
```

Open `Directory.Build.props` in any editor and configure the per-host deploy targets:

**Single-host dev (only working on one Studio Pro version) — 11.x:**
```xml
<MendixDeployTarget11x>C:\Workspace\MendixApps\YourProject</MendixDeployTarget11x>
```

**Single-host dev — 10.x (preview):**
```xml
<MendixDeployTarget10x>C:\Workspace\MendixApps\YourProject10x</MendixDeployTarget10x>
<ExtensionsApi10xVersion>10.21.1</ExtensionsApi10xVersion>
```

**Cross-version dev (two separate test projects, one per Studio Pro version):**
```xml
<MendixDeployTarget10x>C:\Projects\Test_10_24_13</MendixDeployTarget10x>
<MendixDeployTarget11x>C:\Projects\Test_11_10</MendixDeployTarget11x>
<ExtensionsApi10xVersion>10.21.1</ExtensionsApi10xVersion>
```

**macOS paths follow the same pattern** — replace `C:\Projects\...` with `/Users/you/Mendix/...`.

To deploy to **multiple projects of the same host version**, semicolon-separate the paths:
```xml
<MendixDeployTarget11x>C:\Projects\AppOne;C:\Projects\AppTwo</MendixDeployTarget11x>
```

> **Fallback:** `MendixDeployTarget` (no version suffix) is still accepted as a legacy single-target override — both hosts will deploy to it if their per-host property is unset. This means both `Concord11x/` and `Concord10x/` land in the same project, which crashes the wrong-version Studio Pro. Use only in a true single-host, single-project setup where you are certain only one Studio Pro version will ever open that project.

### Build

```sh
dotnet build
```

What happens:
1. The `BuildUi` target runs `npm install` (first build only — about 30 seconds) and `node esbuild.mjs` to bundle the xterm.js TypeScript UI.
2. C# compiles `Concord.Core.dll`, `Concord.Host11x.dll`, and `Concord.Host10x.dll`.
3. The `DeployToMendix` target copies the build output into each deploy-target project's `extensions/Concord11x/` or `extensions/Concord10x/` directory (determined by which per-host property is set) — `xcopy` on Windows, `cp -R` on macOS/Linux. On Mac it also overlays Studio Pro's `<project>/.mendix-cache/extensions-cache/<guid>/` snapshot, since Studio Pro on Mac serves `wwwroot/` from the cache rather than from `extensions/` directly.

**First-build gotcha:** the csproj's `<Content Include="wwwroot\**\*">` copies the UI bundle into the output, but on a fresh clone `wwwroot/` doesn't exist yet — esbuild creates it during the BuildUi step, AFTER MSBuild has already evaluated the Content glob. **Workaround: run `dotnet build` a second time on the very first build of a fresh clone.** Subsequent builds work the first time. (See `LEARNINGS.md` if it lands in repo for the eventual proper fix.)

### Test

```sh
dotnet test
```

95 xunit tests cover the C# side (action server JSON-RPC, action state machine, run-state probe, MCP config emitters, session manager, ring buffer, settings, logging, per-session write-lock serialization, Settings.sqlite probe on both Windows and Mac paths).

```sh
cd ui && npm test
```

33 vitest tests cover the UI side (paste pipeline pure helpers, base64 round-trip, bridge wiring).

### Manual paste regression matrix

The paste path has both a JS-side branch (xterm bracketed-paste-off bypass) and a paced-chunking layer (256B / 25ms intervals against WinPTY). Run this before shipping any change touching `paste.ts`, `xterm-tab.ts`, or `tab-manager.ts`:

| Source        | Target              | Expected                                               |
| ------------- | ------------------- | ------------------------------------------------------ |
| Notepad       | PowerShell          | Multi-line paste; CRLF preserved                       |
| Notepad       | `claude` (CC 2.1+)  | Full paste; no auto-submit per line                    |
| Teams chat    | PowerShell          | Multi-line paste from `text/html`-only clipboard       |
| Teams chat    | `claude` (CC 2.1+)  | Full paste lands as `[Pasted text +N lines]`           |
| VS Code       | `claude` (CC 2.1+)  | Code block paste preserves indentation                 |
| Single line   | any                 | Submits as single line (no LF added)                   |
| 4 KB+ paste   | any                 | Brief notice "Pasting N lines (X KB)"                  |
| 50 KB+ paste  | any                 | Stronger notice with duration estimate                 |
| 1 MB+ paste   | any                 | Refused with "save to file" guidance                   |

Capture the `paste bracketed=...` and `paced-input ...` log lines from each test in `<project>\resources\terminal.log` and diff against the prior run. Architecture rationale + diagnostic playbook: [docs/PASTE.md](./docs/PASTE.md).

### Iterate

After a code change:

```sh
dotnet build       # rebuilds + redeploys to all MendixDeployTarget10x / MendixDeployTarget11x folders
```

Then **fully close and reopen Studio Pro.** .NET assemblies loaded into Studio Pro's AppDomain can't be unloaded without ending the process — Studio Pro's "reload project" does NOT pick up new DLLs. Plan for a full Studio Pro restart per iteration.

If you're only changing TypeScript UI files (xterm tab manager, settings modal, etc.), the rebuild is fast (~3-5 seconds), but Studio Pro still needs a restart because it loaded the old `wwwroot/index.html` into the WebView at pane-open time.

> **macOS iteration tip:** the build copies fresh assets into both `<project>/extensions/Concord11x/` (or `Concord10x/`) and the per-project Studio Pro cache snapshot at `<project>/.mendix-cache/extensions-cache/<guid>/`, so `dotnet build` followed by a full Studio Pro restart picks up your changes without manually clearing the cache.

### Logs (build + runtime)

- **Build log:** stdout/stderr of `dotnet build`.
- **Extension runtime log:** `<MendixProject>/resources/terminal.log` — every extension lifecycle event, action server start/stop, MCP probe result, paste byte trace.
- **Studio Pro's own log:** extension load failures, MEF errors. Find the path via Studio Pro's `Help → About → Open log folder`.
  - Windows: `%APPDATA%\Mendix\Studio Pro <version>\log\`
  - macOS: `~/Library/Application Support/Mendix/Studio Pro <version>/log/`

---

## Upgrading Concord versions

Concord stores the version that last applied wiring defaults in `<project>/resources/terminal-settings.json` (field `lastAppliedVersion`). On first open after an upgrade, Concord compares the saved stamp against the running version:

- **Stamp is older than installed Concord** — Concord re-applies the wiring keys (MCP enables, sub-toggles, skill clients) to the current defaults so any new functionality lands on disk without manually opening Settings and saving. Runtime preferences (shell, theme, ring buffer, scrollback, restore-tabs, refresh hotkey) are preserved verbatim. Banner: `Updated to {ver}. Rewired: ... Open Settings to adjust.`
- **Stamp matches installed Concord** — no-op.
- **Stamp is newer than installed Concord** — also no-op (a colleague pulled a project last edited from a more-recent-Concord machine — Concord never downgrades the wiring).
- **Settings file is corrupt** — Concord renames it to `terminal-settings.json.broken-{timestamp}.bak` and falls back to defaults. You can recover your custom shell / theme / etc. by hand-editing the backup.

The trade-off you should know about: the upgrade-apply re-defaults a small number of wiring keys, including the per-CLI client lists. If you had **deliberately** disabled MCP or removed a CLI from the wiring in an older version, the upgrade will re-enable the new defaults once. The banner points you to Settings to re-disable if needed. This is by design — most users want new defaults on; the rare deliberate opt-out is a one-time annoyance.

---

## Migrating from "Terminal" (the old name)

If you used the predecessor extension (named `Terminal` / `mxTerminal`), Studio Pro will load BOTH the old `Terminal.dll` and the new `Concord.dll` if both folders exist in `<project>\extensions\`. They have different MEF identities so they won't conflict, but you'll get redundant menus and the old Terminal pane will still register itself.

Clean up:

```powershell
Remove-Item -Recurse -Force "C:\Workspace\MendixApps\YourProject\extensions\Terminal"
```

(Substitute your project path.) Restart Studio Pro. The old menu entries disappear; only Concord remains.

If you previously had a `terminal-settings.json` in `<project>\resources\`, Concord reads it and migrates the values forward — your shell selection, MCP enable state, persistent-tabs preference, and so on all carry over. The file is updated on the next Save in Concord's settings modal. Nothing to delete.

---

## Migrating from 4.x (old `extensions/Concord/` layout)

Concord 5.x changed the deploy folder name. Where 4.x used `extensions/Concord/`, 5.x uses `extensions/Concord11x/` (Studio Pro 11.x) or `extensions/Concord10x/` (Studio Pro 10.x).

### Steps

1. **Delete the old folder:**

   ```powershell
   Remove-Item -Recurse -Force "C:\Workspace\MendixApps\YourProject\extensions\Concord"
   ```

2. **Deploy the new folder** — follow the Consumer path above (drop in `Concord11x/` or `Concord10x/`) or rebuild from source with the updated per-host deploy targets.

3. **Wipe the extensions cache** so Studio Pro picks up the new layout cleanly:

   ```powershell
   Remove-Item -Recurse -Force "C:\Workspace\MendixApps\YourProject\.mendix-cache\extensions-cache"
   ```

   (macOS: `rm -rf ~/Mendix/YourProject/.mendix-cache/extensions-cache`)

4. **Restart Studio Pro.** It will rebuild the cache from the new `Concord11x/` (or `Concord10x/`) folder.

### Settings compatibility

Your existing `<project>/resources/terminal-settings.json` is backward-compatible — Concord 5.x reads it, migrates the values forward, and stamps the new version. No manual edits needed.

### WARNING — never co-deploy both folders

Do **NOT** deploy both `extensions/Concord11x/` and `extensions/Concord10x/` to the same Mendix project. Studio Pro of either version will crash when it attempts to load the wrong-version DLL (the 11.x binary references `IMenuExtension` from the 11.x API; the 10.x binary references the `MenuExtension` abstract base class from the 10.x API — type resolution fails hard on the mismatched version).

---

## Extensions cache (Studio Pro caches a snapshot on first load)

Studio Pro snapshots `extensions/` into `<project>/.mendix-cache/extensions-cache/<guid>/` the first time it loads a project. Subsequent launches serve `wwwroot/` from this cache rather than from `extensions/` directly.

**When to wipe the cache:**
- After upgrading from 4.x (the old `Concord/` snapshot is stale).
- After switching from `Concord11x/` to `Concord10x/` in a project (or vice versa).
- If Studio Pro shows a blank WebView or loads old UI despite a fresh build.

**How to wipe:**

```powershell
Remove-Item -Recurse -Force "C:\Workspace\MendixApps\YourProject\.mendix-cache\extensions-cache"
```

```sh
# macOS
rm -rf ~/Mendix/YourProject/.mendix-cache/extensions-cache
```

Restart Studio Pro. It rebuilds the cache from `extensions/` on next launch. The developer-path `dotnet build` refreshes the cache automatically; consumer-path upgrades (drop-in folder swap) require either a manual wipe or a full quit-before-swap.

---

## Troubleshooting

### "Concord doesn't appear in the Extensions menu"

- **Extension Development isn't enabled.** Edit → Preferences → Advanced → check Extension Development → restart Studio Pro.
- **`extensions/Concord11x/` (or `Concord10x/`) folder is in the wrong place.** Must be at `<MendixProject-root>\extensions\Concord11x\` (or `Concord10x\`) — same level as `<Project>.mpr`.
- **Studio Pro hasn't been restarted since the folder appeared.** Extensions are scanned at startup.

### "Build succeeds but pane is empty / shows blank WebView"

- First-build wwwroot chicken-and-egg. Run `dotnet build` once more. (See § Build.)
- Look at `<project>\resources\terminal.log` for `InitWebView` line — if it shows a URL but the WebView is blank, the bundle didn't make it into `extensions\Concord11x\wwwroot\` (or `Concord10x\wwwroot\`). Inspect that folder. Also check that the `extensions-cache` is fresh — if an old snapshot from a 4.x `Concord/` folder is cached, wipe `.mendix-cache\extensions-cache\` and restart.

### "Concord MCP tools time out / Claude says it can't reach the server"

- Check **Settings → Concord MCP** — is "Enable Concord MCP server" checked? Save.
- Look at the readout under the checkbox — should say "Concord MCP is listening on `localhost:7783`" (or another auto-fallback port if 7783 was taken).
- Probe directly:
  ```powershell
  $body = '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
  Invoke-RestMethod -Uri 'http://127.0.0.1:7783/mcp' -Method POST -ContentType 'application/json' -Body $body
  ```
  Expected: 6 tools with **Studio Pro UI actions** sub-toggle on; +6 more (`maia__*`) with **Maia integration** also on.

### "Studio Pro MCP isn't being found by Claude"

- Open Studio Pro **Edit → Preferences → Maia → MCP Server** — is it enabled with a port set?
- In Concord **Settings → Studio Pro MCP** — is it enabled, with the right CLIs ticked (Claude Code / Copilot CLI / Codex)? Save.
- Inside the terminal, run `claude` and use `/mcp` — should list `mendix-studio-pro` and `concord-mcp` as connected.

### "save_all worked / didn't work"

`save_all` is best-effort. It posts Ctrl+S to Studio Pro's main window, which routes the keystroke to whichever child window has focus. If the user's focus is in the terminal pane (typical when Claude is calling tools), Ctrl+S goes to OUR pane and Studio Pro's documents don't save. Workaround: click the document tab once first, then ask Claude. Or just save manually — it's one keystroke.

**F5 (run) and Shift+F5 (stop) are Studio Pro fixed hotkeys** and work regardless of focus. **F4 (refresh from disk) is the Concord default** and is **configurable** in **Settings → Concord MCP → Refresh-from-disk hotkey**.

### macOS-specific issues

- **"Concord MCP says 'macOS Accessibility permission not granted to Studio Pro'."** Open **System Settings → Privacy & Security → Accessibility**, click the **+** button, navigate to your Studio Pro `.app` (e.g. `/Applications/Mendix Studio Pro 11.10.0.app`), add it, and toggle the switch on. Restart Studio Pro. The Studio Pro UI action tools use `osascript` driving System Events to keystroke Studio Pro on Mac (identified by Unix PID); macOS requires Accessibility permission for any app sending synthetic keystrokes via System Events.
- **"My new build doesn't show up after a Studio Pro restart."** Studio Pro on Mac caches `extensions/` into `<project>/.mendix-cache/extensions-cache/<guid>/` at first load and serves `wwwroot/` from there. The developer-path build refreshes this cache automatically; the consumer-path drop-in does NOT — quit Studio Pro fully before swapping the folder, or delete the matching `.mendix-cache/extensions-cache/<guid>/` directory.
- **"Theme probe fails with `db-not-found`."** Studio Pro on Mac stores its settings at `~/Library/Application Support/Mendix/Settings.sqlite`. If the file is missing, you've never opened Studio Pro Preferences with this user account. Open **Studio Pro → Preferences**, change something trivial, save — that creates the SQLite file.
- **"Studio Pro freezes / shows the spinning beachball after I type a character."** This was the symptom of the WKWebView main-thread write blocking; fixed in 1.2.0 by offloading PTY writes to the thread pool. Make sure you're on `Concord 1.2.0` or newer.
