# Image-Paste → Temp-Path Injection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a user pastes raw image bytes into the Concord terminal WebView, write the image to a temp file and inject the absolute path bytes into the PTY in place of the raw bytes — so the CLI (Claude Code / Codex / Copilot) sees a file path and recognizes it as an image attachment.

**Architecture:** JS paste handler detects `image/*` in clipboard, base64-encodes the bytes, sends a new `paste-image` bridge message. C# `PasteImageHandler` writes the bytes to `<TempPath>/Concord/pastes/<stem>-<timestamp>-<guid8>.<ext>`, then calls `TerminalSessionManager.Write(tabId, UTF8(absolutePath))` so the path bytes flow through the same PTY-write path as keystrokes. No JS round-trip.

**Tech Stack:** TypeScript + xterm.js (WebView UI) · vitest (JS tests) · C# / .NET 8 / Mendix WebView bridge · xUnit + FluentAssertions (C# tests).

**Spec:** [`docs/superpowers/specs/2026-05-18-image-paste-temp-path-injection-design.md`](../specs/2026-05-18-image-paste-temp-path-injection-design.md)

---

## File Structure

**New files:**
- `src/Concord.Core/Terminal/PasteImageHandler.cs` — writes image bytes to temp file, returns absolute path; also handles cleanup sweep.
- `tests/PasteImageHandlerTests.cs` — xUnit tests for the handler.

**Modified files:**
- `ui/src/paste.ts` — add `extractClipboardImage`, `mimeToExtension`, `sanitizeNameHint` (pure helpers, unit-testable).
- `ui/src/paste.test.ts` — extend with tests for the three new helpers.
- `ui/src/xterm-tab.ts` — extend `docPasteHandler` to branch on image MIME; add `onPasteImage` callback to `XtermTabOptions`.
- `ui/src/tab-manager.ts` — wire `onPasteImage` to `bridge.send("paste-image", …)`.
- `src/Concord.Core/Ui/Messages/Incoming.cs` — add `ImagePastePayload` record.
- `src/Concord.Host10x/Pane/TerminalPaneViewModel.cs` — add `"paste-image"` case to `OnWebViewMessage`; construct `PasteImageHandler` in ctor.
- `src/Concord.Host11x/Pane/TerminalPaneViewModel.cs` — identical edit (same file structure).

**Responsibilities:**
- `paste.ts` stays pure-functions only (no DOM, no I/O); all DOM-side work in `xterm-tab.ts`.
- `PasteImageHandler.cs` owns the temp-file lifecycle (write + cleanup). The ViewModel is glue.

---

## Branch Setup

### Task 0: Create feature branch

**Files:** none (git only)

- [ ] **Step 1: Confirm on main at v6.0.0**

Run:
```bash
git status && git log --oneline -1
```
Expected: `On branch main … nothing to commit, working tree clean` and HEAD at `96d1023 fix(shim): Concord works on macOS — v6.0.0 (#22)`.

- [ ] **Step 2: Cut feature branch**

Run:
```bash
git checkout -b feat/image-paste-temp-path
```
Expected: `Switched to a new branch 'feat/image-paste-temp-path'`.

---

## Phase 1 — JS pure helpers (TDD)

These helpers go in `ui/src/paste.ts` so they can be tested with vitest under Node, no JSDOM clipboard mocks needed for the extraction logic.

### Task 1: Add `mimeToExtension` helper

**Files:**
- Modify: `ui/src/paste.ts` (append at end)
- Modify: `ui/src/paste.test.ts` (append at end)

- [ ] **Step 1: Write failing tests**

Append to `ui/src/paste.test.ts`:
```ts
import { mimeToExtension } from "./paste.js";

describe("mimeToExtension", () => {
  it("maps known image MIME types", () => {
    expect(mimeToExtension("image/png")).toBe(".png");
    expect(mimeToExtension("image/jpeg")).toBe(".jpg");
    expect(mimeToExtension("image/gif")).toBe(".gif");
    expect(mimeToExtension("image/webp")).toBe(".webp");
    expect(mimeToExtension("image/bmp")).toBe(".bmp");
    expect(mimeToExtension("image/tiff")).toBe(".tiff");
  });

  it("falls back to .png for unknown image MIMEs", () => {
    expect(mimeToExtension("image/avif")).toBe(".png");
    expect(mimeToExtension("image/heic")).toBe(".png");
  });

  it("falls back to .png for empty or non-image strings", () => {
    expect(mimeToExtension("")).toBe(".png");
    expect(mimeToExtension("text/plain")).toBe(".png");
  });

  it("is case-insensitive on the MIME", () => {
    expect(mimeToExtension("IMAGE/PNG")).toBe(".png");
  });
});
```

- [ ] **Step 2: Run tests to verify failure**

Run: `cd ui && npx vitest run src/paste.test.ts`
Expected: tests fail with `mimeToExtension is not exported from "./paste.js"`.

- [ ] **Step 3: Implement helper**

Append to `ui/src/paste.ts`:
```ts
/**
 * Map a clipboard image MIME to a file extension (leading dot included).
 * Unknown / non-image inputs fall back to ".png" — the image bytes are written
 * verbatim regardless, and ".png" is the safest default for screenshot tools
 * that drop MIME info.
 */
export function mimeToExtension(mime: string): string {
  const m = mime.toLowerCase();
  switch (m) {
    case "image/png":  return ".png";
    case "image/jpeg": return ".jpg";
    case "image/gif":  return ".gif";
    case "image/webp": return ".webp";
    case "image/bmp":  return ".bmp";
    case "image/tiff": return ".tiff";
    default: return ".png";
  }
}
```

- [ ] **Step 4: Run tests to verify pass**

Run: `cd ui && npx vitest run src/paste.test.ts`
Expected: all `mimeToExtension` tests pass.

- [ ] **Step 5: Commit**

```bash
git add ui/src/paste.ts ui/src/paste.test.ts
git commit -m "feat(paste): mimeToExtension helper for clipboard image MIMEs"
```

---

### Task 2: Add `sanitizeNameHint` helper

**Files:**
- Modify: `ui/src/paste.ts` (append at end)
- Modify: `ui/src/paste.test.ts` (append at end)

- [ ] **Step 1: Write failing tests**

