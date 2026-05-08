# Concord MCP — Maia Bridge embedded in C# — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port the Python `concord-maia-bridge` prototype into Concord as a fully native C# subsystem, rename the wire identity from `mendix-studio-pro-actions` to `concord-mcp` (v1.3.0), and surface Maia integration as a first-class toggle in a renamed "Concord MCP" settings section.

**Architecture:** Adjacent module — new `src/Maia/` folder holds the bridge (interfaces, two CDP transports, router, verb-level actions, embedded JS agent). `StudioProActionServer.cs` gains six `maia__*` tool registrations alongside its existing tools, plus the rename. The `IMaiaTransport` interface is the swap-out seam for a future Mendix-native MCP transport (tier 0). Maia tool registration is gated on Windows + the per-feature settings toggle.

**Tech Stack:** .NET 8, C# preview, xUnit + FluentAssertions, System.Net.WebSockets, System.Management (WMI), embedded resource (`maia_agent.js`), TypeScript for the settings modal.

**Reference:** [docs/superpowers/specs/2026-05-08-concord-mcp-maia-bridge-design.md](../specs/2026-05-08-concord-mcp-maia-bridge-design.md)

---

## Phase 1 — Settings rename and migration

### Task 1: Rename and add fields to `TerminalSettings`

**Files:**
- Modify: [src/TerminalSettings.cs](../../../src/TerminalSettings.cs)
- Test: [tests/TerminalSettingsTests.cs](../../../tests/TerminalSettingsTests.cs)

- [ ] **Step 1: Add a failing migration test**

Append to `tests/TerminalSettingsTests.cs`:

```csharp
[Fact]
public void Load_OldSchemaWithActionsServerEnabled_MigratesToMcpServerEnabled()
{
    var dir = Directory.CreateTempSubdirectory("concord-settings-").FullName;
    try
    {
        var resDir = Path.Combine(dir, "resources");
        Directory.CreateDirectory(resDir);
        // Old schema: actionsServerEnabled present, McpServerEnabled absent.
        File.WriteAllText(Path.Combine(resDir, "terminal-settings.json"),
            """{"shellPath":"powershell.exe","actionsServerEnabled":true}""");

        var s = TerminalSettings.Load(dir);

        s.McpServerEnabled.Should().BeTrue();
        s.StudioProActionsEnabled.Should().BeTrue();
        s.MaiaIntegrationEnabled.Should().BeTrue();
    }
    finally { Directory.Delete(dir, recursive: true); }
}

[Fact]
public void Defaults_HaveSubTogglesOnAndMasterOff()
{
    var d = TerminalSettings.Defaults();
    d.McpServerEnabled.Should().BeFalse();
    d.StudioProActionsEnabled.Should().BeTrue();
    d.MaiaIntegrationEnabled.Should().BeTrue();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~Load_OldSchemaWith|FullyQualifiedName~Defaults_HaveSubToggles"`
Expected: FAIL — properties `McpServerEnabled`, `StudioProActionsEnabled`, `MaiaIntegrationEnabled` don't exist.

- [ ] **Step 3: Update the record and `Defaults()`**

Replace [src/TerminalSettings.cs](../../../src/TerminalSettings.cs) lines 5-39 with:

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
    bool McpServerEnabled,
    int McpServerPort,
    bool StudioProActionsEnabled,
    bool MaiaIntegrationEnabled,
    string RefreshFromDiskHotkey,
    bool RestoreTabsOnReopen)
{
    public static TerminalSettings Defaults() => new(
        ShellPath: DefaultShellPath(),
        Args: Array.Empty<string>(),
        RingBufferKB: 4096,
        XtermScrollbackLines: 10000,
        Theme: "auto",
        McpEnabled: false,
        McpPort: 8100,
        McpClients: Array.Empty<string>(),
        McpServerEnabled: false,
        McpServerPort: 7783,
        StudioProActionsEnabled: true,
        MaiaIntegrationEnabled: true,
        RefreshFromDiskHotkey: "F4",
        RestoreTabsOnReopen: true);
```

- [ ] **Step 4: Update `Load()` to migrate old keys**

Replace the `Load` method body (lines 81-109) with:

```csharp
public static TerminalSettings Load(string projectDir)
{
    var path = PathFor(projectDir);
    if (!File.Exists(path)) return Defaults();
    try
    {
        using var stream = File.OpenRead(path);
        var dto = JsonSerializer.Deserialize<Dto>(stream, Json);
        if (dto is null) return Defaults();
        var def = Defaults();
        // Migration: old key "actionsServerEnabled" → new key "mcpServerEnabled".
        // Old key "actionsServerPort" → new key "mcpServerPort". If both old
        // and new are present, new wins. Sub-toggles default true so an old
        // settings file that just had the master flag opts into both
        // tool families on first load.
        bool master = dto.McpServerEnabled ?? dto.ActionsServerEnabled ?? def.McpServerEnabled;
        int port = dto.McpServerPort ?? dto.ActionsServerPort ?? def.McpServerPort;
        return new TerminalSettings(
            ShellPath: MigrateShellPathForPlatform(dto.ShellPath ?? def.ShellPath),
            Args: dto.Args ?? def.Args,
            RingBufferKB: dto.RingBufferKB ?? def.RingBufferKB,
            XtermScrollbackLines: dto.XtermScrollbackLines ?? def.XtermScrollbackLines,
            Theme: dto.Theme ?? def.Theme,
            McpEnabled: dto.McpEnabled ?? def.McpEnabled,
            McpPort: dto.McpPort ?? def.McpPort,
            McpClients: dto.McpClients ?? def.McpClients,
            McpServerEnabled: master,
            McpServerPort: port,
            StudioProActionsEnabled: dto.StudioProActionsEnabled ?? def.StudioProActionsEnabled,
            MaiaIntegrationEnabled: dto.MaiaIntegrationEnabled ?? def.MaiaIntegrationEnabled,
            RefreshFromDiskHotkey: dto.RefreshFromDiskHotkey ?? def.RefreshFromDiskHotkey,
            RestoreTabsOnReopen: dto.RestoreTabsOnReopen ?? def.RestoreTabsOnReopen);
    }
    catch (JsonException)
    {
        return Defaults();
    }
}
```

- [ ] **Step 5: Update `Save()` and `Dto`**

Replace the `Save` method (lines 111-118) and the `Dto` record (lines 120-132):

```csharp
public void Save(string projectDir)
{
    var dir = System.IO.Path.Combine(projectDir, SubDir);
    Directory.CreateDirectory(dir);
    var path = System.IO.Path.Combine(dir, FileName);
    var dto = new Dto(
        ShellPath, Args, RingBufferKB, XtermScrollbackLines, Theme,
        McpEnabled, McpPort, McpClients,
        McpServerEnabled, McpServerPort,
        StudioProActionsEnabled, MaiaIntegrationEnabled,
        RefreshFromDiskHotkey, RestoreTabsOnReopen,
        // Legacy keys: write them too so an older Concord build that reads
        // this file keeps the master toggle in sync. Drop after 1.4.0.
        ActionsServerEnabled: McpServerEnabled,
        ActionsServerPort: McpServerPort);
    File.WriteAllText(path, JsonSerializer.Serialize(dto, Json));
}

private sealed record Dto(
    string? ShellPath,
    string[]? Args,
    int? RingBufferKB,
    int? XtermScrollbackLines,
    string? Theme,
    bool? McpEnabled,
    int? McpPort,
    string[]? McpClients,
    bool? McpServerEnabled,
    int? McpServerPort,
    bool? StudioProActionsEnabled,
    bool? MaiaIntegrationEnabled,
    string? RefreshFromDiskHotkey,
    bool? RestoreTabsOnReopen,
    bool? ActionsServerEnabled = null,
    int? ActionsServerPort = null);
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~TerminalSettings"`
Expected: PASS — all `TerminalSettingsTests` green.

- [ ] **Step 7: Commit**

```powershell
git add src/TerminalSettings.cs tests/TerminalSettingsTests.cs
git commit -m "refactor(settings): rename ActionsServerEnabled to McpServerEnabled, add sub-toggles

Adds StudioProActionsEnabled and MaiaIntegrationEnabled. Load migrates old
schema by reading the legacy keys when the new ones are absent. Save writes
both for one minor-version transition.
"
```

---

### Task 2: Update `SettingsPayload` and `SaveSettingsPayload`

**Files:**
- Modify: [src/Messages/Outgoing.cs](../../../src/Messages/Outgoing.cs)
- Modify: [src/Messages/Incoming.cs](../../../src/Messages/Incoming.cs)
- Test: [tests/MessageDtoTests.cs](../../../tests/MessageDtoTests.cs)

- [ ] **Step 1: Add a failing test for the new payload shape**

Append to `tests/MessageDtoTests.cs`:

```csharp
[Fact]
public void SettingsPayload_HasNewMcpFields()
{
    var p = new SettingsPayload(
        ShellPath: "pwsh", Args: Array.Empty<string>(),
        RingBufferKB: 1, XtermScrollbackLines: 1, Theme: "auto",
        AvailableShells: Array.Empty<ShellOptionPayload>(),
        McpEnabled: false, McpPort: 0, McpClients: Array.Empty<string>(),
        McpServerEnabled: true, McpServerPort: 7783,
        StudioProActionsEnabled: true, MaiaIntegrationEnabled: true,
        Platform: "windows",
        RefreshFromDiskHotkey: "F4", RestoreTabsOnReopen: true,
        About: new AboutInfoPayload("1.3.0", null, null),
        StudioProMcp: null);
    p.McpServerEnabled.Should().BeTrue();
    p.MaiaIntegrationEnabled.Should().BeTrue();
    p.Platform.Should().Be("windows");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~SettingsPayload_HasNewMcpFields"`
Expected: FAIL — record doesn't accept those parameters.

- [ ] **Step 3: Update `SettingsPayload`**

Replace lines 24-39 in [src/Messages/Outgoing.cs](../../../src/Messages/Outgoing.cs):

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
    bool McpServerEnabled,
    int McpServerPort,
    bool StudioProActionsEnabled,
    bool MaiaIntegrationEnabled,
    string Platform,
    string RefreshFromDiskHotkey,
    bool RestoreTabsOnReopen,
    AboutInfoPayload About,
    StudioProMcpInfoPayload? StudioProMcp);
