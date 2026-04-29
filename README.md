# Mendix Studio Pro Terminal Extension

A C# extension for Mendix Studio Pro 11.x that embeds a tabbed terminal (Claude Code, Codex, etc.) in a dockable pane, starting at the open Mendix app's project root. No external bridge server required.

## Setup

1. Copy `Directory.Build.props.example` → `Directory.Build.props` and set `MendixDeployTarget` to your Mendix project path.
2. Ensure Node.js 18+ is on PATH (used at C# build time to bundle the UI).
3. Build: `dotnet build`
4. Launch Studio Pro with `--enable-extension-development`.
5. F4 in Studio Pro to reload extensions.
6. Menu: Extensions → Terminal.

See `docs/superpowers/specs/2026-04-29-terminal-extension-design.md` for design.