Append to `ui/src/paste.test.ts`:
```ts
import { sanitizeNameHint } from "./paste.js";

describe("sanitizeNameHint", () => {
  it("returns 'image' for null, empty, or whitespace-only input", () => {
    expect(sanitizeNameHint(null)).toBe("image");
    expect(sanitizeNameHint("")).toBe("image");
    expect(sanitizeNameHint("   ")).toBe("image");
  });

  it("strips file extension from hint", () => {
    expect(sanitizeNameHint("screenshot.png")).toBe("screenshot");
    expect(sanitizeNameHint("photo.jpeg")).toBe("photo");
    expect(sanitizeNameHint("scan.tar.gz")).toBe("scan.tar"); // only last dot stripped
  });

  it("strips forbidden filesystem chars", () => {
    expect(sanitizeNameHint("a/b\\c:d*e?f\"g<h>i|j.png")).toBe("a_b_c_d_e_f_g_h_i_j");
  });

  it("collapses internal whitespace to single underscore", () => {
    expect(sanitizeNameHint("my   image  copy.png")).toBe("my_image_copy");
  });

  it("strips control chars", () => {
    expect(sanitizeNameHint("name.png")).toBe("name");
  });

  it("caps length at 64 chars before extension strip", () => {
    const longName = "x".repeat(200) + ".png";
    const result = sanitizeNameHint(longName);
    expect(result.length).toBeLessThanOrEqual(64);
    expect(result).toBe("x".repeat(64));
  });

  it("returns 'image' when sanitization wipes everything", () => {
    expect(sanitizeNameHint("///")).toBe("image");
    expect(sanitizeNameHint(".png")).toBe("image"); // only extension
  });
});
```

- [ ] **Step 2: Run tests to verify failure**

Run: `cd ui && npx vitest run src/paste.test.ts`
Expected: `sanitizeNameHint is not exported`.

- [ ] **Step 3: Implement helper**

Append to `ui/src/paste.ts`:
```ts
/**
 * Sanitize a clipboard-provided filename hint into a filesystem-safe stem.
 * - Strips path separators, shell metacharacters, control chars.
 * - Strips the trailing extension (caller appends one based on MIME).
 * - Collapses runs of whitespace to single underscore.
 * - Caps length at 64 chars.
 * - Returns "image" for null/empty/wipe-to-nothing inputs.
 */
export function sanitizeNameHint(hint: string | null): string {
  if (hint == null) return "image";
  let s = hint.trim();
  if (s.length === 0) return "image";
  // Cap length first so the rest of the sanitization works on a bounded string.
  if (s.length > 64) s = s.slice(0, 64);
  // Strip last extension (".png", ".jpeg", etc.) if present.
  const dot = s.lastIndexOf(".");
  if (dot > 0) s = s.slice(0, dot);
  // Replace forbidden chars and whitespace runs with single underscore.
  // Forbidden: / \ : * ? " < > | and ASCII control chars (0x00-0x1F, 0x7F).
  s = s.replace(/[\/\\:*?"<>| -]+/g, "_");
  s = s.replace(/\s+/g, "_");
  // Strip leading/trailing underscores.
  s = s.replace(/^_+|_+$/g, "");
  return s.length === 0 ? "image" : s;
}
```

- [ ] **Step 4: Run tests to verify pass**

Run: `cd ui && npx vitest run src/paste.test.ts`
Expected: all `sanitizeNameHint` tests pass.

- [ ] **Step 5: Commit**

```bash
git add ui/src/paste.ts ui/src/paste.test.ts
git commit -m "feat(paste): sanitizeNameHint helper for clipboard filename hints"
```

---

### Task 3: Add `extractClipboardImage` helper

**Files:**
- Modify: `ui/src/paste.ts` (append at end)
- Modify: `ui/src/paste.test.ts` (append at end)

- [ ] **Step 1: Write failing tests**

Append to `ui/src/paste.test.ts`:
```ts
import { extractClipboardImage } from "./paste.js";

// Minimal DataTransferItem mock — vitest runs under happy-dom which has
// DataTransferItem but not a programmatic way to populate it for tests.
function makeItem(kind: string, type: string, file: File | null) {
  return {
    kind,
    type,
    getAsFile: () => file,
    getAsString: (_cb: (s: string) => void) => {},
  } as unknown as DataTransferItem;
}

function makeClipboard(items: DataTransferItem[], types: string[]) {
  return {
    items: items as unknown as DataTransferItemList,
    types,
    files: items.filter((i) => i.kind === "file").map((i) => i.getAsFile()!) as unknown as FileList,
    getData: (t: string) => (t === "text/plain" ? "" : ""),
    setData: () => {},
    clearData: () => {},
    dropEffect: "none" as const,
    effectAllowed: "all" as const,
  } as unknown as DataTransfer;
}

describe("extractClipboardImage", () => {
  it("returns null when clipboard has only text", () => {
    const cd = makeClipboard(
      [makeItem("string", "text/plain", null)],
      ["text/plain"],
    );
    expect(extractClipboardImage(cd)).toBeNull();
  });

  it("returns null when clipboard is empty", () => {
    const cd = makeClipboard([], []);
    expect(extractClipboardImage(cd)).toBeNull();
  });

  it("returns the file for an image-only clipboard", () => {
    const f = new File([new Uint8Array([1, 2, 3])], "screenshot.png", { type: "image/png" });
    const cd = makeClipboard(
      [makeItem("file", "image/png", f)],
      ["image/png"],
    );
    const result = extractClipboardImage(cd);
    expect(result).not.toBeNull();
    expect(result!.mime).toBe("image/png");
    expect(result!.file).toBe(f);
  });

  it("prefers image when clipboard has both image and text", () => {
    const f = new File([new Uint8Array([4, 5])], "img.jpg", { type: "image/jpeg" });
    const cd = makeClipboard(
      [
        makeItem("string", "text/plain", null),
        makeItem("file", "image/jpeg", f),
      ],
      ["text/plain", "image/jpeg"],
    );
    const result = extractClipboardImage(cd);
    expect(result).not.toBeNull();
    expect(result!.mime).toBe("image/jpeg");
  });

  it("returns first image when multiple images are present", () => {
    const f1 = new File([new Uint8Array([1])], "a.png", { type: "image/png" });
    const f2 = new File([new Uint8Array([2])], "b.png", { type: "image/png" });
    const cd = makeClipboard(
      [
        makeItem("file", "image/png", f1),
        makeItem("file", "image/png", f2),
      ],
      ["image/png"],
    );
    const result = extractClipboardImage(cd);
    expect(result!.file).toBe(f1);
  });

  it("returns null when image item has no File (getAsFile returns null)", () => {
    const cd = makeClipboard(
      [makeItem("file", "image/png", null)],
      ["image/png"],
    );
    expect(extractClipboardImage(cd)).toBeNull();
  });

  it("accepts the input null/undefined defensively", () => {
    expect(extractClipboardImage(null)).toBeNull();
    expect(extractClipboardImage(undefined as unknown as DataTransfer)).toBeNull();
  });
});
```

