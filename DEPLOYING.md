# Deploying Concord

Two paths. Pick whichever matches your situation.

- **Developer path** — you're building from source and want to iterate. Skip to [§ Developer path](#developer-path-build-from-source).
- **Consumer path** — you just want Concord working in your Mendix project. Skip to [§ Consumer path](#consumer-path-drop-in-a-prebuilt-folder).

Both paths have the same Studio Pro one-time setup: see [§ Studio Pro setup](#studio-pro-setup) below.

If you're upgrading from the older "**Terminal**" extension (the original `mxTerminal`), see [§ Migrating from Terminal](#migrating-from-terminal-the-old-name) at the bottom.

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

### Get a prebuilt `Concord/` folder

Two ways:

- **From a colleague who built from source:** ask them to zip up `<their-project>\extensions\Concord\` and send it to you. That folder contains `Concord.dll`, `manifest.json`, all the runtime DLLs, and the `wwwroot/` UI bundle.
- **From a release artifact:** if a prebuilt zip is published (e.g. as a GitHub release), download and unzip.

### Install into your Mendix project

1. Open the project folder in Explorer (e.g. `C:\Workspace\MendixApps\YourProject`).
2. If an `extensions\` folder doesn't exist at the project root, create it.
3. Copy the entire `Concord` folder you received into `extensions\`. You should end up with:

```
YourProject\
   extensions\
      Concord\
         Concord.dll
         manifest.json
         wwwroot\
         (...other DLLs and assets)
```

4. Make sure **Studio Pro setup** above is done.
5. Start Studio Pro and open the project. Studio Pro will:
   - Scan `extensions\` and find Concord.
   - Show a one-time **"Trust this extension"** prompt (per Mendix's extension-trust flow). Approve it.
6. Open the pane: **Extensions → Concord → Open Pane**. The pane appears in the right-side pane strip (next to Properties / Toolbox / Maia). Click the **Concord** tab in that strip to focus it.

To install in additional projects, repeat steps 1–6 in each.

To remove: delete the `extensions\Concord\` folder. Restart Studio Pro.

---

## Developer path (build from source)

### Prerequisites

| What | Version | Verify |
|---|---|---|
| Node.js | 18 or newer | `node --version` |
| .NET SDK | 8.x **or** 10.x with the `net8.0` reference pack present | `dotnet --version` and `dotnet --list-runtimes` should show `Microsoft.NETCore.App 8.0.x` |
| Git | any recent version | `git --version` |
| Studio Pro | 11.10.0 or newer | check **Help → About** in Studio Pro |

The .NET 10 SDK can target `net8.0` if the .NET 8 runtime + reference pack is installed (which it usually is on a Windows dev box that's seen any .NET work). If a build fails with "no reference pack for net8.0", install the .NET 8 SDK from https://dotnet.microsoft.com/.

### One-time setup

```sh
git clone https://github.com/rperdiga/mxTerminal.git
cd mxTerminal

# Per-developer deploy config (gitignored — your machine's paths)
copy Directory.Build.props.example Directory.Build.props
# Edit Directory.Build.props and set MendixDeployTarget to your Mendix project root, e.g.:
#   <MendixDeployTarget>C:\Workspace\MendixApps\YourProject</MendixDeployTarget>
#
# To deploy to MULTIPLE projects on each build, semicolon-separate them:
#   <MendixDeployTarget>C:\Projects\AppOne;C:\Projects\AppTwo</MendixDeployTarget>
```

### Build

```sh
dotnet build
```

What happens:
1. The csproj's `BuildUi` target runs `npm install` (first build only — about 30 seconds) and `node esbuild.mjs` to bundle the xterm.js TypeScript UI.
2. C# compiles `Concord.dll`.
3. The `DeployToMendix` target `xcopy`s the build output into each `MendixDeployTarget`'s `extensions\Concord\` directory.

**First-build gotcha:** the csproj's `<Content Include="wwwroot\**\*">` copies the UI bundle into the output, but on a fresh clone `wwwroot/` doesn't exist yet — esbuild creates it during the BuildUi step, AFTER MSBuild has already evaluated the Content glob. **Workaround: run `dotnet build` a second time on the very first build of a fresh clone.** Subsequent builds work the first time. (See `LEARNINGS.md` if it lands in repo for the eventual proper fix.)

### Test

```sh
dotnet test
```

88 xunit tests cover the C# side (action server JSON-RPC, action state machine, run-state probe, MCP config emitters, session manager, ring buffer, settings, logging, per-session write-lock serialization).

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
dotnet build       # rebuilds + redeploys to all MendixDeployTarget folders
```

Then **fully close and reopen Studio Pro.** .NET assemblies loaded into Studio Pro's AppDomain can't be unloaded without ending the process — Studio Pro's "reload project" does NOT pick up new DLLs. Plan for a full Studio Pro restart per iteration.

If you're only changing TypeScript UI files (xterm tab manager, settings modal, etc.), the rebuild is fast (~3-5 seconds), but Studio Pro still needs a restart because it loaded the old `wwwroot/index.html` into the WebView at pane-open time.

### Logs (build + runtime)

- **Build log:** stdout/stderr of `dotnet build`.
- **Extension runtime log:** `<MendixProject>\resources\terminal.log` — every extension lifecycle event, action server start/stop, MCP probe result, paste byte trace.
- **Studio Pro's own log:** `%APPDATA%\Mendix\Studio Pro <version>\log\` — extension load failures, MEF errors. Find the path via Studio Pro's `Help → About → Open log folder`.

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

## Troubleshooting

### "Concord doesn't appear in the Extensions menu"

- **Extension Development isn't enabled.** Edit → Preferences → Advanced → check Extension Development → restart Studio Pro.
- **`extensions/Concord/` folder is in the wrong place.** Must be at `<MendixProject-root>\extensions\Concord\` — same level as `<Project>.mpr`.
- **Studio Pro hasn't been restarted since the folder appeared.** Extensions are scanned at startup.

### "Build succeeds but pane is empty / shows blank WebView"

- First-build wwwroot chicken-and-egg. Run `dotnet build` once more. (See § Build.)
- Look at `<project>\resources\terminal.log` for `InitWebView` line — if it shows a URL but the WebView is blank, the bundle didn't make it into `extensions\Concord\wwwroot\`. Inspect that folder.

### "Action bridge tools time out / Claude says it can't reach the bridge"

- Check **Settings → Action bridge** — is "Expose UI actions to the CLIs above" checked? Save.
- Look at the readout under the checkbox — should say "Action bridge is listening on `localhost:7783`" (or another auto-fallback port if 7783 was taken).
- Probe directly:
  ```powershell
  $body = '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'
  Invoke-RestMethod -Uri 'http://127.0.0.1:7783/mcp' -Method POST -ContentType 'application/json' -Body $body
  ```
  Expected: 6 tools.

### "Studio Pro MCP isn't being found by Claude"

- Open Studio Pro **Edit → Preferences → Maia → MCP Server** — is it enabled with a port set?
- In Concord **Settings → Studio Pro MCP** — is it enabled, with the right CLIs ticked (Claude Code / Copilot CLI / Codex)? Save.
- Inside the terminal, run `claude` and use `/mcp` — should list `mendix-studio-pro` and `mendix-studio-pro-actions` as connected.

### "save_all worked / didn't work"

`save_all` is best-effort. It posts Ctrl+S to Studio Pro's main window, which routes the keystroke to whichever child window has focus. If the user's focus is in the terminal pane (typical when Claude is calling tools), Ctrl+S goes to OUR pane and Studio Pro's documents don't save. Workaround: click the document tab once first, then ask Claude. Or just save manually — it's one keystroke. F5 (run), Shift+F5 (stop), F4 (refresh) are global hotkeys and work regardless of focus.
