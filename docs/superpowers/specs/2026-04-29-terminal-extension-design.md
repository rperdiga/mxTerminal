# Mendix Studio Pro Terminal Extension — Design

**Date:** 2026-04-29
**Status:** Approved for implementation planning

## 1. Goal

Build a Mendix Studio Pro 11.x C# extension that embeds a tabbed terminal in a dockable pane. New tabs start at the open Mendix app's project root. The user runs Claude Code, Codex, and other GenAI CLIs inside it without leaving Studio Pro.

This replaces a prior solution (`mxCoPilot` at `C:\Extensions\mxCoPilot`), a web extension that proxied a terminal through a separate Node.js bridge server on port 3456. The bridge had to be started manually, ran outside Studio Pro's lifecycle, and added an extra failure surface. **The C# extension brings the PTY into Studio Pro's process — no second process, no second port, no manual setup.**

## 2. Non-goals (v1)

- Multi-OS support — Windows-only is fine.
- Studio Pro 10.x compatibility — 11.x only; no `backport-10x/` folder.
- MCP server integration or other AI assistant features beyond running CLIs at a shell.
- Drag-to-reorder tabs, split panes, search-in-scrollback UI, theme switching.
- Tab title auto-detection from PTY content (e.g., "this is `claude`, retitle"). Tabs default to `<shell name>` and are user-renameable.
- Per-tab settings persistence (only a global default with per-tab override at create time).
- Command history sync across sessions.

## 3. Architecture

```
Studio Pro 11.x process (per Mendix app)
├── [Export(WebServerExtension)]   TerminalWebServer
│     └─ routes index.html / terminal.bundle.js / terminal.bundle.css
│         (via IExtensionFileService.ResolvePath("wwwroot", ...))
│
├── [Export]  TerminalSessionManager   ← singleton, MEF-exported
│     ├─ Map<TabId, PtySession>   (each owns Pty.Net + 4 MB ring buffer)
│     ├─ event Output / Exited (output is coalesced ~16ms per tab)
│     └─ disposes all PTYs on ExtensionUnloading + ProcessExit
│
├── [Export(DockablePaneExtension)]   TerminalPaneExtension
│     ├─ injects TerminalSessionManager
│     └─ Open() → fresh TerminalPaneViewModel
│
├── [Export(MenuExtension)]   TerminalMenuExtension
│     └─ adds "Terminal" menu item → IDockingWindowService.OpenPane(ID)
│
└── WebView (Studio Pro-managed WebView2)
      ├── index.html — tab strip + xterm host divs
      ├── terminal.bundle.js — xterm.js + addons + tab manager + bridge
      └── messages: chrome.webview.postMessage  ⇄  IWebView.PostMessage / .MessageReceived
```

### Key boundaries

- **`TerminalSessionManager` is the only thing that touches `Pty.Net`.** It exposes a small interface: `CreateSession`, `Write`, `Resize`, `Close`, `SnapshotBuffer`, `ListSessions`, plus `Output` and `Exited` events. No knowledge of WebViews, JSON, or the message protocol.
- **`TerminalPaneViewModel` is a thin bridge.** It translates WebView messages → manager calls and forwards manager events → WebView messages. No PTY logic in the ViewModel; no UI logic in the manager.
- **`TerminalWebServer` is stateless.** It serves files only. If we ever need a JSON endpoint, we wire the manager in via constructor injection.

### Why a singleton manager (not state on the pane)

`DockablePaneExtension.Open()` returns a *new* `DockablePaneViewModelBase` each time the pane is opened. If PTYs lived on the ViewModel, closing the pane would orphan them. Putting the manager in the MEF container as a singleton means it lives for the **Mendix app's** lifetime — which is exactly the scope we want for sessions (see §6 on lifecycle).

## 4. Asset hosting

We use the documented **`WebServerExtension`** + **`IExtensionFileService.ResolvePath`** pattern, which is what Studio Pro's own samples (`automated/studio-pro/Extensions/Apps/App-level-apps/SimpleApiCoverageTest/SimpleWebServerExtension.cs` in `C:\Extensions\appdev-master`) use.

```csharp
[Export(typeof(WebServerExtension))]
public class TerminalWebServer : WebServerExtension
{
    readonly IExtensionFileService extensionFileService;

    [ImportingConstructor]
    public TerminalWebServer(IExtensionFileService extensionFileService)
        => this.extensionFileService = extensionFileService;

    public override void InitializeWebServer(IWebServer webServer)
    {
        webServer.AddRoute("index.html",          ServeIndex);
        webServer.AddRoute("terminal.bundle.js",  ServeBundleJs);
        webServer.AddRoute("terminal.bundle.css", ServeBundleCss);
    }

    async Task ServeIndex(HttpListenerRequest req, HttpListenerResponse res, CancellationToken ct)
    {
        var path = extensionFileService.ResolvePath("wwwroot", "index.html");
        await res.SendFileAndClose("text/html", path, ct);
    }
    // ServeBundleJs / ServeBundleCss are the same shape with mime + filename swapped
}
```

