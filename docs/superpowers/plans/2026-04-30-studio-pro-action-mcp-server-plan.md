# Studio Pro Action MCP Server — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a second, in-process MCP server to the Mendix Studio Pro Terminal extension that exposes `run_app`, `stop_app`, and `refresh_project` tools so Claude Code / Codex / Copilot CLI running inside the Terminal pane can drive Studio Pro's runtime without alt-tabbing.

**Architecture:** A hand-rolled `HttpListener`-based JSON-RPC server (`StudioProActionServer`) hosted by the singleton `TerminalSessionManager`. Tool calls dispatch to handlers that consult `RunStateProbe` (TCP-connect on the active local-run config's port, parsed out of `IConfiguration.ApplicationRootUrl`) and trigger Studio Pro's own keyboard handlers via Win32 `PostMessage` to the main HWND (`StudioProUiAutomation`). Off by default; opt-in via the existing settings modal; reuses `McpJsonConfigurator` / `McpTomlConfigurator` to publish a second `mendix-studio-pro-actions` server entry to `.mcp.json` and `~/.codex/config.toml`.

**Tech Stack:** C# / .NET 8, MEF, Mendix.StudioPro.ExtensionsAPI 11.6.2, `System.Net.HttpListener`, `System.Text.Json`, Win32 `user32!PostMessage` via `[DllImport]`, xUnit + FluentAssertions, TypeScript (existing `ui/`), esbuild.

**Spec:** [`docs/superpowers/specs/2026-04-30-studio-pro-action-mcp-server-design.md`](../specs/2026-04-30-studio-pro-action-mcp-server-design.md) — all architectural decisions.

**Prior art:** Builds on the existing extension shipped by [`docs/superpowers/plans/2026-04-29-terminal-extension-plan.md`](2026-04-29-terminal-extension-plan.md). The settings record, MCP probe, MCP-config writers, viewmodel, and pane wiring already exist — this plan extends them.

---

## Resolutions of spec §10 open questions

These were left as TODOs in the design doc and are resolved here so implementation has concrete values to use:

1. **Studio Pro hotkeys (Run / Stop / Refresh-from-disk).** Confirmed from `RunMenuItemsProvider` strings in `C:\Program Files\Mendix\11.10.0\modeler\Translations\en-US.po`:
   - **Run Locally** → `F5` (caption `Run &Locally` under `RunMenuItemsProvider.cs:39`).
   - **Stop** → `Shift+F5` (caption `&Stop` under `RunMenuItemsProvider.cs:40`).
   - **Refresh-from-disk**: not exposed as a documented default hotkey in the menu provider strings. The implementation defaults to `F4` (the same key the user already presses to reload extensions, per [`README.md`](../../../README.md)) but exposes a `terminal-settings.json` override (`refreshFromDiskHotkey`) so the smoke-test step can swap it without a recompile if a different binding turns out to fire the project-reload path.
   - All three hotkey strings are configuration-driven (`StudioProUiAutomation` parses them via a small `TryParse` for `Ctrl/Shift/Alt + Fn`) so they survive Studio Pro version drift without a recompile.
2. **Runtime port discovery.** Reflection of `Mendix.StudioPro.ExtensionsAPI.dll` (11.6.2 and 11.10.0) confirms `ILocalRunConfigurationsService.GetActiveConfiguration(IModel)` returns `Mendix.StudioPro.ExtensionsAPI.Model.Settings.IConfiguration`, which exposes:
   - `string Name`
   - `string ApplicationRootUrl` (e.g. `http://localhost:8080`)
   No raw `Port` property exists — `RunStateProbe` parses the port via `new Uri(config.ApplicationRootUrl).Port`. Returning to `unknown` if the URL is empty or unparseable.
3. **Probe timeouts.** Defaults baked into `StudioProActionServer`:
   - **`run_app` total wait for port to open:** `60_000 ms` (Mendix M2EE startup is slow on cold builds; 30 s as suggested in the spec is too tight).
   - **`stop_app` total wait for port to close:** `15_000 ms`.
   - **TCP-probe per-attempt timeout:** `250 ms`.
   - **Polling interval while waiting:** `500 ms`.
   Each is a `const` at the top of `StudioProActionServer.cs` so tuning during smoke testing is a one-line change.
4. **`INotificationPopupService` does not exist in the public Mendix.StudioPro.ExtensionsAPI surface (verified by enumerating all public service interfaces in the 11.10.0 DLL).** The spec was wrong about this. Drop the toast notification entirely — the structured tool return plus a `Logger.Info("[actions] run triggered")` line is sufficient.

These resolutions are recorded here rather than in the spec because the spec is frozen at design time; implementation choices live in the plan.

---

## Mendix per-project extension cache pitfall (read this BEFORE iterating)

Studio Pro caches the loaded extension DLL under `<project>\.mendix-cache\extensions-cache\<guid>\`. After a `dotnet build` followed by a Studio Pro F4 reload, the version of `Terminal.dll` actually loaded into memory is whatever Studio Pro decided to copy into that cache directory at startup time — not necessarily the one your build target just wrote.

Before claiming a code change took effect, **always verify the loaded DLL hash**:

```powershell
Get-Process studiopro | %{ $_.Modules } | ? ModuleName -eq Terminal.dll | Get-FileHash
```

Compare against the freshly built `Terminal.dll` under `bin\Debug\net8.0\` (or wherever the deploy target writes). If they differ, close Studio Pro, delete the cache directory, and relaunch.

Test deploy target (configured via [`Directory.Build.props`](../../../Directory.Build.props)): `C:\Projects\AltairTraversalViewer`. Confirm the deploy target is set there before iterating. The csproj already supports a semicolon-separated multi-target list — keep using that.

---

## Test strategy

| Component | Strategy |
|-----------|----------|
| `TerminalSettings` (new fields) | Extend existing `TerminalSettingsTests` with cases for `ActionsServerEnabled` / `ActionsServerPort` / `RefreshFromDiskHotkey`. TDD strictly. |
| `McpJsonConfigurator` (new methods) | NEW test file `McpJsonConfiguratorTests.cs` covering both legacy `Upsert`/`Remove` and new `UpsertActions`/`RemoveActions`. Tests use a temp dir. TDD strictly. |
| `McpTomlConfigurator` (new methods) | NEW test file `McpTomlConfiguratorTests.cs` mirroring the JSON one. TDD strictly. |
| `RunStateProbe` | xUnit unit tests pointed at a `TcpListener` bound to `127.0.0.1:0` for the running case, and at a known-closed port for the not-running case. TDD strictly. |
| Action handlers (`RunAppHandler` etc.) | Pure xUnit with `IStudioProUiAutomation` and `IRunStateProbe` faked. TDD strictly. |
| `StudioProActionServer` | xUnit + `HttpClient` against the listener bound to `127.0.0.1:0`. TDD strictly. |
| `StudioProUiAutomation` | No automated test — sending Win32 messages requires a real Studio Pro main HWND. Covered by manual smoke (Task 14). |
| Settings UI (HTML + TS) | No automated test — covered by manual smoke. |
| End-to-end Claude Code → MCP → UI flow | Manual smoke (Task 14). |

The convention is the same as the existing project: anything `[DllImport]` or `MEF`-instantiated is smoke-tested, everything else is TDD'd.

---

## Task 1: Extend `TerminalSettings` with actions-server fields (TDD)

**Files:**
- Modify: [`src/TerminalSettings.cs`](../../../src/TerminalSettings.cs)
- Modify: [`tests/TerminalSettingsTests.cs`](../../../tests/TerminalSettingsTests.cs)

The existing record adds settings backward-compatibly via the `Dto` shadow record with nullable fields. Three new fields:

- `bool ActionsServerEnabled` (default `false`)
- `int ActionsServerPort` (default `7783`)
- `string RefreshFromDiskHotkey` (default `"F4"`) — single key string parsed by `StudioProUiAutomation` (Task 7). Stored as a string so the user can swap to `"Ctrl+F5"` etc. without code changes.

- [ ] **Step 1: Add failing tests**

In [`tests/TerminalSettingsTests.cs`](../../../tests/TerminalSettingsTests.cs), append three tests inside the existing class:

```csharp
[Fact]
public void Load_NoFile_ActionsServerDefaults()
{
    var settings = TerminalSettings.Load(tmpDir);
    settings.ActionsServerEnabled.Should().BeFalse();
    settings.ActionsServerPort.Should().Be(7783);
    settings.RefreshFromDiskHotkey.Should().Be("F4");
}

[Fact]
public void Save_ThenLoad_PreservesActionsServerFields()
{
    var original = new TerminalSettings("bash.exe", new[] { "--login" }, 8192, 20000, "light",
        McpEnabled: true, McpPort: 7782, McpClients: new[] { "claude" },
        ActionsServerEnabled: true, ActionsServerPort: 7799, RefreshFromDiskHotkey: "Ctrl+F5");
    original.Save(tmpDir);

    var loaded = TerminalSettings.Load(tmpDir);
    loaded.ActionsServerEnabled.Should().BeTrue();
    loaded.ActionsServerPort.Should().Be(7799);
    loaded.RefreshFromDiskHotkey.Should().Be("Ctrl+F5");
}

[Fact]
public void Load_OldFileWithoutActionsServer_DefaultsToOffOn7783F4()
{
    var resourcesDir = Path.Combine(tmpDir, "resources");
    Directory.CreateDirectory(resourcesDir);
    File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"),
        """{"shellPath":"cmd.exe","mcpEnabled":false,"mcpPort":7782}""");

    var loaded = TerminalSettings.Load(tmpDir);
    loaded.ActionsServerEnabled.Should().BeFalse();
    loaded.ActionsServerPort.Should().Be(7783);
    loaded.RefreshFromDiskHotkey.Should().Be("F4");
}
```

Then update the **two existing constructor calls** in `Save_ThenLoad_PreservesAllFields` and `Save_CreatesResourcesDirIfMissing` so they compile against the new positional record. Pass the three new defaults explicitly:

```csharp
// in Save_ThenLoad_PreservesAllFields:
var original = new TerminalSettings("bash.exe", new[] { "--login" }, 8192, 20000, "light",
    McpEnabled: true, McpPort: 7782, McpClients: new[] { "claude", "codex" },
    ActionsServerEnabled: false, ActionsServerPort: 7783, RefreshFromDiskHotkey: "F4");

