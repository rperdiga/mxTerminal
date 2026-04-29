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
