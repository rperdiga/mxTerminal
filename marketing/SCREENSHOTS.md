# Concord — Marketplace screenshot shot list

11 candidate shots; the marketplace caps the upload at 10. If forced
to drop one, drop **#6 (Settings: About)** — it's the least sales-y
of the set. Each is 600×420, captured by Neo, processed (cropped +
light annotation) afterwards.

## General framing rules

- **Output dimensions:** crop / scale to exactly **600×420**. Aspect
  ratio is 10:7 — slightly wider than 4:3.
- **Resolution while capturing:** capture at native Studio Pro
  resolution, then crop. Don't shrink first; loses detail.
- **Theme:** alternate dark and light across the set so reviewers
  see Concord works in both. Default to dark for the hero.
- **Project context:** TestOSApp3 is fine — it's a real-looking
  project name. Avoid showing anything labeled "test" or "delete me".
- **Cleanliness:** close unrelated panes (Toolbox, Properties) when
  they'd compete with the main subject. Bring them back for the
  one screenshot that's about pane integration.
- **No real secrets.** Before each capture, verify there are no API
  keys, tokens, or `op://` references visible in any terminal
  output, log line, or settings field.
- **No personal info.** Hide or obscure anything that names a real
  person other than the OneSource credit.

## Shot list

### 1 — Hero (the showcase)

**Goal:** Sell the product in one image. Front-page candidate.

- Studio Pro window, dark theme
- Concord pane docked right (or bottom — pick whichever is more
  recognizable as Studio Pro)
- A Concord tab running `claude` mid-conversation. Show 1-2 turns of
  meaningful text — not the welcome screen
- Studio Pro's project tree visible on the left so context is clear
- The pane's tab strip shows 2-3 tabs (`Pwsh - 1`, `Pwsh - 2`,
  maybe `Bash - 3`) so the multi-tab feature is implicit
- Crop to focus on the Concord pane + just enough Studio Pro chrome
  to identify the host

### 2 — Settings: General

**Goal:** "It's a real, polished settings panel."

- Open Settings → General section
- Show Theme dropdown set to Auto, Restore tabs ON, Scrollback
  field populated
- Capture the entire settings modal including the left rail (with
  the OneSource logo at the bottom of the rail)
- Either theme

### 3 — Settings: Studio Pro MCP

**Goal:** "It wires up MCP for you."

- Open Settings → Studio Pro MCP section
- Show the toggle ON, the port readout populated, the green status
  pill if applicable
- Highlight the "writes .mcp.json" / "writes config.toml" affordance

### 4 — Settings: Concord MCP

**Goal:** "Concord can drive Studio Pro itself, not just the model — and call Maia programmatically."

- Open Settings → Concord MCP section
- Show master toggle ON, port readout populated (probably 7783)
- Both sub-toggles visible: **Studio Pro UI actions** (ON) and
  **Maia integration** (ON on Windows; disabled-with-tooltip on Mac
  if doing the Mac variant)
- If there's a list of exposed tools or hover-popovers showing
  `run_app`/`stop_app`/`refresh_project`/`save_all`/
  `get_active_run_configuration`/`get_app_status` plus
  `maia__send`/`status`/`wait`/`ask`/`reset`/`force_tier`, show that

### 5 — Settings: Skills

**Goal:** "Concord ships prescriptive Mendix know-how the CLIs auto-discover."

- Open Settings → Skills section
- Show master toggle ON
- "Bundled in this Concord" list visible with all 7 skill names:
  `mendix-microflow-common`, `mendix-microflow-syntax`,
  `mendix-microflow-update`, `mendix-page-gen`, `mendix-view-entities`,
  `mendix-workflow-common`, `mendix-workflow-update`
- Per-CLI checkboxes visible: Claude Code (✓), Copilot CLI (✓),
  Codex (unchecked is fine — proves user controls per-CLI)

### 6 — Settings: About

**Goal:** "Built by professionals."

- Open Settings → About section
- Show the Concord ASCII art, version (4.0.0), log file path,
  settings file path
- Footer credit "A Siemens CoE extension for Studio Pro." visible

### 7 — Multi-tab terminal

**Goal:** "Real PTY tabs, not a single shell."

- Concord pane with 3 tabs visible in the tab strip
- The active tab shows PowerShell with some real-looking output
- The other tabs labeled `Bash - 2` and `Cmd - 3`
- Tab close buttons visible

### 8 — Theme follows Studio Pro (dark)

**Goal:** "Concord blends in, not bolted on."

- Studio Pro in dark theme; Concord pane visible inline; chrome
  surfaces match exactly (no visible seam)

### 9 — Theme follows Studio Pro (light)

**Goal:** Pair with #8 — show the same pane in light theme.

- Same shot as #8 but Studio Pro switched to light. Same content
  in the terminal so the reviewer sees the chrome adapting.

### 10 — Paste pipeline working (proof, not promise)

**Goal:** "Concord's paste fix actually works on Windows."

- Concord pane, Claude Code running
- A multi-line paste landed cleanly (not truncated)
- Claude Code's prompt visible at the end with "[Pasted text +N
  lines]" affordance
- Optionally a log tail at the bottom showing
  `bracket-mode SET / paste bracketed=true`

### 11 — Studio Pro menu integration

**Goal:** "It's a first-class Studio Pro citizen."

- Studio Pro's top menu bar with the Concord menu item visible
- Or the View menu with "Show Concord Terminal" highlighted
- Goal: prove discoverability — users find Concord without reading
  docs

## Post-capture processing

For each capture:

1. Crop to 600×420 (10:7 aspect)
2. Add a 1-pixel border in `#1F5A9F` (Mendix-blue light) — subtle
   frame so screenshots don't bleed into the marketplace background
3. (Optional) small "Concord 4.0.0" wordmark in lower-right corner,
   45% opacity, Cascadia Mono — reads as a watermark, not branding
4. Save as `marketing/screenshots/01-hero.png`,
   `02-settings-general.png`, etc.
5. Optimize PNG (pngquant or similar) so each is ≤ 1 MB

## Capture environment tips

- Studio Pro window resized to ~1600×1000 — gives room to crop
  without losing detail
- Hide `%USERPROFILE%`-revealing path components by setting Studio
  Pro to a project under `C:\Workspace\` (already true on this
  machine)
- For terminal content, set the prompt to something neutral; avoid
  showing the laptop hostname (`DINMCGDCJG3`) if possible
