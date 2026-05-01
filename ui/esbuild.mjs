import * as esbuild from "esbuild";
import { copyFileSync, mkdirSync } from "fs";
import { fileURLToPath } from "url";
import { dirname, resolve } from "path";

const here = dirname(fileURLToPath(import.meta.url));
const wwwroot = resolve(here, "..", "wwwroot");
mkdirSync(wwwroot, { recursive: true });
copyFileSync(resolve(here, "index.html"), resolve(wwwroot, "index.html"));

const watch = process.argv.includes("--watch");

const ctx = await esbuild.context({
  entryPoints: [resolve(here, "src/main.ts")],
  bundle: true,
  minify: !watch,
  sourcemap: true,
  target: "es2022",
  format: "iife",
  outfile: resolve(wwwroot, "terminal.bundle.js"),
  loader: { ".css": "text", ".svg": "text", ".png": "dataurl" },
  logLevel: "info"
});

if (watch) await ctx.watch();
else { await ctx.rebuild(); await ctx.dispose(); }
