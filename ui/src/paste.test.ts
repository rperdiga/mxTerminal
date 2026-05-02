import { describe, it, expect } from "vitest";
import {
  classifyPasteSize,
  countLines,
  DEFAULT_PASTE_THRESHOLDS,
  estimatePasteDurationMs,
  normalizePasteLineEndings,
  pasteChunkRanges,
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