- [ ] **Step 2: Run tests to verify failure**

Run: `cd ui && npx vitest run src/paste.test.ts`
Expected: `extractClipboardImage is not exported`.

- [ ] **Step 3: Implement helper**

Append to `ui/src/paste.ts`:
```ts
/**
 * Scan a clipboard DataTransfer for an image item. Returns the first image
 * file found (and its MIME), or null if no image is present.
 *
 * Prefers image over text — when both are present (e.g. browser "Copy Image"
 * also writes a text URL), the user's intent in pasting a screenshot is
 * clearly the image, not the fallback text.
 */
export function extractClipboardImage(
  cd: DataTransfer | null | undefined,
): { file: File; mime: string } | null {
  if (!cd) return null;
  const items = cd.items;
  if (!items) return null;
  for (let i = 0; i < items.length; i++) {
    const item = items[i]!;
    if (item.kind !== "file") continue;
    if (!item.type.toLowerCase().startsWith("image/")) continue;
    const file = item.getAsFile();
    if (!file) continue;
    return { file, mime: item.type };
  }
  return null;
}
```

- [ ] **Step 4: Run tests to verify pass**

Run: `cd ui && npx vitest run src/paste.test.ts`
Expected: all `extractClipboardImage` tests pass.

- [ ] **Step 5: Run the full JS test suite**

Run: `cd ui && npx vitest run`
Expected: all tests pass, including pre-existing ones.

- [ ] **Step 6: Commit**

```bash
git add ui/src/paste.ts ui/src/paste.test.ts
git commit -m "feat(paste): extractClipboardImage helper with prefer-image rule"
```

---

## Phase 2 — JS bridge wiring

### Task 4: Add `onPasteImage` callback to `XtermTab`

**Files:**
- Modify: `ui/src/xterm-tab.ts` (lines 40-52 and 134-225)

- [ ] **Step 1: Extend the options interface**

In `ui/src/xterm-tab.ts`, replace the `XtermTabOptions` interface (lines 40-52) with:
```ts
export interface XtermTabOptions {
  /** Pre-attached host element. Must already be in the DOM with non-zero
   *  dimensions when XtermTab is constructed; xterm.open() needs the host
   *  to have a layout box so its RenderService can initialize. */
  host: HTMLDivElement;
  scrollbackLines: number;
  theme: ThemeName;
  onInput: (bytes: Uint8Array) => void;
  onResize: (cols: number, rows: number) => void;
  /**
   * Called when the user pastes raw image bytes from the clipboard (e.g. a
   * screenshot). The owner (TabManager) base64-encodes and sends a
   * "paste-image" bridge message with the tabId attached. When unset, image
   * pastes fall back to the text path (which usually means "do nothing" —
   * the clipboard text is empty for image-only pastes).
   */
  onPasteImage?: (mime: string, bytes: Uint8Array, nameHint: string | null) => void;
  /** Diagnostic sink — writes to the C# log via the bridge. Avoids
   *  DevTools-filter quirks for things we want logged regardless. */
  diag?: (msg: string) => void;
}
```

- [ ] **Step 2: Update the import block**

In `ui/src/xterm-tab.ts`, replace the import from `./paste.js` (lines 7-12) with:
```ts
import {
  classifyPasteSize,
  countLines,
  estimatePasteDurationMs,
  extractClipboardImage,
  normalizePasteLineEndings,
} from "./paste.js";
```

- [ ] **Step 3: Add image-detection branch in docPasteHandler**

In `ui/src/xterm-tab.ts`, replace the `docPasteHandler` (lines 134-225) with the version below. The change: a new image-branch sits after `ev.stopImmediatePropagation()` and before the text-branch; size cap is 25 MB; on image extraction, calls `opts.onPasteImage` and returns without falling through.

```ts
    this.docPasteHandler = (ev: ClipboardEvent) => {
      const focused = document.activeElement;
      if (!focused || !this.host.contains(focused)) return;
      ev.preventDefault();
      ev.stopImmediatePropagation();
      const cd = ev.clipboardData;

      // Image branch — prefer image when clipboard carries both image and text.
      // Reads File via DataTransferItem.getAsFile, base64-encodes through the
      // bridge, and hands off to the C# side which writes a temp file and
      // injects the path. See docs/superpowers/specs/2026-05-18-image-paste-…
      const image = extractClipboardImage(cd);
      if (image && opts.onPasteImage) {
        const sizeBytes = image.file.size;
        if (sizeBytes > IMAGE_PASTE_MAX_BYTES) {
          showNotice(
            "err",
            `Image too large to paste (${(sizeBytes / 1024 / 1024).toFixed(1)} MB > ${IMAGE_PASTE_MAX_BYTES / 1024 / 1024} MB limit).`,
            15000,
          );
          this.diag(`paste-image rejected: ${sizeBytes} bytes > cap`);
          return;
        }
        this.diag(
          `paste-image mime=${image.mime} bytes=${sizeBytes} name=${image.file.name || "(none)"}`,
        );
        // File.arrayBuffer() returns a Promise; we don't await inside the
        // synchronous paste handler — fire-and-forget is safe because nothing
        // else in the handler runs after the early return.
        image.file
          .arrayBuffer()
          .then((buf) => {
            const bytes = new Uint8Array(buf);
            opts.onPasteImage!(image.mime, bytes, image.file.name || null);
          })
          .catch((err) => {
            this.diag(`paste-image read failed: ${err}`);
            showNotice("err", `Couldn't read pasted image: ${err}`, 8000);
          });
        return;
      }

      // Text branch (unchanged from prior behavior).
      const text = cd?.getData("text/plain") ?? "";
      const bracketed =
        (this.term as unknown as { modes?: { bracketedPasteMode?: boolean } })
          .modes?.bracketedPasteMode ?? "unknown";
      const types = cd ? Array.from(cd.types) : [];
      this.diag(
        `paste bracketed=${bracketed} types=[${types.join(",")}] plainLen=${text.length}`,
      );
      if (!text) return;

      const bracketedActive =
        (this.term as unknown as { modes?: { bracketedPasteMode?: boolean } })
          .modes?.bracketedPasteMode === true;
      const hasNewline = /\n|\r/.test(text);

      const byteLen = new TextEncoder().encode(text).length;
      const lineCount = countLines(text);
      const tier = classifyPasteSize(byteLen);
      if (tier === "block") {
        showNotice(
          "err",
          `Paste too large (${(byteLen / 1024).toFixed(0)} KB). Save to a file and use a 'read this file' command instead.`,
          15000,
        );
        return;
      }
      if (tier === "warn") {
        const seconds = Math.ceil(
          estimatePasteDurationMs(
            byteLen,
            PACED_RATE_CHUNK_BYTES,
            PACED_RATE_DELAY_MS,
          ) / 1000,
        );
        showNotice(
          "info",
          `Pasting ${(byteLen / 1024).toFixed(0)} KB / ${lineCount} lines (~${seconds}s). Some CLIs may have their own input limits.`,
          Math.max(8000, seconds * 1000 + 2000),
        );
      } else if (tier === "notice") {
        showNotice(
          "info",
          `Pasting ${lineCount} lines (${(byteLen / 1024).toFixed(1)} KB).`,
          4000,
        );
      }

      if (!bracketedActive && hasNewline) {
        const bytes = new TextEncoder().encode(normalizePasteLineEndings(text));
        this.diag(
          `paste bypass-CR-coercion len=${bytes.length} (bracketed mode off + multi-line)`,
        );
        opts.onInput(bytes);
        return;
      }

      this.term.paste(text);
    };
