declare module "*.css" {
  const content: string;
  export default content;
}

// esbuild loads .svg as raw text (loader: "text") so we can inline the
// markup directly into innerHTML for theming + animation control.
declare module "*.svg" {
  const content: string;
  export default content;
}

// .png is loaded as a data URL string (loader: "dataurl") so it can be
// dropped into <img src> or background-image without an extra fetch.
declare module "*.png" {
  const content: string;
  export default content;
}
