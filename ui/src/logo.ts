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
 * @param host  Element to populate. Replaces innerHTML.
 * @param size  Pixel dimension (square). Default 80.
 * @param spin  Whether to apply the slow rotation animation. Default true.
 */
export function mountLogo(
  host: Element,
  size: number = 80,
  spin: boolean = true,
): void {
  // We wrap so the rotation animation has a stable container; the SVG
  // itself stays at its native viewBox so glyphs scale crisply.
  const cls = spin ? "os-logo os-logo-spin" : "os-logo";
  host.innerHTML =
    `<div class="${cls}" style="width:${size}px;height:${size}px">` +
    (logoSvg as unknown as string) +
    `</div>`;
}