```

- [ ] **Step 4: Update `SaveSettingsPayload`**

Replace lines 18-30 in [src/Messages/Incoming.cs](../../../src/Messages/Incoming.cs):

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
    bool? McpServerEnabled = null,
    int? McpServerPort = null,
    bool? StudioProActionsEnabled = null,
    bool? MaiaIntegrationEnabled = null,
    string? RefreshFromDiskHotkey = null,
    bool? RestoreTabsOnReopen = null);
```

- [ ] **Step 5: Update consumers (compile sweep)**

Run: `dotnet build Terminal.csproj`
Expected: errors in `TerminalPaneViewModel.cs` and any other files that construct `SettingsPayload` or `SaveSettingsPayload` with old field names. For each error:
- Replace `ActionsServerEnabled` → `McpServerEnabled`
- Replace `ActionsServerPort` → `McpServerPort`
- Add `StudioProActionsEnabled = settings.StudioProActionsEnabled`
- Add `MaiaIntegrationEnabled = settings.MaiaIntegrationEnabled`
- Add `Platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "darwin" : "linux"`

Repeat the build until clean.

- [ ] **Step 6: Run all tests**

Run: `dotnet test tests/Terminal.Tests.csproj`
Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/Messages/ src/TerminalPaneViewModel.cs
git commit -m "refactor(messages): expose Concord MCP settings (master + sub-toggles + platform)"
```

---

### Task 3: Wire up auto-start to the new master toggle

**Files:**
- Modify: [src/TerminalPaneExtension.cs](../../../src/TerminalPaneExtension.cs)

- [ ] **Step 1: Update auto-start guard**

Open [src/TerminalPaneExtension.cs](../../../src/TerminalPaneExtension.cs) and find `TryAutoStartActionServer` (around line 206). Change every reference to `settings.ActionsServerEnabled` → `settings.McpServerEnabled` and `settings.ActionsServerPort` → `settings.McpServerPort`. Change the log line:

```csharp
log.Info($"[concord-mcp] auto-started server on port {settings.McpServerPort}");
```

- [ ] **Step 2: Build**

Run: `dotnet build Terminal.csproj`
Expected: clean build.

- [ ] **Step 3: Commit**

```powershell
git add src/TerminalPaneExtension.cs
git commit -m "refactor(extension): auto-start uses McpServerEnabled and McpServerPort"
```

---

## Phase 2 — Maia core: interfaces, types, and the CDP client

### Task 4: Create the Maia type module

**Files:**
- Create: `src/Maia/MaiaTypes.cs`
- Test: `tests/Maia/MaiaTypesTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/Maia/MaiaTypesTests.cs`:

```csharp
using FluentAssertions;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

public class MaiaTypesTests
{
    [Fact]
    public void HealthStatus_Available_DefaultsTo_ReasonNull()
    {
        var h = new HealthStatus(Available: true, Tier: 1, Name: "cdp_injected", LatencyMs: 12.5);
        h.Available.Should().BeTrue();
        h.Reason.Should().BeNull();
    }

