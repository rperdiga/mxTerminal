import { describe, it, expect } from "vitest";
import {
  classifyPasteSize,
  countLines,
  DEFAULT_PASTE_THRESHOLDS,
  estimatePasteDurationMs,
  extractClipboardImage,
  mimeToExtension,
  normalizePasteLineEndings,
  pasteChunkRanges,
  sanitizeNameHint,
} from "./paste.js";

describe("normalizePasteLineEndings", () => {
  it("converts CRLF to LF", () => {
    expect(normalizePasteLineEndings("a\r\nb\r\nc")).toBe("a\nb\nc");
  });

  it("converts bare CR to LF", () => {
    expect(normalizePasteLineEndings("a\rb\rc")).toBe("a\nb\nc");
  });

  it("leaves bare LF unchanged", () => {
    expect(normalizePasteLineEndings("a\nb\nc")).toBe("a\nb\nc");
  });

  it("handles mixed line endings into a single LF stream", () => {
    expect(normalizePasteLineEndings("a\r\nb\nc\rd")).toBe("a\nb\nc\nd");
  });

  it("handles empty input", () => {
    expect(normalizePasteLineEndings("")).toBe("");
  });

  it("preserves content with no line endings", () => {
    expect(normalizePasteLineEndings("hello world")).toBe("hello world");
  });

  it("does not collapse consecutive newlines", () => {
    expect(normalizePasteLineEndings("a\n\nb")).toBe("a\n\nb");
    expect(normalizePasteLineEndings("a\r\n\r\nb")).toBe("a\n\nb");
  });
});

describe("classifyPasteSize", () => {
  it("classifies small pastes as silent", () => {
    expect(classifyPasteSize(0)).toBe("silent");
    expect(classifyPasteSize(100)).toBe("silent");
    expect(classifyPasteSize(DEFAULT_PASTE_THRESHOLDS.noticeBytes - 1)).toBe(
      "silent",
    );
  });

  it("classifies medium pastes as notice", () => {
    expect(classifyPasteSize(DEFAULT_PASTE_THRESHOLDS.noticeBytes)).toBe(
      "notice",
    );
    expect(classifyPasteSize(DEFAULT_PASTE_THRESHOLDS.warnBytes - 1)).toBe(
      "notice",
    );
  });

  it("classifies large pastes as warn", () => {
    expect(classifyPasteSize(DEFAULT_PASTE_THRESHOLDS.warnBytes)).toBe("warn");
    expect(classifyPasteSize(DEFAULT_PASTE_THRESHOLDS.hardLimitBytes - 1)).toBe(
      "warn",
    );
  });

  it("classifies huge pastes as block", () => {
    expect(classifyPasteSize(DEFAULT_PASTE_THRESHOLDS.hardLimitBytes)).toBe(
      "block",
    );
    expect(classifyPasteSize(10 * 1024 * 1024)).toBe("block");
  });

  it("respects custom thresholds", () => {
    const t = { noticeBytes: 10, warnBytes: 100, hardLimitBytes: 1000 };
    expect(classifyPasteSize(5, t)).toBe("silent");
    expect(classifyPasteSize(50, t)).toBe("notice");
    expect(classifyPasteSize(500, t)).toBe("warn");
    expect(classifyPasteSize(1500, t)).toBe("block");
  });
});

describe("pasteChunkRanges", () => {
  const collect = (total: number, chunk: number) =>
    Array.from(pasteChunkRanges(total, chunk));

  it("yields nothing for zero-byte input", () => {
    expect(collect(0, 256)).toEqual([]);
  });

  it("yields one chunk when input fits", () => {
    expect(collect(100, 256)).toEqual([[0, 100]]);
  });

  it("yields chunks of the requested size", () => {
    expect(collect(512, 256)).toEqual([
      [0, 256],
      [256, 512],
    ]);
  });

  it("yields a final partial chunk for non-multiples", () => {
    expect(collect(700, 256)).toEqual([
      [0, 256],
      [256, 512],
      [512, 700],
    ]);
  });

  it("covers the full byte range without overlap or gaps", () => {
    const total = 1493;
    const ranges = collect(total, 256);
    // Ranges chain head-to-tail
    for (let i = 1; i < ranges.length; i++) {
      expect(ranges[i]![0]).toBe(ranges[i - 1]![1]);
    }
    expect(ranges[0]![0]).toBe(0);
    expect(ranges[ranges.length - 1]![1]).toBe(total);
    // Sum of sizes equals total
    const summed = ranges.reduce((acc, [s, e]) => acc + (e - s), 0);
    expect(summed).toBe(total);
  });

  it("handles chunkBytes <= 0 by yielding the whole range as one chunk", () => {
    expect(collect(100, 0)).toEqual([[0, 100]]);
    expect(collect(100, -5)).toEqual([[0, 100]]);
  });
});

describe("estimatePasteDurationMs", () => {
  it("returns zero for empty input", () => {
    expect(estimatePasteDurationMs(0, 256, 25)).toBe(0);
  });

  it("returns one delay-interval per chunk", () => {
    // 1493 bytes / 256 bytes/chunk = 6 chunks; 6 * 25ms = 150ms
    expect(estimatePasteDurationMs(1493, 256, 25)).toBe(150);
  });

  it("rounds up partial chunks", () => {
    // 257 bytes at 256/chunk = 2 chunks (one full, one 1-byte) = 50ms
    expect(estimatePasteDurationMs(257, 256, 25)).toBe(50);
  });

  it("returns zero for invalid chunk size", () => {
    expect(estimatePasteDurationMs(1000, 0, 25)).toBe(0);
    expect(estimatePasteDurationMs(1000, -1, 25)).toBe(0);
  });
});

describe("countLines", () => {
  it("returns zero for empty string", () => {
    expect(countLines("")).toBe(0);
  });

  it("returns one for single-line text without trailing newline", () => {
    expect(countLines("hello")).toBe(1);
  });

  it("counts newlines + 1", () => {
    expect(countLines("a\nb\nc")).toBe(3);
    expect(countLines("a\nb\nc\n")).toBe(4); // trailing newline = empty 4th line
  });

  it("ignores CR-only line endings (caller should normalize first)", () => {
    expect(countLines("a\rb\rc")).toBe(1);
  });
});

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
    expect(sanitizeNameHint("name.png")).toBe("name");
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
