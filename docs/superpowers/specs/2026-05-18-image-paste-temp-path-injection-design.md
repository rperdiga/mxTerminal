# Image-Paste → Temp-Path Injection — Design

**Status:** Approved (design)
**Date:** 2026-05-18
**Owner:** Concord
**Targets:** Concord.Host10x + Concord.Host11x (via shared `Concord.Core` + shared `ui/` bundle)

## Problem

When a user copies an image *file* from Finder/Explorer and pastes into the Concord terminal, Claude Code (and the other CLIs) auto-recognize the pasted file path and treat it as an image attachment. But when the clipboard holds **raw image bytes** (e.g., a macOS screenshot, a "Copy Image" from a browser, a paste from a screenshot tool), Concord today silently drops the paste — the [JS paste handler at `ui/src/xterm-tab.ts:127-230`](../../../ui/src/xterm-tab.ts#L127-L230) only reads `text/plain` from `clipboardData`.

Goal: intercept raw-image pastes, write the bytes to a temp file on disk, and inject the absolute file path into the PTY in place of the raw bytes. From the CLI's point of view, this is indistinguishable from the user having pasted a file path.

## Non-goals

- Inline image preview in the terminal.
- Image format conversion or re-encoding.
- A user-facing settings toggle (always on for v1; revisit if a real need surfaces).
- CLI-specific behavior — works the same for Claude Code, Codex, Copilot CLI, and a raw bash/zsh shell.
- Cloud / remote temp storage. Local disk only.
- Persisting pasted images beyond 24 h.

## High-level design

**Approach: C# one-shot write + inject.** The JS paste handler detects image MIME types in `clipboardData`, base64-encodes the bytes, and sends a single new bridge message `"paste-image"` with `{ tabId, mime, bytesB64, nameHint? }`. The C# host writes the bytes to a uniquely-named temp file, then injects the absolute path bytes into the PTY via the existing `TerminalSessionManager.Write(tabId, bytes)` path. No round-trip back to JS.

```
clipboard image
      │
      ▼
docPasteHandler (xterm-tab.ts)
  - detects image/* in clipboardData.types
  - reads File via getAsFile()
  - base64-encodes via FileReader
  - bridge.send("paste-image", {tabId, mime, bytesB64, nameHint})
      │
      ▼
WebView ─── postMessage ───► OnWebViewMessage (TerminalPaneViewModel.cs)
                                "paste-image" case:
                                  - validate size ≤ 25 MB
                                  - pick extension from MIME
                                  - sanitize nameHint
                                  - write <TempPath>/Concord/pastes/<name>.<ext>
                                  - manager.Write(tabId, UTF8(absolutePath))
                                       │
                                       ▼
                                  PTY (ConPty / Unix)
                                       │
                                       ▼
                                  CLI sees: /tmp/Concord/pastes/screenshot-…png
```

## Components

### 1. JS: image-paste detection (`ui/src/xterm-tab.ts`)

Extend the existing capture-phase `docPasteHandler`:

1. **Before** reading `text/plain`, inspect `ev.clipboardData.types`.
2. If any type starts with `image/`, take the image branch:
   - `const file = ev.clipboardData.items[i].getAsFile()` for the first image item.
   - `ev.preventDefault()` and `ev.stopPropagation()` so the existing text path doesn't also fire.
   - Read via `FileReader.readAsArrayBuffer`, then base64-encode (chunked to avoid stack-blowing `apply` on large buffers).
   - `bridge.send("paste-image", { tabId, mime: file.type, bytesB64, nameHint: file.name || null })`.
   - Surface the same notice infrastructure used for large pastes if the image exceeds the size cap: show "Image too large to paste (X MB > 25 MB limit)" and abort send.
3. If both image and text are present, **prefer image.** A user who copied a screenshot wants the image, not whatever fallback text the source app supplied.
4. If only text, fall through to the existing branch.

A helper `extractClipboardImage(clipboardData): { file: File, mime: string } | null` lives in `ui/src/paste.ts` so it's unit-testable in isolation. `ui/src/paste.test.ts` gains tests for it.

### 2. JS ↔ C# bridge protocol

New message, no response:

```ts
// JS → C#
{
  message: "paste-image",
  data: {
    tabId: string,
    mime: string,          // e.g. "image/png"
    bytesB64: string,      // base64 of raw image bytes
    nameHint: string | null // clipboard-provided filename, may be "image.png" or null
  }
}
```

No new C# → JS message. On error, C# emits a diagnostic log and (for size-cap) the JS side already rejected before sending.

### 3. C# handler (`TerminalPaneViewModel.OnWebViewMessage`)

Add a `"paste-image"` case to the switch in both [Concord.Host10x](../../../src/Concord.Host10x/Pane/TerminalPaneViewModel.cs) and [Concord.Host11x](../../../src/Concord.Host11x/Pane/TerminalPaneViewModel.cs) variants. To avoid duplication, the actual work lives in a new class in `Concord.Core`:

**`Concord.Core/Terminal/PasteImageHandler.cs`** (new):

```csharp
public sealed class PasteImageHandler
{
    public const long MaxBytes = 25L * 1024 * 1024;  // 25 MB
    private readonly string _baseDir;                 // <TempPath>/Concord/pastes

    public PasteImageHandler(string? baseDirOverride = null) { ... }

    // Returns absolute path written. Throws on size violation or IO error.
    public string WriteImage(string mime, byte[] bytes, string? nameHint);

    // Sweep files older than threshold. Called once on extension startup.
    public int CleanupOlderThan(TimeSpan age);
}
```

Filename composition:

- If `nameHint` is present and non-empty: sanitize (strip `\/:*?"<>|` and control chars, replace whitespace with `_`, trim to 64 chars, strip extension), use as `<stem>`.
- Else: `<stem>` = `"image"`.
- Extension picked from MIME map: `image/png`→`.png`, `image/jpeg`→`.jpg`, `image/gif`→`.gif`, `image/webp`→`.webp`, `image/bmp`→`.bmp`, `image/tiff`→`.tiff`, default → `.png`.
- Final filename: `<stem>-<yyyyMMddTHHmmssZ>-<guid8>.<ext>` (e.g. `screenshot-20260518T143012Z-a1b2c3d4.png`). Timestamp is UTC; guid8 is first 8 hex chars of a new `Guid` for collision safety within the same second.

The `TerminalPaneViewModel` "paste-image" case:

1. Parse payload (use existing JSON deserialization pattern; new `ImagePastePayload` record).
2. Base64-decode `bytesB64`. If decode fails → log + abort.
3. Look up `SessionState` for `tabId`. If missing → log + abort.
4. `var path = _pasteHandler.WriteImage(mime, bytes, nameHint);` (catch and log `IOException` / argument errors).
5. `await manager.Write(tabId, Encoding.UTF8.GetBytes(path));` — no trailing space or newline; user types what they want next.
6. Log via existing diagnostic channel: `paste-image tab={tabId} bytes={len} path={path}`.

### 4. Cleanup on startup

`PasteImageHandler.CleanupOlderThan(TimeSpan.FromHours(24))` is called once from `TerminalPaneExtension`'s lifecycle init (after the existing first-run / upgrade-apply hooks). Failures are logged but never throw — cleanup is best-effort.

### 5. Telemetry / diagnostics

Reuse the existing `bridge.send("diag", ...)` channel from JS for size-cap rejections, so they show up in the same place as other paste diagnostics. C# side logs via the existing pane logger.

## Data flow summary

```
1. User pastes (Cmd+V / Ctrl+V) in WebView terminal pane
2. docPasteHandler fires (capture phase)
3. Detects image/* MIME → extracts File → base64 → bridge.send("paste-image", …)
4. WebView2 / WKWebView posts message → C# OnWebViewMessage
5. PasteImageHandler.WriteImage() writes <TempPath>/Concord/pastes/…<ext>
6. TerminalSessionManager.Write(tabId, UTF8(path)) → PtySession.WriteAsync(bytes)
7. CLI process reads path from stdin, recognizes it as an image attachment
```

## Error handling

| Condition | Behavior |
|---|---|
| Image > 25 MB (JS-side check) | Show in-terminal notice; do not send bridge message |
| Image > 25 MB (C#-side guard) | Log + drop (defense in depth; JS should have already caught) |
| Base64 decode fails | Log + drop |
| `tabId` not found in `_sessions` | Log + drop |
| Disk write fails (full / perms) | Log + drop; no notice to terminal (rare; surfaces in pane diagnostics) |
| Unknown MIME (e.g. `image/avif`) | Use `.png` extension fallback; bytes still written verbatim |
| Both image and text on clipboard | Prefer image; text is ignored |
| Multiple images on clipboard | Take first; ignore rest (rare; could revisit) |

## Testing

**JS (`ui/src/paste.test.ts`):**

- `extractClipboardImage` returns image when only image present.
- `extractClipboardImage` prefers image when both image + text present.
- `extractClipboardImage` returns null when only text.
- Base64 chunked-encoder produces correct output for known inputs (round-trips bit-exact).

**C# (`tests/Terminal.Tests/Terminal/PasteImageHandlerTests.cs`, new):**

- `WriteImage` chooses correct extension per MIME (parameterized).
- `WriteImage` falls back to `.png` on unknown MIME.
- `WriteImage` sanitizes `nameHint` (strips bad chars, caps length, drops extension from hint).
- `WriteImage` uses `"image"` stem when `nameHint` is null/empty/whitespace.
- `WriteImage` throws on bytes > `MaxBytes`.
- `WriteImage` produces a path under `<TempPath>/Concord/pastes/` and the file content equals input bytes.
- `WriteImage` filename matches `<stem>-<yyyyMMddTHHmmssZ>-<guid8>.<ext>` regex.
- `CleanupOlderThan` deletes files older than threshold, leaves newer alone, ignores non-existent base dir.

**End-to-end integration:** The `TerminalPaneViewModel` layer has no unit-test fixture in this repo (the WebView message dispatcher is tested implicitly via real Studio Pro runs). The paste-image case is covered by (1) `PasteImageHandlerTests` for the file-writing logic, (2) `MessageDtoTests` for the wire-format DTO, and (3) the Mac manual-smoke step in the implementation plan.

## Open questions

None blocking. Future considerations (out of scope for v1):

- Settings toggle to disable.
- "Original filename preservation" beyond the sanitized stem (e.g., honor full filename if the user explicitly opts in).
- Multi-image paste (today: first wins).
- Auto-include a leading `@` for CLIs that use mention syntax (today: just the path).

## Risk and mitigations

- **Large clipboard bombs:** 25 MB cap enforced JS-side (before base64-blowup) and C#-side. base64 inflates by ~33 %, so the bridge message tops out at ~33 MB — well within WebView message limits.
- **Path injection / shell metacharacters:** The injected bytes are an absolute filesystem path with controlled characters (alphanumeric, hyphen, dot, slash). No shell escaping needed because the CLI reads from stdin in a REPL context, not as a shell command.
- **Temp file disclosure:** Files in `<TempPath>/Concord/pastes/` are user-owned and live for ≤ 24 h. Same exposure as any other temp file the user creates.
- **Bracketed paste mode interactions:** Injected bytes go through `manager.Write` which writes raw bytes to the PTY. The path has no newlines, so bracketed-paste behavior is irrelevant.