```

- [ ] **Step 4: Add the size constant near other paste constants**

In `ui/src/xterm-tab.ts`, after the existing `PACED_RATE_DELAY_MS` constant (line 19), add:
```ts
// Hard cap for image paste (raw bytes, pre-base64). 25 MB covers an 8K
// screenshot with headroom and prevents a clipboard-bomb from filling
// disk. Enforced JS-side AND C#-side as defense in depth.
const IMAGE_PASTE_MAX_BYTES = 25 * 1024 * 1024;
```

- [ ] **Step 5: Verify the JS bundle still type-checks**

Run: `cd ui && npx tsc --noEmit`
Expected: zero errors.

- [ ] **Step 6: Run all JS tests**

Run: `cd ui && npx vitest run`
Expected: all tests pass (including pre-existing paste tests — the new image branch shouldn't affect text paste behavior).

- [ ] **Step 7: Commit**

```bash
git add ui/src/xterm-tab.ts
git commit -m "feat(xterm-tab): image-paste branch with 25 MB cap and onPasteImage callback"
```

---

### Task 5: Wire `onPasteImage` to bridge in `tab-manager.ts`

**Files:**
- Modify: `ui/src/tab-manager.ts` (around line 228-235 — the `new XtermTab` call site)

- [ ] **Step 1: Add `onPasteImage` callback**

In `ui/src/tab-manager.ts`, locate the `new XtermTab({…})` block (around lines 228-235) and add an `onPasteImage` callback alongside `onInput`/`onResize`/`diag`. The callback base64-encodes via the existing `encodeBase64` import (already used by `sendInputChunked`) and sends a `paste-image` envelope:

```ts
    const xterm = new XtermTab({
      host: tabHost,
      scrollbackLines: this.scrollbackLines,
      theme: this.theme,
      onInput: (bytes) => this.sendInputChunked(tabId, bytes),
      onResize: (cols, rows) =>
        this.bridge.send("resize", { tabId, cols, rows }),
      onPasteImage: (mime, bytes, nameHint) =>
        this.bridge.send("paste-image", {
          tabId,
          mime,
          bytesB64: encodeBase64(bytes),
          nameHint,
        }),
      diag: (msg) => this.bridge.send("diag", { msg }),
    });
```

(The exact other lines you keep depend on what's already there — only insert the `onPasteImage:` line and adjust trailing commas as needed.)

- [ ] **Step 2: Type-check**

Run: `cd ui && npx tsc --noEmit`
Expected: zero errors.

- [ ] **Step 3: Run all JS tests**

Run: `cd ui && npx vitest run`
Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add ui/src/tab-manager.ts
git commit -m "feat(tab-manager): wire onPasteImage to bridge paste-image message"
```

---

## Phase 3 — C# DTO

### Task 6: Add `ImagePastePayload` record

**Files:**
- Modify: `src/Concord.Core/Ui/Messages/Incoming.cs` (append at end)
- Modify: `tests/MessageDtoTests.cs` (append a round-trip test)

- [ ] **Step 1: Write failing test**

Append to `tests/MessageDtoTests.cs` (before the closing `}` of the class):
```csharp
[Fact]
public void ImagePaste_RoundTrip()
{
    var json = """{"tabId":"abc","mime":"image/png","bytesB64":"AAEC","nameHint":"screenshot.png"}""";
    var dto = JsonSerializer.Deserialize<ImagePastePayload>(json, Json)!;
    dto.TabId.Should().Be("abc");
    dto.Mime.Should().Be("image/png");
    dto.BytesB64.Should().Be("AAEC");
    dto.NameHint.Should().Be("screenshot.png");
}

[Fact]
public void ImagePaste_NameHintOptional_LeavesNull()
{
    var json = """{"tabId":"abc","mime":"image/png","bytesB64":"AAEC"}""";
    var dto = JsonSerializer.Deserialize<ImagePastePayload>(json, Json)!;
    dto.NameHint.Should().BeNull();
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~ImagePaste"`
Expected: build fails — `ImagePastePayload` not found.

- [ ] **Step 3: Add the record**

Append to `src/Concord.Core/Ui/Messages/Incoming.cs`:
```csharp
public sealed record ImagePastePayload(
    string TabId,
    string Mime,
    string BytesB64,
    string? NameHint = null);
```

