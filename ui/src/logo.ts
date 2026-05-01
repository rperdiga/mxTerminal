// OneSource logo — vendored from ui/src/assets/onesource-logo.svg.
// Built by the Siemens OneSource Center of Excellence team. Spins slowly
// in the About panel. Inlines as raw SVG (esbuild text loader) so it
// inherits CSS transforms cleanly and avoids an extra HTTP fetch.

import logoSvg from "./assets/onesource-logo.svg";

export function logoSvgString(): string {
  return logoSvg as unknown as string;
}

/**
 * Mount the OneSource logo into a host element.
 * Size is controlled by CSS (e.g. .rail-logo-slot .os-logo { width: 110px; })
 * rather than inline style, so per-context sizing wins specificity battles.
 * @param host  Element to populate. Replaces innerHTML.
 * @param spin  Whether to apply the slow rotation animation. Default false.
 */
export function mountLogo(
  host: Element,
  _legacySize: number = 0, // kept for back-compat; ignored
  spin: boolean = false,
): void {
  void _legacySize;
  const cls = spin ? "os-logo os-logo-spin" : "os-logo";
  host.innerHTML = `<div class="${cls}">${logoSvg as unknown as string}</div>`;
}
