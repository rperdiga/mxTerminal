# Mendix Studio Pro Terminal Extension — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a self-contained Mendix Studio Pro 11.x C# extension that hosts a tabbed terminal in a dockable pane (Pty.Net + ConPTY + xterm.js), starting at the open Mendix app's project root, replacing the bridge-server pattern from `mxCoPilot`.

**Architecture:** A singleton `TerminalSessionManager` owns all PTYs (via Pty.Net). A `WebServerExtension` serves the bundled UI from `wwwroot/`. A `DockablePaneExtension` hosts a Mendix WebView whose ViewModel bridges JSON messages to the manager. UI is xterm.js bundled by esbuild from `ui/` source. PTYs persist while the Mendix app is open; reattach via ring-buffer replay.

**Tech Stack:** C# / .NET 8, MEF, Mendix.StudioPro.ExtensionsAPI 11.*, Pty.Net (ConPTY), xUnit + FluentAssertions, TypeScript, esbuild, xterm.js v5 (with addon-fit, addon-web-links).

**Spec:** `docs/superpowers/specs/2026-04-29-terminal-extension-design.md` — all architectural decisions.

---

## Test strategy

| Component | Strategy |
|-----------|----------|
| `RingBuffer` | Pure xUnit unit tests. TDD strictly. |
| `TerminalSettings` (load/save) | xUnit with temp directory. TDD strictly. |
| Message DTOs | xUnit JSON round-trip. TDD strictly. |
| `Logging` | xUnit with temp file. TDD strictly. |
| `PtySession` | xUnit integration tests spawning real `cmd.exe /c echo`. Skipped on non-Windows. |
| `TerminalSessionManager` | xUnit with a mock `IPtyFactory` so PTY spawn isn't required. |
| `TerminalWebServer` | No automated test (depends on Mendix host). Manual smoke in Task 23. |
| `TerminalPaneViewModel` | No automated test (depends on Mendix host). Manual smoke in Task 23. |
| `TerminalPaneExtension` / `TerminalMenuExtension` | No automated test (MEF-instantiated by Mendix). Manual smoke in Task 23. |
| TS bridge (`bridge.ts`) | vitest unit tests for envelope + base64 helpers. |
| TS UI (`tab-manager`, `xterm-tab`, etc.) | No automated test — tested via the manual smoke in Task 23. |

---

## Task 1: Repository bootstrap

**Files:**
- Create: `.gitignore`
- Create: `README.md`
- Create: `Directory.Build.props.example`
- Create: `.gitattributes`

- [ ] **Step 1: Initialize git repository**

```bash
cd /c/Extensions/Terminal
git init
git checkout -b main
```

- [ ] **Step 2: Create `.gitignore`**

```gitignore
# .NET build outputs
bin/
obj/
*.user
*.suo
.vs/

# Test outputs
TestResults/
coverage.*

# UI build artifacts
ui/node_modules/
wwwroot/

# Per-developer build settings (each dev points at their own Mendix project)
Directory.Build.props

# IDE
.idea/
.vscode/
```

- [ ] **Step 3: Create `.gitattributes`**

```gitattributes
* text=auto eol=lf
*.cs    text eol=crlf
*.csproj text eol=crlf
*.sln   text eol=crlf
*.{cmd,bat,ps1} text eol=crlf
```

- [ ] **Step 4: Create `Directory.Build.props.example`**

```xml
<!--
  Copy this file to Directory.Build.props (gitignored) and set MendixDeployTarget
  to the Mendix project where you want builds to deploy.

  Example:  <MendixDeployTarget>C:\Mendix Projects\MyTestApp</MendixDeployTarget>
-->
<Project>
  <PropertyGroup>
    <MendixDeployTarget></MendixDeployTarget>
  </PropertyGroup>
</Project>
```

- [ ] **Step 5: Create `README.md`**

```markdown
# Mendix Studio Pro Terminal Extension

A C# extension for Mendix Studio Pro 11.x that embeds a tabbed terminal (Claude Code, Codex, etc.) in a dockable pane, starting at the open Mendix app's project root. No external bridge server required.

## Setup

1. Copy `Directory.Build.props.example` → `Directory.Build.props` and set `MendixDeployTarget` to your Mendix project path.
2. Ensure Node.js 18+ is on PATH (used at C# build time to bundle the UI).
3. Build: `dotnet build`
4. Launch Studio Pro with `--enable-extension-development`.
5. F4 in Studio Pro to reload extensions.
6. Menu: Extensions → Terminal.

See `docs/superpowers/specs/2026-04-29-terminal-extension-design.md` for design.
```

- [ ] **Step 6: Initial commit**

```bash
git add .gitignore .gitattributes Directory.Build.props.example README.md docs/
git commit -m "chore: repository bootstrap"
```

---

## Task 2: Solution and project files

**Files:**
- Create: `MxStudioProTerminal.sln`
- Create: `MxStudioProTerminal.csproj`
- Create: `manifest.json`
- Create: `tests/MxStudioProTerminal.Tests.csproj`

- [ ] **Step 1: Create `manifest.json`**

```json
{ "mx_extensions": ["MxStudioProTerminal.dll"] }
```

- [ ] **Step 2: Create `MxStudioProTerminal.csproj`** (without yet adding the BuildUi/Deploy targets — those go in Task 22)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <AssemblyName>MxStudioProTerminal</AssemblyName>
    <RootNamespace>MxStudioProTerminal</RootNamespace>
    <LangVersion>preview</LangVersion>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Mendix.StudioPro.ExtensionsAPI" Version="11.*" />
    <PackageReference Include="Eto.Forms" Version="2.9.*" />
    <PackageReference Include="Pty.Net" Version="*" />
    <PackageReference Include="System.Text.Json" Version="8.0.*" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="manifest.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory></Content>
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `tests/MxStudioProTerminal.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>MxStudioProTerminal.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="FluentAssertions" Version="6.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MxStudioProTerminal.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create `MxStudioProTerminal.sln`**

Run from the project root:

```bash
dotnet new sln --name MxStudioProTerminal
dotnet sln add MxStudioProTerminal.csproj
dotnet sln add tests/MxStudioProTerminal.Tests.csproj
```

- [ ] **Step 5: Verify the projects restore and build**

Run: `dotnet restore && dotnet build`
Expected: Both projects build successfully (no source files yet, so just empty assemblies). If `Pty.Net` Version="*" can't resolve, change it to a specific known version (try `0.1.16` first; if that's broken on .NET 8, try `0.1.18` or `0.1.21`).

- [ ] **Step 6: Pin the Pty.Net version that worked**

Edit `MxStudioProTerminal.csproj` and replace `Version="*"` with whatever version restored cleanly in Step 5.

- [ ] **Step 7: Commit**

```bash
git add MxStudioProTerminal.sln MxStudioProTerminal.csproj tests/MxStudioProTerminal.Tests.csproj manifest.json
git commit -m "chore: solution + test project scaffolding"
```

---

## Task 3: `RingBuffer` class (TDD)

**Files:**
- Create: `src/RingBuffer.cs`
- Test: `tests/RingBufferTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/RingBufferTests.cs`:

```csharp
using FluentAssertions;
using MxStudioProTerminal;
using Xunit;

namespace MxStudioProTerminal.Tests;

