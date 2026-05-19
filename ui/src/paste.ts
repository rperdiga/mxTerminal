// Pure helpers for the paste pipeline. Extracted from xterm-tab.ts and
// tab-manager.ts so they can be unit-tested without xterm.js, the WebView
// bridge, or DOM.

/**
 * Normalize line endings for the bracketed-paste-OFF bypass path. xterm's
 * native `term.paste()` collapses `\r?\n` to `\r`; modern TUI prompts
 * (Claude Code, vim) interpret bare `\r` as Enter/submit, breaking
 * multi-line paste. This normalization preserves the user's intent of
 * "newlines inside the input field" by sending LF (which most line-aware
 * prompts treat as line-continuation).
 */
export function normalizePasteLineEndings(text: string): string {
  return text.replace(/\r\n?/g, "\n");
}

export type PasteSizeTier = "silent" | "notice" | "warn" | "block";

export interface PasteSizeThresholds {
  /** Silent below this many bytes. */
  noticeBytes: number;
  /** Stronger warning notice between warnBytes and hardLimitBytes. */
  warnBytes: number;
  /** Refuse paste above this. */
  hardLimitBytes: number;
}

export const DEFAULT_PASTE_THRESHOLDS: PasteSizeThresholds = {
  noticeBytes: 4 * 1024,
  warnBytes: 50 * 1024,
  hardLimitBytes: 1024 * 1024,
};

export function classifyPasteSize(
  byteLen: number,
  t: PasteSizeThresholds = DEFAULT_PASTE_THRESHOLDS,
): PasteSizeTier {
  if (byteLen >= t.hardLimitBytes) return "block";
  if (byteLen >= t.warnBytes) return "warn";
  if (byteLen >= t.noticeBytes) return "notice";
  return "silent";
}

/**
 * Yield chunk byte ranges for the paced sender. Pure (no timing): callers
 * iterate and insert delays between chunks themselves. Returns
 * [start, end) tuples covering the full byte range without overlap.
 */
export function* pasteChunkRanges(
  totalBytes: number,
  chunkBytes: number,
): IterableIterator<[number, number]> {
  if (totalBytes <= 0) return;
  if (chunkBytes <= 0) {
    yield [0, totalBytes];
    return;
  }
  for (let off = 0; off < totalBytes; off += chunkBytes) {
    const end = Math.min(off + chunkBytes, totalBytes);
    yield [off, end];
  }
}

/**
 * Estimate paste duration in milliseconds at the given paced rate.
 * `chunkBytes / delayMs` is bytes-per-millisecond; total is bytes / rate.
 * Reported to the user as a "this will take ~Ns" hint.
 */
export function estimatePasteDurationMs(
  byteLen: number,
  chunkBytes: number,
  delayMs: number,
): number {
  if (byteLen <= 0 || chunkBytes <= 0) return 0;
  const chunks = Math.ceil(byteLen / chunkBytes);
  return chunks * delayMs;
}

/** Count `\n` characters (the paste handler's "lines" approximation). */
export function countLines(text: string): number {
  if (text.length === 0) return 0;
  return (text.match(/\n/g)?.length ?? 0) + 1;
}

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
