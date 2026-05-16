# Contributing to Concord

Thanks for considering a contribution. Concord is built by the **Siemens CoE Team** and accepts external contributions through the GitHub repository at [github.com/rperdiga/mxTerminal](https://github.com/rperdiga/mxTerminal).

## Reporting issues

- **Bugs:** [open an issue](https://github.com/rperdiga/mxTerminal/issues/new) with the Concord version (visible in **Settings → About**), Studio Pro version, OS, and the relevant tail of `<project>/resources/terminal.log`.
- **Security issues:** see [SECURITY.md](./SECURITY.md) — please use a private security advisory rather than a public issue.
- **Feature requests:** open an issue describing the use case. We're more likely to land features that fit the "terminal + MCP wiring + bundled Mendix know-how" core scope.

## Pull requests

1. **Open an issue first** for non-trivial changes. Spec drift is the #1 cause of merge friction; a 5-minute alignment beats a 5-hour rewrite.
2. **Branch from `main`** with a name like `feat/<short-kebab>` or `fix/<short-kebab>` or `docs/<short-kebab>`.
3. **Tests with the change** — every behavior change ships with a test that proves it. xunit (C#) tests in `tests/`; vitest (TypeScript) tests in `ui/src/*.test.ts`. CI runs both on every PR.
4. **CHANGELOG entry** — add a one-line entry under the next version header. If the change is user-visible, also update the relevant section of `README.md` or `marketing/listing.md`.
5. **Commit titles under 70 chars**; lead with the *why*. PR titles follow the same rule.
6. **PR description covers:** what changed, why, test plan (how a reviewer verifies). Use the existing PR template.

## Local development

See [DEPLOYING.md § Developer path](./DEPLOYING.md#developer-path-build-from-source) for the full build + iterate loop.

Maintainers cutting a release: see [docs/RELEASING.md](./docs/RELEASING.md) for the verified cross-version `.mxmodule` export procedure (Windows-only; merged-shim layout required).

Quick start:

```sh
git clone https://github.com/rperdiga/mxTerminal.git
cd mxTerminal

# Per-developer deploy config (gitignored)
copy Directory.Build.props.example Directory.Build.props
# Edit MendixDeployTarget to your Mendix project root

dotnet build      # builds + auto-deploys to your Mendix project
dotnet test       # 160+ xunit tests
cd ui && npm test # 33 vitest tests
```

The first build of a fresh clone needs to run twice (esbuild creates `wwwroot/` after MSBuild has already evaluated the `<Content>` glob — known issue, tracked in `Terminal.csproj`).

## Coding conventions

- **C#:** follow the existing style — `var` for locals, sealed records for DTOs, internal-by-default, XML doc comments on public surfaces. Nullable reference types on. No regions.
- **TypeScript:** ESM, strict mode, `interface` for shapes that cross the WebView bridge. No default exports.
- **Naming:** the MCP server's wire identity is `concord-mcp`; the user-facing product surface is "Concord MCP"; the older "Action Bridge" name should NOT appear in any new code or doc.
- **Comments:** explain *why*, not *what*. Default to no comment unless the next reader would otherwise wonder why the code is shaped that way.
- **Tests:** describe behavior, not implementation. `IsUpgradeApplyNeeded_ReturnsFalse_WhenStampMatchesCurrent` beats `Test_IsUpgradeApplyNeeded_4`.

## Code review

External PRs are reviewed by the maintainers (Ricardo Perdigao, Kelly Seale). Expect feedback on:

- Security and data-handling at boundaries (settings file, `.mcp.json`, `~/.codex/config.toml`)
- Atomic-write hygiene for any new file writer (use `File.Replace` or the `WriteAtomic` pattern from `McpJsonConfigurator`)
- Cross-platform parity (Windows + macOS) for any feature touching the PTY, theme probe, or hotkey tools

## License

By contributing, you agree your contributions are licensed under the [Apache License 2.0](./LICENSE) — the same license as Concord itself.
