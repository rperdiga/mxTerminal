// Shared toast/notice for the chrome banner element. Originally only
// settings-modal showed banners; refactored out so the paste handler
// (and any future component) can surface user-visible feedback without
// reaching into settings-modal's internals.
//
// The DOM contract is: a `#banner` element with `#banner-icon`,
// `#banner-message`, `#banner-close` children, and CSS classes
// `visible`, `ok`, `err`, `info` toggled to control state.

import { mountIcon } from "./icons.js";

export type NoticeKind = "ok" | "err" | "info";

const DEFAULT_TTL_MS: Record<NoticeKind, number> = {
  ok: 6000,
  err: 12000,
  info: 8000,
};

let timer: number | undefined;

function el<T extends HTMLElement>(id: string): T | null {
  return document.getElementById(id) as T | null;
}

/**
 * Show a banner notice. Idempotent — replaces any current notice.
 * `ttlMs` overrides the default for the kind. 0 = sticky (caller must dismiss).
 */
export function showNotice(
  kind: NoticeKind,
  message: string,
  ttlMs?: number,
): void {
  const banner = el("banner");
  const icon = el("banner-icon");
  const msg = el("banner-message");
  if (!banner || !icon || !msg) return;
  const iconName =
    kind === "ok" ? "checkCircle" : kind === "err" ? "alertCircle" : "info";
  mountIcon(icon, iconName);
  msg.textContent = message;
  banner.className = `visible ${kind}`;
  if (timer !== undefined) window.clearTimeout(timer);
  const ttl = ttlMs ?? DEFAULT_TTL_MS[kind];
  if (ttl > 0) {
    timer = window.setTimeout(() => hideNotice(), ttl);
  }
}

export function hideNotice(): void {
  const banner = el("banner");
  if (!banner) return;
  banner.classList.remove("visible");
  if (timer !== undefined) {
    window.clearTimeout(timer);
    timer = undefined;
  }
}