// in Save_CreatesResourcesDirIfMissing:
var settings = new TerminalSettings("powershell.exe", Array.Empty<string>(), 4096, 10000, "dark",
    McpEnabled: false, McpPort: 7782, McpClients: Array.Empty<string>(),
    ActionsServerEnabled: false, ActionsServerPort: 7783, RefreshFromDiskHotkey: "F4");
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~TerminalSettingsTests`
Expected: 3 new tests fail with "ActionsServerEnabled does not contain a definition" or similar; the two updated tests fail with "no overload matches".

- [ ] **Step 3: Extend `TerminalSettings` record + `Dto`**

In [`src/TerminalSettings.cs`](../../../src/TerminalSettings.cs):

```csharp
public sealed record TerminalSettings(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines,
    string Theme,
    bool McpEnabled,
    int McpPort,
    string[] McpClients,
    bool ActionsServerEnabled,
    int ActionsServerPort,
    string RefreshFromDiskHotkey)
{
    public static TerminalSettings Defaults() => new(
        ShellPath: "powershell.exe",
        Args: Array.Empty<string>(),
        RingBufferKB: 4096,
        XtermScrollbackLines: 10000,
        Theme: "dark",
        McpEnabled: false,
        McpPort: 7782,
        McpClients: Array.Empty<string>(),
        ActionsServerEnabled: false,
        ActionsServerPort: 7783,
        RefreshFromDiskHotkey: "F4");
```

Update `Load` to read the three new nullable fields with default fallback:

```csharp
return new TerminalSettings(
    ShellPath: dto.ShellPath ?? def.ShellPath,
    Args: dto.Args ?? def.Args,
    RingBufferKB: dto.RingBufferKB ?? def.RingBufferKB,
    XtermScrollbackLines: dto.XtermScrollbackLines ?? def.XtermScrollbackLines,
    Theme: dto.Theme ?? def.Theme,
    McpEnabled: dto.McpEnabled ?? def.McpEnabled,
    McpPort: dto.McpPort ?? def.McpPort,
    McpClients: dto.McpClients ?? def.McpClients,
    ActionsServerEnabled: dto.ActionsServerEnabled ?? def.ActionsServerEnabled,
    ActionsServerPort: dto.ActionsServerPort ?? def.ActionsServerPort,
    RefreshFromDiskHotkey: dto.RefreshFromDiskHotkey ?? def.RefreshFromDiskHotkey);
```

Update `Save` to emit the three new fields:

```csharp
var dto = new Dto(ShellPath, Args, RingBufferKB, XtermScrollbackLines, Theme,
    McpEnabled, McpPort, McpClients,
    ActionsServerEnabled, ActionsServerPort, RefreshFromDiskHotkey);
```

Extend the inner `Dto` record:

```csharp
private sealed record Dto(
    string? ShellPath,
    string[]? Args,
    int? RingBufferKB,
    int? XtermScrollbackLines,
    string? Theme,
    bool? McpEnabled,
    int? McpPort,
    string[]? McpClients,
    bool? ActionsServerEnabled,
    int? ActionsServerPort,
    string? RefreshFromDiskHotkey);
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~TerminalSettingsTests`
Expected: All `TerminalSettingsTests` (existing 6 + 3 new = 9) pass.

- [ ] **Step 5: Confirm the rest of the project still compiles**

Run: `dotnet build`
Expected: Compiles. The only consumer of the `TerminalSettings` constructor is [`TerminalPaneViewModel.HandleSaveSettings`](../../../src/TerminalPaneViewModel.cs), which uses the `with` keyword — that survives the new fields without modification because `with` only sets the fields it names. (We will modify `HandleSaveSettings` for actual feature behaviour in Task 11.)

- [ ] **Step 6: Commit**

```bash
git add src/TerminalSettings.cs tests/TerminalSettingsTests.cs
git commit -m "feat(settings): ActionsServerEnabled / ActionsServerPort / RefreshFromDiskHotkey"
```

---

## Task 2: New `McpJsonConfiguratorTests` covering existing behavior (TDD safety net)

**Files:**
- Create: `tests/McpJsonConfiguratorTests.cs`

We will refactor the configurator in Task 3 — pin its current behaviour with tests first so we can refactor safely.

- [ ] **Step 1: Write tests covering current behavior**

Create `tests/McpJsonConfiguratorTests.cs`:

```csharp
using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class McpJsonConfiguratorTests : IDisposable
{
    private readonly string tmpDir;
    private readonly string filePath;

    public McpJsonConfiguratorTests()
    {
        tmpDir = Path.Combine(Path.GetTempPath(), "mcpjson-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        filePath = Path.Combine(tmpDir, ".mcp.json");
    }

    public void Dispose() => Directory.Delete(tmpDir, recursive: true);

    [Fact]
    public void Upsert_NoFile_CreatesFileWithEntry()
    {
        new McpJsonConfigurator(tmpDir).Upsert("http://localhost:7782/mcp");
        File.Exists(filePath).Should().BeTrue();
        var json = File.ReadAllText(filePath);
        json.Should().Contain("\"mendix-studio-pro\"");
        json.Should().Contain("\"http://localhost:7782/mcp\"");
        json.Should().Contain("\"http\"");
    }

    [Fact]
    public void Upsert_TwiceWithDifferentUrl_OverwritesUrl()
    {
        var c = new McpJsonConfigurator(tmpDir);
        c.Upsert("http://localhost:7782/mcp");
        c.Upsert("http://localhost:9999/mcp");
        File.ReadAllText(filePath).Should().Contain("9999").And.NotContain("7782");
    }

    [Fact]
    public void Upsert_PreservesUnrelatedTopLevelKeys()
    {
        File.WriteAllText(filePath, """{"foo":"bar","mcpServers":{"other":{"type":"http","url":"http://x"}}}""");
        new McpJsonConfigurator(tmpDir).Upsert("http://localhost:7782/mcp");
        var json = File.ReadAllText(filePath);
        json.Should().Contain("\"foo\":\"bar\"");
        json.Should().Contain("\"other\":");
        json.Should().Contain("\"mendix-studio-pro\":");
    }

    [Fact]
    public void Remove_OurEntryGone_PreservesOthers()
    {
        File.WriteAllText(filePath,
            """{"mcpServers":{"mendix-studio-pro":{"type":"http","url":"http://localhost:7782/mcp"},"other":{"type":"http","url":"http://x"}}}""");
        new McpJsonConfigurator(tmpDir).Remove();
        var json = File.ReadAllText(filePath);
        json.Should().NotContain("mendix-studio-pro");
        json.Should().Contain("other");
    }

    [Fact]
    public void Remove_LastEntry_DeletesFile()
    {
        File.WriteAllText(filePath,
            """{"mcpServers":{"mendix-studio-pro":{"type":"http","url":"http://localhost:7782/mcp"}}}""");
        new McpJsonConfigurator(tmpDir).Remove();
        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public void Remove_NoFile_NoOp()
    {
        Action act = () => new McpJsonConfigurator(tmpDir).Remove();
        act.Should().NotThrow();
    }
}
```

- [ ] **Step 2: Run tests — verify they pass against the existing implementation**

Run: `dotnet test --filter FullyQualifiedName~McpJsonConfiguratorTests`
Expected: All 6 tests pass. If any fails, the regression existed before this work — fix in a separate commit before continuing.

- [ ] **Step 3: Commit**

```bash
git add tests/McpJsonConfiguratorTests.cs
git commit -m "test: pin existing McpJsonConfigurator behavior before refactor"
```

---

## Task 3: Extend `McpJsonConfigurator` with `UpsertActions` / `RemoveActions` (TDD)

**Files:**
- Modify: [`src/McpJsonConfigurator.cs`](../../../src/McpJsonConfigurator.cs)
- Modify: `tests/McpJsonConfiguratorTests.cs`

- [ ] **Step 1: Add failing tests for the new methods**

Append to `tests/McpJsonConfiguratorTests.cs`:

```csharp
[Fact]
public void UpsertActions_NoFile_CreatesEntryUnderActionsServerName()
{
    new McpJsonConfigurator(tmpDir).UpsertActions("http://localhost:7783/mcp");
    var json = File.ReadAllText(filePath);
    json.Should().Contain("\"mendix-studio-pro-actions\"");
    json.Should().Contain("\"http://localhost:7783/mcp\"");
}

[Fact]
public void UpsertActions_AlongsidePrimary_BothPresent()
{
    var c = new McpJsonConfigurator(tmpDir);
    c.Upsert("http://localhost:7782/mcp");
    c.UpsertActions("http://localhost:7783/mcp");
    var json = File.ReadAllText(filePath);
    json.Should().Contain("\"mendix-studio-pro\"");
    json.Should().Contain("\"mendix-studio-pro-actions\"");
}

[Fact]
public void RemoveActions_KeepsPrimaryEntry()
{
    var c = new McpJsonConfigurator(tmpDir);
    c.Upsert("http://localhost:7782/mcp");
    c.UpsertActions("http://localhost:7783/mcp");
    c.RemoveActions();
    var json = File.ReadAllText(filePath);
    json.Should().Contain("mendix-studio-pro");
    json.Should().NotContain("mendix-studio-pro-actions");
}

[Fact]
public void Remove_KeepsActionsEntry()
{
    var c = new McpJsonConfigurator(tmpDir);
    c.Upsert("http://localhost:7782/mcp");
    c.UpsertActions("http://localhost:7783/mcp");
    c.Remove();
    var json = File.ReadAllText(filePath);
    json.Should().NotContain("\"mendix-studio-pro\":");      // colon prevents matching the actions entry
    json.Should().Contain("mendix-studio-pro-actions");
}

[Fact]
public void RemoveActions_NoFile_NoOp()
{
    Action act = () => new McpJsonConfigurator(tmpDir).RemoveActions();
    act.Should().NotThrow();
}
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~McpJsonConfiguratorTests`
Expected: The 5 new tests fail with "does not contain a definition for 'UpsertActions'/'RemoveActions'".

- [ ] **Step 3: Refactor `McpJsonConfigurator` to share an internal upsert/remove keyed by server name**

Replace the body of [`src/McpJsonConfigurator.cs`](../../../src/McpJsonConfigurator.cs):

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Terminal;

/// <summary>
/// Manages the project-level <c>.mcp.json</c> file (read by Claude Code and
/// GitHub Copilot CLI). Upserts/removes named server entries while preserving
/// anything else the user has in the file.
/// </summary>
public sealed class McpJsonConfigurator
{
    public const string ServerName = "mendix-studio-pro";
    public const string ActionsServerName = "mendix-studio-pro-actions";

    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true };

    private readonly string filePath;

    public McpJsonConfigurator(string projectDir)
    {
        filePath = Path.Combine(projectDir, ".mcp.json");
    }

    public void Upsert(string url)        => UpsertNamed(ServerName, url);
    public void Remove()                  => RemoveNamed(ServerName);
    public void UpsertActions(string url) => UpsertNamed(ActionsServerName, url);
    public void RemoveActions()           => RemoveNamed(ActionsServerName);

    private void UpsertNamed(string serverName, string url)
    {
        var root = ReadOrEmpty();
        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }
        servers[serverName] = new JsonObject
        {
            ["type"] = "http",
            ["url"]  = url,
        };
        WriteAtomic(root);
    }

    private void RemoveNamed(string serverName)
    {
        if (!File.Exists(filePath)) return;
        var root = ReadOrEmpty();
        if (root["mcpServers"] is JsonObject servers && servers.ContainsKey(serverName))
        {
            servers.Remove(serverName);
            if (servers.Count == 0) root.Remove("mcpServers");
        }
        if (root.Count == 0)
        {
            try { File.Delete(filePath); } catch { /* best-effort */ }
            return;
        }
        WriteAtomic(root);
    }

    private JsonObject ReadOrEmpty()
    {
        if (!File.Exists(filePath)) return new JsonObject();
        try
        {
            using var stream = File.OpenRead(filePath);
            var node = JsonNode.Parse(stream);
            return node as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private void WriteAtomic(JsonObject root)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(WriteOpts) + Environment.NewLine);
        if (File.Exists(filePath)) File.Delete(filePath);
        File.Move(tmp, filePath);
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~McpJsonConfiguratorTests`
Expected: All 11 tests pass (6 original + 5 new).

- [ ] **Step 5: Commit**

```bash
git add src/McpJsonConfigurator.cs tests/McpJsonConfiguratorTests.cs
git commit -m "feat(mcp-json): UpsertActions/RemoveActions for second server entry"
```

---

## Task 4: New `McpTomlConfiguratorTests` covering existing behavior (TDD safety net)

**Files:**
- Create: `tests/McpTomlConfiguratorTests.cs`

The TOML configurator currently writes to `~/.codex/config.toml` — we must redirect it at a tmp file for tests. The existing class hard-codes the path in its constructor; the tests need a way to override that.

- [ ] **Step 1: Add a test-only constructor overload to the existing class**

In [`src/McpTomlConfigurator.cs`](../../../src/McpTomlConfigurator.cs), replace the existing single-constructor block with:

```csharp
public McpTomlConfigurator() : this(DefaultPath()) { }

internal McpTomlConfigurator(string filePath) { this.filePath = filePath; }

private static string DefaultPath()
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".codex", "config.toml");
}
```

Make `Terminal.Tests` a friend assembly so the test project can call the `internal` ctor. Add to [`Terminal.csproj`](../../../Terminal.csproj) inside the existing `<ItemGroup>`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="Terminal.Tests" />
</ItemGroup>
```

(If the project already has an `InternalsVisibleTo` entry for the tests, skip this step.)

- [ ] **Step 2: Build and confirm nothing else broke**

Run: `dotnet build`
Expected: Compiles. The parameterless ctor still works for production callers.

- [ ] **Step 3: Write tests covering current behavior**

Create `tests/McpTomlConfiguratorTests.cs`:

```csharp
using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class McpTomlConfiguratorTests : IDisposable
{
    private readonly string tmpDir;
    private readonly string filePath;

    public McpTomlConfiguratorTests()
    {
        tmpDir = Path.Combine(Path.GetTempPath(), "mcptoml-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        filePath = Path.Combine(tmpDir, "config.toml");
    }

    public void Dispose() => Directory.Delete(tmpDir, recursive: true);

    private McpTomlConfigurator NewConfig() => new(filePath);

    [Fact]
    public void Upsert_NoFile_CreatesSection()
    {
        NewConfig().Upsert("http://localhost:7782/mcp");
        File.Exists(filePath).Should().BeTrue();
        var content = File.ReadAllText(filePath);
        content.Should().Contain("[mcp_servers.mendix-studio-pro]");
        content.Should().Contain("command = \"npx\"");
        content.Should().Contain("\"http://localhost:7782/mcp\"");
    }

    [Fact]
    public void Upsert_TwiceWithDifferentUrl_OverwritesSection()
    {
        var c = NewConfig();
        c.Upsert("http://localhost:7782/mcp");
        c.Upsert("http://localhost:9999/mcp");
        var content = File.ReadAllText(filePath);
        content.Should().Contain("9999").And.NotContain("7782");
    }

    [Fact]
    public void Upsert_PreservesUnrelatedSections()
    {
        File.WriteAllText(filePath, "[other]\nfoo = \"bar\"\n");
        NewConfig().Upsert("http://localhost:7782/mcp");
        var content = File.ReadAllText(filePath);
        content.Should().Contain("[other]");
        content.Should().Contain("[mcp_servers.mendix-studio-pro]");
    }

    [Fact]
    public void Remove_OurSectionGone_PreservesOthers()
    {
        File.WriteAllText(filePath,
            "[other]\nfoo = \"bar\"\n\n[mcp_servers.mendix-studio-pro]\ncommand = \"npx\"\n");
        NewConfig().Remove();
        var content = File.ReadAllText(filePath);
        content.Should().Contain("[other]");
        content.Should().NotContain("[mcp_servers.mendix-studio-pro]");
    }

    [Fact]
    public void Remove_NoFile_NoOp()
    {
        Action act = () => NewConfig().Remove();
        act.Should().NotThrow();
    }
}
```

- [ ] **Step 4: Run tests — verify they pass against the existing implementation**

Run: `dotnet test --filter FullyQualifiedName~McpTomlConfiguratorTests`
Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/McpTomlConfigurator.cs Terminal.csproj tests/McpTomlConfiguratorTests.cs
git commit -m "test: pin existing McpTomlConfigurator behavior; testable ctor overload"
```

---

## Task 5: Extend `McpTomlConfigurator` with `UpsertActions` / `RemoveActions` (TDD)

**Files:**
- Modify: [`src/McpTomlConfigurator.cs`](../../../src/McpTomlConfigurator.cs)
- Modify: `tests/McpTomlConfiguratorTests.cs`

Refactor pattern: thread an explicit `sectionHeader` argument through `FindSection` and the upsert/remove method bodies. Public methods `Upsert(url)` / `Remove()` keep their current signatures and delegate to the new internal methods with the primary header; new public methods `UpsertActions(url)` / `RemoveActions()` use the `[mcp_servers.mendix-studio-pro-actions]` header.

- [ ] **Step 1: Add failing tests for the new methods**

Append to `tests/McpTomlConfiguratorTests.cs`:

```csharp
[Fact]
public void UpsertActions_NoFile_CreatesActionsSection()
{
    NewConfig().UpsertActions("http://localhost:7783/mcp");
    var content = File.ReadAllText(filePath);
    content.Should().Contain("[mcp_servers.mendix-studio-pro-actions]");
    content.Should().Contain("\"http://localhost:7783/mcp\"");
}

[Fact]
public void UpsertActions_AlongsidePrimary_BothSectionsPresent()
{
    var c = NewConfig();
    c.Upsert("http://localhost:7782/mcp");
    c.UpsertActions("http://localhost:7783/mcp");
    var content = File.ReadAllText(filePath);
    content.Should().Contain("[mcp_servers.mendix-studio-pro]");
    content.Should().Contain("[mcp_servers.mendix-studio-pro-actions]");
}

[Fact]
public void RemoveActions_KeepsPrimarySection()
{
    var c = NewConfig();
    c.Upsert("http://localhost:7782/mcp");
    c.UpsertActions("http://localhost:7783/mcp");
    c.RemoveActions();
    var content = File.ReadAllText(filePath);
    content.Should().Contain("[mcp_servers.mendix-studio-pro]");
    content.Should().NotContain("[mcp_servers.mendix-studio-pro-actions]");
}

[Fact]
public void Remove_KeepsActionsSection()
{
    var c = NewConfig();
    c.Upsert("http://localhost:7782/mcp");
    c.UpsertActions("http://localhost:7783/mcp");
    c.Remove();
    var content = File.ReadAllText(filePath);
    content.Should().NotContain("[mcp_servers.mendix-studio-pro]\n");
    content.Should().Contain("[mcp_servers.mendix-studio-pro-actions]");
}

[Fact]
public void RemoveActions_NoFile_NoOp()
{
    Action act = () => NewConfig().RemoveActions();
    act.Should().NotThrow();
}
```

(The `Remove_KeepsActionsSection` assertion uses a trailing `\n` to ensure we don't false-match the primary header inside the actions header `mendix-studio-pro-actions]`.)

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~McpTomlConfiguratorTests`
Expected: 5 new tests fail with "does not contain a definition for 'UpsertActions'/'RemoveActions'".

- [ ] **Step 3: Refactor `McpTomlConfigurator`**

Replace the body of [`src/McpTomlConfigurator.cs`](../../../src/McpTomlConfigurator.cs):

```csharp
using System.Text;

namespace Terminal;

/// <summary>
/// Manages the <c>[mcp_servers.&lt;name&gt;]</c> sections of the user-level
/// Codex config at <c>~/.codex/config.toml</c>. Codex's MCP support is stdio-only
/// so we wire it through the npx <c>mcp-remote</c> bridge.
///
/// Hand-rolled TOML editing — no need for a full parser since we own a fixed
/// set of well-known sections and never touch anything else.
/// </summary>
public sealed class McpTomlConfigurator
{
    public const string ServerName        = "mendix-studio-pro";
    public const string ActionsServerName = "mendix-studio-pro-actions";

    private static string HeaderFor(string serverName) => $"[mcp_servers.{serverName}]";

    private readonly string filePath;

    public McpTomlConfigurator() : this(DefaultPath()) { }

    internal McpTomlConfigurator(string filePath) { this.filePath = filePath; }

    private static string DefaultPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".codex", "config.toml");
    }

    public string FilePath => filePath;

    public void Upsert(string url)        => UpsertNamed(ServerName, url);
    public void Remove()                  => RemoveNamed(ServerName);
    public void UpsertActions(string url) => UpsertNamed(ActionsServerName, url);
    public void RemoveActions()           => RemoveNamed(ActionsServerName);

    private void UpsertNamed(string serverName, string url)
    {
        var header = HeaderFor(serverName);
        var lines = ReadLines();
        var (start, end) = FindSection(lines, header);
        var newSection = new[]
        {
            header,
            "command = \"npx\"",
            $"args = [\"-y\", \"mcp-remote\", \"{url}\"]",
        };

        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.AddRange(newSection);
        }
        else
        {
            lines.RemoveRange(start, end - start + 1);
            lines.InsertRange(start, newSection);
        }
        WriteAtomic(lines);
    }

    private void RemoveNamed(string serverName)
    {
        if (!File.Exists(filePath)) return;
        var header = HeaderFor(serverName);
        var lines = ReadLines();
        var (start, end) = FindSection(lines, header);
        if (start < 0) return;

        var until = end;
        if (until + 1 < lines.Count && lines[until + 1].Trim().Length == 0) until++;
        var from = start;
        if (from > 0 && lines[from - 1].Trim().Length == 0) from--;

        lines.RemoveRange(from, until - from + 1);
        WriteAtomic(lines);
    }

    private List<string> ReadLines()
    {
        if (!File.Exists(filePath)) return new List<string>();
        return File.ReadAllLines(filePath, Encoding.UTF8).ToList();
    }

    /// <summary>
    /// Locate the [mcp_servers.&lt;name&gt;] block. Returns (start, end) inclusive
    /// of the lines to remove, or (-1, -1) if not present.
    /// </summary>
    private static (int start, int end) FindSection(List<string> lines, string sectionHeader)
    {
        var start = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            // Use exact-line match (after trimming leading whitespace), not StartsWith,
            // so [mcp_servers.mendix-studio-pro-actions] doesn't match the primary header.
            if (lines[i].TrimStart() == sectionHeader)
            {
                start = i;
                break;
            }
        }
        if (start < 0) return (-1, -1);

        var end = lines.Count - 1;
        for (int i = start + 1; i < lines.Count; i++)
        {
            var t = lines[i].TrimStart();
            if (t.StartsWith("[", StringComparison.Ordinal))
            {
                end = i - 1;
                break;
            }
        }
        return (start, end);
    }

    private void WriteAtomic(List<string> lines)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
        if (File.Exists(filePath)) File.Delete(filePath);
        File.Move(tmp, filePath);
    }
}
```

The critical fix is the `FindSection` exact-line match (`lines[i].TrimStart() == sectionHeader`) instead of `StartsWith` — without that, looking for `[mcp_servers.mendix-studio-pro]` would also match `[mcp_servers.mendix-studio-pro-actions]`. The pre-existing pinning tests (Task 4) verify this didn't regress for the legacy callers.

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~McpTomlConfiguratorTests`
Expected: All 10 tests pass (5 original + 5 new).