public class RingBufferTests
{
    [Fact]
    public void NewBuffer_IsEmpty()
    {
        var rb = new RingBuffer(capacity: 16);
        rb.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public void Write_BelowCapacity_ReturnsAllBytes()
    {
        var rb = new RingBuffer(capacity: 16);
        rb.Write(new byte[] { 1, 2, 3 });
        rb.Snapshot().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Write_ExactCapacity_ReturnsAllBytes()
    {
        var rb = new RingBuffer(capacity: 4);
        rb.Write(new byte[] { 1, 2, 3, 4 });
        rb.Snapshot().Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public void Write_OverCapacity_DropsOldest()
    {
        var rb = new RingBuffer(capacity: 4);
        rb.Write(new byte[] { 1, 2, 3, 4, 5, 6 });
        rb.Snapshot().Should().Equal(3, 4, 5, 6);
    }

    [Fact]
    public void Write_MultipleAppends_ReturnsInOrder()
    {
        var rb = new RingBuffer(capacity: 8);
        rb.Write(new byte[] { 1, 2, 3 });
        rb.Write(new byte[] { 4, 5 });
        rb.Snapshot().Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public void Write_ChunkLargerThanCapacity_KeepsLastCapacityBytes()
    {
        var rb = new RingBuffer(capacity: 3);
        rb.Write(new byte[] { 1, 2, 3, 4, 5 });
        rb.Snapshot().Should().Equal(3, 4, 5);
    }

    [Fact]
    public void Write_WrapAround_RebuildsCorrectOrder()
    {
        var rb = new RingBuffer(capacity: 4);
        rb.Write(new byte[] { 1, 2, 3 });
        rb.Write(new byte[] { 4, 5, 6 });   // wraps: writeIndex was 3, now overwrites slot 0 with 5, slot 1 with 6
        rb.Snapshot().Should().Equal(3, 4, 5, 6);
    }

    [Fact]
    public void Constructor_InvalidCapacity_Throws()
    {
        Action act = () => new RingBuffer(capacity: 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~RingBufferTests`
Expected: All tests fail with "type or namespace 'RingBuffer' could not be found".

- [ ] **Step 3: Implement `RingBuffer`**

Create `src/RingBuffer.cs`:

```csharp
namespace MxStudioProTerminal;

/// <summary>
/// Fixed-capacity circular byte buffer. Thread-unsafe — caller synchronises.
/// </summary>
public sealed class RingBuffer
{
    private readonly byte[] buf;
    private int writeIndex;       // next slot to write (0..capacity-1)
    private int filled;           // bytes currently stored (0..capacity)

    public int Capacity => buf.Length;
    public int Count => filled;

    public RingBuffer(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        buf = new byte[capacity];
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;

        // If incoming chunk is larger than capacity, keep only its tail.
        if (data.Length >= buf.Length)
        {
            data[^buf.Length..].CopyTo(buf);
            writeIndex = 0;
            filled = buf.Length;
            return;
        }

        var firstSlice = Math.Min(data.Length, buf.Length - writeIndex);
        data[..firstSlice].CopyTo(buf.AsSpan(writeIndex));
        var remaining = data.Length - firstSlice;
        if (remaining > 0)
            data[firstSlice..].CopyTo(buf.AsSpan(0));

        writeIndex = (writeIndex + data.Length) % buf.Length;
        filled = Math.Min(filled + data.Length, buf.Length);
    }

    public byte[] Snapshot()
    {
        var result = new byte[filled];
        if (filled == 0) return result;

        if (filled < buf.Length)
        {
            // Buffer not yet wrapped — bytes are in [0..filled).
            buf.AsSpan(0, filled).CopyTo(result);
        }
        else
        {
            // Wrapped — oldest byte is at writeIndex.
            var tail = buf.Length - writeIndex;
            buf.AsSpan(writeIndex, tail).CopyTo(result.AsSpan(0));
            buf.AsSpan(0, writeIndex).CopyTo(result.AsSpan(tail));
        }
        return result;
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~RingBufferTests`
Expected: All 8 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/RingBuffer.cs tests/RingBufferTests.cs
git commit -m "feat: ring buffer with wrap-around snapshot"
```

---

## Task 4: Message DTOs (TDD)

**Files:**
- Create: `src/Messages/Incoming.cs`
- Create: `src/Messages/Outgoing.cs`
- Test: `tests/MessageDtoTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/MessageDtoTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using MxStudioProTerminal.Messages;
using Xunit;

namespace MxStudioProTerminal.Tests;

public class MessageDtoTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CreateTab_RoundTrip_PreservesAllFields()
    {
        var json = """{"cols":120,"rows":30,"shellPath":"powershell.exe","args":["-NoLogo"],"cwd":"C:\\X"}""";
        var dto = JsonSerializer.Deserialize<CreateTabPayload>(json, Json)!;
        dto.Cols.Should().Be(120);
        dto.Rows.Should().Be(30);
        dto.ShellPath.Should().Be("powershell.exe");
        dto.Args.Should().Equal("-NoLogo");
        dto.Cwd.Should().Be(@"C:\X");
    }

    [Fact]
    public void CreateTab_OmitsOptionalFields_LeavesThemNull()
    {
        var dto = JsonSerializer.Deserialize<CreateTabPayload>("""{"cols":80,"rows":24}""", Json)!;
        dto.ShellPath.Should().BeNull();
        dto.Args.Should().BeNull();
        dto.Cwd.Should().BeNull();
    }

    [Fact]
    public void Input_RoundTrip()
    {
        var json = """{"tabId":"abc","dataB64":"aGVsbG8="}""";
        var dto = JsonSerializer.Deserialize<InputPayload>(json, Json)!;
        dto.TabId.Should().Be("abc");
        dto.DataB64.Should().Be("aGVsbG8=");
    }

    [Fact]
    public void Output_Serializes_WithCamelCase()
    {
        var msg = new OutputPayload("abc", "ZGF0YQ==");
        var json = JsonSerializer.Serialize(msg, Json);
        json.Should().Be("""{"tabId":"abc","dataB64":"ZGF0YQ=="}""");
    }

    [Fact]
    public void TabsList_Serializes()
    {
        var msg = new TabsListPayload(new[]
        {
            new SessionInfoPayload("id1", "powershell", "powershell.exe", @"C:\X", true)
        });
        var json = JsonSerializer.Serialize(msg, Json);
        json.Should().Contain("\"tabId\":\"id1\"");
        json.Should().Contain("\"alive\":true");
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~MessageDtoTests`
Expected: All fail (types missing).

- [ ] **Step 3: Implement DTOs**

Create `src/Messages/Incoming.cs`:

```csharp
namespace MxStudioProTerminal.Messages;

public sealed record CreateTabPayload(
    int Cols,
    int Rows,
    string? ShellPath = null,
    string[]? Args = null,
    string? Cwd = null);

public sealed record CloseTabPayload(string TabId);

public sealed record InputPayload(string TabId, string DataB64);

public sealed record ResizePayload(string TabId, int Cols, int Rows);

public sealed record ReplayPayload(string TabId);

public sealed record SaveSettingsPayload(
    string ShellPath,
    string[] Args,
    int? RingBufferKB = null,
    int? XtermScrollbackLines = null);
```

Create `src/Messages/Outgoing.cs`:

```csharp
namespace MxStudioProTerminal.Messages;

public sealed record SessionInfoPayload(
    string TabId,
    string Title,
    string ShellPath,
    string Cwd,
    bool Alive);

public sealed record TabsListPayload(IReadOnlyList<SessionInfoPayload> Tabs);

public sealed record TabCreatedPayload(string TabId, string Title, string ShellPath, string Cwd);

public sealed record TabClosedPayload(string TabId);

public sealed record OutputPayload(string TabId, string DataB64);

public sealed record ExitPayload(string TabId, int? ExitCode = null, string? Signal = null);

public sealed record ReplayDataPayload(string TabId, string DataB64);

public sealed record SettingsPayload(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines);

public sealed record ErrorPayload(string Message, string? Context = null);
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~MessageDtoTests`
Expected: All 5 tests pass. If casing fails, confirm `JsonSerializerDefaults.Web` is being used (it lowercases the first letter of each property name).

- [ ] **Step 5: Commit**

```bash
git add src/Messages/ tests/MessageDtoTests.cs
git commit -m "feat: WebView ⇄ C# message DTOs with JSON round-trip tests"
```

---

## Task 5: `TerminalSettings` load/save (TDD)

**Files:**
- Create: `src/TerminalSettings.cs`
- Test: `tests/TerminalSettingsTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/TerminalSettingsTests.cs`:

```csharp
using FluentAssertions;
using MxStudioProTerminal;
using Xunit;

namespace MxStudioProTerminal.Tests;

public class TerminalSettingsTests : IDisposable
{
    private readonly string tmpDir;

    public TerminalSettingsTests()
    {
        tmpDir = Path.Combine(Path.GetTempPath(), "mxterm-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
    }

    public void Dispose() => Directory.Delete(tmpDir, recursive: true);

    [Fact]
    public void Load_NoFile_ReturnsDefaults()
    {
        var settings = TerminalSettings.Load(tmpDir);
        settings.ShellPath.Should().Be("powershell.exe");
        settings.Args.Should().BeEmpty();
        settings.RingBufferKB.Should().Be(4096);
        settings.XtermScrollbackLines.Should().Be(10000);
    }

    [Fact]
    public void Save_ThenLoad_PreservesAllFields()
    {
        var original = new TerminalSettings("bash.exe", new[] { "--login" }, 8192, 20000);
        original.Save(tmpDir);

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Load_PartialJson_FillsMissingWithDefaults()
    {
        var resourcesDir = Path.Combine(tmpDir, "resources");
        Directory.CreateDirectory(resourcesDir);
        File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"), """{"shellPath":"cmd.exe"}""");

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.ShellPath.Should().Be("cmd.exe");
        loaded.Args.Should().BeEmpty();
        loaded.RingBufferKB.Should().Be(4096);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsDefaults()
    {
        var resourcesDir = Path.Combine(tmpDir, "resources");
        Directory.CreateDirectory(resourcesDir);
        File.WriteAllText(Path.Combine(resourcesDir, "terminal-settings.json"), "{ this is not json");

        var loaded = TerminalSettings.Load(tmpDir);
        loaded.ShellPath.Should().Be("powershell.exe");
    }

    [Fact]
    public void Save_CreatesResourcesDirIfMissing()
    {
        var settings = new TerminalSettings("powershell.exe", Array.Empty<string>(), 4096, 10000);
        settings.Save(tmpDir);
        File.Exists(Path.Combine(tmpDir, "resources", "terminal-settings.json")).Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~TerminalSettingsTests`
Expected: All fail (`TerminalSettings` undefined).

- [ ] **Step 3: Implement `TerminalSettings`**

Create `src/TerminalSettings.cs`:

```csharp
using System.Text.Json;

namespace MxStudioProTerminal;

public sealed record TerminalSettings(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines)
{
    public static TerminalSettings Defaults() => new(
        ShellPath: "powershell.exe",
        Args: Array.Empty<string>(),
        RingBufferKB: 4096,
        XtermScrollbackLines: 10000);

    private const string FileName = "terminal-settings.json";
    private const string SubDir = "resources";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static string PathFor(string projectDir) =>
        System.IO.Path.Combine(projectDir, SubDir, FileName);

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
            return new TerminalSettings(
                ShellPath: dto.ShellPath ?? def.ShellPath,
                Args: dto.Args ?? def.Args,
                RingBufferKB: dto.RingBufferKB ?? def.RingBufferKB,
                XtermScrollbackLines: dto.XtermScrollbackLines ?? def.XtermScrollbackLines);
        }
        catch (JsonException)
        {
            return Defaults();
        }
    }

    public void Save(string projectDir)
    {
        var dir = System.IO.Path.Combine(projectDir, SubDir);
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, FileName);
        var dto = new Dto(ShellPath, Args, RingBufferKB, XtermScrollbackLines);
        File.WriteAllText(path, JsonSerializer.Serialize(dto, Json));
    }

    private sealed record Dto(
        string? ShellPath,
        string[]? Args,
        int? RingBufferKB,
        int? XtermScrollbackLines);
}
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~TerminalSettingsTests`
Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/TerminalSettings.cs tests/TerminalSettingsTests.cs
git commit -m "feat: TerminalSettings load/save with defaults"
```

---

## Task 6: `Logging` (TDD)

**Files:**
- Create: `src/Logging.cs`
- Test: `tests/LoggingTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/LoggingTests.cs`:

```csharp
using FluentAssertions;
using MxStudioProTerminal;
using Xunit;

namespace MxStudioProTerminal.Tests;

public class LoggingTests : IDisposable
{
    private readonly string tmpDir = Path.Combine(Path.GetTempPath(), "mxterm-log-" + Guid.NewGuid().ToString("N"));

    public LoggingTests() => Directory.CreateDirectory(tmpDir);
    public void Dispose() => Directory.Delete(tmpDir, recursive: true);

    [Fact]
    public void Info_WritesLineToFile()
    {
        var log = new Logger(tmpDir);
        log.Info("hello");
        var contents = File.ReadAllText(Path.Combine(tmpDir, "resources", "terminal.log"));
        contents.Should().Contain("hello");
        contents.Should().Contain("INFO");
    }

    [Fact]
    public void Error_IncludesException()
    {
        var log = new Logger(tmpDir);
        log.Error("oops", new InvalidOperationException("boom"));
        var contents = File.ReadAllText(Path.Combine(tmpDir, "resources", "terminal.log"));
        contents.Should().Contain("ERROR");
        contents.Should().Contain("oops");
        contents.Should().Contain("InvalidOperationException");
        contents.Should().Contain("boom");
    }

    [Fact]
    public void Clear_TruncatesFile()
    {
        var log = new Logger(tmpDir);
        log.Info("first run");
        log.Clear();
        log.Info("second run");
        var contents = File.ReadAllText(Path.Combine(tmpDir, "resources", "terminal.log"));
        contents.Should().NotContain("first run");
        contents.Should().Contain("second run");
    }

    [Fact]
    public void Logger_FailsSilently_WhenDirectoryCannotBeCreated()
    {
        // Pass an obviously invalid path; expect no exception.
        var log = new Logger("\0invalid\0");
        Action act = () => log.Info("x");
        act.Should().NotThrow();
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~LoggingTests`
Expected: All fail.

- [ ] **Step 3: Implement `Logger`**

Create `src/Logging.cs`:

```csharp
namespace MxStudioProTerminal;

public sealed class Logger
{
    private readonly string projectDir;
    private readonly object gate = new();

    public Logger(string projectDir) => this.projectDir = projectDir;

    public void Info(string message)  => Write("INFO",  message, exception: null);
    public void Warn(string message)  => Write("WARN",  message, exception: null);
    public void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    public void Clear()
    {
        try
        {
            var path = LogPath();
            if (path != null && File.Exists(path)) File.WriteAllText(path, string.Empty);
        }
        catch { /* best-effort */ }
    }

    private string? LogPath()
    {
        try
        {
            var dir = Path.Combine(projectDir, "resources");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "terminal.log");
        }
        catch
        {
            return null;
        }
    }

    private void Write(string level, string message, Exception? exception)
    {
        var path = LogPath();
        if (path is null) return;

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {level} {message}";
        if (exception != null)
            line += $"{Environment.NewLine}    {exception.GetType().Name}: {exception.Message}{Environment.NewLine}    {exception.StackTrace}";
        line += Environment.NewLine;

        try
        {
            lock (gate) File.AppendAllText(path, line);
        }
        catch { /* best-effort */ }
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~LoggingTests`
Expected: All 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Logging.cs tests/LoggingTests.cs
git commit -m "feat: file-based logger to <project>/resources/terminal.log"
```

---

## Task 7: PTY abstraction interface and `PtySession` integration tests

**Files:**
- Create: `src/IPtyFactory.cs`
- Create: `src/PtySession.cs`
- Test: `tests/PtySessionTests.cs`

This task pins down the Pty.Net API by writing integration tests that spawn real `cmd.exe`. If the assumed Pty.Net surface differs from reality, that surfaces here.

- [ ] **Step 1: Spike — verify the actual Pty.Net API**

Run: `dotnet build` (Pty.Net is already referenced from Task 2).
Then in the project root, open Visual Studio's Object Browser or use `dotnet tool install -g ICSharpCode.Decompiler.Console` to inspect:

```bash
ilspycmd "$(dotnet nuget locals global-packages --list | head -n1 | sed 's/.*: //')/pty.net/<version>/lib/netstandard2.0/Pty.Net.dll" --type Pty.Net.PtyProvider
ilspycmd "$(dotnet nuget locals global-packages --list | head -n1 | sed 's/.*: //')/pty.net/<version>/lib/netstandard2.0/Pty.Net.dll" --type Pty.Net.PtyOptions
ilspycmd "$(dotnet nuget locals global-packages --list | head -n1 | sed 's/.*: //')/pty.net/<version>/lib/netstandard2.0/Pty.Net.dll" --type Pty.Net.IPtyConnection
```

(Or, simpler: open the Pty.Net repo on GitHub matching the pinned version and read the public API.)

Expected discovery — confirm or adjust:
- `static Task<IPtyConnection> PtyProvider.SpawnAsync(PtyOptions, CancellationToken)`
- `class PtyOptions { string App; string[] CommandLine; string Cwd; int Cols; int Rows; IDictionary<string,string> Environment; bool Verbatim; }`
- `interface IPtyConnection { Stream ReaderStream; Stream WriterStream; int Pid; int ExitCode; void Resize(int cols, int rows); event EventHandler<PtyExitedEventArgs> ProcessExited; void Dispose(); }`

Update the `PtySession` implementation in Step 4 if any property names differ.

- [ ] **Step 2: Define `IPtyFactory` interface**

Create `src/IPtyFactory.cs`:

```csharp
namespace MxStudioProTerminal;

/// <summary>
/// Factory abstraction so SessionManager can be unit-tested without spawning processes.
/// Single method: spawn a PTY and return the wrapped session.
/// </summary>
public interface IPtyFactory
{
    Task<IPtySession> SpawnAsync(string shellPath, string[] args, string cwd, int cols, int rows, IDictionary<string,string> environment, CancellationToken ct);
}

public interface IPtySession : IDisposable
{
    int Pid { get; }
    Task WriteAsync(byte[] data, CancellationToken ct);
    Task<int> ReadAsync(byte[] buffer, CancellationToken ct);
    void Resize(int cols, int rows);
    int? ExitCode { get; }
    event EventHandler<int?> Exited;
}
```

- [ ] **Step 3: Write the failing integration tests**

Create `tests/PtySessionTests.cs`:

```csharp
using FluentAssertions;
using MxStudioProTerminal;
using System.Text;
using Xunit;

namespace MxStudioProTerminal.Tests;

public class PtySessionTests
{
    [Fact]
    public async Task Spawn_CmdEcho_ProducesExpectedOutput()
    {
        if (!OperatingSystem.IsWindows())
            return; // PTY tests are Windows-only

        var factory = new PtyNetFactory();
        await using var ctx = TestContext.Create();
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            env[(string)e.Key] = (string)(e.Value ?? "");

        var session = await factory.SpawnAsync(
            shellPath: "cmd.exe",
            args: new[] { "/c", "echo hello-from-pty" },
            cwd: Environment.CurrentDirectory,
            cols: 80, rows: 24,
            environment: env,
            ct: ctx.Token);

        var output = await ReadAllAsync(session, ctx.Token);
        Encoding.UTF8.GetString(output).Should().Contain("hello-from-pty");
    }

    [Fact]
    public async Task Spawn_InvalidExecutable_Throws()
    {
        if (!OperatingSystem.IsWindows()) return;

        var factory = new PtyNetFactory();
        await using var ctx = TestContext.Create();

        Func<Task> act = async () => await factory.SpawnAsync(
            shellPath: "definitely-not-a-real-program-xyz.exe",
            args: Array.Empty<string>(),
            cwd: Environment.CurrentDirectory,
            cols: 80, rows: 24,
            environment: new Dictionary<string,string>(),
            ct: ctx.Token);

        await act.Should().ThrowAsync<Exception>();
    }

    private static async Task<byte[]> ReadAllAsync(IPtySession session, CancellationToken ct)
    {
        var ms = new MemoryStream();
        var buf = new byte[4096];
        var readsWithoutData = 0;
        while (readsWithoutData < 5 && !ct.IsCancellationRequested)
        {
            using var readCt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCt.CancelAfter(TimeSpan.FromMilliseconds(500));
            try
            {
                var n = await session.ReadAsync(buf, readCt.Token);
                if (n <= 0) break;
                ms.Write(buf, 0, n);
                readsWithoutData = 0;
            }
            catch (OperationCanceledException) { readsWithoutData++; }
        }
        return ms.ToArray();
    }

    private sealed class TestContext : IAsyncDisposable
    {
        public CancellationToken Token { get; }
        private readonly CancellationTokenSource cts;
        private TestContext(CancellationTokenSource cts) { this.cts = cts; Token = cts.Token; }
        public static TestContext Create() { var c = new CancellationTokenSource(TimeSpan.FromSeconds(10)); return new(c); }
        public ValueTask DisposeAsync() { cts.Cancel(); cts.Dispose(); return ValueTask.CompletedTask; }
    }
}
```

- [ ] **Step 4: Implement `PtyNetFactory` and `PtySession`**

Create `src/PtySession.cs` (adjust the property/method names if Step 1 turned up differences):

```csharp
using Pty.Net;

namespace MxStudioProTerminal;

public sealed class PtyNetFactory : IPtyFactory
{
    public async Task<IPtySession> SpawnAsync(
        string shellPath, string[] args, string cwd, int cols, int rows,
        IDictionary<string,string> environment, CancellationToken ct)
    {
        var options = new PtyOptions
        {
            App = shellPath,
            CommandLine = args,
            Cwd = cwd,
            Cols = cols,
            Rows = rows,
            Environment = environment,
        };
        var conn = await PtyProvider.SpawnAsync(options, ct);
        return new PtyNetSession(conn);
    }
}

internal sealed class PtyNetSession : IPtySession
{
    private readonly IPtyConnection conn;
    private int? exitCode;
    public event EventHandler<int?>? Exited;

    public PtyNetSession(IPtyConnection conn)
    {
        this.conn = conn;
        conn.ProcessExited += (_, e) =>
        {
            exitCode = e.ExitCode;
            Exited?.Invoke(this, e.ExitCode);
        };
    }

    public int Pid => conn.Pid;
    public int? ExitCode => exitCode;

    public Task WriteAsync(byte[] data, CancellationToken ct) =>
        conn.WriterStream.WriteAsync(data, 0, data.Length, ct);

    public Task<int> ReadAsync(byte[] buffer, CancellationToken ct) =>
        conn.ReaderStream.ReadAsync(buffer, 0, buffer.Length, ct);

    public void Resize(int cols, int rows) => conn.Resize(cols, rows);

    public void Dispose()
    {
        try { conn.Dispose(); } catch { /* best-effort */ }
    }
}
```

- [ ] **Step 5: Run integration tests — verify they pass on Windows**

Run: `dotnet test --filter FullyQualifiedName~PtySessionTests`
Expected on Windows: 2 tests pass. On non-Windows: tests are skipped (return early).

If a `PtyOptions` field name doesn't match (e.g., `CommandLine` vs `Args`, or `Environment` is not the right type), fix the names in `PtyNetFactory.SpawnAsync` and rerun. If `ProcessExited` event signature differs, adjust the subscription.

- [ ] **Step 6: Commit**

```bash
git add src/IPtyFactory.cs src/PtySession.cs tests/PtySessionTests.cs
git commit -m "feat: PTY abstraction with Pty.Net backing + integration tests"
```

---

## Task 8: `TerminalSessionManager` — basic CRUD (TDD with mock factory)

**Files:**
- Create: `src/TerminalSessionManager.cs`
- Create: `src/SessionInfo.cs`
- Test: `tests/TerminalSessionManagerTests.cs`

- [ ] **Step 1: Write the failing tests using a fake `IPtyFactory`**

Create `tests/TerminalSessionManagerTests.cs`:

```csharp
using FluentAssertions;
using MxStudioProTerminal;
using Xunit;

namespace MxStudioProTerminal.Tests;

public class TerminalSessionManagerTests
{
    [Fact]
    public async Task CreateSession_ReturnsTabId_AndSessionAppearsInList()
    {
        var fake = new FakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        var id = await mgr.CreateSessionAsync("powershell.exe", Array.Empty<string>(), "C:\\X", 80, 24);

        id.Should().NotBeNullOrEmpty();
        var list = mgr.ListSessions();
        list.Should().HaveCount(1);
        list[0].TabId.Should().Be(id);
        list[0].ShellPath.Should().Be("powershell.exe");
        list[0].Cwd.Should().Be("C:\\X");
        list[0].Alive.Should().BeTrue();
    }

    [Fact]
    public async Task Close_RemovesSessionFromList()
    {
        var fake = new FakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        var id = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\Y", 80, 24);

        mgr.Close(id);

        mgr.ListSessions().Should().BeEmpty();
        fake.LastSession.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Write_ForwardsBytesToPty()
    {
        var fake = new FakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        var id = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\X", 80, 24);

        mgr.Write(id, new byte[] { 0x68, 0x69 });  // "hi"

        fake.LastSession.WrittenBytes.Should().Equal(0x68, 0x69);
    }

    [Fact]
    public async Task Resize_ForwardsToPty()
    {
        var fake = new FakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        var id = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\X", 80, 24);

        mgr.Resize(id, 120, 40);

        fake.LastSession.Cols.Should().Be(120);
        fake.LastSession.Rows.Should().Be(40);
    }

    [Fact]
    public async Task DisposeAll_KillsAllPtys()
    {
        var fake = new FakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\X", 80, 24);
        await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\X", 80, 24);

        mgr.DisposeAll();

        mgr.ListSessions().Should().BeEmpty();
        fake.AllSessions.Should().AllSatisfy(s => s.Disposed.Should().BeTrue());
    }

    [Fact]
    public void Write_UnknownTabId_DoesNotThrow()
    {
        var mgr = new TerminalSessionManager(new FakePtyFactory());
        Action act = () => mgr.Write("nonexistent", new byte[] { 1 });
        act.Should().NotThrow();
    }
}

internal sealed class FakePtyFactory : IPtyFactory
{
    public List<FakePtySession> AllSessions { get; } = new();
    public FakePtySession LastSession => AllSessions[^1];

    public Task<IPtySession> SpawnAsync(
        string shellPath, string[] args, string cwd, int cols, int rows,
        IDictionary<string,string> environment, CancellationToken ct)
    {
        var s = new FakePtySession(cols, rows);
        AllSessions.Add(s);
        return Task.FromResult<IPtySession>(s);
    }
}

internal sealed class FakePtySession : IPtySession
{
    public int Pid => 1234;
    public int? ExitCode { get; private set; }
    public event EventHandler<int?>? Exited;
    public List<byte> WrittenBytes { get; } = new();
    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public bool Disposed { get; private set; }

    public FakePtySession(int cols, int rows) { Cols = cols; Rows = rows; }

    public Task WriteAsync(byte[] data, CancellationToken ct)
    {
        WrittenBytes.AddRange(data);
        return Task.CompletedTask;
    }

    public async Task<int> ReadAsync(byte[] buffer, CancellationToken ct)
    {
        // Block forever (until disposed) — manager's read loop will exit on Dispose
        try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
        return 0;
    }

    public void Resize(int cols, int rows) { Cols = cols; Rows = rows; }
    public void Dispose() { Disposed = true; ExitCode = 0; Exited?.Invoke(this, 0); }
    public void RaiseExited(int? code) { ExitCode = code; Exited?.Invoke(this, code); }
}
```

- [ ] **Step 2: Run — verify the tests fail**

Run: `dotnet test --filter FullyQualifiedName~TerminalSessionManagerTests`
Expected: All fail with "TerminalSessionManager type not found".

- [ ] **Step 3: Create `SessionInfo`**

Create `src/SessionInfo.cs`:

```csharp
namespace MxStudioProTerminal;

public sealed record SessionInfo(string TabId, string Title, string ShellPath, string Cwd, bool Alive);
```

- [ ] **Step 4: Implement `TerminalSessionManager` (basic CRUD only — no read loop or coalescing yet)**

Create `src/TerminalSessionManager.cs`:

```csharp
using System.Collections.Concurrent;

namespace MxStudioProTerminal;

public sealed class TerminalSessionManager : IDisposable
{
    private readonly IPtyFactory factory;
    private readonly ConcurrentDictionary<string, SessionState> sessions = new();
    private bool disposed;

    public event Action<string, byte[]>? Output;     // (tabId, bytes)  — populated in Task 9
    public event Action<string, int?>? Exited;       // (tabId, exitCode)

    public TerminalSessionManager(IPtyFactory factory)
    {
        this.factory = factory;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public async Task<string> CreateSessionAsync(string shellPath, string[] args, string cwd, int cols, int rows, CancellationToken ct = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(TerminalSessionManager));

        var env = BuildEnvironment();
        var pty = await factory.SpawnAsync(shellPath, args, cwd, cols, rows, env, ct);
        var tabId = Guid.NewGuid().ToString("N");
        var state = new SessionState(tabId, shellPath, cwd, pty);
        pty.Exited += (_, code) => OnPtyExited(tabId, code);
        sessions[tabId] = state;
        return tabId;
    }

    public IReadOnlyList<SessionInfo> ListSessions() =>
        sessions.Values.Select(s => new SessionInfo(
            s.TabId, TitleFor(s.ShellPath), s.ShellPath, s.Cwd, s.Pty.ExitCode is null
        )).ToList();

    public void Write(string tabId, byte[] data)
    {
        if (!sessions.TryGetValue(tabId, out var s)) return;
        try { s.Pty.WriteAsync(data, CancellationToken.None).GetAwaiter().GetResult(); }
        catch { /* PTY may have died — Exited handler removes it */ }
    }

    public void Resize(string tabId, int cols, int rows)
    {
        if (sessions.TryGetValue(tabId, out var s))
            try { s.Pty.Resize(cols, rows); } catch { /* best-effort */ }
    }

    public void Close(string tabId)
    {
        if (sessions.TryRemove(tabId, out var s))
            s.Pty.Dispose();
    }

    public void DisposeAll()
    {
        foreach (var key in sessions.Keys.ToList())
            Close(key);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        DisposeAll();
    }

    private void OnProcessExit(object? sender, EventArgs e) => DisposeAll();

    private void OnPtyExited(string tabId, int? code)
    {
        if (sessions.TryRemove(tabId, out _))
            Exited?.Invoke(tabId, code);
    }

    private static string TitleFor(string shellPath) =>
        Path.GetFileNameWithoutExtension(shellPath);

    private static IDictionary<string,string> BuildEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            env[(string)e.Key] = (string)(e.Value ?? "");

        // Strip Claude-Code session vars to avoid "nested session" errors
        env.Remove("CLAUDECODE");
        env.Remove("CLAUDE_CODE_ENTRY_POINT");
        env.Remove("CLAUDE_CODE_PARENT_SESSION_ID");

        // Set terminal hints
        env["COLORTERM"] = "truecolor";
        env["TERM"] = "xterm-256color";
        env["MCP_TIMEOUT"] = "15000";
        return env;
    }

    private sealed record SessionState(string TabId, string ShellPath, string Cwd, IPtySession Pty);
}
```

- [ ] **Step 5: Run — verify tests pass**

Run: `dotnet test --filter FullyQualifiedName~TerminalSessionManagerTests`
Expected: All 6 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/TerminalSessionManager.cs src/SessionInfo.cs tests/TerminalSessionManagerTests.cs
git commit -m "feat: TerminalSessionManager basic CRUD with mockable PTY factory"
```

---

## Task 9: `TerminalSessionManager` — read loop, ring buffer, coalesced output

**Files:**
- Modify: `src/TerminalSessionManager.cs`
- Test: `tests/TerminalSessionManagerStreamingTests.cs`

- [ ] **Step 1: Write the failing tests for output streaming**

Create `tests/TerminalSessionManagerStreamingTests.cs`:

```csharp
using FluentAssertions;
using MxStudioProTerminal;
using Xunit;

namespace MxStudioProTerminal.Tests;

public class TerminalSessionManagerStreamingTests
{
    [Fact]
    public async Task PtyOutput_FiresOutputEvent_AfterCoalesceWindow()
    {
        var fake = new StreamingFakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        var received = new List<byte[]>();
        mgr.Output += (id, data) => received.Add(data);

        var id = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\", 80, 24);
        fake.LastSession.PushOutput(new byte[] { 0x41, 0x42 });
        fake.LastSession.PushOutput(new byte[] { 0x43 });

        // Output is coalesced on a ~16ms timer; allow >50ms for it to flush.
        await Task.Delay(100);

        received.Should().HaveCount(1, "two pushes within the coalesce window become one event");
        received[0].Should().Equal(0x41, 0x42, 0x43);
    }

    [Fact]
    public async Task SnapshotBuffer_ReturnsAllOutputSeen()
    {
        var fake = new StreamingFakePtyFactory();
        var mgr = new TerminalSessionManager(fake, ringBufferBytes: 64);
        var id = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\", 80, 24);
        fake.LastSession.PushOutput(new byte[] { 1, 2, 3 });
        fake.LastSession.PushOutput(new byte[] { 4, 5 });

        await Task.Delay(100); // let read loop catch up

        mgr.SnapshotBuffer(id).Should().Equal(1, 2, 3, 4, 5);
    }

    [Fact]
    public async Task SnapshotBuffer_TruncatesAtRingCapacity()
    {
        var fake = new StreamingFakePtyFactory();
        var mgr = new TerminalSessionManager(fake, ringBufferBytes: 4);
        var id = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\", 80, 24);
        fake.LastSession.PushOutput(new byte[] { 1, 2, 3, 4, 5, 6 });

        await Task.Delay(100);

        mgr.SnapshotBuffer(id).Should().Equal(3, 4, 5, 6);
    }

    [Fact]
    public async Task PtyExits_FiresExitedEvent_AndRemovesSession()
    {
        var fake = new StreamingFakePtyFactory();
        var mgr = new TerminalSessionManager(fake);
        int? capturedCode = null;
        string? capturedId = null;
        mgr.Exited += (id, code) => { capturedId = id; capturedCode = code; };

        var id = await mgr.CreateSessionAsync("cmd.exe", Array.Empty<string>(), "C:\\", 80, 24);
        fake.LastSession.RaiseExited(7);

        await Task.Delay(50);

        capturedId.Should().Be(id);
        capturedCode.Should().Be(7);
        mgr.ListSessions().Should().BeEmpty();
    }
}

internal sealed class StreamingFakePtyFactory : IPtyFactory
{
    public StreamingFakePtySession LastSession { get; private set; } = null!;

    public Task<IPtySession> SpawnAsync(
        string shellPath, string[] args, string cwd, int cols, int rows,
        IDictionary<string,string> environment, CancellationToken ct)
    {
        LastSession = new StreamingFakePtySession();
        return Task.FromResult<IPtySession>(LastSession);
    }
}

internal sealed class StreamingFakePtySession : IPtySession
{
    private readonly System.Threading.Channels.Channel<byte[]> queue =
        System.Threading.Channels.Channel.CreateUnbounded<byte[]>();
    public int Pid => 4321;
    public int? ExitCode { get; private set; }
    public event EventHandler<int?>? Exited;

    public void PushOutput(byte[] data) => queue.Writer.TryWrite(data);
    public void RaiseExited(int? code) { ExitCode = code; queue.Writer.Complete(); Exited?.Invoke(this, code); }

    public async Task<int> ReadAsync(byte[] buffer, CancellationToken ct)
    {
        try
        {
            var chunk = await queue.Reader.ReadAsync(ct);
            var n = Math.Min(chunk.Length, buffer.Length);
            Array.Copy(chunk, buffer, n);
            return n;
        }
        catch (System.Threading.Channels.ChannelClosedException) { return 0; }
    }

    public Task WriteAsync(byte[] data, CancellationToken ct) => Task.CompletedTask;
    public void Resize(int cols, int rows) { }
    public void Dispose() { try { queue.Writer.Complete(); } catch { } }
}
```

- [ ] **Step 2: Run — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~TerminalSessionManagerStreamingTests`
Expected: Compile fail (missing `SnapshotBuffer`, missing constructor overload taking `ringBufferBytes`).

- [ ] **Step 3: Extend `TerminalSessionManager` with read loop, ring buffer, and coalescing**

Replace `src/TerminalSessionManager.cs` with:

```csharp
using System.Collections.Concurrent;

namespace MxStudioProTerminal;

public sealed class TerminalSessionManager : IDisposable
{
    private const int CoalesceMillis = 16;

    private readonly IPtyFactory factory;
    private readonly int ringBufferBytes;
    private readonly ConcurrentDictionary<string, SessionState> sessions = new();
    private bool disposed;

    public event Action<string, byte[]>? Output;
    public event Action<string, int?>? Exited;

    public TerminalSessionManager(IPtyFactory factory, int ringBufferBytes = 4 * 1024 * 1024)
    {
        this.factory = factory;
        this.ringBufferBytes = ringBufferBytes;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public async Task<string> CreateSessionAsync(
        string shellPath, string[] args, string cwd, int cols, int rows, CancellationToken ct = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(TerminalSessionManager));

        var env = BuildEnvironment();
        var pty = await factory.SpawnAsync(shellPath, args, cwd, cols, rows, env, ct);
        var tabId = Guid.NewGuid().ToString("N");
        var state = new SessionState(tabId, shellPath, cwd, pty, new RingBuffer(ringBufferBytes));
        pty.Exited += (_, code) => OnPtyExited(tabId, code);

        sessions[tabId] = state;
        _ = Task.Run(() => ReadLoopAsync(state, state.Cts.Token));
        return tabId;
    }

    public IReadOnlyList<SessionInfo> ListSessions() =>
        sessions.Values.Select(s => new SessionInfo(
            s.TabId, TitleFor(s.ShellPath), s.ShellPath, s.Cwd, s.Pty.ExitCode is null
        )).ToList();

    public byte[] SnapshotBuffer(string tabId)
    {
        if (!sessions.TryGetValue(tabId, out var s)) return Array.Empty<byte>();
        lock (s.Gate) return s.Ring.Snapshot();
    }

    public void Write(string tabId, byte[] data)
    {
        if (!sessions.TryGetValue(tabId, out var s)) return;
        try { s.Pty.WriteAsync(data, CancellationToken.None).GetAwaiter().GetResult(); }
        catch { /* PTY may have died — Exited handler removes it */ }
    }

    public void Resize(string tabId, int cols, int rows)
    {
        if (sessions.TryGetValue(tabId, out var s))
            try { s.Pty.Resize(cols, rows); } catch { }
    }

    public void Close(string tabId)
    {
        if (sessions.TryRemove(tabId, out var s))
        {
            s.Cts.Cancel();
            s.Pty.Dispose();
        }
    }

    public void DisposeAll()
    {
        foreach (var key in sessions.Keys.ToList())
            Close(key);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        DisposeAll();
    }

    private void OnProcessExit(object? sender, EventArgs e) => DisposeAll();

    private void OnPtyExited(string tabId, int? code)
    {
        if (sessions.TryRemove(tabId, out var s))
        {
            s.Cts.Cancel();
            Exited?.Invoke(tabId, code);
        }
    }

    private async Task ReadLoopAsync(SessionState s, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n;
                try { n = await s.Pty.ReadAsync(buffer, ct); }
                catch (OperationCanceledException) { break; }
                catch { break; }

                if (n <= 0) break;

                var chunk = new byte[n];
                Array.Copy(buffer, chunk, n);

                lock (s.Gate)
                {
                    s.Ring.Write(chunk);
                    s.Pending.Add(chunk);
                    EnsureCoalesceTimerArmed_NoLock(s);
                }
            }
        }
        finally
        {
            // Final flush
            FlushPending(s);
        }
    }

    private void EnsureCoalesceTimerArmed_NoLock(SessionState s)
    {
        if (s.TimerArmed) return;
        s.TimerArmed = true;
        s.Timer.Change(CoalesceMillis, Timeout.Infinite);
    }

    private void FlushPending(SessionState s)
    {
        byte[] toEmit;
        lock (s.Gate)
        {
            s.TimerArmed = false;
            if (s.Pending.Count == 0) return;
            var total = s.Pending.Sum(c => c.Length);
            toEmit = new byte[total];
            var offset = 0;
            foreach (var chunk in s.Pending)
            {
                Array.Copy(chunk, 0, toEmit, offset, chunk.Length);
                offset += chunk.Length;
            }
            s.Pending.Clear();
        }
        Output?.Invoke(s.TabId, toEmit);
    }

    private static string TitleFor(string shellPath) =>
        Path.GetFileNameWithoutExtension(shellPath);

    private static IDictionary<string, string> BuildEnvironment()
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables())
            env[(string)e.Key] = (string)(e.Value ?? "");
        env.Remove("CLAUDECODE");
        env.Remove("CLAUDE_CODE_ENTRY_POINT");
        env.Remove("CLAUDE_CODE_PARENT_SESSION_ID");
        env["COLORTERM"] = "truecolor";
        env["TERM"] = "xterm-256color";
        env["MCP_TIMEOUT"] = "15000";
        return env;
    }

    private sealed class SessionState
    {
        public string TabId { get; }
        public string ShellPath { get; }
        public string Cwd { get; }
        public IPtySession Pty { get; }
        public RingBuffer Ring { get; }
        public List<byte[]> Pending { get; } = new();
        public bool TimerArmed { get; set; }
        public Timer Timer { get; private set; } = null!;
        public CancellationTokenSource Cts { get; } = new();
        public object Gate { get; } = new();

        public SessionState(string tabId, string shellPath, string cwd, IPtySession pty, RingBuffer ring)
        {
            TabId = tabId; ShellPath = shellPath; Cwd = cwd; Pty = pty; Ring = ring;
        }

        public void AttachTimer(TimerCallback cb) => Timer = new Timer(cb);
    }
}
```

The Timer needs to call back into the manager's `FlushPending(state)`. The `SessionState` constructor cannot reference the manager method (no `this` for the manager available), so `AttachTimer` is wired up by the manager after constructing state. Replace the `CreateSessionAsync` body with this version that wires the timer correctly:

```csharp
public async Task<string> CreateSessionAsync(
    string shellPath, string[] args, string cwd, int cols, int rows, CancellationToken ct = default)
{
    if (disposed) throw new ObjectDisposedException(nameof(TerminalSessionManager));

    var env = BuildEnvironment();
    var pty = await factory.SpawnAsync(shellPath, args, cwd, cols, rows, env, ct);
    var tabId = Guid.NewGuid().ToString("N");
    var state = new SessionState(tabId, shellPath, cwd, pty, new RingBuffer(ringBufferBytes));
    state.AttachTimer(_ => FlushPending(state));
    pty.Exited += (_, code) => OnPtyExited(tabId, code);

    sessions[tabId] = state;
    _ = Task.Run(() => ReadLoopAsync(state, state.Cts.Token));
    return tabId;
}
```

- [ ] **Step 4: Run — verify tests pass**

Run: `dotnet test`
Expected: All tests pass — the 6 from Task 8 plus the 4 new streaming tests.

- [ ] **Step 5: Commit**

```bash
git add src/TerminalSessionManager.cs tests/TerminalSessionManagerStreamingTests.cs
git commit -m "feat: read loop with ring buffer + 16ms output coalescing"
```

---

## Task 10: UI build chain — `ui/` scaffold and esbuild

**Files:**
- Create: `ui/package.json`
- Create: `ui/tsconfig.json`
- Create: `ui/.gitignore`
- Create: `ui/esbuild.mjs`
- Create: `ui/index.html`

- [ ] **Step 1: `ui/package.json`**

```json
{
  "name": "mxstudiopro-terminal-ui",
  "private": true,
  "type": "module",
  "scripts": {
    "build": "node esbuild.mjs",
    "watch": "node esbuild.mjs --watch",
    "test": "vitest run"
  },
  "dependencies": {
    "@xterm/xterm": "^5.5.0",
    "@xterm/addon-fit": "^0.10.0",
    "@xterm/addon-web-links": "^0.11.0"
  },
  "devDependencies": {
    "esbuild": "^0.24.0",
    "typescript": "^5.4.0",
    "vitest": "^2.0.0",
    "@vitest/ui": "^2.0.0"
  }
}
```

- [ ] **Step 2: `ui/tsconfig.json`**

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "noImplicitOverride": true,
    "isolatedModules": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "lib": ["ES2022", "DOM", "DOM.Iterable"]
  },
  "include": ["src/**/*"]
}
```

- [ ] **Step 3: `ui/.gitignore`**

```
node_modules/
dist/
```

- [ ] **Step 4: `ui/esbuild.mjs`**

```js
import * as esbuild from "esbuild";
import { copyFileSync, mkdirSync } from "fs";
import { fileURLToPath } from "url";
import { dirname, resolve } from "path";

const here = dirname(fileURLToPath(import.meta.url));
const wwwroot = resolve(here, "..", "wwwroot");
mkdirSync(wwwroot, { recursive: true });
copyFileSync(resolve(here, "index.html"), resolve(wwwroot, "index.html"));

const watch = process.argv.includes("--watch");

const ctx = await esbuild.context({
  entryPoints: [resolve(here, "src/main.ts")],
  bundle: true,
  minify: !watch,
  sourcemap: true,
  target: "es2022",
  format: "iife",
  outfile: resolve(wwwroot, "terminal.bundle.js"),
  loader: { ".css": "text" },
  logLevel: "info"
});

if (watch) await ctx.watch();
else { await ctx.rebuild(); await ctx.dispose(); }
```

- [ ] **Step 5: `ui/index.html` (skeleton)**

```html
<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <title>Terminal</title>
  <style>
    html, body { margin: 0; padding: 0; height: 100%; background: #1e1e1e; color: #d4d4d4; font-family: 'Segoe UI', sans-serif; overflow: hidden; }
    #app { display: flex; flex-direction: column; height: 100vh; }
    #tab-strip { display: flex; align-items: center; background: #252526; border-bottom: 1px solid #3e3e42; flex-shrink: 0; min-height: 32px; }
    .tab { padding: 6px 12px; cursor: pointer; border-right: 1px solid #3e3e42; display: flex; align-items: center; gap: 6px; white-space: nowrap; user-select: none; }
    .tab.active { background: #1e1e1e; border-bottom: 2px solid #0e639c; }
    .tab-title { font-size: 12px; }
    .tab-close { color: #888; padding: 0 4px; border-radius: 2px; }
    .tab-close:hover { background: #3e3e42; color: #fff; }
    .tab-new, .tab-settings { padding: 6px 10px; cursor: pointer; color: #888; }
    .tab-new:hover, .tab-settings:hover { color: #fff; background: #2d2d30; }
    #terminals { flex: 1; min-height: 0; position: relative; }
    .terminal-host { position: absolute; top: 0; left: 0; right: 0; bottom: 0; display: none; }
    .terminal-host.active { display: block; }
    #settings-modal { position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: none; align-items: center; justify-content: center; z-index: 100; }
    #settings-modal.visible { display: flex; }
    .modal { background: #252526; padding: 24px; border-radius: 6px; min-width: 400px; }
    .modal h3 { margin: 0 0 16px; }
    .field { margin-bottom: 12px; }
    .field label { display: block; font-size: 11px; color: #888; margin-bottom: 4px; }
    .field input { width: 100%; padding: 6px 8px; background: #1e1e1e; color: #d4d4d4; border: 1px solid #3e3e42; border-radius: 3px; box-sizing: border-box; }
    .actions { display: flex; gap: 8px; justify-content: flex-end; margin-top: 16px; }
    .actions button { padding: 6px 14px; background: #0e639c; color: #fff; border: none; border-radius: 3px; cursor: pointer; }
    .actions button.secondary { background: #3e3e42; }
  </style>
</head>
<body>
  <div id="app">
    <div id="tab-strip">
      <div id="tabs"></div>
      <div class="tab-new" id="btn-new" title="New tab">+</div>
      <div style="flex:1"></div>
      <div class="tab-settings" id="btn-settings" title="Settings">⚙</div>
    </div>
    <div id="terminals"></div>
  </div>
  <div id="settings-modal">
    <div class="modal">
      <h3>Terminal settings</h3>
      <div class="field"><label>Shell path</label><input id="set-shell" type="text"></div>
      <div class="field"><label>Args (space-separated)</label><input id="set-args" type="text"></div>
      <div class="field"><label>Ring buffer (KB)</label><input id="set-ring" type="number" min="64"></div>
      <div class="field"><label>xterm scrollback (lines)</label><input id="set-scroll" type="number" min="100"></div>
      <div class="actions">
        <button class="secondary" id="set-cancel">Cancel</button>
        <button id="set-save">Save</button>
      </div>
    </div>
  </div>
  <script src="terminal.bundle.js"></script>
</body>
</html>
```

- [ ] **Step 6: Smoke build**

```bash
cd ui
npm install
mkdir -p src
echo 'console.log("placeholder");' > src/main.ts
npm run build
ls -la ../wwwroot/
```

Expected: `wwwroot/index.html`, `wwwroot/terminal.bundle.js`, `wwwroot/terminal.bundle.js.map` exist.

- [ ] **Step 7: Commit**

```bash
git add ui/package.json ui/tsconfig.json ui/.gitignore ui/esbuild.mjs ui/index.html ui/src/main.ts
git commit -m "build(ui): esbuild + xterm scaffolding"
```

(Note: `wwwroot/` is gitignored.)

---

## Task 11: TS bridge module (postMessage envelope + base64) (TDD with vitest)

**Files:**
- Create: `ui/src/bridge.ts`
- Test: `ui/src/bridge.test.ts`
- Modify: `ui/package.json` (add `vitest` config if needed)

- [ ] **Step 1: Write the failing tests**

Create `ui/src/bridge.test.ts`:

```typescript
import { describe, it, expect, vi, beforeEach } from "vitest";
import { encodeBase64, decodeBase64, Bridge } from "./bridge.js";

describe("base64 helpers", () => {
  it("round-trips ascii bytes", () => {
    const bytes = new Uint8Array([72, 105]); // "Hi"
    const enc = encodeBase64(bytes);
    expect(enc).toBe("SGk=");
    expect([...decodeBase64(enc)]).toEqual([72, 105]);
  });

  it("round-trips non-utf8 bytes (escape sequences)", () => {
    const bytes = new Uint8Array([0x1b, 0x5b, 0x33, 0x31, 0x6d]); // ESC[31m
    const enc = encodeBase64(bytes);
    expect([...decodeBase64(enc)]).toEqual([0x1b, 0x5b, 0x33, 0x31, 0x6d]);
  });

  it("handles empty array", () => {
    expect(encodeBase64(new Uint8Array())).toBe("");
    expect(decodeBase64("")).toEqual(new Uint8Array());
  });
});

describe("Bridge", () => {
  let postSpy: ReturnType<typeof vi.fn>;
  let listener: ((e: MessageEvent) => void) | null = null;

  beforeEach(() => {
    postSpy = vi.fn();
    listener = null;
    (globalThis as any).chrome = {
      webview: {
        postMessage: postSpy,
        addEventListener: (_evt: string, l: (e: MessageEvent) => void) => { listener = l; },
        removeEventListener: () => { listener = null; }
      }
    };
  });

  it("send wraps payload in {message, data}", () => {
    const b = new Bridge();
    b.send("createTab", { cols: 80, rows: 24 });
    expect(postSpy).toHaveBeenCalledWith({ message: "createTab", data: { cols: 80, rows: 24 } });
  });

  it("send without payload omits data property", () => {
    const b = new Bridge();
    b.send("ready");
    expect(postSpy).toHaveBeenCalledWith({ message: "ready" });
  });

  it("on(message) routes to the handler with parsed data", () => {
    const b = new Bridge();
    const handler = vi.fn();
    b.on("output", handler);

    // Simulate incoming
    listener!({ data: { message: "output", data: { tabId: "x", dataB64: "SGk=" } } } as MessageEvent);

    expect(handler).toHaveBeenCalledWith({ tabId: "x", dataB64: "SGk=" });
  });

  it("on(message) ignores other message types", () => {
    const b = new Bridge();
    const handler = vi.fn();
    b.on("output", handler);
    listener!({ data: { message: "something-else", data: {} } } as MessageEvent);
    expect(handler).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run — verify tests fail (because `bridge.ts` doesn't exist)**

```bash
cd ui
npx vitest run
```

Expected: Module-not-found error on `./bridge.js`.

- [ ] **Step 3: Implement `bridge.ts`**

Create `ui/src/bridge.ts`:

```typescript
declare global {
  interface Window {
    chrome: {
      webview: {
        postMessage: (msg: any) => void;
        addEventListener: (type: "message", listener: (e: MessageEvent) => void) => void;
        removeEventListener: (type: "message", listener: (e: MessageEvent) => void) => void;
      };
    };
  }
}

export function encodeBase64(bytes: Uint8Array): string {
  if (bytes.length === 0) return "";
  let s = "";
  for (let i = 0; i < bytes.length; i++) s += String.fromCharCode(bytes[i]!);
  return btoa(s);
}

export function decodeBase64(b64: string): Uint8Array {
  if (b64.length === 0) return new Uint8Array();
  const s = atob(b64);
  const bytes = new Uint8Array(s.length);
  for (let i = 0; i < s.length; i++) bytes[i] = s.charCodeAt(i);
  return bytes;
}

type Handler<T = any> = (data: T) => void;

export class Bridge {
  private handlers = new Map<string, Set<Handler>>();
  private bound = (e: MessageEvent) => this.dispatch(e);

  constructor() {
    (window as any).chrome.webview.addEventListener("message", this.bound);
  }

  dispose() {
    (window as any).chrome.webview.removeEventListener("message", this.bound);
    this.handlers.clear();
  }

  send(message: string, data?: object): void {
    const env: any = { message };
    if (data !== undefined) env.data = data;
    (window as any).chrome.webview.postMessage(env);
  }

  on<T = any>(message: string, handler: Handler<T>): void {
    if (!this.handlers.has(message)) this.handlers.set(message, new Set());
    this.handlers.get(message)!.add(handler as Handler);
  }

  off<T = any>(message: string, handler: Handler<T>): void {
    this.handlers.get(message)?.delete(handler as Handler);
  }

  private dispatch(e: MessageEvent): void {
    const env = e.data;
    if (!env || typeof env.message !== "string") return;
    const set = this.handlers.get(env.message);
    if (!set) return;
    for (const h of set) {
      try { h(env.data); } catch (err) { console.error("bridge handler", err); }
    }
  }
}
```

- [ ] **Step 4: Run tests — verify they pass**

```bash
cd ui && npx vitest run
```

Expected: All 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add ui/src/bridge.ts ui/src/bridge.test.ts
git commit -m "feat(ui): bridge wrapper with base64 helpers + vitest"
```

---

## Task 12: TS `xterm-tab` module (no automated test — manual smoke later)

**Files:**
- Create: `ui/src/xterm-tab.ts`

- [ ] **Step 1: Implement `xterm-tab.ts`**

Create `ui/src/xterm-tab.ts`:

```typescript
import { Terminal } from "@xterm/xterm";
import { FitAddon } from "@xterm/addon-fit";
import { WebLinksAddon } from "@xterm/addon-web-links";
import xtermCss from "@xterm/xterm/css/xterm.css";

let cssInjected = false;
function ensureCssInjected() {
  if (cssInjected) return;
  const style = document.createElement("style");
  style.textContent = xtermCss;
  document.head.appendChild(style);
  cssInjected = true;
}

export interface XtermTabOptions {
  scrollbackLines: number;
  onInput: (bytes: Uint8Array) => void;
  onResize: (cols: number, rows: number) => void;
}

export class XtermTab {
  readonly host: HTMLDivElement;
  private term: Terminal;
  private fit: FitAddon;

  constructor(opts: XtermTabOptions) {
    ensureCssInjected();
    this.host = document.createElement("div");
    this.host.className = "terminal-host";

    this.term = new Terminal({
      scrollback: opts.scrollbackLines,
      fontFamily: "Cascadia Mono, Consolas, 'Courier New', monospace",
      fontSize: 13,
      theme: { background: "#1e1e1e", foreground: "#d4d4d4" },
      allowProposedApi: true,
      cursorBlink: true,
    });
    this.fit = new FitAddon();
    this.term.loadAddon(this.fit);
    this.term.loadAddon(new WebLinksAddon());
    this.term.open(this.host);

    // xterm gives strings; convert to UTF-8 bytes for the C# side
    const enc = new TextEncoder();
    this.term.onData(s => opts.onInput(enc.encode(s)));
    this.term.onResize(({ cols, rows }) => opts.onResize(cols, rows));
  }

  fitToContainer(): { cols: number; rows: number } {
    this.fit.fit();
    return { cols: this.term.cols, rows: this.term.rows };
  }

  writeBytes(bytes: Uint8Array): void {
    this.term.write(bytes);
  }

  focus(): void { this.term.focus(); }

  dispose(): void {
    this.term.dispose();
    this.host.remove();
  }
}
```

- [ ] **Step 2: Verify it compiles in the UI bundle**

```bash
cd ui && npm run build
```

Expected: Bundle builds with no TypeScript errors. (At this point `main.ts` is still the placeholder from Task 10.)

- [ ] **Step 3: Commit**

```bash
git add ui/src/xterm-tab.ts
git commit -m "feat(ui): XtermTab wraps xterm.js + fit + web-links addons"
```

---

## Task 13: TS `tab-manager` module

**Files:**
- Create: `ui/src/tab-manager.ts`

- [ ] **Step 1: Implement `tab-manager.ts`**

Create `ui/src/tab-manager.ts`:

```typescript
import { Bridge, encodeBase64, decodeBase64 } from "./bridge.js";
import { XtermTab } from "./xterm-tab.js";

interface TabState {
  tabId: string;
  title: string;
  xterm: XtermTab;
  tabEl: HTMLDivElement;
}

export class TabManager {
  private tabs = new Map<string, TabState>();
  private activeTabId: string | null = null;
  private scrollbackLines = 10000;

  constructor(
    private bridge: Bridge,
    private tabsContainer: HTMLDivElement,
    private terminalsContainer: HTMLDivElement,
  ) {
    bridge.on("tabsList", (d: { tabs: { tabId: string; title: string; alive: boolean }[] }) => {
      // On reattach: rebuild tabs we don't have yet, then request replay for each.
      for (const t of d.tabs) {
        if (!this.tabs.has(t.tabId)) this.attachExistingTab(t.tabId, t.title);
        this.bridge.send("replay", { tabId: t.tabId });
      }
      if (d.tabs.length > 0 && !this.activeTabId) this.activate(d.tabs[0]!.tabId);
    });

    bridge.on("tabCreated", (d: { tabId: string; title: string }) => {
      this.attachExistingTab(d.tabId, d.title);
      this.activate(d.tabId);
    });

    bridge.on("tabClosed", (d: { tabId: string }) => this.removeTab(d.tabId));

    bridge.on("output", (d: { tabId: string; dataB64: string }) => {
      this.tabs.get(d.tabId)?.xterm.writeBytes(decodeBase64(d.dataB64));
    });

    bridge.on("replayData", (d: { tabId: string; dataB64: string }) => {
      this.tabs.get(d.tabId)?.xterm.writeBytes(decodeBase64(d.dataB64));
    });

    bridge.on("exit", (d: { tabId: string; exitCode?: number }) => {
      const t = this.tabs.get(d.tabId);
      if (!t) return;
      t.tabEl.querySelector(".tab-title")!.textContent = `${t.title} (exited)`;
      t.tabEl.style.opacity = "0.6";
    });

    bridge.on("error", (d: { message: string; context?: string }) => {
      console.error("[terminal:error]", d.message, d.context);
    });

    window.addEventListener("resize", () => this.resizeActive());
  }

  setScrollbackLines(n: number) { this.scrollbackLines = n; }

  newTab(): void {
    // Build a temporary xterm to measure cols/rows for the host viewport
    const probe = new XtermTab({
      scrollbackLines: 100,
      onInput: () => {},
      onResize: () => {},
    });
    this.terminalsContainer.appendChild(probe.host);
    probe.host.classList.add("active");
    const { cols, rows } = probe.fitToContainer();
    probe.dispose();
    this.bridge.send("createTab", { cols, rows });
  }

  closeActiveTab(): void {
    if (this.activeTabId) this.bridge.send("closeTab", { tabId: this.activeTabId });
  }

  private attachExistingTab(tabId: string, title: string) {
    const xterm = new XtermTab({
      scrollbackLines: this.scrollbackLines,
      onInput: bytes => this.bridge.send("input", { tabId, dataB64: encodeBase64(bytes) }),
      onResize: (cols, rows) => this.bridge.send("resize", { tabId, cols, rows }),
    });
    this.terminalsContainer.appendChild(xterm.host);

    const tabEl = document.createElement("div");
    tabEl.className = "tab";
    tabEl.innerHTML = `<span class="tab-title">${title}</span><span class="tab-close">×</span>`;
    tabEl.addEventListener("click", e => {
      if ((e.target as HTMLElement).classList.contains("tab-close")) {
        this.bridge.send("closeTab", { tabId });
      } else {
        this.activate(tabId);
      }
    });
    this.tabsContainer.appendChild(tabEl);

    this.tabs.set(tabId, { tabId, title, xterm, tabEl });
  }

  private removeTab(tabId: string) {
    const t = this.tabs.get(tabId);
    if (!t) return;
    t.xterm.dispose();
    t.tabEl.remove();
    this.tabs.delete(tabId);
    if (this.activeTabId === tabId) {
      this.activeTabId = null;
      const next = this.tabs.keys().next().value;
      if (next) this.activate(next);
    }
  }

  private activate(tabId: string) {
    if (this.activeTabId === tabId) return;
    this.activeTabId = tabId;
    for (const [id, t] of this.tabs) {
      t.tabEl.classList.toggle("active", id === tabId);
      t.xterm.host.classList.toggle("active", id === tabId);
    }
    const t = this.tabs.get(tabId);
    if (t) {
      const { cols, rows } = t.xterm.fitToContainer();
      this.bridge.send("resize", { tabId, cols, rows });
      t.xterm.focus();
    }
  }

  private resizeActive() {
    if (!this.activeTabId) return;
    const t = this.tabs.get(this.activeTabId);
    if (!t) return;
    const { cols, rows } = t.xterm.fitToContainer();
    this.bridge.send("resize", { tabId: this.activeTabId, cols, rows });
  }
}
```

- [ ] **Step 2: Verify it compiles**

```bash
cd ui && npm run build
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ui/src/tab-manager.ts
git commit -m "feat(ui): tab manager with reattach + activation"
```

---

## Task 14: TS `settings-modal` module

**Files:**
- Create: `ui/src/settings-modal.ts`

- [ ] **Step 1: Implement `settings-modal.ts`**

Create `ui/src/settings-modal.ts`:

```typescript
import { Bridge } from "./bridge.js";

export class SettingsModal {
  private modal = document.getElementById("settings-modal") as HTMLDivElement;
  private inpShell = document.getElementById("set-shell") as HTMLInputElement;
  private inpArgs = document.getElementById("set-args") as HTMLInputElement;
  private inpRing = document.getElementById("set-ring") as HTMLInputElement;
  private inpScroll = document.getElementById("set-scroll") as HTMLInputElement;

  constructor(private bridge: Bridge, private onScrollbackChanged: (lines: number) => void) {
    document.getElementById("btn-settings")!.addEventListener("click", () => this.open());
    document.getElementById("set-cancel")!.addEventListener("click", () => this.close());
    document.getElementById("set-save")!.addEventListener("click", () => this.save());

    bridge.on("settings", (d: { shellPath: string; args: string[]; ringBufferKB: number; xtermScrollbackLines: number }) => {
      this.inpShell.value = d.shellPath;
      this.inpArgs.value = d.args.join(" ");
      this.inpRing.value = String(d.ringBufferKB);
      this.inpScroll.value = String(d.xtermScrollbackLines);
      this.onScrollbackChanged(d.xtermScrollbackLines);
    });
  }

  open() {
    this.bridge.send("openSettings");
    this.modal.classList.add("visible");
  }

  close() { this.modal.classList.remove("visible"); }

  private save() {
    const args = this.inpArgs.value.trim();
    this.bridge.send("saveSettings", {
      shellPath: this.inpShell.value.trim() || "powershell.exe",
      args: args ? args.split(/\s+/) : [],
      ringBufferKB: parseInt(this.inpRing.value, 10) || 4096,
      xtermScrollbackLines: parseInt(this.inpScroll.value, 10) || 10000,
    });
    this.onScrollbackChanged(parseInt(this.inpScroll.value, 10) || 10000);
    this.close();
  }
}
```

- [ ] **Step 2: Verify it compiles**

```bash
cd ui && npm run build
```

Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add ui/src/settings-modal.ts
git commit -m "feat(ui): settings modal"
```

---

## Task 15: TS `main.ts` — entry point

**Files:**
- Modify: `ui/src/main.ts`

- [ ] **Step 1: Replace placeholder with real entry**

Overwrite `ui/src/main.ts`:

```typescript
import { Bridge } from "./bridge.js";
import { TabManager } from "./tab-manager.js";
import { SettingsModal } from "./settings-modal.js";

function boot() {
  const bridge = new Bridge();
  const tabsContainer = document.getElementById("tabs") as HTMLDivElement;
  const terminalsContainer = document.getElementById("terminals") as HTMLDivElement;
  const tabMgr = new TabManager(bridge, tabsContainer, terminalsContainer);
  const settings = new SettingsModal(bridge, lines => tabMgr.setScrollbackLines(lines));

  document.getElementById("btn-new")!.addEventListener("click", () => tabMgr.newTab());

  // Tell C# we're ready, then ask what tabs already exist
  bridge.send("ready");
  bridge.send("listTabs");

  // First-time use: if no tabs after a short delay, open one
  setTimeout(() => {
    if ((document.getElementById("tabs") as HTMLDivElement).childElementCount === 0) {
      tabMgr.newTab();
    }
  }, 200);

  // Suppress unused warning
  void settings;
}

if (document.readyState === "loading")
  document.addEventListener("DOMContentLoaded", boot);
else
  boot();
```

- [ ] **Step 2: Verify it compiles and bundles**

```bash
cd ui && npm run build
```

Expected: `wwwroot/terminal.bundle.js` builds with all imports resolved.

- [ ] **Step 3: Commit**

```bash
git add ui/src/main.ts
git commit -m "feat(ui): main entry — boot bridge + tab manager + settings"
```

---

## Task 16: `TerminalWebServer` (no test — manual smoke later)

**Files:**
- Create: `src/TerminalWebServer.cs`

- [ ] **Step 1: Implement `TerminalWebServer`**

Create `src/TerminalWebServer.cs`:

```csharp
using System.ComponentModel.Composition;
using System.Net;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Mendix.StudioPro.ExtensionsAPI.UI.WebServer;

namespace MxStudioProTerminal;

[Export(typeof(WebServerExtension))]
public sealed class TerminalWebServer : WebServerExtension
{
    private readonly IExtensionFileService extensionFileService;

    [ImportingConstructor]
    public TerminalWebServer(IExtensionFileService extensionFileService)
    {
        this.extensionFileService = extensionFileService;
    }

    public override void InitializeWebServer(IWebServer webServer)
    {
        webServer.AddRoute("index.html",          (req, res, ct) => Serve(res, "index.html", "text/html", ct));
        webServer.AddRoute("terminal.bundle.js",  (req, res, ct) => Serve(res, "terminal.bundle.js", "text/javascript", ct));
        webServer.AddRoute("terminal.bundle.js.map", (req, res, ct) => Serve(res, "terminal.bundle.js.map", "application/json", ct));
    }

    private async Task Serve(HttpListenerResponse response, string fileName, string contentType, CancellationToken ct)
    {
        var path = extensionFileService.ResolvePath("wwwroot", fileName);
        if (!File.Exists(path))
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }
        await response.SendFileAndClose(contentType, path, ct);
    }
}
```

- [ ] **Step 2: Verify it compiles**

```bash
dotnet build
```

Expected: No errors. If `IExtensionFileService.ResolvePath` or `SendFileAndClose` signatures differ, adjust based on the IntelliSense / Mendix.StudioPro.ExtensionsAPI.xml types (`C:\Extensions\mxSuperMCP\Mendix.StudioPro.ExtensionsAPI.xml` is the authoritative reference).

- [ ] **Step 3: Commit**

```bash
git add src/TerminalWebServer.cs
git commit -m "feat: WebServerExtension serving wwwroot/index.html + bundle"
```

---

## Task 17: `TerminalPaneViewModel` — WebView ⇄ manager bridge

**Files:**
- Create: `src/TerminalPaneViewModel.cs`

- [ ] **Step 1: Implement `TerminalPaneViewModel`**

Create `src/TerminalPaneViewModel.cs`:

```csharp
using Eto.Forms;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.WebView;
using MxStudioProTerminal.Messages;
using System.Text.Json;

namespace MxStudioProTerminal;

public sealed class TerminalPaneViewModel : WebViewDockablePaneViewModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TerminalSessionManager manager;
    private readonly Func<IModel?> getCurrentApp;
    private readonly Uri webIndexUri;
    private readonly Logger log;

    private IWebView? webView;
    private Action<string, byte[]>? outputHandler;
    private Action<string, int?>?  exitedHandler;

    public TerminalPaneViewModel(
        string title,
        TerminalSessionManager manager,
        Func<IModel?> getCurrentApp,
        Uri webIndexUri,
        Logger log)
    {
        Title = title;
        this.manager = manager;
        this.getCurrentApp = getCurrentApp;
        this.webIndexUri = webIndexUri;
        this.log = log;
    }

    public override void InitWebView(IWebView webView)
    {
        this.webView = webView;
        webView.MessageReceived += OnWebViewMessage;
        webView.Address = webIndexUri;

        outputHandler = (tabId, bytes) => Post("output", new OutputPayload(tabId, Convert.ToBase64String(bytes)));
        exitedHandler = (tabId, code) => Post("exit", new ExitPayload(tabId, code));
        manager.Output += outputHandler;
        manager.Exited += exitedHandler;

        OnClosed += () =>
        {
            if (outputHandler != null) manager.Output -= outputHandler;
            if (exitedHandler != null) manager.Exited -= exitedHandler;
            outputHandler = null;
            exitedHandler = null;
        };
    }

    private void OnWebViewMessage(object? sender, MessageReceivedEventArgs e)
    {
        try
        {
            switch (e.Message)
            {
                case "ready":
                case "listTabs":
                    Post("tabsList", new TabsListPayload(
                        manager.ListSessions().Select(s => new SessionInfoPayload(s.TabId, s.Title, s.ShellPath, s.Cwd, s.Alive)).ToList()
                    ));
                    break;

                case "createTab":
                    HandleCreateTab(GetData<CreateTabPayload>(e));
                    break;

                case "closeTab":
                    manager.Close(GetData<CloseTabPayload>(e).TabId);
                    Post("tabClosed", new TabClosedPayload(GetData<CloseTabPayload>(e).TabId));
                    break;

                case "input":
                {
                    var p = GetData<InputPayload>(e);
                    manager.Write(p.TabId, Convert.FromBase64String(p.DataB64));
                    break;
                }

                case "resize":
                {
                    var p = GetData<ResizePayload>(e);
                    manager.Resize(p.TabId, p.Cols, p.Rows);
                    break;
                }

                case "replay":
                {
                    var p = GetData<ReplayPayload>(e);
                    var snap = manager.SnapshotBuffer(p.TabId);
                    Post("replayData", new ReplayDataPayload(p.TabId, Convert.ToBase64String(snap)));
                    break;
                }

                case "openSettings":
                {
                    var dir = GetProjectDir();
                    var s = dir != null ? TerminalSettings.Load(dir) : TerminalSettings.Defaults();
                    Post("settings", new SettingsPayload(s.ShellPath, s.Args, s.RingBufferKB, s.XtermScrollbackLines));
                    break;
                }

                case "saveSettings":
                {
                    var p = GetData<SaveSettingsPayload>(e);
                    var dir = GetProjectDir();
                    if (dir == null) { Post("error", new ErrorPayload("No Mendix app is open")); break; }
                    var current = TerminalSettings.Load(dir);
                    var updated = current with
                    {
                        ShellPath = p.ShellPath,
                        Args = p.Args,
                        RingBufferKB = p.RingBufferKB ?? current.RingBufferKB,
                        XtermScrollbackLines = p.XtermScrollbackLines ?? current.XtermScrollbackLines,
                    };
                    updated.Save(dir);
                    Post("settings", new SettingsPayload(updated.ShellPath, updated.Args, updated.RingBufferKB, updated.XtermScrollbackLines));
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            log.Error($"OnWebViewMessage({e.Message}) failed", ex);
            Post("error", new ErrorPayload(ex.Message, e.Message));
        }
    }

    private async void HandleCreateTab(CreateTabPayload p)
    {
        try
        {
            var dir = GetProjectDir() ?? Environment.CurrentDirectory;
            var settings = TerminalSettings.Load(GetProjectDir() ?? "");
            var shell = p.ShellPath ?? settings.ShellPath;
            var args = p.Args ?? settings.Args;
            var cwd = p.Cwd ?? dir;

            var tabId = await manager.CreateSessionAsync(shell, args, cwd, p.Cols, p.Rows);
            Post("tabCreated", new TabCreatedPayload(tabId, Path.GetFileNameWithoutExtension(shell), shell, cwd));
        }
        catch (Exception ex)
        {
            log.Error("CreateTab failed", ex);
            Application.Instance.Invoke(() => Post("error", new ErrorPayload($"Failed to start shell: {ex.Message}", "createTab")));
        }
    }

    private void Post(string message, object data)
    {
        if (webView == null) return;
        try { Application.Instance.Invoke(() => webView.PostMessage(message, data)); }
        catch (Exception ex) { log.Error($"PostMessage({message}) failed", ex); }
    }

    private static T GetData<T>(MessageReceivedEventArgs e) where T : class
    {
        if (e.Data is null) throw new InvalidOperationException("Missing data");
        var json = e.Data.ToString();
        return JsonSerializer.Deserialize<T>(json!, Json)
            ?? throw new InvalidOperationException($"Bad payload for {typeof(T).Name}");
    }

    private string? GetProjectDir() => (getCurrentApp()?.Root as IProject)?.DirectoryPath;
}
```

- [ ] **Step 2: Verify it compiles**

```bash
dotnet build
```

If `e.Data` is a `JObject`/`JsonElement` rather than something whose `ToString()` is JSON, adjust `GetData<T>` — the `MCPExtension` codebase has `args.Data["response"]?.ToString()` syntax, suggesting Newtonsoft `JObject`. If so, install `Newtonsoft.Json` and use `((JObject)e.Data).ToObject<T>()`. Confirm the actual type by hovering `e.Data` in the IDE or checking `Mendix.StudioPro.ExtensionsAPI.xml` for `MessageReceivedEventArgs.Data`.

- [ ] **Step 3: Commit**

```bash
git add src/TerminalPaneViewModel.cs
git commit -m "feat: ViewModel bridges WebView messages to SessionManager"
```

---

## Task 18: `TerminalPaneExtension` — dockable pane with lifecycle

**Files:**
- Create: `src/TerminalPaneExtension.cs`

- [ ] **Step 1: Implement `TerminalPaneExtension`**

Create `src/TerminalPaneExtension.cs`:

```csharp
using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI.DockablePane;
using Mendix.StudioPro.ExtensionsAPI.UI.Events;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;

namespace MxStudioProTerminal;

[Export(typeof(DockablePaneExtension))]
public sealed class TerminalPaneExtension : DockablePaneExtension
{
    public const string ID = "MxStudioProTerminal";
    public override string Id => ID;

    private readonly TerminalSessionManager manager;
    private Logger log = null!;
    private bool subscribed;

    [ImportingConstructor]
    public TerminalPaneExtension()
    {
        // Singleton manager — created once, lives for app lifetime
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
            log: log);
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
            try { log.Info("ExtensionUnloading — disposing all PTYs"); manager.DisposeAll(); }
            catch (Exception ex) { log.Error("DisposeAll on unload failed", ex); }
        });
    }
}
```

- [ ] **Step 2: Verify it compiles**

```bash
dotnet build
```

If `Subscribe<ExtensionUnloading>(Action)` signature differs (e.g., expects `Action<ExtensionUnloading>` instead of `Action`), adjust by changing the lambda's parameter list. Check `Mendix.StudioPro.ExtensionsAPI.xml` — search for `UIExtensionBase.Subscribe`.

- [ ] **Step 3: Commit**

```bash
git add src/TerminalPaneExtension.cs
git commit -m "feat: DockablePaneExtension hosting Terminal pane"
```

---

## Task 19: `TerminalMenuExtension` — menu wiring

**Files:**
- Create: `src/TerminalMenuExtension.cs`

- [ ] **Step 1: Implement `TerminalMenuExtension`**

Create `src/TerminalMenuExtension.cs`:

```csharp
using System.ComponentModel.Composition;
using Mendix.StudioPro.ExtensionsAPI.UI.Menu;
using Mendix.StudioPro.ExtensionsAPI.UI.Services;

namespace MxStudioProTerminal;

[Export(typeof(MenuExtension))]
public sealed class TerminalMenuExtension : MenuExtension
{
    private readonly IDockingWindowService docking;

    [ImportingConstructor]
    public TerminalMenuExtension(IDockingWindowService docking) => this.docking = docking;

    public override IEnumerable<MenuViewModel> GetMenus()
    {
        yield return new MenuViewModel(
            caption: "Terminal",
            action: () => docking.OpenPane(TerminalPaneExtension.ID));
    }
}
```

- [ ] **Step 2: Verify compile**

```bash
dotnet build
```

Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add src/TerminalMenuExtension.cs
git commit -m "feat: menu item to open the Terminal pane"
```

---

## Task 20: csproj — wire UI build + deploy

**Files:**
- Modify: `MxStudioProTerminal.csproj`

- [ ] **Step 1: Add `BuildUi` and `DeployToMendix` MSBuild targets**

Replace `MxStudioProTerminal.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <AssemblyName>MxStudioProTerminal</AssemblyName>
    <RootNamespace>MxStudioProTerminal</RootNamespace>
    <LangVersion>preview</LangVersion>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Mendix.StudioPro.ExtensionsAPI" Version="11.*" />
    <PackageReference Include="Eto.Forms" Version="2.9.*" />
    <PackageReference Include="Pty.Net" Version="0.1.21" />  <!-- pinned in Task 2 -->
    <PackageReference Include="System.Text.Json" Version="8.0.*" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="manifest.json"><CopyToOutputDirectory>Always</CopyToOutputDirectory></Content>
    <Content Include="wwwroot\**\*">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
      <Visible>false</Visible>
    </Content>
  </ItemGroup>

  <Target Name="BuildUi" BeforeTargets="BeforeBuild"
          Inputs="ui\src\**\*;ui\package.json;ui\esbuild.mjs;ui\index.html"
          Outputs="wwwroot\terminal.bundle.js">
    <Message Importance="high" Text="Installing UI dependencies (first build only)…" Condition="!Exists('ui\node_modules')" />
    <Exec Command="npm install --prefix ui --silent" Condition="!Exists('ui\node_modules')" />
    <Message Importance="high" Text="Bundling UI…" />
    <Exec Command="node ui\esbuild.mjs" />
  </Target>

  <Target Name="DeployToMendix" AfterTargets="PostBuildEvent" Condition="'$(MendixDeployTarget)' != ''">
    <Message Importance="high" Text="Deploying to $(MendixDeployTarget)\extensions\MxStudioProTerminal" />
    <Exec Command="xcopy /y /s /i /q &quot;$(TargetDir)*&quot; &quot;$(MendixDeployTarget)\extensions\MxStudioProTerminal&quot;" />
  </Target>
</Project>
```

(Replace `0.1.21` with whatever Pty.Net version actually pinned in Task 2.)

- [ ] **Step 2: Smoke build the full pipeline**

```bash
rm -rf wwwroot bin obj
dotnet build
ls wwwroot/   # should contain index.html, terminal.bundle.js
ls bin/Debug/net8.0/wwwroot/  # should contain the same files
```

Expected: `wwwroot/` populated by esbuild, then copied into `bin/Debug/net8.0/wwwroot/`.

- [ ] **Step 3: Commit**

```bash
git add MxStudioProTerminal.csproj
git commit -m "build: BuildUi + DeployToMendix MSBuild targets"
```

---

## Task 21: Setup `Directory.Build.props` for local deploy

**Files:**
- Create: `Directory.Build.props` (gitignored — per developer)

- [ ] **Step 1: Copy the example, point at the deploy target**

```bash
cp Directory.Build.props.example Directory.Build.props
```

Edit `Directory.Build.props` to set `MendixDeployTarget` to a real Mendix project path on your machine, e.g.:

```xml
<Project>
  <PropertyGroup>
    <MendixDeployTarget>C:\Mendix Projects\MyTestApp</MendixDeployTarget>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Verify deploy works**

```bash
dotnet build
ls "C:/Mendix Projects/MyTestApp/extensions/MxStudioProTerminal/"
```

Expected: `MxStudioProTerminal.dll`, `manifest.json`, `wwwroot/index.html`, etc. all present.

- [ ] **Step 3: Do NOT commit `Directory.Build.props`** (it's gitignored — per-developer file).

---

## Task 22: Manual smoke — first run in Studio Pro

**Files:** none (manual verification).

- [ ] **Step 1: Confirm the deploy target is set**

```bash
cat Directory.Build.props
dotnet build
```

Expected: Build succeeds; `extensions/MxStudioProTerminal/` folder populated in your Mendix project.

- [ ] **Step 2: Launch Studio Pro with extension development mode**

Right-click your Studio Pro shortcut → Properties → Target field, append `--enable-extension-development`. Or run from a terminal:

```cmd
"C:\Program Files\Mendix\<version>\modeler\studiopro.exe" --enable-extension-development
```

- [ ] **Step 3: Open the Mendix project where the extension was deployed**

In Studio Pro: File → Open Project → select the project directory.

- [ ] **Step 4: Verify the menu item appears**

Look for "Terminal" under the extension menu (location depends on Studio Pro version — usually under `Extensions` or `View`).

- [ ] **Step 5: Open the pane, verify a default tab spawns**

Click "Terminal" menu → the dockable pane should open. Within ~200 ms, a default PowerShell tab should appear, prompted at `<MendixProject>/`.

Expected: `PS C:\Mendix Projects\MyTestApp>` prompt.

- [ ] **Step 6: Type a command, verify it executes**

```
Get-ChildItem
```

Expected: Listing of the project directory (`Modules`, `resources`, etc.).

- [ ] **Step 7: Diagnose if anything fails**

Check `<project>/resources/terminal.log` for errors. Common issues:

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| Menu item missing | Extension dev mode off | Confirm `--enable-extension-development` flag |
| Menu missing after F4 reload | Files not deployed | Check `Directory.Build.props` and `bin/.../` paths |
| Pane opens but blank | WebServer route 404s | Check WebView DevTools (right-click pane → Inspect, if `AllowReload`/`AllowedDevTools` enabled), or check Studio Pro's own log |
| Pane opens, "Failed to start shell" | PowerShell not on PATH | Settings cog → set absolute path like `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe` |
| Tabs don't appear | `MessageReceivedEventArgs.Data` shape wrong | Inspect `e.Data.GetType()` at runtime; adjust `GetData<T>()` in `TerminalPaneViewModel` |

If any failure persists, write a minimal repro test against the failing layer (manager, web server, viewmodel), fix, and rerun.

- [ ] **Step 8: Commit a record of what worked**

If anything in the test required code changes, commit those. If the smoke passed clean:

```bash
echo "Smoke passed at $(date)" >> docs/smoke-log.md
git add docs/smoke-log.md
git commit -m "docs: smoke-test pass record"
```

---

## Task 23: Manual smoke — Claude Code session

**Files:** none.

- [ ] **Step 1: Ensure Claude Code CLI is installed**

```bash
where claude
# or
npm list -g --depth=0 | grep claude-code
```

Expected: a path. If missing: `npm i -g @anthropic-ai/claude-code` and `claude login`.

- [ ] **Step 2: From the open Terminal tab in Studio Pro, launch Claude**

```
claude
```

Expected: Claude Code TUI starts. Input box visible at bottom, "claude>" prompt.

- [ ] **Step 3: Send a query**

```
> What is in this project?
```

Expected: Claude responds, streaming text. Colors render. The input box re-renders without artifacts.

- [ ] **Step 4: Verify reattach**

While Claude is mid-conversation:
1. Click the X on the pane (close it).
2. Wait 5 seconds.
3. Reopen via menu.
4. Confirm: tab strip rebuilt, your tab present, last ~50 messages visible (replayed from ring buffer), conversation can continue.

- [ ] **Step 5: Verify multiple tabs**

1. Click "+" → second tab opens (PowerShell at project root).
2. In second tab, run `codex` (or any other CLI you want to validate).
3. Switch between tabs — both stay alive.

- [ ] **Step 6: Verify settings persistence**

1. Cog icon → change shell to `cmd.exe` → Save.
2. Click "+" → new tab uses `cmd.exe`.
3. Restart Studio Pro → reopen project → cog icon shows `cmd.exe` still.
4. Inspect `<project>/resources/terminal-settings.json` — confirms persistence.

- [ ] **Step 7: Verify Mendix-app-close cleanup**

1. With a Claude session active, close the Mendix app (File → Close Project).
2. Open a different Mendix project.
3. Open Terminal pane → expect: empty tab strip, default new tab spawns.
4. Confirm: in Task Manager, no orphan `claude.exe` / `powershell.exe` from the previous session.

- [ ] **Step 8: Tag a v0.1 release**

```bash
git tag -a v0.1.0 -m "Initial working terminal extension"
```

(Don't push the tag — just mark locally for now.)

- [ ] **Step 9: Document any bugs found**

For each issue surfaced in Steps 1-7, capture in `docs/known-issues.md`:

```markdown
# Known Issues — v0.1.0

## <issue title>
**Symptom:** …
**Repro:** …
**Suspected cause:** …
**Workaround:** …
```

Then commit:

```bash
git add docs/known-issues.md
git commit -m "docs: capture v0.1 known issues"
```

---

## Self-review

The plan covers each section of the spec:

| Spec section | Implementing task(s) |
|--------------|---------------------|
| §1 Goal / §2 Non-goals | Task 1 README, scope discipline throughout |
| §3 Architecture / boundaries | Tasks 8, 9, 16, 17, 18, 19 — five C# classes with the prescribed split |
| §4 Asset hosting (`WebServerExtension`) | Task 16 |
| §5 Message protocol | Task 4 (DTOs) + Task 11 (TS bridge) + Task 17 (ViewModel switch) |
| §5 Streaming, batching, ring buffer | Tasks 3, 9 |
| §5 Reattach flow | Task 13 (`tabsList` → `replay` in TabManager) + Task 17 (`replay` handler) |
| §6 Spawn / env stripping | Task 8 (`BuildEnvironment`) |
| §6 Output read loop / coalescing | Task 9 |
| §6 Failure modes | Tasks 8 (Write swallow), 9 (read-loop catch), 17 (try/catch + error message) |
| §6 Lifecycle (`ExtensionUnloading` + `ProcessExit`) | Tasks 8 (ProcessExit), 18 (ExtensionUnloading) |
| §7 `TerminalSettings` | Task 5 |
| §7 `Logging` | Task 6 |
| §7 `wwwroot/` UI structure | Tasks 10–15 |
| §8 csproj + deploy | Tasks 2, 20, 21 |
| §8 Logs path | Task 6 implementation, used by Task 17 |
| §9 Open implementation questions | Tasks 2 step 5–6 (Pty.Net pin), Task 7 step 1 (API spike), Task 17 step 2 (`MessageReceivedEventArgs.Data` type), Task 18 step 2 (`Subscribe` signature) |

Every task has either explicit code or a manual verification step. No "TBD" / "TODO" / placeholders. Type names are consistent across tasks (`TabId` is `string` everywhere; `IPtySession` matches its definition in Task 7; `OutputPayload` has `dataB64` lowercase as defined in Task 4).

Two TDD-resistant components are explicitly excluded from automated tests (TerminalWebServer, TerminalPaneExtension/MenuExtension/ViewModel) and covered by the manual smoke in Tasks 22–23. This is honest — they need a Mendix host to run.