The pane sets `webView.Address = new Uri(WebServerBaseUrl, "index.html")` in `InitWebView`. `WebServerBaseUrl` is a property on every `UIExtensionBase`-derived class — fully resolved by the time `InitWebView` is called (the API docs warn it throws if accessed in the constructor, which we don't).

**Rejected alternatives:**
- Inline `data:text/html,...` URL (the `MCPExtension` style): hostile to xterm.js-scale JS authoring, no source maps, escaping pain.
- Self-hosted `HttpListener` on a loopback port: redundant when Studio Pro already has one designed for exactly this.
- WebView2 virtual host (`SetVirtualHostNameToFolderMapping`): impossible — `IWebView` doesn't expose `CoreWebView2`.

## 5. WebView ⇄ C# message protocol

We use Mendix's native `{ message: string, data: object }` envelope rather than nesting JSON-as-string in the message field. JS sends with `chrome.webview.postMessage({ message, data })`; C# sends with `webView.PostMessage(message, dataObject)`. C# receives `MessageReceivedEventArgs` exposing `Message` (string) and `Data` (JsonElement-like).

**All PTY bytes are base64-encoded** in `dataB64` fields. Pty.Net delivers raw bytes; Claude/Codex emit non-UTF-8 sequences that lose data round-tripped through strings. Base64 is binary-safe and decodes to a `Uint8Array` that `xterm.write()` consumes natively.

**Tab IDs** are opaque GUIDs minted by C# in `TerminalSessionManager.CreateSession`. The WebView never invents IDs.

### Message catalog

| Direction | `message` | `data` payload | Purpose |
|-----------|-----------|----------------|---------|
| JS → C# | `ready` | – | Page loaded, ready for messages |
| JS → C# | `listTabs` | – | Request the current roster (used after pane reopen) |
| JS → C# | `createTab` | `{cols, rows, shellPath?, args?, cwd?}` | New PTY at given size; missing fields fall back to settings/project root |
| JS → C# | `closeTab` | `{tabId}` | Kill PTY and forget |
| JS → C# | `input` | `{tabId, dataB64}` | Keystrokes / paste |
| JS → C# | `resize` | `{tabId, cols, rows}` | Sent on xterm-fit recalc |
| JS → C# | `replay` | `{tabId}` | Ask for scrollback ring buffer (sent on reattach) |
| JS → C# | `openSettings` | – | Read current shell settings |
| JS → C# | `saveSettings` | `{shellPath, args, ringBufferKB?, xtermScrollbackLines?}` | Persist to project's `terminal-settings.json` |
| C# → JS | `tabsList` | `{tabs: [{tabId, title, shellPath, cwd, alive}]}` | Reply to `listTabs` |
| C# → JS | `tabCreated` | `{tabId, title, shellPath, cwd}` | Confirms creation |
| C# → JS | `tabClosed` | `{tabId}` | Tab closed |
| C# → JS | `output` | `{tabId, dataB64}` | Live PTY output chunk |
| C# → JS | `exit` | `{tabId, exitCode?, signal?}` | PTY process exited |
| C# → JS | `replayData` | `{tabId, dataB64}` | Snapshot of ring buffer (atomic) |
| C# → JS | `settings` | `{shellPath, args, ringBufferKB, xtermScrollbackLines}` | Reply to `openSettings` |
| C# → JS | `error` | `{message, context?}` | Spawn failure, etc. |

### Streaming, batching, ring buffer

- **Output coalescing**: per-tab 16 ms one-shot timer. Bytes arriving in that window become a single `output` message. Quiet tabs pay no cost; busy tabs get one event per frame at 60 fps.
- **Ring buffer**: per `PtySession`, a fixed-size circular buffer of recent output. Default 4 MB (≈ 70–100 turns of a Claude conversation). Configurable via `terminal-settings.json`. Old data is overwritten.
- **xterm.js scrollback** (the live scroll-up history within an open tab): set to 10,000 lines (xterm default is 1,000). Independent of the ring buffer.

### Reattach flow

1. Pane reopens → WebView page loads → sends `ready`.
2. WebView sends `listTabs`.
3. C# replies `tabsList`. WebView reconstructs the tab strip.
4. For each tab to display, WebView sends `replay`.
5. C# locks the session, snapshots its ring buffer, sends `replayData`, then unlocks. Live `output` messages naturally continue from that point — no race, no dedup.

## 6. PTY lifecycle, streaming, and failure modes

### Spawn

`TerminalSessionManager.CreateSession(shellPath, args, cwd, cols, rows)`:

1. Resolve `cwd`. Must exist; fall back to `Environment.CurrentDirectory` with a warning to the log.
2. Build environment from `Environment.GetEnvironmentVariables()`, then:
   - **Strip:** `CLAUDECODE`, `CLAUDE_CODE_ENTRY_POINT`, `CLAUDE_CODE_PARENT_SESSION_ID` (mxCoPilot learned the hard way: these cause Claude's "nested session" error).
   - **Set:** `COLORTERM=truecolor`, `TERM=xterm-256color`, `MCP_TIMEOUT=15000`.
3. `PtyProvider.SpawnAsync(new PtyOptions { App=shellPath, CommandLine=args, Cwd=cwd, Cols=cols, Rows=rows, Environment=env })` → `IPtyConnection`.
4. Mint a `TabId` (GUID), construct `PtySession` (PTY + ring buffer + per-tab coalesce timer), insert into dictionary.
5. **Spawn fails** → exception bubbles up to the ViewModel, which sends an `error` message with details. WebView shows the failure inline in the would-be tab.

### Output read loop

A long-running `Task` per session:

```
while not cancelled:
    n = await pty.ReaderStream.ReadAsync(scratchBuffer, ct)
    if n == 0: break             // PTY closed
    lock(session.lock):
        ringBuffer.Write(scratch[..n])
        coalesceQueue.Add(scratch[..n].ToArray())
        EnsureCoalesceTimerArmed()  // 16ms one-shot
on exit:
    raise Exited(tabId, pty.ExitCode)
```

The coalesce timer fires once per ~16 ms *per tab*, drains the queue under lock, concatenates, and raises `Output(tabId, bytes)`.

### Ring buffer ↔ snapshot atomicity

The same `lock` guards both writes (read loop) and snapshots (replay). `SnapshotBuffer(tabId)` returns `byte[]` of current ring contents in correct order (handles wraparound). Snapshot is fast — single `memcpy`-class copy — so the lock holds the read loop for microseconds.

### Input

WebView `input` → base64 decode → `pty.WriterStream.WriteAsync(bytes, ct)`. Large pastes (megabytes) work — Pty.Net handles backpressure via the stream. No artificial chunking.

### Resize

WebView `resize` → `pty.Resize(cols, rows)`. xterm-fit-addon fires resize on dock/undock, font change, font-size change. No throttling.

### Failure modes

| Failure | Detection | Behavior |
|---------|-----------|----------|
| Spawn (shell missing, ENOENT, permission) | `SpawnAsync` throws | `error` message to WebView; no tab appears |
| PTY dies unexpectedly | Read returns 0 or throws | `Exited(exitCode=null)` → `exit` message; tab UI shows "[process exited]" badge but stays open so user can read final output |
| User typed `exit` in the shell | Same path as above | Same behavior; `exitCode` is whatever the shell reported |
| Write to dead PTY | `WriteAsync` throws | Caught silently (the death already triggered `Exited`); user keystrokes harmlessly drop |
| Read loop crashes for any other reason | Exception in task | Logged to `<MendixProject>/resources/terminal.log`; `Exited(null)` raised; manager removes session from dictionary |

### Lifecycle summary

| Trigger | Effect |
|---------|--------|
| Pane closed (X button) | ViewModel unsubscribes from manager events. **PTYs continue running** — the manager state is unchanged. Reopening the pane reattaches via `listTabs` + `replay`. |
| New tab created (within open pane) | `createTab` → manager spawns PTY, returns `TabId` → WebView allocates a tab UI. |
| Tab closed (X on the tab in the WebView) | `closeTab` → manager kills PTY, removes from dictionary, raises `Exited`. |
| Mendix app closed (file → close, or open another app) | `ExtensionUnloading` fires → `TerminalPaneExtension` calls `manager.DisposeAll()`. All PTYs killed. |
| Studio Pro shut down gracefully | Same `ExtensionUnloading` path. |
| Studio Pro killed / crashes | `ExtensionUnloading` is **not guaranteed**. `AppDomain.CurrentDomain.ProcessExit`, registered in the manager constructor, runs as a fallback to kill PTY processes. Belt-and-suspenders. |

**Important nuance**: PTYs are bound to the **open Mendix app's lifetime**, not to Studio Pro's process lifetime. Switching apps inside Studio Pro will kill all sessions. This is correct behavior — the cwd was that app's project root, so a session that survived an app switch would be operating on the wrong directory.

## 7. Component breakdown

### `TerminalSessionManager` (singleton, MEF-exported)

```csharp
public TabId CreateSession(string shellPath, string[] args, string cwd, int cols, int rows);
public void  Write(TabId id, byte[] data);
public void  Resize(TabId id, int cols, int rows);
public void  Close(TabId id);
public byte[] SnapshotBuffer(TabId id);            // atomic
public IReadOnlyList<SessionInfo> ListSessions();
public event Action<TabId, byte[]> Output;         // already coalesced
public event Action<TabId, int?>   Exited;
public void  DisposeAll();
```

Internals: `Dictionary<TabId, PtySession>` keyed by GUID. Each `PtySession` owns one PTY, one ring buffer, one per-tab coalescing timer, and a lock. Constructor registers `AppDomain.CurrentDomain.ProcessExit += (_,_) => DisposeAll()`.

### `TerminalWebServer` (`WebServerExtension`)

Stateless. Three routes (see §4). Uses `IExtensionFileService.ResolvePath("wwwroot", filename)`.

### `TerminalPaneExtension` (`DockablePaneExtension`)

```csharp
public override string Id => "MxStudioProTerminal";
public override DockablePaneViewModelBase Open() => new TerminalPaneViewModel(manager, settings);
```

Constructor-injects `TerminalSessionManager` and `TerminalSettings`. Subscribes to `ExtensionUnloading` event on its first life event so the manager gets `DisposeAll()` called on app close.

### `TerminalPaneViewModel : WebViewDockablePaneViewModel`

The bridge. In `InitWebView(webView)`:

```csharp
webView.Address = new Uri(WebServerBaseUrl, "index.html");
webView.MessageReceived += OnMessageReceived;
manager.Output += OnPtyOutput;
manager.Exited += OnPtyExited;
```

`OnMessageReceived`: switch on `args.Message`, deserialize `args.Data` into the right C# DTO, call manager, reply.

Disposal (when WebView torn down on pane close): unsubscribe from manager events. **Do not** close PTY sessions here.

### `TerminalMenuExtension` (`MenuExtension`)

Adds a top-level "Terminal" item under the Extensions or View menu. Action: `dockingWindowService.OpenPane("MxStudioProTerminal")`. Same shape as `MyMenuExtension` in `MCPExtension`.

### `wwwroot/` — the UI

Built from `ui/` source via esbuild:

```
ui/
  index.html               — skeleton: tab strip + xterm host divs
  src/
    main.ts                — entry: bootstraps tab manager
    bridge.ts              — postMessage/MessageReceived wrapper, base64 helpers
    tab-manager.ts         — creates xterm instances, handles tab UI
    xterm-tab.ts           — one xterm.Terminal + FitAddon + WebLinksAddon
    settings-modal.ts      — cog icon → modal → save (mirrors MCPExtension)
  styles/
    terminal.css
esbuild.mjs                 — bundles to ../wwwroot/{terminal.bundle.js, terminal.bundle.css}
```

### Settings (`terminal-settings.json`)

Persisted at `<MendixProject>/resources/terminal-settings.json`:

```json
{
  "shellPath": "powershell.exe",
  "args": [],
  "ringBufferKB": 4096,
  "xtermScrollbackLines": 10000
}
```

Defaults applied if file missing or fields absent. Read on `createTab` for the default shell. User-supplied `shellPath` in a `createTab` message overrides.

## 8. Project layout, build & deploy

### Repository structure

```
C:\Extensions\Terminal\
├── MxStudioProTerminal.sln
├── MxStudioProTerminal.csproj
├── manifest.json                   — { "mx_extensions": ["MxStudioProTerminal.dll"] }
├── README.md
├── Directory.Build.props.example   — template; user copies to Directory.Build.props (gitignored) with their MendixDeployTarget
│
├── src/
│   ├── TerminalMenuExtension.cs
│   ├── TerminalPaneExtension.cs
│   ├── TerminalPaneViewModel.cs
│   ├── TerminalWebServer.cs
│   ├── TerminalSessionManager.cs
│   ├── PtySession.cs
│   ├── RingBuffer.cs
│   ├── TerminalSettings.cs
│   ├── Messages/
│   │   ├── Incoming.cs             — CreateTab, Input, Resize, ...
│   │   └── Outgoing.cs             — Output, TabsList, Exit, ...
│   └── Logging.cs                  — writes to <MendixProject>/resources/terminal.log
│
├── ui/
│   ├── package.json
│   ├── tsconfig.json
│   ├── esbuild.mjs
│   ├── index.html
│   └── src/
│       ├── main.ts
│       ├── bridge.ts
│       ├── tab-manager.ts
│       ├── xterm-tab.ts
│       ├── settings-modal.ts
│       └── styles/terminal.css
│
├── wwwroot/                        — build output (gitignored)
│
└── docs/superpowers/specs/
    └── 2026-04-29-terminal-extension-design.md
```

### `MxStudioProTerminal.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <AssemblyName>MxStudioProTerminal</AssemblyName>
    <RootNamespace>MxStudioProTerminal</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Mendix.StudioPro.ExtensionsAPI" Version="11.*" />
    <PackageReference Include="Pty.Net" Version="*" />
    <PackageReference Include="System.Text.Json" Version="8.0.*" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="manifest.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory></Content>
    <Content Include="wwwroot\**\*"><CopyToOutputDirectory>Always</CopyToOutputDirectory></Content>
  </ItemGroup>

  <Target Name="BuildUi" BeforeTargets="BeforeBuild"
          Inputs="ui\src\**\*;ui\package.json;ui\esbuild.mjs"
          Outputs="wwwroot\terminal.bundle.js">
    <Exec Command="npm install --prefix ui --silent" Condition="!Exists('ui\node_modules')" />
    <Exec Command="node ui\esbuild.mjs" />
  </Target>

  <Target Name="DeployToMendix" AfterTargets="PostBuildEvent" Condition="'$(MendixDeployTarget)' != ''">
    <Exec Command="xcopy /y /s /i &quot;$(TargetDir)&quot; &quot;$(MendixDeployTarget)\extensions\MxStudioProTerminal&quot;" />
  </Target>
</Project>
```

`MendixDeployTarget` comes from a per-developer `Directory.Build.props` (gitignored) so each contributor points at their own Mendix project without dirtying the repo. (The exact `Pty.Net` version pins at implementation time after a quick smoke test against .NET 8.)

### `ui/esbuild.mjs`

```js
import * as esbuild from "esbuild";
import { copyFileSync, mkdirSync } from "fs";

mkdirSync("../wwwroot", { recursive: true });
copyFileSync("index.html", "../wwwroot/index.html");

await esbuild.build({
  entryPoints: ["src/main.ts"],
  bundle: true,
  minify: true,
  sourcemap: true,
  target: "es2022",
  outfile: "../wwwroot/terminal.bundle.js",
  loader: { ".css": "text" }              // import xterm.css as text → injected at runtime
});
```

`@xterm/xterm`, `@xterm/addon-fit`, `@xterm/addon-web-links` get bundled in (same as `mxCoPilot`).

### Dev workflow

| Edit | Build | Reload |
|------|-------|--------|
| C# only | `dotnet build` (UI build is a no-op via `Inputs/Outputs`) | F4 in Studio Pro |
| UI only | `npm run build` in `ui/` (or `npm run watch`) + `dotnet build` to copy `wwwroot/` to output | F4, or right-click pane → DevTools → Reload (if `AllowReload=true`) |
| Both | `dotnet build` | F4 |

Studio Pro must be launched with `--enable-extension-development` (same requirement as `MCPExtension`).

### Logs

`<MendixProject>/resources/terminal.log` for everything: spawn failures, read loop exceptions, env-var stripping decisions, settings load/save. Cleared on first session of a new app load (mirrors `MCPExtension.ClearLogFile`).

## 9. Open implementation questions

These are not architectural decisions — they're details to resolve while writing the implementation plan:

1. **Pty.Net version pin**: smoke-test the latest version against .NET 8 + ConPTY on Windows 11; fall back to a known-good version if needed.
2. **Exact MEF wiring** for the singleton manager: confirm via testing whether MEF's default `[Export]` lifetime gives us a single instance shared between `TerminalPaneExtension` and any future consumers, or whether we need `CreationPolicy.Shared` explicitly.
3. **Subscribe API**: confirm signature of `UIExtensionBase.Subscribe<ExtensionUnloading>(Action)` and which class is best to host the subscription. Likely `TerminalPaneExtension`, but `TerminalMenuExtension` would also work.
4. **xterm.js renderer**: default canvas vs. WebGL addon. WebGL is faster but adds a dep and can fail on older GPUs — start with canvas, add WebGL as an opt-in setting if needed.
5. **CSS injection strategy**: import xterm CSS as `text` and `<style>`-inject in `main.ts` (mxCoPilot pattern) vs. ship a separate `terminal.bundle.css` file. The latter is cleaner; pick at impl time.