- [ ] **Step 5: Commit**

```bash
git add src/McpTomlConfigurator.cs tests/McpTomlConfiguratorTests.cs
git commit -m "feat(mcp-toml): UpsertActions/RemoveActions for second server section"
```

---

## Task 6: `IRunStateProbe` + `RunStateProbe` (TDD)

**Files:**
- Create: `src/IRunStateProbe.cs`
- Create: `src/RunStateProbe.cs`
- Create: `tests/RunStateProbeTests.cs`

`RunStateProbe` is the only place that touches Mendix's `ILocalRunConfigurationsService`. The probe takes the URL string in via a `Func<string?>` so tests don't need a real Mendix model — production code wires that lambda to `service.GetActiveConfiguration(model)?.ApplicationRootUrl`.

- [ ] **Step 1: Write failing tests**

Create `tests/RunStateProbeTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class RunStateProbeTests
{
    [Fact]
    public async Task IsRunningAsync_NoActiveConfiguration_ReturnsUnknown()
    {
        var probe = new RunStateProbe(getApplicationRootUrl: () => null);
        var result = await probe.IsRunningAsync();
        result.Should().Be(RunState.Unknown);
    }

    [Fact]
    public async Task IsRunningAsync_UrlEmpty_ReturnsUnknown()
    {
        var probe = new RunStateProbe(getApplicationRootUrl: () => "");
        var result = await probe.IsRunningAsync();
        result.Should().Be(RunState.Unknown);
    }

    [Fact]
    public async Task IsRunningAsync_UrlMalformed_ReturnsUnknown()
    {
        var probe = new RunStateProbe(getApplicationRootUrl: () => "not a url");
        var result = await probe.IsRunningAsync();
        result.Should().Be(RunState.Unknown);
    }

    [Fact]
    public async Task IsRunningAsync_PortOpen_ReturnsRunning()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var probe = new RunStateProbe(() => $"http://localhost:{port}");
            var result = await probe.IsRunningAsync();
            result.Should().Be(RunState.Running);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task IsRunningAsync_PortClosed_ReturnsStopped()
    {
        // Bind, capture port, immediately stop — port is now refused.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var probe = new RunStateProbe(() => $"http://localhost:{port}");
        var result = await probe.IsRunningAsync();
        result.Should().Be(RunState.Stopped);
    }

    [Fact]
    public void GetActivePort_ReadsPortFromUrl()
    {
        var probe = new RunStateProbe(() => "http://localhost:8123");
        probe.GetActivePort().Should().Be(8123);
    }

    [Fact]
    public void GetActiveUrl_Exposes_RawUrl()
    {
        var probe = new RunStateProbe(() => "http://localhost:8080");
        probe.GetActiveUrl().Should().Be("http://localhost:8080");
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~RunStateProbeTests`
Expected: All fail with "type or namespace 'RunStateProbe' could not be found".

- [ ] **Step 3: Implement the interface**

Create `src/IRunStateProbe.cs`:

```csharp
namespace Terminal;

public enum RunState { Unknown, Running, Stopped }

public interface IRunStateProbe
{
    /// <summary>Last-known absolute URL, e.g. <c>http://localhost:8080</c>, or null if no active config.</summary>
    string? GetActiveUrl();

    /// <summary>Port parsed out of <see cref="GetActiveUrl"/>, or null if absent/unparseable.</summary>
    int? GetActivePort();

    /// <summary>Probe the runtime port via TCP connect. Result reflects current observable state.</summary>
    Task<RunState> IsRunningAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Implement `RunStateProbe`**

Create `src/RunStateProbe.cs`:

```csharp
using System.Net.Sockets;

namespace Terminal;

public sealed class RunStateProbe : IRunStateProbe
{
    private const int ConnectTimeoutMs = 250;

    private readonly Func<string?> getApplicationRootUrl;

    public RunStateProbe(Func<string?> getApplicationRootUrl)
    {
        this.getApplicationRootUrl = getApplicationRootUrl;
    }

    public string? GetActiveUrl() => getApplicationRootUrl();

    public int? GetActivePort()
    {
        var url = getApplicationRootUrl();
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        return uri.Port;
    }

    public async Task<RunState> IsRunningAsync(CancellationToken ct = default)
    {
        var port = GetActivePort();
        if (port is null or <= 0) return RunState.Unknown;

        try
        {
            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ConnectTimeoutMs);
            await client.ConnectAsync("127.0.0.1", port.Value, timeoutCts.Token);
            return RunState.Running;
        }
        catch (SocketException)
        {
            return RunState.Stopped;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Connect timed out — treat as stopped (caller can decide).
            return RunState.Stopped;
        }
        catch
        {
            return RunState.Unknown;
        }
    }
}
```

- [ ] **Step 5: Run tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~RunStateProbeTests`
Expected: All 7 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/IRunStateProbe.cs src/RunStateProbe.cs tests/RunStateProbeTests.cs
git commit -m "feat: RunStateProbe via TCP-connect on ApplicationRootUrl port"
```

---

## Task 7: `IStudioProUiAutomation` + `StudioProUiAutomation` (no automated test)

**Files:**
- Create: `src/IStudioProUiAutomation.cs`
- Create: `src/StudioProUiAutomation.cs`

This is the only file that uses `[DllImport]` against `user32.dll`. No automated test — the manual smoke step in Task 14 covers it. The hotkey strings are accepted as text (e.g. `"F5"`, `"Shift+F5"`, `"Ctrl+F4"`) and parsed locally.

- [ ] **Step 1: Create the interface**

Create `src/IStudioProUiAutomation.cs`:

```csharp
namespace Terminal;

public interface IStudioProUiAutomation
{
    /// <summary>Send F5 (or whatever the configured run hotkey is) to the Studio Pro main window.</summary>
    /// <returns>true if a window handle was found and the message was posted; false otherwise.</returns>
    bool TriggerRun();

    /// <summary>Send Shift+F5 (or whatever the configured stop hotkey is).</summary>
    bool TriggerStop();

    /// <summary>Send the configured refresh-from-disk hotkey (default F4).</summary>
    bool TriggerRefreshFromDisk();
}
```

- [ ] **Step 2: Implement `StudioProUiAutomation`**

Create `src/StudioProUiAutomation.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Terminal;

/// <summary>
/// Posts Win32 keyboard messages to Studio Pro's own main window so the
/// existing menu/hotkey handlers fire. PostMessage doesn't require focus,
/// so this works while the user types in the Terminal pane.
/// </summary>
public sealed class StudioProUiAutomation : IStudioProUiAutomation
{
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP   = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP   = 0x0105;

    // Virtual-key codes we use.
    private const ushort VK_SHIFT   = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU    = 0x12; // Alt
    private const ushort VK_F1      = 0x70;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private readonly string runHotkey;
    private readonly string stopHotkey;
    private readonly string refreshHotkey;
    private readonly Logger? log;

    public StudioProUiAutomation(string runHotkey, string stopHotkey, string refreshHotkey, Logger? log = null)
    {
        this.runHotkey = runHotkey;
        this.stopHotkey = stopHotkey;
        this.refreshHotkey = refreshHotkey;
        this.log = log;
    }

    public bool TriggerRun()              => Send(runHotkey);
    public bool TriggerStop()             => Send(stopHotkey);
    public bool TriggerRefreshFromDisk()  => Send(refreshHotkey);

