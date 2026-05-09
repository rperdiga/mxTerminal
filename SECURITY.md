# Security

## Reporting a vulnerability

Open a **private security advisory** on GitHub:
[github.com/rperdiga/mxTerminal/security/advisories/new](https://github.com/rperdiga/mxTerminal/security/advisories/new)

Include:

- A short description of the issue and affected Concord version(s)
- Steps to reproduce, ideally against the bundled testbed pattern in `DEPLOYING.md`
- Your assessment of impact (information disclosure, code execution, denial of service, etc.)
- Whether you'd like credit in the fix's release notes

We'll acknowledge within **5 business days** and aim for a coordinated-disclosure fix within **30 days** for high/critical severity. Lower-severity issues land in the next routine release.

If GitHub Security Advisories isn't an option for you, fall back to opening a regular GitHub issue marked `[security]` in the title — keep the report low on detail in the public issue and we'll DM you a private channel.

## Supported versions

Active patch support is the current minor + the previous minor:

| Version | Status |
|---|---|
| 4.1.x | ✅ Active — security + bug fixes |
| 4.0.x | ✅ Active — security fixes only |
| 1.x | ❌ End of life — please upgrade |

Older versions may receive a fix at maintainer discretion if the issue is critical and the upgrade path is non-trivial.

## Threat model in scope

Concord is a Mendix Studio Pro 11.10+ extension that runs **with the privileges of the Studio Pro process** on the developer's local machine. Specifically in scope:

- **Loopback MCP server.** Concord MCP binds to `127.0.0.1`. We treat any path that would expose it on `0.0.0.0` or to remote callers as in-scope.
- **Settings file reads/writes.** Anything in `<project>/resources/terminal-settings.json` and the bundled-skill installer's writes to `<project>/.claude/skills/`, `<project>/.github/skills/`, `<project>/.codex/skills/`. Any path-traversal issue, partial-write data loss, or write outside the intended scope is in scope.
- **MCP config file edits.** `<project>/.mcp.json` and `~/.codex/config.toml` are upserted/removed. Any data loss in unrelated entries (we use named-key upserts and preserve siblings) is in scope.
- **Maia CDP transport.** The injected JS agent runs inside Studio Pro's WebView2 panel via `--remote-debugging-port`. Any path that would inject untrusted code OR escape the agent's bounded surface is in scope.
- **Subprocess spawning.** PTY tabs spawn user-selected shells. Any way to bypass shell selection or inject arguments through Concord's UI without the user's consent is in scope.

## Out of scope

- Vulnerabilities in third-party CLIs (Claude Code, Codex, GitHub Copilot CLI) running inside Concord's PTY tabs — report those upstream.
- Vulnerabilities in Studio Pro itself, Mendix runtime, or Mendix's MCP server — report to Mendix.
- Issues that require physical or local user-account access to the developer's machine (Concord trusts the user account it runs under).
- Vulnerabilities in the Apache 2.0 license file or marketplace listing copy.

## Privacy

Concord is loopback-only. No network traffic leaves the developer's machine through Concord itself. Telemetry: none. Crash reporting: none. The CLI agents you run inside Concord follow their own privacy contracts.