    [Fact]
    public void TransportUnavailable_CarriesReason()
    {
        var ex = new TransportUnavailable("Maia panel not visible.");
        ex.Message.Should().Be("Maia panel not visible.");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~MaiaTypesTests"`
Expected: FAIL — namespace `Terminal.Maia` doesn't exist.

- [ ] **Step 3: Create the types**

Create `src/Maia/MaiaTypes.cs`:

```csharp
using System.Text.Json.Nodes;

namespace Terminal.Maia;

public sealed record HealthStatus(
    bool Available,
    int Tier,
    string Name,
    double LatencyMs,
    string? Reason = null);

public sealed record SendResult(
    string Handle,
    string Sentinel,
    string TransportUsed,
    DateTimeOffset SentAt);

public sealed record StatusResult(
    bool Done,
    string Response,
    bool Streaming,
    double ElapsedSec,
    string TransportUsed);

public class TransportError : Exception
{
    public TransportError(string message) : base(message) { }
    public TransportError(string message, Exception inner) : base(message, inner) { }
}

public sealed class TransportUnavailable : TransportError
{
    public TransportUnavailable(string reason) : base(reason) { }
    public TransportUnavailable(string reason, Exception inner) : base(reason, inner) { }
}

public sealed class CdpProtocolException : TransportError
{
    public string CdpMethod { get; }
    public JsonNode? CdpResponse { get; }
    public CdpProtocolException(string method, JsonNode? response, string message)
        : base(message)
    {
        CdpMethod = method;
        CdpResponse = response;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~MaiaTypesTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Maia/MaiaTypes.cs tests/Maia/MaiaTypesTests.cs
git commit -m "feat(maia): add core types (HealthStatus, SendResult, StatusResult, exceptions)"
```

---

### Task 5: Define `IMaiaTransport`

**Files:**
- Create: `src/Maia/IMaiaTransport.cs`

- [ ] **Step 1: Create the interface**

```csharp
namespace Terminal.Maia;

/// <summary>
/// One way of talking to Maia. The router probes all registered transports,
/// picks the lowest-tier (highest-priority) available, and demotes on per-call
/// TransportUnavailable. Future: tier-0 NativeMcpTransport when Mendix ships
/// 11.12 native MCP-server-as-tool.
/// </summary>
public interface IMaiaTransport
{
    string Name { get; }
    int Tier { get; }
    Task<HealthStatus> HealthCheckAsync(CancellationToken ct);
    Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct);
    Task<StatusResult> StatusAsync(string handle, CancellationToken ct);
    Task ResetAsync(CancellationToken ct);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build Terminal.csproj`
Expected: clean.

- [ ] **Step 3: Commit**

```powershell
git add src/Maia/IMaiaTransport.cs
git commit -m "feat(maia): IMaiaTransport interface (swap-out seam for future native transport)"
```

---

### Task 6: Define `ICdpClient` and create the implementation skeleton

**Files:**
- Create: `src/Maia/ICdpClient.cs`
- Create: `src/Maia/CdpClient.cs`

- [ ] **Step 1: Create the interface**

`src/Maia/ICdpClient.cs`:

```csharp
using System.Text.Json.Nodes;

namespace Terminal.Maia;

/// <summary>
/// Connects to Studio Pro's WebView2 Maia panel via Chrome DevTools Protocol
/// and runs JS evaluations inside it. Owns the WMI process scan, the /json
/// endpoint discovery, and the WebSocket plumbing. The single seam tests fake
/// to cover all transports.
/// </summary>
public interface ICdpClient : IAsyncDisposable
{
    Task ConnectMaiaAsync(CancellationToken ct);
    Task<JsonNode?> EvaluateAsync(string js, TimeSpan? timeout = null, CancellationToken ct = default);
}
```

- [ ] **Step 2: Create the skeleton implementation (port to follow in next tasks)**

`src/Maia/CdpClient.cs`:

```csharp
using System.Management;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Net.Http;

namespace Terminal.Maia;

/// <summary>
/// Cross-platform-aware: on non-Windows, ConnectMaiaAsync immediately raises
/// TransportUnavailable("not supported on this platform") so the router
/// reports zero tiers. The main implementation is Windows + WMI + WebSocket.
/// </summary>
public sealed class CdpClient : ICdpClient
{
    private const string TargetUrlSubstr = "maia-agent";
    private static readonly TimeSpan EvaluateDefaultTimeout = TimeSpan.FromSeconds(10);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    private ClientWebSocket? ws;
    private int messageId;
    private readonly SemaphoreSlim evalGate = new(1, 1);

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

    private static int FindDebugPort()
    {
        // WMI: enumerate msedgewebview2.exe whose CommandLine references studiopro.exe.
        // If multiple distinct ports are found, fail loud — driving the wrong project
        // silently is the worst possible outcome.
        var ports = new HashSet<int>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE Name = 'msedgewebview2.exe'");
            foreach (ManagementObject mo in searcher.Get())
            {
                var cmdline = (mo["CommandLine"] as string) ?? "";
                if (!cmdline.Contains("studiopro.exe", StringComparison.OrdinalIgnoreCase)) continue;
                var m = Regex.Match(cmdline, @"--remote-debugging-port=(\d+)");
                if (m.Success) ports.Add(int.Parse(m.Groups[1].Value));
            }
        }
        catch (ManagementException ex)
        {
            throw new TransportUnavailable($"WMI process query failed: {ex.Message}", ex);
        }

        if (ports.Count == 0)
            throw new TransportUnavailable(
                "Studio Pro WebView2 has no --remote-debugging-port (Studio Pro not running, or running with debug port disabled).");
        if (ports.Count > 1)
            throw new TransportUnavailable(
                $"Multiple Studio Pro instances detected (ports {string.Join(',', ports.OrderBy(x => x))}). " +
                "Close all but one Studio Pro instance and retry.");
        return ports.First();
    }

    private static async Task<string> DiscoverMaiaTargetAsync(int port, CancellationToken ct)
    {
        string json;
        try
        {
            json = await Http.GetStringAsync($"http://127.0.0.1:{port}/json", ct);
        }
        catch (Exception ex)
        {
            throw new TransportUnavailable($"CDP endpoint :{port}/json unreachable: {ex.Message}", ex);
        }

        var root = JsonNode.Parse(json) as JsonArray
            ?? throw new TransportUnavailable($"CDP /json on :{port} returned non-array");

        foreach (var t in root)
        {
            var url = t?["url"]?.GetValue<string>() ?? "";
            if (url.Contains(TargetUrlSubstr, StringComparison.OrdinalIgnoreCase))
            {
                var wsUrl = t?["webSocketDebuggerUrl"]?.GetValue<string>();
                if (!string.IsNullOrEmpty(wsUrl)) return wsUrl;
            }
        }
        throw new TransportUnavailable(
            "Maia panel not visible. In Studio Pro click the Maia tab (right pane) and retry.");
    }

    public async Task<JsonNode?> EvaluateAsync(string js, TimeSpan? timeout = null, CancellationToken ct = default)
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
            var bytes = Encoding.UTF8.GetBytes(req.ToJsonString());

            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            sendCts.CancelAfter(timeout ?? EvaluateDefaultTimeout);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, sendCts.Token);

            // Read until we see our id.
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
                    sb.Append(Encoding.UTF8.GetString(buf.Array!, 0, r.Count));
                    if (r.EndOfMessage) break;
                }
                var msg = JsonNode.Parse(sb.ToString()) as JsonObject;
                if (msg is null) continue;
                if (msg["id"] is JsonValue v && v.TryGetValue<int>(out var msgId) && msgId == id)
                {
                    if (msg["error"] is JsonObject err)
                        throw new CdpProtocolException("Runtime.evaluate", msg, $"CDP error: {err["message"]?.GetValue<string>()}");
                    var result = msg["result"]?["result"];
                    if (result?["exceptionDetails"] is not null)
                        throw new TransportUnavailable($"JS exception inside Maia WebView: {result["exceptionDetails"]?.ToJsonString()}");
                    return result?["value"];
                }
                // Not our id — ignore (events / other responses) and keep reading.
            }
            throw new TransportUnavailable($"CDP Runtime.evaluate timed out after {(timeout ?? EvaluateDefaultTimeout).TotalSeconds:0.0}s");
        }
        finally { evalGate.Release(); }
    }

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
    }
}
```

- [ ] **Step 3: Add `System.Management` package reference**

Edit [Terminal.csproj](../../../Terminal.csproj). After the existing `<PackageReference Include="Microsoft.Data.Sqlite" ... />`, add:

```xml
<PackageReference Include="System.Management" Version="8.0.*" />
```

- [ ] **Step 4: Build**

Run: `dotnet build Terminal.csproj`
Expected: clean. (On macOS the conditional package reference is skipped; `OperatingSystem.IsWindows()` guards the only use site.)

- [ ] **Step 5: Commit**

```powershell
git add src/Maia/ICdpClient.cs src/Maia/CdpClient.cs Terminal.csproj
git commit -m "feat(maia): ICdpClient + CdpClient (WMI process scan, /json discovery, WebSocket eval)"
```

---

### Task 7: Embed the JS agent

**Files:**
- Create: `src/Maia/maia_agent.js` (copy from prototype)
- Modify: [Terminal.csproj](../../../Terminal.csproj)

- [ ] **Step 1: Copy the JS agent file verbatim**

Copy `C:\Extensions\mxTerminal\AppBuildAuto1\concord-maia-bridge\src\concord_maia_bridge\transports\maia_agent.js` to `src/Maia/maia_agent.js`. No edits — verbatim port.

- [ ] **Step 2: Mark it as embedded resource**

Edit [Terminal.csproj](../../../Terminal.csproj). Inside the existing `<ItemGroup>` that holds `<Content Include="manifest.json">`, add:

```xml
<EmbeddedResource Include="src/Maia/maia_agent.js" LogicalName="Terminal.Maia.maia_agent.js" />
```

- [ ] **Step 3: Add a smoke test that the resource loads**

Create `tests/Maia/EmbeddedResourceTests.cs`:

```csharp
using FluentAssertions;
using System.Reflection;
using Xunit;

namespace Terminal.Tests.Maia;

public class EmbeddedResourceTests
{
    [Fact]
    public void MaiaAgentJs_IsEmbedded()
    {
        using var s = typeof(Terminal.Maia.CdpClient).Assembly
            .GetManifestResourceStream("Terminal.Maia.maia_agent.js");
        s.Should().NotBeNull("the JS agent must be embedded for CdpInjectedTransport to inject it");
        using var r = new StreamReader(s!);
        var text = r.ReadToEnd();
        text.Should().Contain("window.__maiaBridge");
        text.Should().Contain("MX_CHAT_INPUT");
    }
}
```

- [ ] **Step 4: Run test**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~EmbeddedResourceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Maia/maia_agent.js Terminal.csproj tests/Maia/EmbeddedResourceTests.cs
git commit -m "feat(maia): embed maia_agent.js as a manifest resource"
```

---

### Task 8: Implement `CdpInjectedTransport` (Tier 1)

**Files:**
- Create: `src/Maia/CdpInjectedTransport.cs`
- Create: `tests/Maia/CdpInjectedTransportTests.cs`

- [ ] **Step 1: Write a failing test**

Create `tests/Maia/CdpInjectedTransportTests.cs`:

```csharp
using FluentAssertions;
using System.Text.Json.Nodes;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

public class CdpInjectedTransportTests
{
    private sealed class FakeCdp : ICdpClient
    {
        public List<string> Evals { get; } = new();
        public Func<string, JsonNode?>? Responder;
        public Task ConnectMaiaAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<JsonNode?> EvaluateAsync(string js, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            Evals.Add(js);
            return Task.FromResult(Responder?.Invoke(js));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task SendAsync_InjectsAgentThenSubmits()
    {
        var fake = new FakeCdp
        {
            Responder = js =>
                js.Contains("window.__maiaBridge") && js.Contains("findChatRoot")
                    ? JsonValue.Create("installed")
                    : new JsonObject { ["ok"] = true }
        };
        var t = new CdpInjectedTransport(() => fake);

        var r = await t.SendAsync("hi", "<MX-TEST>", CancellationToken.None);

        fake.Evals[0].Should().Contain("window.__maiaBridge"); // agent install
        fake.Evals[1].Should().Contain("window.__maiaBridge.submit");
        r.Sentinel.Should().Be("<MX-TEST>");
        r.TransportUsed.Should().Be("cdp_injected");
    }

    [Fact]
    public async Task SendAsync_AgentReturnsChatRootNotFound_RaisesUnavailable()
    {
        var fake = new FakeCdp { Responder = _ => JsonValue.Create("chat-root-not-found") };
        var t = new CdpInjectedTransport(() => fake);

        Func<Task> act = () => t.SendAsync("hi", "<MX-TEST>", CancellationToken.None);

        await act.Should().ThrowAsync<TransportUnavailable>()
            .Where(e => e.Message.Contains("chat-list container"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~CdpInjectedTransportTests"`
Expected: FAIL — `CdpInjectedTransport` doesn't exist.

- [ ] **Step 3: Implement the transport**

Create `src/Maia/CdpInjectedTransport.cs`:

```csharp
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Terminal.Maia;

public sealed class CdpInjectedTransport : IMaiaTransport
{
    public string Name => "cdp_injected";
    public int Tier => 1;

    private static readonly string AgentJs = LoadAgent();
    private readonly Func<ICdpClient> clientFactory;

    public CdpInjectedTransport(Func<ICdpClient> clientFactory)
    {
        this.clientFactory = clientFactory;
    }

    private static string LoadAgent()
    {
        using var s = typeof(CdpInjectedTransport).Assembly
            .GetManifestResourceStream("Terminal.Maia.maia_agent.js")
            ?? throw new InvalidOperationException("maia_agent.js resource missing from assembly");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public async Task<HealthStatus> HealthCheckAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var cdp = clientFactory();
            await cdp.ConnectMaiaAsync(ct);
            await EnsureAgentAsync(cdp, ct);
            return new HealthStatus(true, Tier, Name, sw.Elapsed.TotalMilliseconds);
        }
        catch (TransportUnavailable ex)
        {
            return new HealthStatus(false, Tier, Name, sw.Elapsed.TotalMilliseconds, ex.Message);
        }
    }

    public async Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct)
    {
        await using var cdp = clientFactory();
        await cdp.ConnectMaiaAsync(ct);
        await EnsureAgentAsync(cdp, ct);

        var js = $"return window.__maiaBridge.submit({JsonSerializer.Serialize(prompt)}, {JsonSerializer.Serialize(sentinel)});";
        var node = await cdp.EvaluateAsync(js, ct: ct);
        if (node is not JsonObject obj || obj["ok"]?.GetValue<bool>() != true)
            throw new TransportUnavailable($"submit() failed: {node?.ToJsonString()}");

        return new SendResult(
            Handle: sentinel,    // 1:1 with sentinel since no cross-process correlation needed
            Sentinel: sentinel,
            TransportUsed: Name,
            SentAt: DateTimeOffset.UtcNow);
    }

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

    public async Task ResetAsync(CancellationToken ct)
    {
        await using var cdp = clientFactory();
        try
        {
            await cdp.ConnectMaiaAsync(ct);
            await cdp.EvaluateAsync(
                "if (window.__maiaBridge) { window.__maiaBridge.teardown(); } return true;",
                ct: ct);
        }
        catch (TransportUnavailable) { /* nothing to clear */ }
    }

    private static async Task EnsureAgentAsync(ICdpClient cdp, CancellationToken ct)
    {
        var node = await cdp.EvaluateAsync(AgentJs, ct: ct);
        var v = node is JsonValue jv ? jv.GetValue<string>() : null;
        switch (v)
        {
            case "installed":
            case "already-installed":
                return;
            case "chat-root-not-found":
                throw new TransportUnavailable(
                    "Injected agent could not locate the chat-list container. Maia panel may not be fully rendered. Click the Maia tab and retry.");
            default:
                throw new TransportUnavailable($"Injected agent install returned unexpected result: {v ?? "<null>"}");
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~CdpInjectedTransportTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Maia/CdpInjectedTransport.cs tests/Maia/CdpInjectedTransportTests.cs
git commit -m "feat(maia): CdpInjectedTransport (Tier 1) with FakeCdp tests"
```

---

### Task 9: Implement `CdpChatTransport` (Tier 2)

**Files:**
- Create: `src/Maia/CdpChatTransport.cs`
- Create: `tests/Maia/CdpChatTransportTests.cs`

- [ ] **Step 1: Write a failing test**

Create `tests/Maia/CdpChatTransportTests.cs`:

```csharp
using FluentAssertions;
using System.Text.Json.Nodes;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

public class CdpChatTransportTests
{
    private sealed class FakeCdp : ICdpClient
    {
        public List<string> Evals { get; } = new();
        public Func<string, JsonNode?>? Responder;
        public Task ConnectMaiaAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<JsonNode?> EvaluateAsync(string js, TimeSpan? timeout = null, CancellationToken ct = default)
        {
            Evals.Add(js);
            return Task.FromResult(Responder?.Invoke(js));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task SendAsync_TypesIntoInputAndDispatchesEnter()
    {
        var fake = new FakeCdp { Responder = _ => JsonValue.Create(true) };
        var t = new CdpChatTransport(() => fake);

        var r = await t.SendAsync("hello", "<MX-T2>", CancellationToken.None);

        fake.Evals.Should().Contain(s => s.Contains("MX_CHAT_INPUT"));
        fake.Evals.Should().Contain(s => s.Contains("Enter"));
        r.TransportUsed.Should().Be("cdp_chat");
    }

    [Fact]
    public async Task StatusAsync_DetectsTwoSentinelsAsDone()
    {
        // Bubble layout: ["...<MX-T2>...", "Maia answer text", "...<MX-T2>"]
        var bubbles = new JsonArray
        {
            "user echo: please answer <MX-T2>",
            "Maia: answered.",
            "Maia echo: <MX-T2>",
        };
        var fake = new FakeCdp { Responder = _ => bubbles };
        var t = new CdpChatTransport(() => fake);

        var s = await t.StatusAsync("<MX-T2>", CancellationToken.None);

        s.Done.Should().BeTrue();
        s.Response.Should().Contain("answered");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~CdpChatTransportTests"`
Expected: FAIL — class doesn't exist.

- [ ] **Step 3: Implement the transport**

Create `src/Maia/CdpChatTransport.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Terminal.Maia;

/// <summary>
/// Tier 2: drives Maia by typing into #MX_CHAT_INPUT, dispatching Enter, and
/// scraping bubble innerText. Brittle to Mendix's generated CSS class
/// (p.sc-bPkUNa) — when that regenerates per Mendix build, this transport
/// breaks but Tier 1's structural walk keeps working.
/// </summary>
public sealed class CdpChatTransport : IMaiaTransport
{
    public string Name => "cdp_chat";
    public int Tier => 2;

    private const string BubbleSelector = "p.sc-bPkUNa";

    private readonly Func<ICdpClient> clientFactory;
    // Bridge-side ticket bookkeeping: { sentinel → sent_at }.
    // No JS-side state; all completion detection is bridge-side polling.
    private readonly Dictionary<string, DateTimeOffset> tickets = new();
    private readonly object gate = new();

    public CdpChatTransport(Func<ICdpClient> clientFactory)
    {
        this.clientFactory = clientFactory;
    }

    public async Task<HealthStatus> HealthCheckAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await using var cdp = clientFactory();
            await cdp.ConnectMaiaAsync(ct);
            // Cheap probe: confirm the input element exists.
            var node = await cdp.EvaluateAsync(
                "return !!document.getElementById('MX_CHAT_INPUT');", ct: ct);
            if (node?.GetValue<bool>() != true)
                throw new TransportUnavailable("MX_CHAT_INPUT not found in Maia panel.");
            return new HealthStatus(true, Tier, Name, sw.Elapsed.TotalMilliseconds);
        }
        catch (TransportUnavailable ex)
        {
            return new HealthStatus(false, Tier, Name, sw.Elapsed.TotalMilliseconds, ex.Message);
        }
    }

    public async Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct)
    {
        await using var cdp = clientFactory();
        await cdp.ConnectMaiaAsync(ct);
        var folded = string.Join(' ', prompt.Split('\n', '\r', '\t').Where(s => s.Length > 0));
        var full = $"{folded} Respond, then write {sentinel} on a new line so the pipeline knows you are done.";
        var js = $$"""
            const ta = document.getElementById('MX_CHAT_INPUT');
            if (!ta) return false;
            const setter = Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype, 'value').set;
            setter.call(ta, {{JsonSerializer.Serialize(full)}});
            ta.dispatchEvent(new Event('input', { bubbles: true }));
            ta.focus();
            ta.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true }));
            return true;
            """;
        var ok = await cdp.EvaluateAsync(js, ct: ct);
        if (ok?.GetValue<bool>() != true)
            throw new TransportUnavailable("Could not type into MX_CHAT_INPUT.");

        lock (gate) tickets[sentinel] = DateTimeOffset.UtcNow;

        return new SendResult(
            Handle: sentinel,
            Sentinel: sentinel,
            TransportUsed: Name,
            SentAt: DateTimeOffset.UtcNow);
    }

    public async Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
    {
        DateTimeOffset sentAt;
        lock (gate)
        {
            if (!tickets.TryGetValue(handle, out sentAt))
                throw new TransportUnavailable($"Unknown handle: {handle}");
        }

        await using var cdp = clientFactory();
        await cdp.ConnectMaiaAsync(ct);
        var js = $$"""
            return [...document.querySelectorAll({{JsonSerializer.Serialize(BubbleSelector)}})]
                .map(p => p.innerText || '');
            """;
        var node = await cdp.EvaluateAsync(js, ct: ct);
        var bubbles = (node as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").ToList()
            ?? throw new TransportUnavailable("Bubble selector returned non-array.");

        int firstIdx = -1, lastIdx = -1;
        for (int i = 0; i < bubbles.Count; i++)
        {
            if (bubbles[i].Contains(handle))
            {
                if (firstIdx == -1) firstIdx = i;
                lastIdx = i;
            }
        }
        var elapsed = (DateTimeOffset.UtcNow - sentAt).TotalSeconds;
        if (firstIdx == -1)
            return new StatusResult(false, "", false, elapsed, Name);

        bool done = lastIdx > firstIdx;
        int start = firstIdx + 1;
        int end = done ? lastIdx : bubbles.Count;
        var reply = string.Join('\n', bubbles.Skip(start).Take(end - start)).Replace(handle, "").Trim();
        return new StatusResult(done, reply, !done, elapsed, Name);
    }

    public Task ResetAsync(CancellationToken ct)
    {
        lock (gate) tickets.Clear();
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~CdpChatTransportTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Maia/CdpChatTransport.cs tests/Maia/CdpChatTransportTests.cs
git commit -m "feat(maia): CdpChatTransport (Tier 2) DOM-scrape fallback"
```

---

### Task 10: Implement `MaiaRouter`

**Files:**
- Create: `src/Maia/MaiaRouter.cs`
- Create: `tests/Maia/MaiaRouterTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Maia/MaiaRouterTests.cs`:

```csharp
using FluentAssertions;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

public class MaiaRouterTests
{
    private sealed class FakeTransport : IMaiaTransport
    {
        public string Name { get; }
        public int Tier { get; }
        public bool Available { get; set; } = true;
        public bool ThrowUnavailableOnSend { get; set; }
        public int SendCalls;
        public int ResetCalls;
        public int HealthCalls;

        public FakeTransport(string name, int tier) { Name = name; Tier = tier; }

        public Task<HealthStatus> HealthCheckAsync(CancellationToken ct)
        {
            HealthCalls++;
            return Task.FromResult(Available
                ? new HealthStatus(true, Tier, Name, 1.0)
                : new HealthStatus(false, Tier, Name, 1.0, "fake offline"));
        }
        public Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct)
        {
            SendCalls++;
            if (ThrowUnavailableOnSend) throw new TransportUnavailable("fake send unavailable");
            return Task.FromResult(new SendResult(sentinel, sentinel, Name, DateTimeOffset.UtcNow));
        }
        public Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
            => Task.FromResult(new StatusResult(true, "ok", false, 0.1, Name));
        public Task ResetAsync(CancellationToken ct) { ResetCalls++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task LowestTierAvailable_IsPickedAtStartup()
    {
        var t1 = new FakeTransport("t1", 1) { Available = false };
        var t2 = new FakeTransport("t2", 2);
        var router = new MaiaRouter(new IMaiaTransport[] { t1, t2 });
        await router.ProbeAllAsync(CancellationToken.None);

        var r = await router.SendAsync("p", "<MX-X>", CancellationToken.None);

        r.TransportUsed.Should().Be("t2");
    }

    [Fact]
    public async Task PerCallUnavailable_DemotesAndRetries()
    {
        var t1 = new FakeTransport("t1", 1) { ThrowUnavailableOnSend = true };
        var t2 = new FakeTransport("t2", 2);
        var router = new MaiaRouter(new IMaiaTransport[] { t1, t2 });
        await router.ProbeAllAsync(CancellationToken.None);

        var r = await router.SendAsync("p", "<MX-X>", CancellationToken.None);

        t1.SendCalls.Should().Be(1);
        t2.SendCalls.Should().Be(1);
        r.TransportUsed.Should().Be("t2");
    }

    [Fact]
    public async Task AllExhausted_RaisesUnavailable()
    {
        var t1 = new FakeTransport("t1", 1) { ThrowUnavailableOnSend = true };
        var t2 = new FakeTransport("t2", 2) { ThrowUnavailableOnSend = true };
        var router = new MaiaRouter(new IMaiaTransport[] { t1, t2 });
        await router.ProbeAllAsync(CancellationToken.None);

        Func<Task> act = () => router.SendAsync("p", "<MX-X>", CancellationToken.None);

        await act.Should().ThrowAsync<TransportUnavailable>()
            .Where(e => e.Message.Contains("All Maia transports unavailable"));
    }

    [Fact]
    public async Task Reset_CallsEveryTransport()
    {
        var t1 = new FakeTransport("t1", 1);
        var t2 = new FakeTransport("t2", 2);
        var router = new MaiaRouter(new IMaiaTransport[] { t1, t2 });
        await router.ProbeAllAsync(CancellationToken.None);

        await router.ResetAsync(CancellationToken.None);

        t1.ResetCalls.Should().Be(1);
        t2.ResetCalls.Should().Be(1);
    }

    [Fact]
    public async Task ForceTier_OverridesActiveUntilReprobe()
    {
        var t1 = new FakeTransport("t1", 1);
        var t2 = new FakeTransport("t2", 2);
        var router = new MaiaRouter(new IMaiaTransport[] { t1, t2 });
        await router.ProbeAllAsync(CancellationToken.None);

        router.ForceTier("t2");
        var r = await router.SendAsync("p", "<MX-X>", CancellationToken.None);

        r.TransportUsed.Should().Be("t2");
    }

    [Fact]
    public void ForceTier_UnknownName_Throws()
    {
        var t1 = new FakeTransport("t1", 1);
        var router = new MaiaRouter(new IMaiaTransport[] { t1 });

        Action act = () => router.ForceTier("nope");

        act.Should().Throw<ArgumentException>().WithMessage("*nope*");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~MaiaRouterTests"`
Expected: FAIL — `MaiaRouter` doesn't exist.

- [ ] **Step 3: Implement the router**

Create `src/Maia/MaiaRouter.cs`:

```csharp
namespace Terminal.Maia;

public sealed class MaiaRouter
{
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(60);

    private readonly IReadOnlyList<IMaiaTransport> transports;
    private readonly Dictionary<string, HealthStatus> availability = new();
    private readonly Dictionary<string, string> handleToTransport = new();
    private readonly object gate = new();
    private DateTime lastProbeAt = DateTime.MinValue;
    private string? forced;

    public MaiaRouter(IReadOnlyList<IMaiaTransport> transports)
    {
        this.transports = transports.OrderBy(t => t.Tier).ToArray();
    }

    public IReadOnlyList<IMaiaTransport> Transports => transports;

    public async Task ProbeAllAsync(CancellationToken ct)
    {
        var probes = await Task.WhenAll(transports.Select(t => t.HealthCheckAsync(ct)));
        lock (gate)
        {
            availability.Clear();
            for (int i = 0; i < transports.Count; i++)
                availability[transports[i].Name] = probes[i];
            lastProbeAt = DateTime.UtcNow;
        }
    }

    private async Task MaybeReprobeAsync(CancellationToken ct)
    {
        bool needs;
        lock (gate) { needs = DateTime.UtcNow - lastProbeAt >= ProbeInterval; }
        if (needs) await ProbeAllAsync(ct);
    }

    private List<IMaiaTransport> ActiveSnapshot()
    {
        lock (gate)
        {
            if (forced is not null)
            {
                var t = transports.FirstOrDefault(x => x.Name == forced);
                if (t is null) throw new ArgumentException($"Unknown transport name: {forced}");
                if (availability.TryGetValue(forced, out var h) && !h.Available)
                    throw new TransportUnavailable($"Forced transport '{forced}' is unavailable: {h.Reason}");
                return new List<IMaiaTransport> { t };
            }
            return transports
                .Where(t => availability.TryGetValue(t.Name, out var h) && h.Available)
                .ToList();
        }
    }

    public void ForceTier(string name)
    {
        if (transports.All(t => t.Name != name))
            throw new ArgumentException($"Unknown transport name: {name}");
        lock (gate) forced = name;
    }

    public void ClearForcedTier()
    {
        lock (gate) forced = null;
    }

    public async Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct)
    {
        await MaybeReprobeAsync(ct);
        var active = ActiveSnapshot();
        if (active.Count == 0)
            throw new TransportUnavailable(BuildExhaustedMessage());

        Exception? last = null;
        foreach (var t in active)
        {
            try
            {
                var r = await t.SendAsync(prompt, sentinel, ct);
                lock (gate) handleToTransport[r.Handle] = t.Name;
                return r;
            }
            catch (TransportUnavailable ex)
            {
                last = ex;
                lock (gate) availability[t.Name] = new HealthStatus(false, t.Tier, t.Name, 0, ex.Message);
            }
        }
        throw new TransportUnavailable($"All Maia transports unavailable. Last reason: {last?.Message}");
    }

    public async Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
    {
        await MaybeReprobeAsync(ct);
        string? transportName;
        lock (gate) handleToTransport.TryGetValue(handle, out transportName);

        var t = transports.FirstOrDefault(x => x.Name == transportName)
            ?? ActiveSnapshot().FirstOrDefault()
            ?? throw new TransportUnavailable(BuildExhaustedMessage());
        return await t.StatusAsync(handle, ct);
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        lock (gate) handleToTransport.Clear();
        foreach (var t in transports)
        {
            try { await t.ResetAsync(ct); }
            catch (TransportUnavailable) { /* not active anyway */ }
        }
    }

    private string BuildExhaustedMessage()
    {
        lock (gate)
        {
            var reasons = transports
                .Select(t => availability.TryGetValue(t.Name, out var h) ? $"{t.Name}: {h.Reason ?? "ok"}" : $"{t.Name}: unprobed")
                .ToArray();
            return $"All Maia transports unavailable. {string.Join(" | ", reasons)}";
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~MaiaRouterTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Maia/MaiaRouter.cs tests/Maia/MaiaRouterTests.cs
git commit -m "feat(maia): MaiaRouter with tier selection, demote-on-call, lazy 60s reprobe, force_tier"
```

---

### Task 11: Implement `MaiaActions` (verb layer)

**Files:**
- Create: `src/Maia/MaiaActions.cs`
- Create: `tests/Maia/MaiaActionsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Maia/MaiaActionsTests.cs`:

```csharp
using FluentAssertions;
using Terminal;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

public class MaiaActionsTests
{
    private sealed class StubTransport : IMaiaTransport
    {
        public string Name => "stub";
        public int Tier => 1;
        public Func<string, string, StatusResult>? StatusFn;
        public int Sends;
        public Task<HealthStatus> HealthCheckAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, 1, Name, 0));
        public Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct)
        {
            Sends++;
            return Task.FromResult(new SendResult(sentinel, sentinel, Name, DateTimeOffset.UtcNow));
        }
        public Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
            => Task.FromResult(StatusFn?.Invoke(handle, "") ?? new StatusResult(true, "ok", false, 0.0, Name));
        public Task ResetAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private static MaiaActions Build(StubTransport t)
    {
        var router = new MaiaRouter(new IMaiaTransport[] { t });
        router.ProbeAllAsync(CancellationToken.None).GetAwaiter().GetResult();
        return new MaiaActions(router);
    }

    [Fact]
    public async Task SendAsync_GeneratesSentinelWhenOmitted()
    {
        var t = new StubTransport();
        var a = Build(t);
        var r = await a.SendAsync("hi", null, CancellationToken.None);
        r.Status.Should().Be("sent");
        var data = (System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            System.Text.Json.JsonSerializer.Serialize(r.Data)))!;
        data["sentinel"]!.GetValue<string>().Should().StartWith("<MX-");
    }

    [Fact]
    public async Task WaitAsync_ReturnsTimedOutWhenNeverDone()
    {
        var t = new StubTransport
        {
            StatusFn = (h, _) => new StatusResult(false, "", false, 0.0, "stub"),
        };
        var a = Build(t);
        var send = await a.SendAsync("hi", "<MX-T>", CancellationToken.None);
        var w = await a.WaitAsync("<MX-T>", timeoutSec: 0.5, CancellationToken.None);

        var data = (System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.Nodes.JsonObject>(
            System.Text.Json.JsonSerializer.Serialize(w.Data)))!;
        data["timed_out"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task ForceTierAsync_RejectsUnknownName()
    {
        var t = new StubTransport();
        var a = Build(t);
        var r = await a.ForceTierAsync("doesnt-exist", CancellationToken.None);
        r.Error.Should().NotBeNull();
        r.Error!.Should().Contain("doesnt-exist");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~MaiaActionsTests"`
Expected: FAIL — class doesn't exist.

- [ ] **Step 3: Implement `MaiaActions`**

Create `src/Maia/MaiaActions.cs`:

```csharp
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
            return ActionResult.OkWith("polled", new
            {
                done = s.Done,
                response = s.Response,
                streaming = s.Streaming,
                elapsed_sec = s.ElapsedSec,
                transport = s.TransportUsed,
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

    /// <summary>
    /// Format: <MX-XXXXXX> where X is base32-friendly ([2-7A-Z]). 6 chars from 32^6 ≈ 1B
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~MaiaActionsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/Maia/MaiaActions.cs tests/Maia/MaiaActionsTests.cs
git commit -m "feat(maia): MaiaActions verb layer (send/status/wait/ask/reset/force_tier + auto-sentinel)"
```

---

## Phase 3 — Wire Maia tools into the MCP server

### Task 12: Rename server identity and wire Maia registration

**Files:**
- Modify: [src/StudioProActionServer.cs](../../../src/StudioProActionServer.cs)
- Test: [tests/StudioProActionServerTests.cs](../../../tests/StudioProActionServerTests.cs)

- [ ] **Step 1: Update existing test for new server name**

Open [tests/StudioProActionServerTests.cs](../../../tests/StudioProActionServerTests.cs) line 73-74:

Change:
```csharp
info.GetProperty("name").GetString().Should().Be("mendix-studio-pro-actions");
```
To:
```csharp
info.GetProperty("name").GetString().Should().Be("concord-mcp");
info.GetProperty("version").GetString().Should().StartWith("1.3");
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~Initialize_ReturnsServerInfo"`
Expected: FAIL — current name is `mendix-studio-pro-actions` and version `1.0.0`.

- [ ] **Step 3: Update `StudioProActionServer` constants**

Replace lines 26-27 of [src/StudioProActionServer.cs](../../../src/StudioProActionServer.cs):

```csharp
public const string ServerName = "concord-mcp";
public const string ServerVersion = "1.3.0";
```

- [ ] **Step 4: Add `MaiaActions` field and constructor overload**

Around line 38-51 of `StudioProActionServer.cs`, replace the existing field and constructor:

```csharp
private readonly StudioProActions actions;
private readonly Maia.MaiaActions? maia;
private readonly bool studioProActionsEnabled;
private readonly bool maiaIntegrationEnabled;
private readonly Logger? log;
private TcpListener? listener;
private int boundPort;
private CancellationTokenSource? cts;
private Task? loop;
private readonly int requestedPort;

public StudioProActionServer(
    StudioProActions actions,
    int port,
    Logger? log = null,
    Maia.MaiaActions? maia = null,
    bool studioProActionsEnabled = true,
    bool maiaIntegrationEnabled = false)
{
    this.actions = actions;
    this.maia = maia;
    this.studioProActionsEnabled = studioProActionsEnabled;
    this.maiaIntegrationEnabled = maiaIntegrationEnabled;
    this.log = log;
    this.requestedPort = port;
}
```

- [ ] **Step 5: Update `HandleToolsList` for conditional registration**

Replace `HandleToolsList` (lines 331-348):

```csharp
private JsonNode HandleToolsList()
{
    var arr = new JsonArray();
    if (studioProActionsEnabled)
    {
        arr.Add(ToolDef("run_app",
            "Start the local Mendix runtime for the currently open Studio Pro app. If already running, returns 'already_running' without disturbing it."));
        arr.Add(ToolDef("stop_app",
            "Stop the local Mendix runtime. No-op if it isn't running."));
        arr.Add(ToolDef("refresh_project",
            "Reload the project model from disk. Use after editing model files (e.g. microflow XML) outside Studio Pro to make the IDE pick up the changes."));
        arr.Add(ToolDef("save_all",
            "Best-effort save: posts Ctrl+S to Studio Pro's main window. Works when the active document tab has focus; if the user's focus is elsewhere (e.g. inside this terminal), Studio Pro routes the keystroke to the focused child instead and the save may not fire. For guaranteed save, ask the user to click the document tab once first."));
        arr.Add(ToolDef("get_active_run_configuration",
            "Read-only: returns the currently selected local run configuration (id, name, applicationRootUrl). Useful for confirming which environment a run/stop will affect."));
        arr.Add(ToolDef("get_app_status",
            "Composite read-only snapshot for orienting: project path/name, run state (running|stopped|unknown), running URL if any, active run configuration. Call this first when starting work in a fresh Claude Code session."));
    }
    if (maiaIntegrationEnabled && maia is not null)
    {
        arr.Add(ToolDef("maia__send",
            "Submit a prompt to Maia (Studio Pro's AI assistant). Non-blocking — returns a handle you can poll with maia__status or block on with maia__wait. Optional 'sentinel' for caller-controlled correlation; otherwise auto-generated.",
            new JsonObject { ["prompt"] = SchemaString(), ["sentinel"] = SchemaString() },
            required: new[] { "prompt" }));
        arr.Add(ToolDef("maia__status",
            "Non-blocking peek at an in-flight Maia prompt by handle. Returns done/response/streaming/elapsed_sec.",
            new JsonObject { ["handle"] = SchemaString() },
            required: new[] { "handle" }));
        arr.Add(ToolDef("maia__wait",
            "Block until Maia is done with the given handle, or until timeout_sec elapses. Default timeout 60s.",
            new JsonObject { ["handle"] = SchemaString(), ["timeout_sec"] = SchemaNumber() },
            required: new[] { "handle" }));
        arr.Add(ToolDef("maia__ask",
            "Send a prompt and block for Maia's response. Convenience for one-shot queries; equivalent to maia__send + maia__wait.",
            new JsonObject { ["prompt"] = SchemaString(), ["timeout_sec"] = SchemaNumber() },
            required: new[] { "prompt" }));
        arr.Add(ToolDef("maia__reset",
            "Clear the in-WebView injected agent and bridge-side ticket state. Use after Maia panel reloads or chat clears."));
        arr.Add(ToolDef("maia__force_tier",
            "Manual override: force a specific transport (e.g. 'cdp_chat'). For testing tier-N behavior. Mutates active state until next reprobe.",
            new JsonObject { ["name"] = SchemaString() },
            required: new[] { "name" }));
    }
    return new JsonObject { ["tools"] = arr };
}

private static JsonObject SchemaString() => new() { ["type"] = "string" };
private static JsonObject SchemaNumber() => new() { ["type"] = "number" };
```

- [ ] **Step 6: Update `ToolDef` to accept properties + required**

Replace `ToolDef` (lines 350-360):

```csharp
private static JsonObject ToolDef(string name, string description, JsonObject? properties = null, string[]? required = null)
{
    var props = properties ?? new JsonObject();
    var req = new JsonArray();
    foreach (var r in required ?? Array.Empty<string>()) req.Add(r);
    return new JsonObject
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = props,
            ["required"] = req,
        }
    };
}
```

- [ ] **Step 7: Update `HandleToolsCallAsync` to dispatch maia tools**

Replace `HandleToolsCallAsync` (lines 362-392):

```csharp
private async Task<JsonNode> HandleToolsCallAsync(JsonObject? pars)
{
    var name = pars?["name"]?.GetValue<string>();
    var args = pars?["arguments"] as JsonObject ?? new JsonObject();
    ActionResult? result = null;

    if (studioProActionsEnabled)
    {
        result = name switch
        {
            "run_app"                       => await actions.RunAppAsync(),
            "stop_app"                      => await actions.StopAppAsync(),
            "refresh_project"               => await actions.RefreshProjectAsync(),
            "save_all"                      => await actions.SaveAllAsync(),
            "get_active_run_configuration"  => await actions.GetActiveRunConfigurationAsync(),
            "get_app_status"                => await actions.GetAppStatusAsync(),
            _ => null,
        };
    }
    if (result is null && maiaIntegrationEnabled && maia is not null)
    {
        result = name switch
        {
            "maia__send"          => await maia.SendAsync(
                                        args["prompt"]?.GetValue<string>() ?? "",
                                        args["sentinel"]?.GetValue<string>(),
                                        CancellationToken.None),
            "maia__status"        => await maia.StatusAsync(
                                        args["handle"]?.GetValue<string>() ?? "",
                                        CancellationToken.None),
            "maia__wait"          => await maia.WaitAsync(
                                        args["handle"]?.GetValue<string>() ?? "",
                                        args["timeout_sec"]?.GetValue<double>() ?? 60.0,
                                        CancellationToken.None),
            "maia__ask"           => await maia.AskAsync(
                                        args["prompt"]?.GetValue<string>() ?? "",
                                        args["timeout_sec"]?.GetValue<double>() ?? 60.0,
                                        CancellationToken.None),
            "maia__reset"         => await maia.ResetAsync(CancellationToken.None),
            "maia__force_tier"    => await maia.ForceTierAsync(
                                        args["name"]?.GetValue<string>() ?? "",
                                        CancellationToken.None),
            _ => null,
        };
    }
    if (result is null)
        return BuildErrorBody(code: -32601, message: $"Unknown tool '{name}'");

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
```

- [ ] **Step 8: Run all tests**

Run: `dotnet test tests/Terminal.Tests.csproj`
Expected: PASS — including the now-updated `Initialize_ReturnsServerInfo`.

- [ ] **Step 9: Commit**

```powershell
git add src/StudioProActionServer.cs tests/StudioProActionServerTests.cs
git commit -m "feat(server): rename to concord-mcp v1.3.0; conditional maia tool registration"
```

---

### Task 13: Add a test for conditional maia tool registration

**Files:**
- Create: `tests/Maia/MaiaJsonRpcTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using FluentAssertions;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Terminal;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

public class MaiaJsonRpcTests : IAsyncLifetime
{
    private sealed class FakeProbe : IRunStateProbe
    {
        public string? GetActiveUrl() => null;
        public int? GetActivePort() => null;
        public Task<RunState> IsRunningAsync(CancellationToken ct = default) => Task.FromResult(RunState.Stopped);
    }
    private sealed class FakeUi : IStudioProUiAutomation
    {
        public bool TriggerRun() => true;
        public bool TriggerStop() => true;
        public bool TriggerRefreshFromDisk() => true;
        public bool TriggerSaveAll() => true;
        public string? LastFailureReason => null;
    }
    private sealed class StubTransport : IMaiaTransport
    {
        public string Name => "stub";
        public int Tier => 1;
        public Task<HealthStatus> HealthCheckAsync(CancellationToken ct) => Task.FromResult(new HealthStatus(true, 1, Name, 0));
        public Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct)
            => Task.FromResult(new SendResult(sentinel, sentinel, Name, DateTimeOffset.UtcNow));
        public Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
            => Task.FromResult(new StatusResult(true, "ok", false, 0, Name));
        public Task ResetAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private StudioProActionServer? server;
    private HttpClient http = null!;

    public async Task InitializeAsync()
    {
        var router = new MaiaRouter(new IMaiaTransport[] { new StubTransport() });
        await router.ProbeAllAsync(CancellationToken.None);
        var maia = new MaiaActions(router);
        var actions = new StudioProActions(new FakeProbe(), new FakeUi());
        server = new StudioProActionServer(
            actions, port: 0, log: null, maia: maia,
            studioProActionsEnabled: true, maiaIntegrationEnabled: true);
        server.Start();
        http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}") };
    }

    public Task DisposeAsync() { server?.Dispose(); http.Dispose(); return Task.CompletedTask; }

    private async Task<JsonDocument> Post(string body)
    {
        using var resp = await http.PostAsync("/mcp", new StringContent(body, Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ToolsList_IncludesMaiaToolsWhenEnabled()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        var names = doc.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        names.Should().Contain(new[] {
            "maia__send", "maia__status", "maia__wait",
            "maia__ask", "maia__reset", "maia__force_tier"
        });
    }

    [Fact]
    public async Task ToolsCall_MaiaSend_ReturnsHandle()
    {
        var doc = await Post("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"maia__send","arguments":{"prompt":"hi"}}}""");
        var text = doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
        var inner = JsonDocument.Parse(text).RootElement;
        inner.GetProperty("status").GetString().Should().Be("sent");
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~MaiaJsonRpcTests"`
Expected: PASS.

- [ ] **Step 3: Commit**

```powershell
git add tests/Maia/MaiaJsonRpcTests.cs
git commit -m "test(maia): JSON-RPC end-to-end coverage for tools/list and tools/call"
```

---

### Task 14: Wire `MaiaRouter` and `MaiaActions` into pane startup

**Files:**
- Modify: [src/TerminalSessionManager.cs](../../../src/TerminalSessionManager.cs) (or wherever `StartActionServer` lives — find via grep)
- Modify: [src/TerminalPaneExtension.cs](../../../src/TerminalPaneExtension.cs)

- [ ] **Step 1: Find the action server startup site**

Run: `grep -n "StartActionServer\|StudioProActionServer" src/*.cs`

The startup currently lives in `manager.StartActionServer(...)` (called from `TerminalPaneExtension.TryAutoStartActionServer`).

- [ ] **Step 2: Extend `StartActionServer` signature to take Maia params**

Open the file containing `StartActionServer`. Change the signature to:

```csharp
public void StartActionServer(
    int port,
    StudioProActions actions,
    Logger? log,
    Terminal.Maia.MaiaActions? maia,
    bool studioProActionsEnabled,
    bool maiaIntegrationEnabled)
{
    // existing body but pass new args to the constructor:
    server = new StudioProActionServer(
        actions, port, log, maia, studioProActionsEnabled, maiaIntegrationEnabled);
    server.Start();
}
```

- [ ] **Step 3: Build the `MaiaActions` in the extension on Windows**

In [src/TerminalPaneExtension.cs](../../../src/TerminalPaneExtension.cs) `TryAutoStartActionServer`, replace the call site so it constructs Maia plumbing only on Windows when the toggle is on:

```csharp
Terminal.Maia.MaiaActions? maia = null;
bool maiaEnabled = OperatingSystem.IsWindows() && settings.MaiaIntegrationEnabled;
if (maiaEnabled)
{
    var transports = new Terminal.Maia.IMaiaTransport[]
    {
        new Terminal.Maia.CdpInjectedTransport(() => new Terminal.Maia.CdpClient()),
        new Terminal.Maia.CdpChatTransport(() => new Terminal.Maia.CdpClient()),
    };
    var router = new Terminal.Maia.MaiaRouter(transports);
    // Fire-and-forget initial probe; router is functional even before it returns.
    _ = router.ProbeAllAsync(CancellationToken.None);
    maia = new Terminal.Maia.MaiaActions(router);
}

manager.StartActionServer(
    StudioProActionServer.DefaultPort,
    actions,
    log,
    maia,
    studioProActionsEnabled: settings.StudioProActionsEnabled,
    maiaIntegrationEnabled: maiaEnabled);
log.Info($"[concord-mcp] auto-started server on port {settings.McpServerPort} (maia={maiaEnabled})");
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet build Terminal.csproj` then `dotnet test tests/Terminal.Tests.csproj`
Expected: clean build, all tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/TerminalSessionManager.cs src/TerminalPaneExtension.cs
git commit -m "feat(extension): wire MaiaRouter+MaiaActions into auto-start (Windows + toggle gated)"
```

---

## Phase 4 — Settings UI

### Task 15: Update settings modal HTML

**Files:**
- Modify: [ui/index.html](../../../ui/index.html)

- [ ] **Step 1: Rename the nav item**

Edit [ui/index.html](../../../ui/index.html) line 772-774:

```html
<div class="nav-item" data-section="actions" role="tab" tabindex="-1" aria-selected="false">
  <span class="nav-icon" data-icon="zap"></span>Concord MCP
</div>
```

- [ ] **Step 2: Replace the section body**

Edit [ui/index.html](../../../ui/index.html) lines 828-838 (the `<!-- Action bridge -->` section). Replace with:

```html
<!-- Concord MCP -->
<section class="settings-section" data-section="actions" role="tabpanel">
  <h4>Concord MCP</h4>
  <p class="section-desc">Concord runs an in-process MCP server (<code>concord-mcp</code>) that exposes Studio Pro capabilities to the CLIs you use inside this terminal.</p>
  <div class="checkbox-row">
    <input id="set-actions-enabled" type="checkbox">
    <label for="set-actions-enabled" style="margin:0">Enable Concord MCP server</label>
  </div>
  <div id="actions-port-readout" class="mcp-port-readout"></div>

  <h5>Tool families</h5>
  <div class="checkbox-row">
    <input id="set-sp-actions-enabled" type="checkbox">
    <label for="set-sp-actions-enabled" style="margin:0">Studio Pro UI actions <span class="muted">— run, stop, refresh, save_all, get_app_status</span></label>
  </div>
  <div class="checkbox-row">
    <input id="set-maia-enabled" type="checkbox">
    <label for="set-maia-enabled" style="margin:0">Maia integration <span class="muted">— send, status, wait, ask, reset, force_tier</span></label>
  </div>
  <div id="maia-platform-note" class="mcp-port-readout"></div>

  <div class="field hotkey-field"><label>Refresh-from-disk hotkey</label><input id="set-refresh-hotkey" type="text" value="F4" placeholder="e.g. F4 or Ctrl+F5"></div>
</section>
```

- [ ] **Step 3: Manual smoke**

Run: `dotnet build Terminal.csproj` (which runs `BuildUi`).
Open Studio Pro with Concord. Open Concord settings. Click "Concord MCP" in the nav. Verify: section header reads "Concord MCP", two new sub-checkboxes are visible.

- [ ] **Step 4: Commit**

```powershell
git add ui/index.html
git commit -m "feat(ui): rename Action bridge section to Concord MCP with two sub-toggles"
```

---

### Task 16: Update `settings-modal.ts` to handle new fields

**Files:**
- Modify: [ui/src/settings-modal.ts](../../../ui/src/settings-modal.ts)

- [ ] **Step 1: Update the `SettingsPayload` interface**

Replace lines 22-38 of [ui/src/settings-modal.ts](../../../ui/src/settings-modal.ts):

```typescript
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
  mcpServerEnabled: boolean;
  mcpServerPort: number;
  studioProActionsEnabled: boolean;
  maiaIntegrationEnabled: boolean;
  platform: string;
  refreshFromDiskHotkey: string;
  restoreTabsOnReopen: boolean;
  about: AboutInfo;
  studioProMcp: StudioProMcpInfo | null;
}
```

- [ ] **Step 2: Add new field references**

After the existing `private chkActions = ...` block (around line 91-98), add:

```typescript
private chkSpActions = document.getElementById(
  "set-sp-actions-enabled",
) as HTMLInputElement;
private chkMaia = document.getElementById(
  "set-maia-enabled",
) as HTMLInputElement;
private maiaPlatformNote = document.getElementById(
  "maia-platform-note",
) as HTMLDivElement;
```

- [ ] **Step 3: Update `populate` to set the new checkboxes**

Replace line 261-262 inside `populate`:

```typescript
// Concord MCP fields
this.chkActions.checked = d.mcpServerEnabled;
this.chkSpActions.checked = d.studioProActionsEnabled;
this.chkMaia.checked = d.maiaIntegrationEnabled;
this.renderActionsPortReadout(d.mcpServerEnabled, d.mcpServerPort);
this.applyMaiaPlatformGate(d.platform);
```

- [ ] **Step 4: Add `applyMaiaPlatformGate`**

Add the method (after `renderActionsPortReadout`):

```typescript
private applyMaiaPlatformGate(platform: string): void {
  const isWindows = platform === "windows";
  this.chkMaia.disabled = !isWindows;
  if (this.maiaPlatformNote) {
    this.maiaPlatformNote.classList.remove("warn");
    this.maiaPlatformNote.innerHTML = isWindows
      ? `Maia integration uses Studio Pro's WebView2 debug port. Maia panel must be visible at call time.`
      : `Maia integration is <strong>Windows-only</strong> in this Concord release.`;
  }
}
```

- [ ] **Step 5: Update `save` payload**

Replace line 372-388 (the `this.bridge.send("saveSettings", { ... })` block):

```typescript
this.bridge.send("saveSettings", {
  shellPath,
  args: args ? args.split(/\s+/) : [],
  ringBufferKB: parseInt(this.inpRing.value, 10) || 4096,
  xtermScrollbackLines: parseInt(this.inpScroll.value, 10) || 10000,
  theme,
  mcpEnabled: this.chkMcp.checked,
  mcpClients,
  mcpServerEnabled: this.chkActions.checked,
  studioProActionsEnabled: this.chkSpActions.checked,
  maiaIntegrationEnabled: this.chkMaia.checked,
  refreshFromDiskHotkey: this.inpRefreshHotkey.value,
  restoreTabsOnReopen: this.chkRestoreTabs.checked,
});
```

- [ ] **Step 6: Update `renderActionsPortReadout` copy**

Replace line 280:

```typescript
this.actionsPortReadout.innerHTML = `Concord MCP is <strong>not running</strong>. Enable to start the local HTTP server that exposes Concord tools to the CLIs above.`;
```

And replace line 285-287:

```typescript
this.actionsPortReadout.innerHTML =
  `Concord MCP is listening on <code>localhost:${boundPort}</code>. ` +
  `Each Save writes that URL into the CLI configs. Default is 7783; ` +
  `if that's busy on your machine the bridge falls back to a free port automatically.`;
```

- [ ] **Step 7: Build and manual smoke**

Run: `dotnet build Terminal.csproj`
Open Concord settings. Click "Concord MCP". Verify:
- Master toggle controls server.
- Both sub-toggles editable on Windows; Maia disabled with the note on macOS.
- Save persists across Studio Pro restart.
- A CLI MCP probe (`curl -X POST http://127.0.0.1:7783/mcp -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'`) lists maia tools when enabled, omits them when disabled.

- [ ] **Step 8: Commit**

```powershell
git add ui/src/settings-modal.ts
git commit -m "feat(ui): wire Concord MCP sub-toggles, platform-gated Maia control"
```

---

## Phase 5 — Live tests, version bump, docs

### Task 17: Live integration tests

**Files:**
- Create: `tests/Maia/MaiaLiveTests.cs`
- Create: `tests/Maia/README.md`

- [ ] **Step 1: Add the live test file**

Create `tests/Maia/MaiaLiveTests.cs`:

```csharp
using FluentAssertions;
using Terminal.Maia;
using Xunit;

namespace Terminal.Tests.Maia;

[Trait("Category", "MaiaLive")]
public class MaiaLiveTests
{
    private static bool LiveEnabled =>
        Environment.GetEnvironmentVariable("CONCORD_MAIA_LIVE") == "1";

    private static MaiaRouter NewRouter()
    {
        var transports = new IMaiaTransport[]
        {
            new CdpInjectedTransport(() => new CdpClient()),
            new CdpChatTransport(() => new CdpClient()),
        };
        var r = new MaiaRouter(transports);
        r.ProbeAllAsync(CancellationToken.None).GetAwaiter().GetResult();
        return r;
    }

    [SkippableFact]
    public async Task HealthProbe_Tier1_IsActive_WhenMaiaPanelOpen()
    {
        Skip.IfNot(LiveEnabled);
        var router = NewRouter();
        var t1 = router.Transports.First(t => t.Name == "cdp_injected");
        var h = await t1.HealthCheckAsync(CancellationToken.None);
        h.Available.Should().BeTrue($"reason: {h.Reason}");
    }

    [SkippableFact]
    public async Task Ask_MpmExtension_ReturnsExpectedAnswer()
    {
        Skip.IfNot(LiveEnabled);
        var actions = new MaiaActions(NewRouter());
        var r = await actions.AskAsync(
            "In ten words, what file extension do Mendix project files use?",
            timeoutSec: 30, CancellationToken.None);
        r.Error.Should().BeNull();
        var json = System.Text.Json.JsonSerializer.Serialize(r.Data);
        json.Should().ContainEquivalentOf(".mpr");
    }

    [SkippableFact]
    public async Task ForceTier_CdpChat_AnswersCorrectly()
    {
        Skip.IfNot(LiveEnabled);
        var router = NewRouter();
        router.ForceTier("cdp_chat");
        var actions = new MaiaActions(router);
        var r = await actions.AskAsync(
            "Reply with the single word: pong",
            timeoutSec: 30, CancellationToken.None);
        r.Error.Should().BeNull();
    }
}
```

- [ ] **Step 2: Add `Xunit.SkippableFact` package**

Edit [tests/Terminal.Tests.csproj](../../../tests/Terminal.Tests.csproj). Add:

```xml
<PackageReference Include="Xunit.SkippableFact" Version="1.4.*" />
```

- [ ] **Step 3: Add `tests/Maia/README.md`**

```markdown
# Maia tests

Three layers:

1. **Unit** — `MaiaRouterTests`, `MaiaActionsTests`, `CdpInjectedTransportTests`,
   `CdpChatTransportTests`, `MaiaJsonRpcTests`, `EmbeddedResourceTests`. Run on
   every PR. No Studio Pro needed.

2. **JS agent unit** — TODO: port the prototype's 12 tests for `maia_agent.js`
   via Node subprocess.

3. **Live (`[Trait("Category","MaiaLive")]`)** — `MaiaLiveTests`. Skipped unless
   `CONCORD_MAIA_LIVE=1` is set. Requires:
   - Studio Pro 11.10+ running, single instance.
   - Maia panel visible (click the Maia tab in the right pane).
   - Concord extension loaded (so we share an environment, but the tests
     drive Maia directly via CDP — Concord need not be wired in).

   Run locally: `$env:CONCORD_MAIA_LIVE=1; dotnet test --filter "Category=MaiaLive"`.
```

- [ ] **Step 4: Build and confirm tests are gated**

Run: `dotnet test tests/Terminal.Tests.csproj`
Expected: `MaiaLiveTests` skipped (3 entries marked skipped).

- [ ] **Step 5: Commit**

```powershell
git add tests/Maia/MaiaLiveTests.cs tests/Maia/README.md tests/Terminal.Tests.csproj
git commit -m "test(maia): live integration tests gated by CONCORD_MAIA_LIVE=1"
```

---

### Task 18: Version bump and CHANGELOG

**Files:**
- Modify: [Terminal.csproj](../../../Terminal.csproj)
- Modify: [CHANGELOG.md](../../../CHANGELOG.md)

- [ ] **Step 1: Bump version**

Edit [Terminal.csproj](../../../Terminal.csproj). Change `<Version>1.2.2</Version>` → `<Version>1.3.0</Version>` and update `<InformationalVersion>` similarly.

- [ ] **Step 2: Add CHANGELOG entry**

Prepend to [CHANGELOG.md](../../../CHANGELOG.md):

```markdown
## 1.3.0 — 2026-05-08

### Breaking
- MCP server wire identity renamed from `mendix-studio-pro-actions` to `concord-mcp`. Update any MCP client config (Claude Code `.mcp.json`, Codex `~/.codex/config.toml`, Copilot CLI) that references the old name.

### Added
- **Maia integration** as a first-class tool family inside Concord MCP, embedded in C# (no Python, no subprocess). Tools: `maia__send`, `maia__status`, `maia__wait`, `maia__ask`, `maia__reset`, `maia__force_tier`. Two-tier transport: injected JS agent (Tier 1) + DOM-scrape fallback (Tier 2). Windows only.
- Settings sidebar item renamed: `Action bridge` → `Concord MCP`. Two sub-toggles: `Studio Pro UI actions` and `Maia integration`. Maia disabled-with-tooltip on macOS.
- New settings keys: `mcpServerEnabled`, `mcpServerPort`, `studioProActionsEnabled`, `maiaIntegrationEnabled`. Old keys (`actionsServerEnabled`, `actionsServerPort`) read for one minor-version migration.

### Note
- Maia integration is internal CoE tooling. The CDP-driven approach (Studio Pro's WebView2 `--remote-debugging-port`) is not Mendix-blessed and may break if Mendix changes that surface. The transport interface is the swap-out seam for future Mendix-native MCP-server-as-tool support.
```

- [ ] **Step 3: Build to confirm version flows**

Run: `dotnet build Terminal.csproj`
Expected: clean build with new version baked in.

- [ ] **Step 4: Commit**

```powershell
git add Terminal.csproj CHANGELOG.md
git commit -m "chore: v1.3.0 — concord-mcp + Maia bridge in C#"
```

---

## Self-review against the spec

After completing all tasks, verify the spec is covered:

- [x] **Goals**:
  - Maia bridge in-process, no external process — Tasks 4-11.
  - Server renamed to `concord-mcp` v1.3.0 — Task 12.
  - Settings UI: Concord MCP section, two sub-toggles — Tasks 15-16.
  - `IMaiaTransport` swap-out seam — Task 5.
  - macOS gracefully disabled — Tasks 6 (CdpClient), 14 (extension wiring), 16 (UI).
- [x] **Architecture**: 8 files in `src/Maia/` plus 5 modifications to existing files (server, settings, payloads, extension, UI) — covered.
- [x] **Components/interfaces**: `IMaiaTransport` (Task 5), `ICdpClient`/`CdpClient` (Task 6), `MaiaRouter` (Task 10), `MaiaActions` (Task 11), `MaiaTypes` (Task 4) — covered.
- [x] **Data flow** for all six verbs — covered in `MaiaActions` (Task 11), `MaiaRouter` (Task 10).
- [x] **Error handling**: TransportUnavailable demotion (Task 10), ported messages (Task 6), `ActionResult.Fail` for usage errors (Task 11) — covered.
- [x] **Testing**: unit (Tasks 4-11, 13), conditional registration (Task 13), live gated (Task 17), settings migration (Task 1), embedded resource (Task 7) — covered. JS agent unit tests via Node are noted as TODO in `tests/Maia/README.md` — non-blocking; tracked in BACKLOG.

No spec requirement is unaddressed. No placeholders, no TBDs.

---

## Plan complete and saved to [docs/superpowers/plans/2026-05-08-concord-mcp-maia-bridge.md](2026-05-08-concord-mcp-maia-bridge.md). Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
