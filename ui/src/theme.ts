// Centralised theme palette. The CSS chrome (tab strip, modal, etc.)
// is themed via CSS variables on `body.theme-{name}` (see index.html);
// xterm.js needs an explicit theme object passed to the Terminal ctor
// or assigned at runtime via `term.options.theme = ...`.

export type ThemeName = "dark" | "light";

export interface XtermTheme {
  background: string;
  foreground: string;
  cursor: string;
  selectionBackground: string;
}

export const XtermThemes: Record<ThemeName, XtermTheme> = {
  dark: {
    background:          "#1e1e1e",
    foreground:          "#d4d4d4",
    cursor:              "#d4d4d4",
    selectionBackground: "#264f78",
  },
  light: {
    background:          "#ffffff",
    foreground:          "#1f1f1f",
    cursor:              "#1f1f1f",
    selectionBackground: "#add6ff",
  },
};

export function applyBodyTheme(theme: ThemeName): void {
  document.body.classList.remove("theme-dark", "theme-light");
  document.body.classList.add(`theme-${theme}`);
}

export function isThemeName(s: string): s is ThemeName {
  return s === "dark" || s === "light";
}