- [ ] **Step 4: Run test to verify pass**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~ImagePaste"`
Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Concord.Core/Ui/Messages/Incoming.cs tests/MessageDtoTests.cs
git commit -m "feat(messages): ImagePastePayload DTO for paste-image bridge message"
```

---

## Phase 4 — C# `PasteImageHandler` (TDD)

### Task 7: Create handler class skeleton + first failing test

**Files:**
- Create: `src/Concord.Core/Terminal/PasteImageHandler.cs`
- Create: `tests/PasteImageHandlerTests.cs`

- [ ] **Step 1: Write the first failing test**

Create `tests/PasteImageHandlerTests.cs`:
```csharp
using System;
using System.IO;
using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class PasteImageHandlerTests : IDisposable
{
    private readonly string baseDir;

    public PasteImageHandlerTests()
    {
        baseDir = Path.Combine(Path.GetTempPath(), "Concord-test-pastes-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(baseDir))
        {
            try { Directory.Delete(baseDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void WriteImage_WritesBytesToTempFile_AndReturnsPath()
    {
        var handler = new PasteImageHandler(baseDir);
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var path = handler.WriteImage("image/png", bytes, nameHint: null);

        path.Should().StartWith(baseDir);
        File.Exists(path).Should().BeTrue();
        File.ReadAllBytes(path).Should().Equal(bytes);
    }
}
```

- [ ] **Step 2: Run test to verify failure**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~PasteImageHandler"`
Expected: build fails — `PasteImageHandler` not found.

- [ ] **Step 3: Create the handler class**

Create `src/Concord.Core/Terminal/PasteImageHandler.cs`:
```csharp
using System;
using System.IO;
using System.Threading.Tasks;

namespace Terminal;

/// <summary>
/// Writes raw image bytes from a clipboard paste to a uniquely-named temp
/// file and returns the absolute path. The path is then injected into the
/// PTY by the caller in place of the raw bytes, so the receiving CLI sees
/// a file path (which Claude Code / Codex / Copilot CLI auto-recognize as
/// an image attachment).
///
/// Files live under &lt;TempPath&gt;/Concord/pastes/ and are swept by
/// <see cref="CleanupOlderThan"/> on extension startup (24 h retention).
/// </summary>
public sealed class PasteImageHandler
{
    public const long MaxBytes = 25L * 1024 * 1024;  // 25 MB

    private readonly string baseDir;

    public PasteImageHandler(string? baseDirOverride = null)
    {
        baseDir = baseDirOverride ?? Path.Combine(Path.GetTempPath(), "Concord", "pastes");
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> to a new file under the configured
    /// temp dir and returns the absolute path. Throws if the byte count
    /// exceeds <see cref="MaxBytes"/> (defense in depth — the JS side
    /// rejects oversized pastes before the bridge call).
    /// </summary>
    public string WriteImage(string mime, byte[] bytes, string? nameHint)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));
        if (bytes.LongLength > MaxBytes)
            throw new ArgumentException($"Image too large: {bytes.LongLength} bytes (max {MaxBytes}).", nameof(bytes));

        Directory.CreateDirectory(baseDir);

        var stem = SanitizeNameHint(nameHint);
        var ext = MimeToExtension(mime);
        var ts = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var guid8 = Guid.NewGuid().ToString("N").Substring(0, 8);
        var fileName = $"{stem}-{ts}-{guid8}{ext}";
        var path = Path.Combine(baseDir, fileName);

        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// Delete files in the base dir older than <paramref name="age"/>.
    /// Best-effort: any IO error on an individual file is swallowed so a
    /// single permission/lock issue can't break cleanup of the rest.
    /// Returns the count of files deleted. No-op if base dir doesn't exist.
    /// </summary>
    public int CleanupOlderThan(TimeSpan age)
    {
        if (!Directory.Exists(baseDir)) return 0;
        var cutoff = DateTime.UtcNow - age;
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(baseDir))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch { /* best-effort */ }
        }
        return deleted;
    }

    private static string SanitizeNameHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return "image";
        var s = hint.Trim();
        if (s.Length > 64) s = s.Substring(0, 64);
        var dot = s.LastIndexOf('.');
        if (dot > 0) s = s.Substring(0, dot);
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' ||
                c == '"' || c == '<' || c == '>' || c == '|' ||
                c < 0x20 || c == 0x7F || char.IsWhiteSpace(c))
            {
                chars[i] = '_';
            }
        }
        s = new string(chars);
        // Collapse runs of underscore and trim leading/trailing.
        while (s.Contains("__")) s = s.Replace("__", "_");
        s = s.Trim('_');
        return string.IsNullOrEmpty(s) ? "image" : s;
    }

    private static string MimeToExtension(string mime)
    {
        return (mime ?? "").ToLowerInvariant() switch
        {
            "image/png"  => ".png",
            "image/jpeg" => ".jpg",
            "image/gif"  => ".gif",
            "image/webp" => ".webp",
            "image/bmp"  => ".bmp",
            "image/tiff" => ".tiff",
            _            => ".png",
        };
    }
}
```

- [ ] **Step 4: Run test to verify pass**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~PasteImageHandler"`
Expected: 1 test passes.

- [ ] **Step 5: Commit**

```bash
git add src/Concord.Core/Terminal/PasteImageHandler.cs tests/PasteImageHandlerTests.cs
git commit -m "feat(paste-image): PasteImageHandler — writes bytes to temp file"
```

---

### Task 8: Extension-from-MIME tests

**Files:**
- Modify: `tests/PasteImageHandlerTests.cs` (append before the closing `}`)

- [ ] **Step 1: Add parameterized extension test**

Append to `tests/PasteImageHandlerTests.cs`:
```csharp
[Theory]
[InlineData("image/png",  ".png")]
[InlineData("image/jpeg", ".jpg")]
[InlineData("image/gif",  ".gif")]
[InlineData("image/webp", ".webp")]
[InlineData("image/bmp",  ".bmp")]
[InlineData("image/tiff", ".tiff")]
[InlineData("IMAGE/PNG",  ".png")]  // case-insensitive
public void WriteImage_PicksExtensionFromMime(string mime, string expectedExt)
{
    var handler = new PasteImageHandler(baseDir);
    var path = handler.WriteImage(mime, new byte[] { 1, 2, 3 }, nameHint: null);
    Path.GetExtension(path).Should().Be(expectedExt);
}

[Theory]
[InlineData("image/avif")]
[InlineData("image/heic")]
[InlineData("")]
[InlineData("text/plain")]
public void WriteImage_FallsBackToPng_OnUnknownOrEmptyMime(string mime)
{
    var handler = new PasteImageHandler(baseDir);
    var path = handler.WriteImage(mime, new byte[] { 1, 2, 3 }, nameHint: null);
    Path.GetExtension(path).Should().Be(".png");
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~PasteImageHandler"`
Expected: all `PasteImageHandler` tests pass (1 existing + 8 new from Theory).

- [ ] **Step 3: Commit**

```bash
git add tests/PasteImageHandlerTests.cs
git commit -m "test(paste-image): extension-from-MIME parameterized cases"
```

---

### Task 9: NameHint sanitization tests

**Files:**
- Modify: `tests/PasteImageHandlerTests.cs` (append before the closing `}`)

- [ ] **Step 1: Add sanitization tests**

Append to `tests/PasteImageHandlerTests.cs`:
```csharp
[Theory]
[InlineData(null,                "image")]
[InlineData("",                  "image")]
[InlineData("   ",               "image")]
[InlineData("screenshot.png",    "screenshot")]
[InlineData("my photo.jpeg",     "my_photo")]
[InlineData("a/b\\c:d*e?f.png",  "a_b_c_d_e_f")]
[InlineData("///",               "image")]      // wipes to empty → fallback
[InlineData(".png",              "image")]      // extension-only → fallback
public void WriteImage_SanitizesNameHint(string? hint, string expectedStem)
{
    var handler = new PasteImageHandler(baseDir);
    var path = handler.WriteImage("image/png", new byte[] { 1 }, hint);
    var name = Path.GetFileName(path);
    // Filename is "<stem>-<ts>-<guid8>.png". Pull the leading stem.
    var firstDash = name.IndexOf('-');
    var stem = firstDash > 0 ? name.Substring(0, firstDash) : name;
    // For composite hints (e.g. "a_b_c_d_e_f") the stem itself may contain
    // dashes via prior sanitization. Use Last dash + format check instead.
    var ts = "20"; // year prefix is the first character after the stem-dash
    var idxOfTs = name.IndexOf("-20");
    stem = idxOfTs > 0 ? name.Substring(0, idxOfTs) : stem;
    stem.Should().Be(expectedStem);
}

[Fact]
public void WriteImage_CapsLongNameHintAt64Chars()
{
    var handler = new PasteImageHandler(baseDir);
    var longHint = new string('x', 200) + ".png";
    var path = handler.WriteImage("image/png", new byte[] { 1 }, longHint);
    var name = Path.GetFileName(path);
    var idxOfTs = name.IndexOf("-20");
    var stem = name.Substring(0, idxOfTs);
    stem.Length.Should().BeLessThanOrEqual(64);
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/Terminal.Tests.csproj --filter "FullyQualifiedName~PasteImageHandler"`
Expected: all `PasteImageHandler` tests pass.

- [ ] **Step 3: Commit**

```bash
git add tests/PasteImageHandlerTests.cs
git commit -m "test(paste-image): nameHint sanitization cases"
```

---

### Task 10: Size-cap, filename-format, cleanup tests

**Files:**
- Modify: `tests/PasteImageHandlerTests.cs` (append before the closing `}`)

- [ ] **Step 1: Add the remaining tests**

Append to `tests/PasteImageHandlerTests.cs`:
```csharp
[Fact]
public void WriteImage_Throws_WhenBytesExceedMaxBytes()
{
    var handler = new PasteImageHandler(baseDir);
    var oversized = new byte[PasteImageHandler.MaxBytes + 1];
    var act = () => handler.WriteImage("image/png", oversized, nameHint: null);
    act.Should().Throw<ArgumentException>().WithMessage("*too large*");
}

[Fact]
public void WriteImage_FilenameMatchesExpectedFormat()
{
    var handler = new PasteImageHandler(baseDir);
    var path = handler.WriteImage("image/png", new byte[] { 1 }, "screenshot");
    var name = Path.GetFileName(path);
    // <stem>-<yyyyMMddTHHmmssZ>-<guid8>.<ext>
    name.Should().MatchRegex(@"^screenshot-\d{8}T\d{6}Z-[0-9a-f]{8}\.png$");
}

[Fact]
public void CleanupOlderThan_DeletesOldFiles_LeavesNewerAlone()
{
    var handler = new PasteImageHandler(baseDir);
    Directory.CreateDirectory(baseDir);

    var oldFile = Path.Combine(baseDir, "old.png");
    var newFile = Path.Combine(baseDir, "new.png");
    File.WriteAllBytes(oldFile, new byte[] { 1 });
    File.WriteAllBytes(newFile, new byte[] { 2 });
    File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddHours(-25));
    File.SetLastWriteTimeUtc(newFile, DateTime.UtcNow.AddHours(-1));

    var deleted = handler.CleanupOlderThan(TimeSpan.FromHours(24));

    deleted.Should().Be(1);
    File.Exists(oldFile).Should().BeFalse();
    File.Exists(newFile).Should().BeTrue();
}

[Fact]
public void CleanupOlderThan_NoOp_WhenBaseDirMissing()
{
    var handler = new PasteImageHandler(Path.Combine(baseDir, "does-not-exist"));
    var deleted = handler.CleanupOlderThan(TimeSpan.FromHours(24));
    deleted.Should().Be(0);
}
```

- [ ] **Step 2: Run all C# tests**

Run: `dotnet test tests/Terminal.Tests.csproj`
Expected: all tests pass, including the full pre-existing suite (no regressions).

- [ ] **Step 3: Commit**

```bash
git add tests/PasteImageHandlerTests.cs
git commit -m "test(paste-image): size cap, filename format, and cleanup sweep"
```

---

## Phase 5 — Wire handler into ViewModels

### Task 11: Wire `paste-image` case into `Host10x/Pane/TerminalPaneViewModel.cs`

**Files:**
- Modify: `src/Concord.Host10x/Pane/TerminalPaneViewModel.cs` (lines 20-58, ~211)

- [ ] **Step 1: Add handler field and ctor init**

In `src/Concord.Host10x/Pane/TerminalPaneViewModel.cs`, add a `pasteHandler` field after the existing `manager` field declaration (line 20):
```csharp
    private readonly TerminalSessionManager manager;
    private readonly PasteImageHandler pasteHandler = new();
```

(The default ctor uses `<TempPath>/Concord/pastes/`. No need to pass anything from the outer ctor.)

- [ ] **Step 2: Add `"paste-image"` case to the switch**

In `OnWebViewMessage` (line 110-211), add a new case immediately after the `"input"` case (after line 156):
```csharp
                case "paste-image":
                {
                    var p = GetData<ImagePastePayload>(e);
                    byte[] bytes;
                    try { bytes = Convert.FromBase64String(p.BytesB64); }
                    catch (FormatException fex)
                    {
                        log.Warn($"paste-image base64 decode failed tab={p.TabId}: {fex.Message}");
                        break;
                    }
                    string path;
                    try
                    {
                        path = pasteHandler.WriteImage(p.Mime, bytes, p.NameHint);
                    }
                    catch (Exception wex)
                    {
                        log.Warn($"paste-image write failed tab={p.TabId} mime={p.Mime} bytes={bytes.Length}: {wex.Message}");
                        break;
                    }
                    log.Info($"paste-image tab={p.TabId.Substring(0, 8)} bytes={bytes.Length} mime={p.Mime} path={path}");
                    _ = manager.Write(p.TabId, System.Text.Encoding.UTF8.GetBytes(path));
                    break;
                }
```

- [ ] **Step 3: Add `using` if needed**

The file already has `using Terminal.Messages;` and `using Terminal;` so `PasteImageHandler` and `ImagePastePayload` resolve. Verify by building.

- [ ] **Step 4: Build the Host10x project**

Run: `dotnet build src/Concord.Host10x/Concord.Host10x.csproj`
Expected: build succeeds, no warnings related to the new code.

- [ ] **Step 5: Commit**

```bash
git add src/Concord.Host10x/Pane/TerminalPaneViewModel.cs
git commit -m "feat(host10x): handle paste-image — write temp file + inject path"
```

---

### Task 12: Same wiring for `Host11x/Pane/TerminalPaneViewModel.cs`

**Files:**
- Modify: `src/Concord.Host11x/Pane/TerminalPaneViewModel.cs` (mirror Task 11 edits exactly)

- [ ] **Step 1: Add `pasteHandler` field**

In `src/Concord.Host11x/Pane/TerminalPaneViewModel.cs`, after the `manager` field declaration (line 20), add:
```csharp
    private readonly TerminalSessionManager manager;
    private readonly PasteImageHandler pasteHandler = new();
```

- [ ] **Step 2: Add `"paste-image"` case**

In Host11x's `OnWebViewMessage`, add the same `case "paste-image"` block as Task 11, immediately after the existing `"input"` case. (Identical code — both hosts share the same switch structure.)

- [ ] **Step 3: Build the full solution**

Run: `dotnet build`
Expected: build succeeds for all projects (Host10x, Host11x, Core, Shim, tests).

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test tests/Terminal.Tests.csproj`
Expected: all tests pass — original 241+ plus the new `PasteImageHandlerTests` (and the `ImagePaste*` DTO tests).

- [ ] **Step 5: Commit**

```bash
git add src/Concord.Host11x/Pane/TerminalPaneViewModel.cs
git commit -m "feat(host11x): handle paste-image — write temp file + inject path"
```

---

## Phase 6 — Startup cleanup hook

### Task 13: Sweep stale files on extension startup

**Files:**
- Modify: `src/Concord.Host10x/Pane/TerminalPaneViewModel.cs` (ctor)
- Modify: `src/Concord.Host11x/Pane/TerminalPaneViewModel.cs` (ctor)

We sweep on every ViewModel construction (i.e. each time the pane opens). The handler is a one-line fire-and-forget so it doesn't gate UI init. The sweep is idempotent and cheap (just a directory listing).

- [ ] **Step 1: Add sweep call in Host10x ViewModel ctor**

In `src/Concord.Host10x/Pane/TerminalPaneViewModel.cs`, at the end of the constructor (after line 58, the `consumePendingFirstRunNotices` assignment), add:
```csharp
        // Best-effort sweep of stale pasted-image temp files older than 24h.
        // Runs once per pane construction. Failures are swallowed by
        // CleanupOlderThan; we only log a Warn if Task.Run itself throws.
        _ = Task.Run(() =>
        {
            try
            {
                var deleted = pasteHandler.CleanupOlderThan(TimeSpan.FromHours(24));
                if (deleted > 0) log.Info($"[paste-image] swept {deleted} stale file(s)");
            }
            catch (Exception ex) { log.Warn($"[paste-image] sweep failed: {ex.Message}"); }
        });
```

- [ ] **Step 2: Add the same sweep in Host11x ViewModel ctor**

In `src/Concord.Host11x/Pane/TerminalPaneViewModel.cs`, append the identical block at the end of its constructor.

- [ ] **Step 3: Build and test**

Run: `dotnet build && dotnet test tests/Terminal.Tests.csproj`
Expected: build succeeds, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add src/Concord.Host10x/Pane/TerminalPaneViewModel.cs src/Concord.Host11x/Pane/TerminalPaneViewModel.cs
git commit -m "chore(paste-image): sweep stale paste tempfiles older than 24h on pane open"
```

---

## Phase 7 — Verification

### Task 14: Full build + test sweep

**Files:** none

- [ ] **Step 1: Build everything**

Run: `dotnet build`
Expected: zero errors, zero new warnings.

- [ ] **Step 2: Run all .NET tests**

Run: `dotnet test tests/Terminal.Tests.csproj`
Expected: all tests pass. Note the total count — it should be > the prior 241 by the count of new `PasteImageHandlerTests` (≈15) plus `MessageDtoTests` (+2).

- [ ] **Step 3: Run Concord.Core sub-tests**

Run: `dotnet test tests/Concord.Core.Tests/Concord.Core.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 4: Run Concord.Shim sub-tests**

Run: `dotnet test tests/Concord.Shim.Tests/Concord.Shim.Tests.csproj`
Expected: all tests pass.

- [ ] **Step 5: Run JS tests + typecheck**

Run: `cd ui && npx tsc --noEmit && npx vitest run`
Expected: zero TypeScript errors; all vitest tests pass.

---

### Task 15: Manual smoke test (Mac)

**Files:** none — verification only

This step requires Studio Pro 11.10+ running locally with the dev-built `Concord.mxmodule` deployed. The `DeployToMendix` MSBuild target auto-copies on `dotnet build` (see `Terminal.csproj`), so once Task 14 is done the Concord build is in TestOSApp3 / ConcordPublisher.

- [ ] **Step 1: Take a screenshot**

Press `Cmd+Shift+4` (or use `Cmd+Shift+5` Region) to capture an area into clipboard.

- [ ] **Step 2: Open the Concord pane and start a Claude Code tab**

In Studio Pro, open the Concord terminal pane. Start a Claude Code session (or any tab — the path injection works on raw bash too).

- [ ] **Step 3: Paste with Cmd+V**

Expected behaviors:
- No raw image bytes appear in the terminal.
- An absolute path like `/var/folders/.../T/Concord/pastes/image-20260518T143012Z-a1b2c3d4.png` is typed into the prompt.
- Claude Code recognizes it as an image attachment (visible in its UI as `[Image #1]` or similar).
- The file exists on disk at that path.

- [ ] **Step 4: Check the Concord log for the diagnostic line**

In a separate terminal:
```bash
grep "paste-image" ~/Library/Logs/Concord/terminal.log | tail -5
```
Expected: a recent line of the form `paste-image tab=… bytes=… mime=image/png path=…`.

- [ ] **Step 5: Verify cleanup works**

Backdate the file to 25 hours ago and reopen the pane:
```bash
touch -t $(date -v -25H +%Y%m%d%H%M.%S) /var/folders/.../T/Concord/pastes/image-*.png
```
Then close & reopen the Concord pane in Studio Pro. The file should be gone. Check log for `[paste-image] swept N stale file(s)`.

- [ ] **Step 6: Verify size-cap rejection**

If you have a way to put a > 25 MB image on the clipboard (e.g. screenshot a very large monitor at high DPI), paste it. Expected: an in-terminal red notice "Image too large to paste (X MB > 25 MB limit)" appears; nothing is written to disk; nothing reaches the PTY.

- [ ] **Step 7: Verify text paste still works**

Copy a normal multi-line text snippet (e.g. a function from this file). Paste with Cmd+V. Expected: the text appears in the terminal exactly as before this change (no regression in the existing text-paste path).

---

### Task 16: Push branch and open PR

**Files:** none

- [ ] **Step 1: Confirm the branch state**

Run:
```bash
git log --oneline main..HEAD
```
Expected: a clean chain of one commit per task above (Tasks 1-13 = 13 atomic commits).

- [ ] **Step 2: Push the branch**

Run: `git push -u origin feat/image-paste-temp-path`
Expected: push succeeds.

- [ ] **Step 3: Open a PR**

Run:
```bash
gh pr create --title "feat: paste raw image data as temp-file path" --body "$(cat <<'EOF'
## Summary
- Intercept raw image bytes from the clipboard (e.g. macOS screenshots) in the Concord WebView's paste handler.
- Write the bytes to a uniquely-named file under `<TempPath>/Concord/pastes/` and inject the absolute path into the PTY in place of the raw bytes.
- Claude Code (and Codex / Copilot CLI) auto-recognize the path as an image attachment — same UX as copying a file from Finder.

## Architecture
- New `PasteImageHandler` in `Concord.Core` owns the temp-file write + 24h cleanup sweep.
- New `paste-image` bridge message carries `{ tabId, mime, bytesB64, nameHint }` from JS to C#.
- ViewModel routes the message to the handler, then calls `manager.Write(tabId, UTF8(path))` — same PTY-write path as keystrokes.

## Spec & plan
- Spec: `docs/superpowers/specs/2026-05-18-image-paste-temp-path-injection-design.md`
- Plan: `docs/superpowers/plans/2026-05-18-image-paste-temp-path-injection.md`

## Test plan
- [ ] `dotnet test tests/Terminal.Tests.csproj` — green (new `PasteImageHandlerTests` + 2 new `MessageDtoTests` cases; pre-existing suite unchanged)
- [ ] `cd ui && npx vitest run` — green (extended `paste.test.ts` with `mimeToExtension`, `sanitizeNameHint`, `extractClipboardImage`)
- [ ] `cd ui && npx tsc --noEmit` — zero errors
- [ ] Manual Mac smoke: paste screenshot → path injected, Claude Code attaches image; paste 25 MB+ → rejection notice; text paste unchanged
- [ ] Verify 24h cleanup sweep deletes backdated paste files on next pane open

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```
Expected: PR URL is returned. Capture it.

---

## Self-Review (executed during plan authoring)

**1. Spec coverage:**
- Spec § "JS: image-paste detection" → Tasks 1, 2, 3 (helpers) + Task 4 (xterm-tab integration) + Task 5 (tab-manager wiring) ✓
- Spec § "JS ↔ C# bridge protocol" → Task 6 (DTO) ✓
- Spec § "C# handler" → Tasks 7-10 (PasteImageHandler + tests) + Tasks 11-12 (ViewModel switch case) ✓
- Spec § "Cleanup on startup" → Task 13 ✓
- Spec § "Testing — JS" → Tasks 1-3 ✓ (plus pre-existing tests stay green per Task 4/5 steps)
- Spec § "Testing — C#" → Tasks 7-10 ✓
- Spec § "Error handling" table → JS-side size cap (Task 4), C#-side guard (Task 7's `MaxBytes` check tested in Task 10), base64 decode catch + tabId-missing handled by viewmodel try/catch fallback to error message (Tasks 11-12)

**2. Placeholder scan:** none — every code step has full code; every command has an exact form.

**3. Type consistency:**
- `extractClipboardImage` returns `{ file: File; mime: string } | null` — consistent across Tasks 3, 4.
- `onPasteImage(mime, bytes, nameHint)` signature — Task 4 (declaration) and Task 5 (call site) match.
- `paste-image` envelope shape `{ tabId, mime, bytesB64, nameHint }` — Task 5 (JS send) and Task 6 (`ImagePastePayload` C# record) and Tasks 11-12 (deserialize) all align (camelCase wire, PascalCase records).
- `PasteImageHandler.MaxBytes = 25 MB` (Task 7) and JS `IMAGE_PASTE_MAX_BYTES = 25 * 1024 * 1024` (Task 4) match.
- `<TempPath>/Concord/pastes/` is the path in both spec and Task 7's default ctor.

---

## Notes for the Implementer

- **Atomic commits per task:** the repo's culture is one commit per phase boundary (see `CHANGELOG.md` history). Each task above maps to one commit. Don't squash; Neo's adversarial-review pass reads the chain.
- **`DeployToMendix` MSBuild target:** every `dotnet build` auto-deploys to TestOSApp3 + ConcordPublisher. After a successful build, Studio Pro will pick up the change on next pane open. No manual copy.
- **`.mxmodule` for release:** out of scope for this PR. That's a separate marketplace step (see `reference_concord_release_playbook.md`).
- **No CHANGELOG entry yet:** the repo's pattern is to add the changelog entry at version-bump time, not per-feature. Defer to release prep.
- **If a TypeScript change requires a UI rebuild before the WebView picks it up:** `dotnet build` triggers the UI bundle via `BuildUi.targets`. No separate `npm run build` needed in normal flow.
