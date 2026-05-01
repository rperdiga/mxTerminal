// Centralised theme palette. The CSS chrome is themed via CSS variables
// on `.theme-dark` / `.theme-light` (set on documentElement, see index.html
// and the inline boot script that runs before this bundle to avoid
// flash-of-wrong-theme). xterm.js needs an explicit theme object.
//
// ThemeName is the SETTING the user picks. ResolvedTheme is what's
// actually applied — "auto" resolves to dark or light at runtime via
// prefers-color-scheme, which the WebView host (Studio Pro) sets based
// on its own theme.

export type ThemeName = "auto" | "dark" | "light";
export type ResolvedTheme = "dark" | "light";

export interface XtermTheme {
  background: string;
  foreground: string;
  cursor: string;
  cursorAccent: string;
  selectionBackground: string;
  scrollbarSliderBackground: string;
  scrollbarSliderHoverBackground: string;
  scrollbarSliderActiveBackground: string;
  // ANSI 16-color palette overrides — applied to programs that emit named ANSI
  // colors (e.g. PowerShell error red, ls --color, git diff). 24-bit truecolor
  // escape codes (e.g. Claude Code's brand red) bypass this and render as-is.
  black: string;
  red: string;
  green: string;
  yellow: string;
  blue: string;
  magenta: string;
  cyan: string;
  white: string;
  brightBlack: string;
  brightRed: string;
  brightGreen: string;
  brightYellow: string;
  brightBlue: string;
  brightMagenta: string;
  brightCyan: string;
  brightWhite: string;
}

// Aligned with Studio Pro 11.10 chrome (sampled 2026-04-30):
//   - dark.background  matches --surface-0 (#313131) so the terminal
//     blends into the canvas with no visible seam
//   - light.background matches --surface-0 (#FFFFFF)
//   - selection uses --accent-tint so selected text keeps Mendix-blue
//     identity without losing readability
// ANSI palettes — slightly muted variants of Windows Terminal "Campbell"
// (dark) and a Solarized-Light-inspired set (light). Goal: PowerShell error
// red and similar ANSI-named colors read as informative, not alarming. Bright
// variants stay close to defaults for compatibility with TUI apps.
export const XtermThemes: Record<ResolvedTheme, XtermTheme> = {
  dark: {
    background: "#313131",
    foreground: "#E8E8E8",
    cursor: "#E8E8E8",
    cursorAccent: "#313131",
    selectionBackground: "#3D516E",
    scrollbarSliderBackground: "rgba(79,79,79,0.5)",
    scrollbarSliderHoverBackground: "rgba(79,79,79,0.8)",
    scrollbarSliderActiveBackground: "#4F4F4F",
    // Muted Campbell — slightly desaturated red/yellow so PowerShell errors
    // are still legible-as-error but don't shout.
    black: "#0C0C0C",
    red: "#B0413E",
    green: "#3FA858",
    yellow: "#C19C00",
    blue: "#4476B7",
    magenta: "#9D5BAE",
    cyan: "#3A96DD",
    white: "#CCCCCC",
    brightBlack: "#767676",
    brightRed: "#D26A65",
    brightGreen: "#54C454",
    brightYellow: "#E89641",
    brightBlue: "#5588C4",
    brightMagenta: "#B477C5",
    brightCyan: "#61D6D6",
    brightWhite: "#F2F2F2",
  },
  light: {
    background: "#FFFFFF",
    foreground: "#1F1F1F",
    cursor: "#1F1F1F",
    cursorAccent: "#FFFFFF",
    selectionBackground: "#E4EFFE",
    scrollbarSliderBackground: "rgba(180,180,185,0.5)",
    scrollbarSliderHoverBackground: "rgba(180,180,185,0.8)",
    scrollbarSliderActiveBackground: "#C8C8CC",
    // Light-mode ANSI: deeper hues for readability on white backgrounds.
    black: "#1F1F1F",
    red: "#A4262C",
    green: "#04841F",
    yellow: "#946F00",
    blue: "#1F5A9F",
    magenta: "#872B7E",
    cyan: "#007ACC",
    white: "#3D4759",
    brightBlack: "#5A6473",
    brightRed: "#C8302E",
    brightGreen: "#0E9B33",
    brightYellow: "#B5862D",
    brightBlue: "#2870BD",
    brightMagenta: "#A23F98",
    brightCyan: "#1898C9",
    brightWhite: "#1F1F1F",
  },
};

export function isThemeName(s: string): s is ThemeName {
  return s === "auto" || s === "dark" || s === "light";
}

/** Resolve the user's setting to a concrete dark|light. */
export function resolveTheme(setting: ThemeName): ResolvedTheme {
  if (setting === "dark" || setting === "light") return setting;
  // 1. Highest-priority signal: ?theme=dark|light pushed from C# after the
  //    SQLite probe of Studio Pro's persisted ThemeName. This is the host's
  //    actual theme — NOT the OS app theme that prefers-color-scheme returns.
  const fromUrl = readThemeFromUrl();
  if (fromUrl) return fromUrl;
  // 2. Fallback: prefers-color-scheme. WebView2 follows the OS app theme,
  //    which often matches Studio Pro but isn't guaranteed.
  if (typeof window !== "undefined" && window.matchMedia) {
    return window.matchMedia("(prefers-color-scheme: dark)").matches
      ? "dark"
      : "light";
  }
  return "dark";
}

function readThemeFromUrl(): ResolvedTheme | null {
  if (typeof window === "undefined" || !window.location) return null;
  try {
    const v = new URLSearchParams(window.location.search)
      .get("theme")
      ?.toLowerCase();
    if (v === "dark" || v === "light") return v;
  } catch {
    /* ignore — fall through to matchMedia */
  }
  return null;
}

/** Apply the resolved theme to the documentElement so CSS variables
 *  cascade everywhere. The class is set on `<html>` (not `<body>`) so the
 *  inline boot script in index.html can set it before the bundle loads. */
export function applyTheme(setting: ThemeName): ResolvedTheme {
  const resolved = resolveTheme(setting);
  document.documentElement.classList.remove("theme-dark", "theme-light");
  document.documentElement.classList.add(`theme-${resolved}`);
  return resolved;
}

/** Subscribe to OS / WebView host theme changes. Caller decides whether
 *  to re-apply (e.g. only if current setting is "auto"). Returns an
 *  unsubscribe function. */
export function onSystemThemeChange(
  cb: (resolved: ResolvedTheme) => void,
): () => void {
  if (typeof window === "undefined" || !window.matchMedia) return () => {};
  const mq = window.matchMedia("(prefers-color-scheme: dark)");
  const handler = () => cb(mq.matches ? "dark" : "light");
  mq.addEventListener("change", handler);
  return () => mq.removeEventListener("change", handler);
}