    private bool Send(string hotkeyText)
    {
        var hwnd = Process.GetCurrentProcess().MainWindowHandle;
        if (hwnd == IntPtr.Zero)
        {
            log?.Warn($"UI automation: Studio Pro MainWindowHandle is zero; cannot send {hotkeyText}");
            return false;
        }
        if (!TryParse(hotkeyText, out var modifiers, out var vk))
        {
            log?.Warn($"UI automation: cannot parse hotkey '{hotkeyText}'");
            return false;
        }

        // Press modifiers (in canonical order: Ctrl, Shift, Alt) then the key, then release in reverse.
        if (modifiers.HasFlag(Modifiers.Ctrl))  PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_CONTROL, IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Shift)) PostMessage(hwnd, WM_KEYDOWN, (IntPtr)VK_SHIFT,   IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Alt))   PostMessage(hwnd, WM_SYSKEYDOWN, (IntPtr)VK_MENU, IntPtr.Zero);
        PostMessage(hwnd, WM_KEYDOWN, (IntPtr)vk, IntPtr.Zero);
        PostMessage(hwnd, WM_KEYUP,   (IntPtr)vk, IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Alt))   PostMessage(hwnd, WM_SYSKEYUP, (IntPtr)VK_MENU, IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Shift)) PostMessage(hwnd, WM_KEYUP,   (IntPtr)VK_SHIFT,   IntPtr.Zero);
        if (modifiers.HasFlag(Modifiers.Ctrl))  PostMessage(hwnd, WM_KEYUP,   (IntPtr)VK_CONTROL, IntPtr.Zero);

        log?.Info($"[actions] sent {hotkeyText} to Studio Pro main window");
        return true;
    }

    [Flags]
    private enum Modifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

    /// <summary>Parses "F5", "Shift+F5", "Ctrl+F4", "Ctrl+Shift+F12" into modifiers + VK.</summary>
    private static bool TryParse(string text, out Modifiers modifiers, out ushort vk)
    {
        modifiers = Modifiers.None;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":  case "control": modifiers |= Modifiers.Ctrl;  break;
                case "shift": modifiers |= Modifiers.Shift; break;
                case "alt":   modifiers |= Modifiers.Alt;   break;
                default: return false;
            }
        }

        var key = parts[^1];
        if (key.Length >= 2 && (key[0] == 'F' || key[0] == 'f') &&
            int.TryParse(key.AsSpan(1), out var n) && n is >= 1 and <= 24)
        {
            vk = (ushort)(VK_F1 + (n - 1));
            return true;
        }
        return false;
    }
}
```

- [ ] **Step 3: Confirm it compiles**

Run: `dotnet build`
Expected: Compiles. No tests run for this file — covered by smoke in Task 14.

- [ ] **Step 4: Commit**

```bash
git add src/IStudioProUiAutomation.cs src/StudioProUiAutomation.cs
git commit -m "feat: StudioProUiAutomation via Win32 PostMessage to main HWND"
```

---

## Task 8: Action handlers + dispatcher (TDD)

**Files:**
- Create: `src/StudioProActions.cs`
- Create: `tests/StudioProActionsTests.cs`

The handlers are pure dispatch logic with one `IRunStateProbe` and one `IStudioProUiAutomation` dependency. We test the full state machine with fakes.

- [ ] **Step 1: Write failing tests**

Create `tests/StudioProActionsTests.cs`:

```csharp
using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class StudioProActionsTests
{
    private sealed class FakeProbe : IRunStateProbe
    {
        public Queue<RunState> States { get; } = new();
        public string? Url { get; set; } = "http://localhost:8080";
        public int? Port { get; set; } = 8080;
        public string? GetActiveUrl() => Url;
        public int? GetActivePort() => Port;
        public Task<RunState> IsRunningAsync(CancellationToken ct = default) =>
            Task.FromResult(States.Count > 0 ? States.Dequeue() : RunState.Unknown);
    }

    private sealed class FakeUi : IStudioProUiAutomation
    {
        public int RunCount, StopCount, RefreshCount;
        public bool RunOk = true, StopOk = true, RefreshOk = true;
        public bool TriggerRun()             { RunCount++;     return RunOk; }
        public bool TriggerStop()            { StopCount++;    return StopOk; }
        public bool TriggerRefreshFromDisk() { RefreshCount++; return RefreshOk; }
    }

    private static StudioProActions NewActions(FakeProbe probe, FakeUi ui) =>
        new(probe, ui, runTimeout: TimeSpan.FromMilliseconds(500), stopTimeout: TimeSpan.FromMilliseconds(500),
            pollInterval: TimeSpan.FromMilliseconds(50));

    [Fact]
    public async Task RunApp_AlreadyRunning_ReturnsAlreadyRunning_DoesNotTrigger()
    {
        var probe = new FakeProbe();
        probe.States.Enqueue(RunState.Running);
        var ui = new FakeUi();
        var result = await NewActions(probe, ui).RunAppAsync();

        result.Status.Should().Be("already_running");
        result.Url.Should().Be("http://localhost:8080");
        ui.RunCount.Should().Be(0);
    }

    [Fact]
    public async Task RunApp_Stopped_TriggersAndWaitsForRunning()
    {
        var probe = new FakeProbe();
        probe.States.Enqueue(RunState.Stopped);   // before
        probe.States.Enqueue(RunState.Stopped);   // first poll — still starting
        probe.States.Enqueue(RunState.Running);   // second poll — up
        var ui = new FakeUi();

        var result = await NewActions(probe, ui).RunAppAsync();

        result.Status.Should().Be("started");
        result.Url.Should().Be("http://localhost:8080");
        ui.RunCount.Should().Be(1);
    }

    [Fact]
    public async Task RunApp_TimesOut_ReturnsCommandSent()
    {
        var probe = new FakeProbe();
        probe.States.Enqueue(RunState.Stopped);
        // No subsequent enqueues; FakeProbe returns Unknown forever after.
        var ui = new FakeUi();

        var result = await NewActions(probe, ui).RunAppAsync();

        result.Status.Should().Be("command_sent");
        ui.RunCount.Should().Be(1);
    }

    [Fact]
    public async Task RunApp_ProbeUnknownBefore_ReturnsCommandSent()
    {
        var probe = new FakeProbe();   // empty -> Unknown
        var ui = new FakeUi();

        var result = await NewActions(probe, ui).RunAppAsync();

        result.Status.Should().Be("command_sent");
        ui.RunCount.Should().Be(1);
    }

    [Fact]
    public async Task RunApp_HwndZero_ReturnsError()
    {
        var probe = new FakeProbe();
        probe.States.Enqueue(RunState.Stopped);
        var ui = new FakeUi { RunOk = false };

        var result = await NewActions(probe, ui).RunAppAsync();

        result.Error.Should().Contain("main window unavailable");
    }

    [Fact]
    public async Task StopApp_NotRunning_ReturnsWasntRunning()
    {
        var probe = new FakeProbe();
        probe.States.Enqueue(RunState.Stopped);
        var ui = new FakeUi();
        var result = await NewActions(probe, ui).StopAppAsync();
        result.Status.Should().Be("wasnt_running");
        ui.StopCount.Should().Be(0);
    }

    [Fact]
    public async Task StopApp_Running_TriggersAndWaitsForStopped()
    {
        var probe = new FakeProbe();
        probe.States.Enqueue(RunState.Running);   // before
        probe.States.Enqueue(RunState.Running);   // first poll
        probe.States.Enqueue(RunState.Stopped);   // second poll
        var ui = new FakeUi();
        var result = await NewActions(probe, ui).StopAppAsync();
        result.Status.Should().Be("stopped");
        ui.StopCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshProject_ReturnsReloaded_OnSuccess()
    {
        var probe = new FakeProbe();
        var ui = new FakeUi();
        var result = await NewActions(probe, ui).RefreshProjectAsync();
        result.Status.Should().Be("reloaded");
        ui.RefreshCount.Should().Be(1);
    }

    [Fact]
    public async Task RefreshProject_HwndZero_ReturnsError()
    {
        var probe = new FakeProbe();
        var ui = new FakeUi { RefreshOk = false };
        var result = await NewActions(probe, ui).RefreshProjectAsync();
        result.Error.Should().Contain("main window unavailable");
    }

    [Fact]
    public async Task ConcurrentCalls_AreSerialized()
    {
        var probe = new FakeProbe();
        // Both calls see Running before, so both return already_running.
        probe.States.Enqueue(RunState.Running);
        probe.States.Enqueue(RunState.Running);
        var ui = new FakeUi();
        var actions = NewActions(probe, ui);

        var t1 = actions.RunAppAsync();
        var t2 = actions.RunAppAsync();
        await Task.WhenAll(t1, t2);
        t1.Result.Status.Should().Be("already_running");
        t2.Result.Status.Should().Be("already_running");
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~StudioProActionsTests`
Expected: All fail (`StudioProActions` undefined).

- [ ] **Step 3: Implement `StudioProActions`**

Create `src/StudioProActions.cs`:

```csharp
namespace Terminal;

/// <summary>
/// Result of one action call. Exactly one of <see cref="Status"/> or
/// <see cref="Error"/> is non-null.
/// </summary>
public sealed record ActionResult(string? Status = null, string? Url = null, string? Error = null)
{
    public static ActionResult Ok(string status, string? url = null) => new(Status: status, Url: url);
    public static ActionResult Fail(string error) => new(Error: error);
}

/// <summary>
/// State machine for run_app / stop_app / refresh_project. Pure logic — no
/// HTTP, no DllImports. Acquires a single semaphore so only one action runs at a time.
/// </summary>
public sealed class StudioProActions
{
    private static readonly TimeSpan DefaultRunTimeout    = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultStopTimeout   = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultPollInterval  = TimeSpan.FromMilliseconds(500);

    private readonly IRunStateProbe probe;
    private readonly IStudioProUiAutomation ui;
    private readonly TimeSpan runTimeout;
    private readonly TimeSpan stopTimeout;
    private readonly TimeSpan pollInterval;
    private readonly SemaphoreSlim gate = new(1, 1);

    public StudioProActions(
        IRunStateProbe probe,
        IStudioProUiAutomation ui,
        TimeSpan? runTimeout = null,
        TimeSpan? stopTimeout = null,
        TimeSpan? pollInterval = null)
    {
        this.probe = probe;
        this.ui = ui;
        this.runTimeout = runTimeout ?? DefaultRunTimeout;
        this.stopTimeout = stopTimeout ?? DefaultStopTimeout;
        this.pollInterval = pollInterval ?? DefaultPollInterval;
    }

    public async Task<ActionResult> RunAppAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before = await probe.IsRunningAsync(ct);
            if (before == RunState.Running)
                return ActionResult.Ok("already_running", probe.GetActiveUrl());

            if (!ui.TriggerRun())
                return ActionResult.Fail("Studio Pro main window unavailable; try again after the IDE finishes loading");

            var after = await WaitForAsync(RunState.Running, runTimeout, ct);
            return after switch
            {
                RunState.Running => ActionResult.Ok("started", probe.GetActiveUrl()),
                _                => ActionResult.Ok("command_sent"),
            };
        }
        finally { gate.Release(); }
    }

    public async Task<ActionResult> StopAppAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var before = await probe.IsRunningAsync(ct);
            if (before == RunState.Stopped)
                return ActionResult.Ok("wasnt_running");

            if (!ui.TriggerStop())
                return ActionResult.Fail("Studio Pro main window unavailable; try again after the IDE finishes loading");

            var after = await WaitForAsync(RunState.Stopped, stopTimeout, ct);
            return after switch
            {
                RunState.Stopped => ActionResult.Ok("stopped"),
                _                => ActionResult.Ok("command_sent"),
            };
        }
        finally { gate.Release(); }
    }

    public async Task<ActionResult> RefreshProjectAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!ui.TriggerRefreshFromDisk())
                return ActionResult.Fail("Studio Pro main window unavailable; try again after the IDE finishes loading");
            return ActionResult.Ok("reloaded");
        }
        finally { gate.Release(); }
    }

    private async Task<RunState> WaitForAsync(RunState target, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var s = await probe.IsRunningAsync(ct);
            if (s == target) return s;
            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(pollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
        return await probe.IsRunningAsync(ct);
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~StudioProActionsTests`
Expected: All 10 tests pass. If `RunApp_TimesOut_ReturnsCommandSent` is flaky, raise the test's `runTimeout` or sleep slack.

- [ ] **Step 5: Commit**

```bash
git add src/StudioProActions.cs tests/StudioProActionsTests.cs
git commit -m "feat: StudioProActions state machine with single-flight gate"
```

---

## Task 9: `StudioProActionServer` HTTP + JSON-RPC (TDD)

**Files:**
- Create: `src/StudioProActionServer.cs`
- Create: `tests/StudioProActionServerTests.cs`

Hand-rolled `HttpListener` per the spec. Wire format mirrors `McpProbe`'s expectations: streamable-HTTP MCP — POST a JSON-RPC envelope, respond with `application/json`. Three methods: `initialize`, `tools/list`, `tools/call`.

- [ ] **Step 1: Write failing tests**

Create `tests/StudioProActionServerTests.cs`:

```csharp
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class StudioProActionServerTests : IAsyncLifetime
{
    private sealed class FakeProbe : IRunStateProbe
    {
        public string? Url { get; set; } = "http://localhost:8080";
        public int? Port { get; set; } = 8080;
        public Func<RunState> Next = () => RunState.Stopped;
        public string? GetActiveUrl() => Url;
        public int? GetActivePort() => Port;
        public Task<RunState> IsRunningAsync(CancellationToken ct = default) => Task.FromResult(Next());
    }

    private sealed class FakeUi : IStudioProUiAutomation
    {
        public bool TriggerRun() => true;
        public bool TriggerStop() => true;
        public bool TriggerRefreshFromDisk() => true;
    }

    private StudioProActionServer? server;
    private HttpClient http = null!;

    public Task InitializeAsync()
    {
        var probe = new FakeProbe { Next = () => RunState.Running };
        var actions = new StudioProActions(probe, new FakeUi(),
            runTimeout: TimeSpan.FromMilliseconds(200),
            stopTimeout: TimeSpan.FromMilliseconds(200),
            pollInterval: TimeSpan.FromMilliseconds(50));
        server = new StudioProActionServer(actions, port: 0);  // ephemeral
        server.Start();
        http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}") };
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        server?.Dispose();
        http.Dispose();
        return Task.CompletedTask;
    }

    private async Task<JsonDocument> Post(string body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var text = await resp.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text);
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        var info = doc.RootElement.GetProperty("result").GetProperty("serverInfo");
        info.GetProperty("name").GetString().Should().Be("mendix-studio-pro-actions");
        info.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ToolsList_ReturnsThreeTools()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        tools.GetArrayLength().Should().Be(3);
        var names = new List<string>();
        foreach (var t in tools.EnumerateArray()) names.Add(t.GetProperty("name").GetString()!);
        names.Should().BeEquivalentTo(new[] { "run_app", "stop_app", "refresh_project" });
    }

    [Fact]
    public async Task ToolsCall_RunApp_ReturnsAlreadyRunningResult()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"run_app","arguments":{}}}""");
        // MCP tools/call result: { content: [ { type: "text", text: "<json>" } ] }
        var content = doc.RootElement.GetProperty("result").GetProperty("content");
        content.GetArrayLength().Should().BeGreaterThan(0);
        var text = content[0].GetProperty("text").GetString()!;
        var inner = JsonDocument.Parse(text).RootElement;
        inner.GetProperty("status").GetString().Should().Be("already_running");
        inner.GetProperty("url").GetString().Should().Be("http://localhost:8080");
    }

    [Fact]
    public async Task ToolsCall_UnknownTool_ReturnsMcpError()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"nope","arguments":{}}}""");
        doc.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32601);
    }

    [Fact]
    public async Task MalformedJson_ReturnsParseError()
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{ not json", Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        var resp = await http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(text);
        doc.RootElement.GetProperty("error").GetProperty("code").GetInt32().Should().Be(-32700);
    }

    [Fact]
    public void Port_AvailableAfterStart()
    {
        // Sanity: Port property exposes the bound port. The listener prefix is http://127.0.0.1:{port}/
        // so the bind is loopback-only by definition.
        server!.Port.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DoubleStart_Throws()
    {
        Action act = () => server!.Start();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task DisposeStopsListener()
    {
        var probe = new FakeProbe();
        var actions = new StudioProActions(probe, new FakeUi());
        var s = new StudioProActionServer(actions, port: 0);
        s.Start();
        var port = s.Port;
        s.Dispose();
        // After dispose, attempt to connect should fail.
        using var c = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromMilliseconds(500) };
        Func<Task> call = async () => await c.GetAsync("/mcp");
        await call.Should().ThrowAsync<HttpRequestException>();
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~StudioProActionServerTests`
Expected: All fail (type undefined).

- [ ] **Step 3: Implement `StudioProActionServer`**

Create `src/StudioProActionServer.cs`:

```csharp
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Terminal;

/// <summary>
/// In-process MCP streamable-HTTP server. Listens on 127.0.0.1:port.
/// Implements three JSON-RPC methods: initialize, tools/list, tools/call.
/// One-action-at-a-time serialization is enforced inside <see cref="StudioProActions"/>.
/// </summary>
public sealed class StudioProActionServer : IDisposable
{
    public const string ServerName = "mendix-studio-pro-actions";
    public const string ServerVersion = "1.0.0";

    private readonly StudioProActions actions;
    private readonly Logger? log;
    private HttpListener? listener;
    private int boundPort;
    private CancellationTokenSource? cts;
    private Task? loop;
    private readonly int requestedPort;

    public StudioProActionServer(StudioProActions actions, int port, Logger? log = null)
    {
        this.actions = actions;
        this.log = log;
        this.requestedPort = port;
    }

    /// <summary>Bound port. Valid only after <see cref="Start"/> succeeds.</summary>
    public int Port => boundPort;

    public void Start()
    {
        if (listener != null) throw new InvalidOperationException("Server already started");

        boundPort = requestedPort > 0 ? requestedPort : PickFreePort();
        listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{boundPort}/");
        listener.Start();
        cts = new CancellationTokenSource();
        loop = Task.Run(() => AcceptLoopAsync(cts.Token));
        log?.Info($"[actions] HTTP server listening on http://127.0.0.1:{boundPort}/mcp");
    }

    public void Dispose()
    {
        try { cts?.Cancel(); } catch { }
        try { listener?.Stop(); } catch { }
        try { listener?.Close(); } catch { }
        try { loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        listener = null;
        cts = null;
        loop = null;
    }

    private static int PickFreePort()
    {
        // HttpListener doesn't support port 0; bind a TcpListener to discover one.
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await listener!.GetContextAsync(); }
            catch (HttpListenerException) { return; }   // Stop() called
            catch (ObjectDisposedException) { return; }
            _ = Task.Run(() => HandleAsync(ctx));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            if (ctx.Request.Url?.AbsolutePath != "/mcp" || ctx.Request.HttpMethod != "POST")
            {
                await Respond(ctx, 404, """{"error":"not found"}""");
                return;
            }

            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();

            JsonNode? root;
            try { root = JsonNode.Parse(body); }
            catch
            {
                await RespondJson(ctx, BuildError(id: null, code: -32700, message: "Parse error"));
                return;
            }
            if (root is not JsonObject req)
            {
                await RespondJson(ctx, BuildError(id: null, code: -32600, message: "Invalid Request"));
                return;
            }

            var id = req["id"];
            var method = req["method"]?.GetValue<string>();
            var pars = req["params"] as JsonObject;

            JsonNode result = method switch
            {
                "initialize" => HandleInitialize(),
                "tools/list" => HandleToolsList(),
                "tools/call" => await HandleToolsCallAsync(pars),
                _ => BuildErrorBody(code: -32601, message: $"Method '{method}' not found"),
            };

            JsonObject envelope;
            if (result is JsonObject obj && obj.ContainsKey("__error"))
            {
                var err = (JsonObject)obj["__error"]!.DeepClone();
                envelope = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = err };
            }
            else
            {
                envelope = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result };
            }
            await RespondJson(ctx, envelope);
        }
        catch (Exception ex)
        {
            log?.Error("[actions] request failure", ex);
            try { await RespondJson(ctx, BuildError(id: null, code: -32603, message: $"Internal error: {ex.Message}")); }
            catch { }
        }
    }

    private static JsonNode HandleInitialize() => new JsonObject
    {
        ["protocolVersion"] = "2025-03-26",
        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
        ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = ServerVersion },
    };

    private static JsonNode HandleToolsList() => new JsonObject
    {
        ["tools"] = new JsonArray
        {
            ToolDef("run_app",
                "Start the local Mendix runtime for the currently open Studio Pro app. If already running, returns 'already_running' without disturbing it."),
            ToolDef("stop_app",
                "Stop the local Mendix runtime. No-op if it isn't running."),
            ToolDef("refresh_project",
                "Reload the project model from disk. Use after editing model files (e.g. microflow XML) outside Studio Pro to make the IDE pick up the changes."),
        }
    };

    private static JsonObject ToolDef(string name, string description) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray(),
        }
    };

    private async Task<JsonNode> HandleToolsCallAsync(JsonObject? pars)
    {
        var name = pars?["name"]?.GetValue<string>();
        ActionResult result = name switch
        {
            "run_app"         => await actions.RunAppAsync(),
            "stop_app"        => await actions.StopAppAsync(),
            "refresh_project" => await actions.RefreshProjectAsync(),
            _ => null!,
        };

        if (result is null)
            return BuildErrorBody(code: -32601, message: $"Unknown tool '{name}'");

        // MCP tools/call result format: result.content = [ { type: "text", text: "<json>" } ]
        var payload = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        return new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = payload }
            },
            ["isError"] = result.Error != null,
        };
    }

    private static JsonObject BuildError(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static JsonObject BuildErrorBody(int code, string message) =>
        new() { ["__error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static Task RespondJson(HttpListenerContext ctx, JsonNode body) =>
        Respond(ctx, 200, body.ToJsonString());

    private static async Task Respond(HttpListenerContext ctx, int status, string body)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.OutputStream.Close();
    }
}
```

A couple of subtle points worth flagging in the implementation:

- `HttpListener` does **not** accept port `0` directly; we sniff a free ephemeral port via a transient `TcpListener`. The probe-friendly `Port` property exposes the bound value.
- `BuildErrorBody` returns a sentinel-shaped `JsonObject` (`{ "__error": {...} }`) so the dispatcher in `HandleAsync` can convert it into a JSON-RPC `error` envelope without losing the `id`. This keeps the per-method handlers from each having to know about the envelope.
- Listening on `http://127.0.0.1:{port}/` (not `+` or `localhost`) makes the bind localhost-only by definition — no firewall surface.

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~StudioProActionServerTests`
Expected: All 8 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/StudioProActionServer.cs tests/StudioProActionServerTests.cs
git commit -m "feat: StudioProActionServer (HTTP/JSON-RPC, three MCP tools)"
```

---

## Task 10: Wire the server into `TerminalSessionManager` lifecycle

**Files:**
- Modify: [`src/TerminalSessionManager.cs`](../../../src/TerminalSessionManager.cs)

The session manager is the singleton with an MEF lifecycle that already disposes correctly on `ExtensionUnloading` and `ProcessExit`. Hosting the action server here means we get the same disposal guarantees for free — no new lifecycle owner.

API surface added:
- `void StartActionServer(int port, StudioProActions actions, Logger log)` — idempotent; if a server is already running on a different port, stops it and starts on the new port.
- `void StopActionServer()` — idempotent.
- `int? CurrentActionServerPort` — for diagnostics.

`TerminalSessionManager` does **not** construct `StudioProActions` itself — the pane extension does that (Task 13) because that's where `IRunStateProbe` and `IStudioProUiAutomation` get assembled. The manager only owns the lifecycle.

- [ ] **Step 1: Add fields and methods**

In [`src/TerminalSessionManager.cs`](../../../src/TerminalSessionManager.cs), add at class scope:

```csharp
private StudioProActionServer? actionServer;
private readonly object actionServerGate = new();

public int? CurrentActionServerPort
{
    get { lock (actionServerGate) return actionServer?.Port; }
}

public void StartActionServer(int port, StudioProActions actions, Logger? log = null)
{
    if (disposed) throw new ObjectDisposedException(nameof(TerminalSessionManager));
    lock (actionServerGate)
    {
        // Always rebuild — the caller may have constructed a fresh `actions` with
        // updated hotkey config that we can't detect from here. The cost is one
        // TCP listener bind cycle, which is cheap.
        actionServer?.Dispose();
        var s = new StudioProActionServer(actions, port, log);
        s.Start();
        actionServer = s;
    }
}

public void StopActionServer()
{
    lock (actionServerGate)
    {
        actionServer?.Dispose();
        actionServer = null;
    }
}
```

Update the existing `Dispose()` to also stop the action server. Replace:

```csharp
public void Dispose()
{
    if (disposed) return;
    disposed = true;
    AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    DisposeAll();
}
```

with:

```csharp
public void Dispose()
{
    if (disposed) return;
    disposed = true;
    AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    StopActionServer();
    DisposeAll();
}
```

- [ ] **Step 2: Confirm it still compiles and existing tests still pass**

Run: `dotnet test`
Expected: All tests still pass — no behavior changed for existing callers.

- [ ] **Step 3: Commit**

```bash
git add src/TerminalSessionManager.cs
git commit -m "feat(session-mgr): own StudioProActionServer lifecycle"
```

---

## Task 11: Wire the actions toggle into `HandleSaveSettings`

**Files:**
- Modify: [`src/Messages/Incoming.cs`](../../../src/Messages/Incoming.cs)
- Modify: [`src/Messages/Outgoing.cs`](../../../src/Messages/Outgoing.cs)
- Modify: [`src/TerminalPaneViewModel.cs`](../../../src/TerminalPaneViewModel.cs)

Two surfaces change: the DTO contract with the UI, and the `HandleSaveSettings` diff/apply logic. The contract change is minimal — the UI sends three new optional fields; the C# server emits three new fields in the settings reply.

- [ ] **Step 1: Extend `SaveSettingsPayload`**

In [`src/Messages/Incoming.cs`](../../../src/Messages/Incoming.cs), replace the existing `SaveSettingsPayload` record with:

```csharp
public sealed record SaveSettingsPayload(
    string ShellPath,
    string[] Args,
    int? RingBufferKB = null,
    int? XtermScrollbackLines = null,
    string? Theme = null,
    bool? McpEnabled = null,
    int? McpPort = null,
    string[]? McpClients = null,
    bool? ActionsServerEnabled = null,
    int? ActionsServerPort = null,
    string? RefreshFromDiskHotkey = null);
```

- [ ] **Step 2: Extend `SettingsPayload`**

In [`src/Messages/Outgoing.cs`](../../../src/Messages/Outgoing.cs), replace the existing `SettingsPayload` record with:

```csharp
public sealed record SettingsPayload(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines,
    string Theme,
    IReadOnlyList<ShellOptionPayload> AvailableShells,
    bool McpEnabled,
    int McpPort,
    string[] McpClients,
    bool ActionsServerEnabled,
    int ActionsServerPort,
    string RefreshFromDiskHotkey);
```

- [ ] **Step 3: Inject the action wiring into the viewmodel**

The viewmodel needs to know how to:
1. Probe whether the action server is reachable (after starting it) — re-uses `McpProbe.ProbeAsync` since our server speaks the same wire format.
2. Start/stop the server via the session manager.
3. Construct `StudioProActions` and `StudioProUiAutomation` with the resolved hotkeys and the runtime URL probe.

Add fields and constructor parameters to [`src/TerminalPaneViewModel.cs`](../../../src/TerminalPaneViewModel.cs):

```csharp
// near the existing fields
private readonly Func<string?> getApplicationRootUrl;

public TerminalPaneViewModel(
    string title,
    TerminalSessionManager manager,
    Func<IModel?> getCurrentApp,
    Uri webIndexUri,
    Logger log,
    Func<string?> getApplicationRootUrl)
{
    Title = title;
    this.manager = manager;
    this.getCurrentApp = getCurrentApp;
    this.webIndexUri = webIndexUri;
    this.log = log;
    this.getApplicationRootUrl = getApplicationRootUrl;
}
```

(`TerminalPaneExtension` updates in Task 13 to pass the new constructor argument.)

- [ ] **Step 4: Update `BuildSettingsPayload` to emit the three new fields**

```csharp
private static SettingsPayload BuildSettingsPayload(TerminalSettings s) => new(
    ShellPath: s.ShellPath,
    Args: s.Args,
    RingBufferKB: s.RingBufferKB,
    XtermScrollbackLines: s.XtermScrollbackLines,
    Theme: s.Theme,
    AvailableShells: ShellDetector.Detect()
        .Select(o => new ShellOptionPayload(o.Name, o.Path))
        .ToList(),
    McpEnabled: s.McpEnabled,
    McpPort: s.McpPort,
    McpClients: s.McpClients,
    ActionsServerEnabled: s.ActionsServerEnabled,
    ActionsServerPort: s.ActionsServerPort,
    RefreshFromDiskHotkey: s.RefreshFromDiskHotkey);
```

- [ ] **Step 5: Update `HandleSaveSettings` to handle the actions toggle**

Modify the body of `HandleSaveSettings` so that, after the existing MCP probe-and-update section but before the `current with { … }`, it:

1. Reads the three new optional fields off `SaveSettingsPayload`.
2. If actions enabled and either flag was just flipped on or the port changed, start the server with the new port. After starting, probe via `McpProbe.ProbeAsync(newPort, log)` — bail out and post `mcpResult` with `Ok: false` if the probe fails.
3. If actions disabled and was previously enabled, stop the server.
4. Apply the actions diff to both configurators (Task 11 step 6).

Replace the existing body of `HandleSaveSettings` with:

```csharp
private async void HandleSaveSettings(SaveSettingsPayload p)
{
    try
    {
        var dir = GetProjectDir();
        if (dir == null) { Post("error", new ErrorPayload("No Mendix app is open")); return; }

        var current = TerminalSettings.Load(dir);

        var newClients         = p.McpClients ?? current.McpClients;
        var newEnabled         = p.McpEnabled ?? current.McpEnabled;
        var newPort            = p.McpPort    ?? current.McpPort;
        var newActionsEnabled  = p.ActionsServerEnabled ?? current.ActionsServerEnabled;
        var newActionsPort     = p.ActionsServerPort    ?? current.ActionsServerPort;
        var newRefreshHotkey   = p.RefreshFromDiskHotkey ?? current.RefreshFromDiskHotkey;

        // 1. Probe Studio Pro's primary MCP server (existing behaviour).
        if (newEnabled)
        {
            var probe = await McpProbe.ProbeAsync(newPort, log);
            if (!probe.Ok)
            {
                Post("mcpResult", new McpResultPayload(false,
                    $"{probe.Message}. Enable Studio Pro's MCP server in Preferences → Maia → MCP Server, then try again.",
                    Array.Empty<string>()));
                Post("settings", BuildSettingsPayload(current));
                return;
            }
        }

        // 2. Manage our own action-server lifecycle.
        if (newActionsEnabled)
        {
            // Re-create on each save when port or hotkey changed; StartActionServer is idempotent on no-op.
            var ui = new StudioProUiAutomation(
                runHotkey: "F5",
                stopHotkey: "Shift+F5",
                refreshHotkey: newRefreshHotkey,
                log: log);
            var probe = new RunStateProbe(getApplicationRootUrl);
            var actions = new StudioProActions(probe, ui);
            manager.StartActionServer(newActionsPort, actions, log);

            // Probe our own server. Re-use McpProbe since wire formats match.
            var pr = await McpProbe.ProbeAsync(newActionsPort, log);
            if (!pr.Ok)
            {
                manager.StopActionServer();
                Post("mcpResult", new McpResultPayload(false,
                    $"Action server failed to answer on port {newActionsPort}: {pr.Message}",
                    Array.Empty<string>()));
                Post("settings", BuildSettingsPayload(current));
                return;
            }
        }
        else if (current.ActionsServerEnabled)
        {
            manager.StopActionServer();
        }

        var updated = current with
        {
            ShellPath = p.ShellPath,
            Args = p.Args,
            RingBufferKB = p.RingBufferKB ?? current.RingBufferKB,
            XtermScrollbackLines = p.XtermScrollbackLines ?? current.XtermScrollbackLines,
            Theme = p.Theme ?? current.Theme,
            McpEnabled = newEnabled,
            McpPort = newPort,
            McpClients = newClients,
            ActionsServerEnabled = newActionsEnabled,
            ActionsServerPort = newActionsPort,
            RefreshFromDiskHotkey = newRefreshHotkey,
        };

        // 3. Apply file changes BEFORE saving settings.
        var touchedPrimary = ApplyMcpConfig(dir, current, updated);
        var touchedActions = ApplyActionsMcpConfig(dir, current, updated);

        updated.Save(dir);
        Post("settings", BuildSettingsPayload(updated));

        var allTouched = touchedPrimary.Concat(touchedActions).ToArray();
        if (allTouched.Length > 0)
        {
            Post("mcpResult", new McpResultPayload(true, BuildResultMessage(updated, allTouched), allTouched));

            _ = Task.Run(async () =>
            {
                try
                {
                    var recycled = await manager.RecycleAllAsync();
                    foreach (var r in recycled)
                    {
                        Post("tabClosed", new TabClosedPayload(r.OldTabId));
                        Post("tabCreated", new TabCreatedPayload(r.NewTabId, r.Title, r.ShellPath, r.Cwd));
                    }
                }
                catch (Exception ex) { log.Error("RecycleAll after MCP save failed", ex); }
            });
        }
    }
    catch (Exception ex)
    {
        log.Error("SaveSettings failed", ex);
        Post("error", new ErrorPayload($"Save failed: {ex.Message}", "saveSettings"));
    }
}

private static string BuildResultMessage(TerminalSettings s, string[] touched) =>
    (s.McpEnabled || s.ActionsServerEnabled)
        ? $"MCP servers updated for {string.Join(", ", touched)}. Restarting open terminals…"
        : $"MCP servers disabled (cleaned up: {string.Join(", ", touched)}). Restarting open terminals…";
```

- [ ] **Step 6: Add `ApplyActionsMcpConfig` helper**

Append to the same class:

```csharp
/// <summary>
/// Diff the actions-server toggle (and which CLIs are selected via the existing
/// McpClients list) and write the second server entry into .mcp.json / config.toml.
/// We piggy-back on McpClients — the action server is registered for the same
/// CLIs the user already chose in the primary MCP toggle.
/// </summary>
private string[] ApplyActionsMcpConfig(string projectDir, TerminalSettings prev, TerminalSettings next)
{
    var prevClients = new HashSet<string>(prev.McpClients, StringComparer.OrdinalIgnoreCase);
    var nextClients = next.ActionsServerEnabled
        ? new HashSet<string>(next.McpClients, StringComparer.OrdinalIgnoreCase)
        : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var jsonNeeded = nextClients.Contains("claude") || nextClients.Contains("copilot");
    var jsonHadIt  = prev.ActionsServerEnabled && (prevClients.Contains("claude") || prevClients.Contains("copilot"));
    var tomlNeeded = nextClients.Contains("codex");
    var tomlHadIt  = prev.ActionsServerEnabled && prevClients.Contains("codex");

    var url = $"http://localhost:{next.ActionsServerPort}/mcp";
    var json = new McpJsonConfigurator(projectDir);
    var toml = new McpTomlConfigurator();
    var touched = new List<string>();

    if (jsonNeeded) { json.UpsertActions(url); touched.Add(LabelForJson(nextClients) + " actions"); }
    else if (jsonHadIt) { json.RemoveActions(); touched.Add(LabelForJson(prevClients) + " actions (removed)"); }

    if (tomlNeeded) { toml.UpsertActions(url); touched.Add("Codex actions"); }
    else if (tomlHadIt) { toml.RemoveActions(); touched.Add("Codex actions (removed)"); }

    return touched.ToArray();
}
```

- [ ] **Step 7: Compile**

Run: `dotnet build`
Expected: Build fails because `TerminalPaneExtension` doesn't yet pass `getApplicationRootUrl`. Defer the fix to Task 13 — for now, accept the compile error or temporarily pass `() => null` from the extension to keep the tree green. Easiest:

In `TerminalPaneExtension.Open()`, change the constructor call to:

```csharp
return new TerminalPaneViewModel(
    title: "Terminal",
    manager: manager,
    getCurrentApp: () => CurrentApp,
    webIndexUri: indexUri,
    log: log,
    getApplicationRootUrl: () => null);   // wired up in Task 13
```

Now `dotnet build` should succeed.

- [ ] **Step 8: Run all tests — confirm no regressions**

Run: `dotnet test`
Expected: All tests pass (no new tests added in this task).

- [ ] **Step 9: Commit**

```bash
git add src/Messages src/TerminalPaneViewModel.cs src/TerminalPaneExtension.cs
git commit -m "feat(viewmodel): actions-server lifecycle in HandleSaveSettings"
```

---

## Task 12: Settings UI — actions section in HTML and TS

**Files:**
- Modify: [`ui/index.html`](../../../ui/index.html)
- Modify: [`ui/src/settings-modal.ts`](../../../ui/src/settings-modal.ts)

Two new fields in the modal, mirroring the existing MCP section: a checkbox `set-actions-enabled` and a port input `set-actions-port`. Plus a text field for the refresh hotkey. Hotkey field is shown unconditionally so users can edit it even with the server off (no harm — change only takes effect when the server starts).

The actions section depends on the primary MCP toggle being on (since the action server registers itself for whichever CLIs are checked under MCP) — disable it when MCP is off. Document that in the UI label.

- [ ] **Step 1: Add HTML**

In [`ui/index.html`](../../../ui/index.html), insert immediately after the existing Codex checkbox row (after the line containing `set-mcp-codex`) and before the closing `<div class="actions">`:

```html
<h4>MCP Actions Server</h4>
<div class="checkbox-row">
  <input id="set-actions-enabled" type="checkbox">
  <label for="set-actions-enabled" style="margin:0">Enable Studio Pro action tools (run_app / stop_app / refresh_project) for the CLIs above</label>
</div>
<div class="field"><label>Port</label><input id="set-actions-port" type="number" min="1" max="65535" value="7783"></div>
<div class="field"><label>Refresh-from-disk hotkey</label><input id="set-refresh-hotkey" type="text" value="F4" placeholder="e.g. F4 or Ctrl+F5"></div>
```

- [ ] **Step 2: Wire it up in TS**

In [`ui/src/settings-modal.ts`](../../../ui/src/settings-modal.ts), extend the `SettingsPayload` interface:

```ts
interface SettingsPayload {
  shellPath: string;
  args: string[];
  ringBufferKB: number;
  xtermScrollbackLines: number;
  theme: string;
  availableShells: ShellOption[];
  mcpEnabled: boolean;
  mcpPort: number;
  mcpClients: string[];
  actionsServerEnabled: boolean;
  actionsServerPort: number;
  refreshFromDiskHotkey: string;
}
```

Add new field captures next to the existing `chkMcp` etc.:

```ts
  private chkActions = document.getElementById("set-actions-enabled") as HTMLInputElement;
  private inpActionsPort = document.getElementById("set-actions-port") as HTMLInputElement;
  private inpRefreshHotkey = document.getElementById("set-refresh-hotkey") as HTMLInputElement;
```

Inside the constructor, listen for changes on the actions toggle so the UI also disables the actions inputs when the master MCP toggle goes off:

```ts
    this.chkActions.addEventListener("change", () => this.onActionsEnabledChange());
```

Update `onMcpEnabledChange` to also disable the actions controls when the master MCP toggle is off (since the action server registers via the same CLI list):

```ts
  private onMcpEnabledChange() {
    const enabled = this.chkMcp.checked;
    if (!enabled) {
      this.chkMcpClaude.checked = false;
      this.chkMcpCopilot.checked = false;
      this.chkMcpCodex.checked = false;
      this.chkActions.checked = false;        // actions can't run without primary MCP wiring
    }
    this.chkMcpClaude.disabled = !enabled;
    this.chkMcpCopilot.disabled = !enabled;
    this.chkMcpCodex.disabled = !enabled;
    this.inpMcpPort.disabled = !enabled;
    this.chkActions.disabled = !enabled;
    this.onActionsEnabledChange();
  }

  private onActionsEnabledChange() {
    const on = this.chkMcp.checked && this.chkActions.checked;
    this.inpActionsPort.disabled = !on;
  }
```

Update the `populate` method (which the bridge calls when receiving a `settings` event) to also populate the new fields. Find the existing `populate(d: SettingsPayload)` method and add at the end:

```ts
    this.chkActions.checked = d.actionsServerEnabled;
    this.inpActionsPort.value = String(d.actionsServerPort);
    this.inpRefreshHotkey.value = d.refreshFromDiskHotkey;
    this.onMcpEnabledChange();   // also flips actions enabled state
```

Update `save()` (the method invoked by the Save button) so the outgoing message includes the three new fields. Find the existing `bridge.send("saveSettings", { … })` and extend its body:

```ts
    this.bridge.send("saveSettings", {
      shellPath: this.shellSelectedPath(),
      args: this.parseArgs(),
      ringBufferKB: parseInt(this.inpRing.value, 10),
      xtermScrollbackLines: parseInt(this.inpScroll.value, 10),
      theme: this.selTheme.value,
      mcpEnabled: this.chkMcp.checked,
      mcpPort: parseInt(this.inpMcpPort.value, 10),
      mcpClients: this.collectClients(),
      actionsServerEnabled: this.chkActions.checked,
      actionsServerPort: parseInt(this.inpActionsPort.value, 10),
      refreshFromDiskHotkey: this.inpRefreshHotkey.value,
    });
```

(If the existing `save()` method's exact field shape differs slightly, preserve all existing fields and add only the three new ones.)

- [ ] **Step 3: Confirm the UI builds**

Run: `dotnet build`
Expected: The MSBuild `BuildUi` target invokes `node ui/esbuild.mjs`. Build succeeds with no TypeScript errors.

If TypeScript reports `Cannot read properties of null` on the new element references, ensure the corresponding HTML nodes were saved.

- [ ] **Step 4: Commit**

```bash
git add ui/index.html ui/src/settings-modal.ts
git commit -m "feat(ui): MCP actions server section in settings modal"
```

---

## Task 13: MEF inject `ILocalRunConfigurationsService` into the pane extension

**Files:**
- Modify: [`src/TerminalPaneExtension.cs`](../../../src/TerminalPaneExtension.cs)

`ILocalRunConfigurationsService` is exported by Studio Pro and consumed via MEF's `[ImportingConstructor]`. The pane extension wires it up and passes a closure that resolves the active configuration's `ApplicationRootUrl` on every call. The closure is recomputed each time so when the user opens a different app (or no app), the probe reflects the current state.

- [ ] **Step 1: Update `TerminalPaneExtension`**

Replace [`src/TerminalPaneExtension.cs`](../../../src/TerminalPaneExtension.cs):

```csharp
using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.Events;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;

namespace Terminal;

[Export(typeof(DockablePaneExtension))]
public sealed class TerminalPaneExtension : DockablePaneExtension
{
    public const string ID = "Terminal";
    public override string Id => ID;

    private readonly TerminalSessionManager manager;
    private readonly ILocalRunConfigurationsService localRunConfigs;
    private Logger log = null!;
    private bool subscribed;

    [ImportingConstructor]
    public TerminalPaneExtension(ILocalRunConfigurationsService localRunConfigs)
    {
        this.localRunConfigs = localRunConfigs;
        manager = new TerminalSessionManager(new PtyNetFactory());
    }

    public override DockablePaneViewModelBase Open()
    {
        EnsureLogger();
        EnsureLifecycleSubscribed();

        var indexUri = new Uri(WebServerBaseUrl, "index.html");
        return new TerminalPaneViewModel(
            title: "Terminal",
            manager: manager,
            getCurrentApp: () => CurrentApp,
            webIndexUri: indexUri,
            log: log,
            getApplicationRootUrl: () =>
            {
                var model = CurrentApp;
                if (model is null) return null;
                try { return localRunConfigs.GetActiveConfiguration(model)?.ApplicationRootUrl; }
                catch (Exception ex) { log?.Warn($"GetActiveConfiguration threw: {ex.Message}"); return null; }
            });
    }

    private void EnsureLogger()
    {
        var dir = (CurrentApp?.Root as IProject)?.DirectoryPath ?? Environment.CurrentDirectory;
        log = new Logger(dir);
    }

    private void EnsureLifecycleSubscribed()
    {
        if (subscribed) return;
        subscribed = true;
        Subscribe<ExtensionUnloading>(() =>
        {
            try { log.Info("ExtensionUnloading — disposing all PTYs and action server"); manager.Dispose(); }
            catch (Exception ex) { log.Error("Dispose on unload failed", ex); }
        });
    }
}
```

Two important changes vs the previous file:

1. `[ImportingConstructor]` now requests `ILocalRunConfigurationsService`, which Studio Pro provides via MEF.
2. `ExtensionUnloading` now calls `manager.Dispose()` instead of `manager.DisposeAll()` so the action server is also stopped (we updated `Dispose` in Task 10 to call `StopActionServer`).

- [ ] **Step 2: Auto-start the action server if settings have it enabled at extension load**

If a previous session left `actionsServerEnabled=true` in `terminal-settings.json`, the server should come up automatically the first time the pane is opened (or on first project load). Otherwise the user has to re-toggle it after every restart. Add to `Open()`, just before constructing the viewmodel:

```csharp
        TryAutoStartActionServer();
```

And the implementation at class scope:

```csharp
private void TryAutoStartActionServer()
{
    var dir = (CurrentApp?.Root as IProject)?.DirectoryPath;
    if (dir is null) return;
    var settings = TerminalSettings.Load(dir);
    if (!settings.ActionsServerEnabled) return;

    try
    {
        var ui = new StudioProUiAutomation(
            runHotkey: "F5",
            stopHotkey: "Shift+F5",
            refreshHotkey: settings.RefreshFromDiskHotkey,
            log: log);
        var probe = new RunStateProbe(() =>
        {
            var model = CurrentApp;
            if (model is null) return null;
            try { return localRunConfigs.GetActiveConfiguration(model)?.ApplicationRootUrl; }
            catch { return null; }
        });
        var actions = new StudioProActions(probe, ui);
        manager.StartActionServer(settings.ActionsServerPort, actions, log);
        log.Info($"[actions] auto-started server on port {settings.ActionsServerPort}");
    }
    catch (Exception ex)
    {
        log.Error("[actions] auto-start failed", ex);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Compiles cleanly. If MEF complains at runtime that `ILocalRunConfigurationsService` is unavailable on an older Mendix host, that's evidence we need a graceful fallback — but the spec confirms this is a publicly-exported service in 11.6.2+, so MEF should resolve it.

- [ ] **Step 4: Run all tests**

Run: `dotnet test`
Expected: Everything passes.

- [ ] **Step 5: Commit**

```bash
git add src/TerminalPaneExtension.cs
git commit -m "feat(extension): inject ILocalRunConfigurationsService; auto-start action server"
```

---

## Task 14: Manual smoke test (end-to-end)

This is **not** automated. It's the acceptance gate before merging.

**Prerequisites:**
- Test deploy target configured: `Directory.Build.props` contains `<MendixDeployTarget>C:\Projects\AltairTraversalViewer</MendixDeployTarget>` (or the same path from your `Directory.Build.props.example`).
- Latest commits checked out and built: `dotnet build` produces a fresh `Terminal.dll` and the deploy target has it under `<project>\extensions\Terminal\Terminal.dll`.
- The Mendix per-project extension cache pitfall is acknowledged — see the section at the top of this plan.

- [ ] **Step 1: Verify the loaded DLL hash matches the freshly built one**

After launching Studio Pro on `C:\Projects\AltairTraversalViewer`, run:

```powershell
Get-Process studiopro | %{ $_.Modules } | ? ModuleName -eq Terminal.dll | Get-FileHash
Get-FileHash "C:\Extensions\Terminal\bin\Debug\net8.0\Terminal.dll"
```

If the hashes differ, close Studio Pro, delete `C:\Projects\AltairTraversalViewer\.mendix-cache\extensions-cache\`, relaunch, and retry. Do not continue smoke-testing while these hashes mismatch — you will be testing stale code.

- [ ] **Step 2: Open the Terminal pane**

In Studio Pro: Extensions → Terminal. The pane opens and shows the Terminal UI.

- [ ] **Step 3: Open Settings and enable the actions server**

Click the gear icon. The settings modal opens. Verify:
- The new "MCP Actions Server" section is present beneath the existing "MCP Server" section.
- The actions enable checkbox is disabled until the master MCP toggle is on.

Turn on the master MCP toggle, check at least one CLI (e.g. Claude Code), set port to 7783, then turn on the actions toggle. Confirm the port input enables. Click Save.

Expected:
- The banner shows a green "MCP servers updated…" message including "actions" labels.
- `terminal-settings.json` in the project's `resources/` folder now has `actionsServerEnabled: true` and `actionsServerPort: 7783`.
- `<project>\.mcp.json` contains both `mendix-studio-pro` and `mendix-studio-pro-actions` entries.
- The terminal log shows `[actions] HTTP server listening on http://127.0.0.1:7783/mcp`.

- [ ] **Step 4: Verify the action server answers an MCP probe**

In a host PowerShell:

```powershell
$body = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"smoke","version":"1"}}}'
Invoke-WebRequest -UseBasicParsing -Method POST -Uri http://127.0.0.1:7783/mcp -ContentType application/json -Body $body | Select-Object -ExpandProperty Content
```

Expected: JSON body containing `"name":"mendix-studio-pro-actions"`.

- [ ] **Step 5: `tools/list` shape**

```powershell
$body = '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
Invoke-WebRequest -UseBasicParsing -Method POST -Uri http://127.0.0.1:7783/mcp -ContentType application/json -Body $body | Select-Object -ExpandProperty Content
```

Expected: `tools` array with `run_app`, `stop_app`, `refresh_project`. Each has `inputSchema.type == "object"` with empty `properties`/`required`.

- [ ] **Step 6: `run_app` happy path**

With the local run config currently stopped, call:

```powershell
$body = '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"run_app","arguments":{}}}'
Invoke-WebRequest -UseBasicParsing -Method POST -Uri http://127.0.0.1:7783/mcp -ContentType application/json -Body $body | Select-Object -ExpandProperty Content
```

Expected:
- Studio Pro starts running the app (M2EE console comes up; runtime port opens).
- The HTTP response JSON contains `result.content[0].text` whose body parses to either `{"status":"started", "url":"http://localhost:8080"}` or `{"status":"command_sent"}` (timeout case).

If "command_sent" is observed and the app actually started, that's the 60 s timeout being too tight on a cold build. Bump `DefaultRunTimeout` in `StudioProActions.cs` and rebuild.

- [ ] **Step 7: `run_app` already-running**

Call the same body again. Expected: `"already_running"` with the URL — and Studio Pro does **not** restart the app (no second M2EE console). This verifies `RunStateProbe` correctly read the active port.

- [ ] **Step 8: `stop_app`**

```powershell
$body = '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"stop_app","arguments":{}}}'
Invoke-WebRequest -UseBasicParsing -Method POST -Uri http://127.0.0.1:7783/mcp -ContentType application/json -Body $body | Select-Object -ExpandProperty Content
```

Expected: Studio Pro stops the running app; response contains `"status":"stopped"`. Calling again returns `"wasnt_running"`.

- [ ] **Step 9: `refresh_project`**

Edit a model file outside Studio Pro: pick any microflow XML under `C:\Projects\AltairTraversalViewer\Modules\…\documents\…\*.microflow` and append a benign comment (or use Claude Code in the Terminal pane to do so).

Call:

```powershell
$body = '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"refresh_project","arguments":{}}}'
Invoke-WebRequest -UseBasicParsing -Method POST -Uri http://127.0.0.1:7783/mcp -ContentType application/json -Body $body | Select-Object -ExpandProperty Content
```

Expected: Studio Pro picks up the on-disk change (the flow shows the new state, or a "files changed on disk" reload-prompt fires). Response: `"status":"reloaded"`.

If `F4` does **not** trigger a project-from-disk reload in this Studio Pro version, change `RefreshFromDiskHotkey` in `terminal-settings.json` to whichever binding does (most plausibly something under the Edit menu — open Studio Pro and search the menus for "reload from disk" / "refresh project"). Update the default in `TerminalSettings.Defaults()` if a better default is found.

- [ ] **Step 10: End-to-end via Claude Code in the Terminal pane**

In the Terminal pane, start `claude` (or `codex` / `copilot`). Inside the assistant's session:

```
/mcp
```

Expected: Claude lists both `mendix-studio-pro` and `mendix-studio-pro-actions` servers, with the action server exposing `run_app` / `stop_app` / `refresh_project`.

Ask Claude: "Use the mendix-studio-pro-actions tools to start the app, then tell me the URL." Confirm the runtime starts. Stop the app, ask Claude to stop it again — confirm `wasnt_running` is reported gracefully. Ask Claude to refresh the project after editing a model file — confirm Studio Pro reloads.

- [ ] **Step 11: Disable and clean up**

Open Settings, turn off the actions toggle, Save.

Expected:
- Banner shows "MCP servers disabled (cleaned up: … actions (removed)…)".
- `<project>\.mcp.json` no longer contains `mendix-studio-pro-actions`.
- `~/.codex/config.toml` (if Codex was selected) no longer contains the `[mcp_servers.mendix-studio-pro-actions]` section.
- The terminal log records the server stopping.
- A subsequent `Invoke-WebRequest` to `http://127.0.0.1:7783/mcp` fails to connect.

- [ ] **Step 12: Reload extension and verify auto-start works**

Re-enable the actions toggle and Save. Press F4 in Studio Pro to reload extensions. Reopen the Terminal pane. Verify the server is back up:

```powershell
$body = '{"jsonrpc":"2.0","id":1,"method":"initialize"}'
Invoke-WebRequest -UseBasicParsing -Method POST -Uri http://127.0.0.1:7783/mcp -ContentType application/json -Body $body
```

Expected: 200 + initialize response. The terminal log shows `[actions] auto-started server on port 7783`.

- [ ] **Step 13: Document any tuning required**

If during smoke you adjusted `DefaultRunTimeout` / `DefaultStopTimeout` / `DefaultPollInterval`, or changed the default `RefreshFromDiskHotkey`, update them in code, rebuild, and re-run Steps 6–9. Commit those changes:

```bash
git add src/StudioProActions.cs src/TerminalSettings.cs
git commit -m "chore(actions): tune defaults from smoke test"
```

- [ ] **Step 14: Final clean state**

Run all unit tests once more:

```bash
dotnet test
```

Expected: every test passes. The branch is ready for review/merge.

---

## Roll-out / file inventory

After every task is checked off, the repo will contain these new and modified files:

**New (C#):**
- `src/IRunStateProbe.cs`
- `src/RunStateProbe.cs`
- `src/IStudioProUiAutomation.cs`
- `src/StudioProUiAutomation.cs`
- `src/StudioProActions.cs`
- `src/StudioProActionServer.cs`

**New (tests):**
- `tests/RunStateProbeTests.cs`
- `tests/StudioProActionsTests.cs`
- `tests/StudioProActionServerTests.cs`
- `tests/McpJsonConfiguratorTests.cs`
- `tests/McpTomlConfiguratorTests.cs`

**Modified (C#):**
- `src/TerminalSettings.cs` — three new fields
- `src/Messages/Incoming.cs` — three new optional fields on `SaveSettingsPayload`
- `src/Messages/Outgoing.cs` — three new fields on `SettingsPayload`
- `src/McpJsonConfigurator.cs` — `UpsertActions`/`RemoveActions`
- `src/McpTomlConfigurator.cs` — `UpsertActions`/`RemoveActions`; testable internal ctor
- `src/TerminalSessionManager.cs` — action-server lifecycle plumbing
- `src/TerminalPaneViewModel.cs` — actions toggle in `HandleSaveSettings`
- `src/TerminalPaneExtension.cs` — MEF inject `ILocalRunConfigurationsService`; auto-start
- `Terminal.csproj` — `InternalsVisibleTo` for tests (if not already present)

**Modified (UI):**
- `ui/index.html` — actions section
- `ui/src/settings-modal.ts` — new fields and toggle behaviour

**Modified (tests):**
- `tests/TerminalSettingsTests.cs` — three new tests + two existing tests updated to compile

There are no new build targets, NuGet references, or runtime dependencies. The implementation reuses the existing `Mendix.StudioPro.ExtensionsAPI`, `System.Net.HttpListener` (in-box .NET), `System.Text.Json` (already referenced), and Win32 `user32.dll` (always present on Windows).
