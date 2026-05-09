# Publishing Concord to the Mendix Marketplace

End-to-end playbook for shipping a new version under MxLabs.

## Pre-flight checklist

- [ ] Source code merged to `main`, version bumped in
      `Terminal.csproj` (`<Version>` and `<InformationalVersion>`)
- [ ] `CHANGELOG.md` updated with the new version's release notes
- [ ] **All public docs reference the current version, no leftover
      prior-version refs.** Grep the repo for the previous version
      string before tagging — `README.md`, `marketing/listing.md`,
      `marketing/SCREENSHOTS.md`, `marketing/PUBLISHING.md`, and
      `Terminal.csproj` `<Description>` are the usual suspects.
- [ ] Tests green (`cd ui && npm test`; `dotnet test`)
- [ ] Production smoke test on a clean Studio Pro restart against
      the testbed (TestOSApp3) — paste matrix in `DEPLOYING.md`
- [ ] All thumbnail / screenshot assets prepared at 600×420
- [ ] License file present (`LICENSE` — Apache 2.0)
- [ ] `SECURITY.md` and `CONTRIBUTING.md` present at repo root

## Step 1 — Build the deploy artifact

From the repo root on a clean checkout:

```sh
dotnet build Terminal.csproj -p:Platform=x64
```

Output lands at `bin/x64/Debug/net8.0/`. Verify the folder contains:

- `Concord.dll`
- `manifest.json`
- `wwwroot/` (xterm.js bundle + index.html)
- All transitive .NET runtime DLLs
  (`Microsoft.Data.Sqlite.*`, `System.Text.Json`, etc.)
- Should be ~55 files. **No `winpty.dll` / `winpty-agent.exe`** —
  ConPTY backend doesn't need them.

## Step 2 — Build the add-on module wrapper in Studio Pro

The Mendix Marketplace expects an `.mxmodule` file. Studio Pro
Extensions are published as **add-on modules** that bundle the
extension files inside.

1. Open Studio Pro 11.10+ on a **throwaway** Mendix app (NOT
   TestOSApp3 — keep the testbed clean).
2. Create a new module: right-click app root → **Add Module** →
   name it `Concord`.
3. Right-click the new `Concord` module → **Mark as Add-on Module**
   (or equivalent menu — exact label varies by Studio Pro point
   release; look for "add-on" or "solution" toggle).
4. Place the contents of `bin/x64/Debug/net8.0/` (the entire deploy
   folder we built in Step 1) into the add-on module's bundled
   resources. Studio Pro's add-on module export will copy these into
   the consumer app's `extensions/Concord/` on install.
5. Set add-on module metadata:
   - **Name:** Concord
   - **Description:** "The terminal Studio Pro was missing." (or
     paste the short pitch from `marketing/listing.md`)
   - **Version:** match `Terminal.csproj` `<Version>` exactly (e.g.
     `4.1.2`)
   - **Author:** Siemens CoE Team
6. **File → Export Add-on Module Package** → save as
   `Concord.mxmodule` somewhere outside the repo (or under
   `marketing/dist/`, gitignored).

## Step 3 — Smoke-test the .mxmodule

Before publishing, prove the wrapper works:

1. Open a **second** throwaway Mendix app.
2. **App → Import Module Package** → pick `Concord.mxmodule`.
3. Verify Studio Pro creates `<that-app>/extensions/Concord/` with
   the deploy folder's contents.
4. Restart Studio Pro on that app.
5. Verify the Concord menu item appears, the pane opens, a tab
   spawns, and `claude` runs inside it.
6. Paste a multi-line block (any clipboard source) → confirm
   `bracket-mode SET` in `<that-app>/resources/terminal.log` and
   the paste lands intact.

If any of those fail, the wrapper is mis-built — fix before tagging.

## Step 4 — Tag and release on GitHub

```sh
# from main, with all changes committed
git tag -a v$VERSION -m "v$VERSION — <one-line theme>"
git push origin v$VERSION

# Create GitHub release with the .mxmodule attached
gh release create v$VERSION \
    --repo rperdiga/mxTerminal \
    --title "v$VERSION — <one-line theme>" \
    --notes-file CHANGELOG.md \
    modules/Concord.mxmodule
```

Replace `$VERSION` with the literal version (e.g. `4.1.2`) and the
`<one-line theme>` with the release headline from CHANGELOG. If the
tag already exists from earlier work, use
`gh release upload v$VERSION modules/Concord.mxmodule` to attach the
binary to the existing release.

## Step 5 — Submit to Mendix Marketplace via MxLabs

1. Sign in to https://marketplace.mendix.com under the MxLabs
   publisher account (ask the MxLabs admin if you don't have access).
2. Top-left menu → **Marketplace** → **Publish Component**.
3. Form values — copy from `marketing/listing.md`:
   - **Component Type:** Module
   - **Component Name:** "Concord — Terminal for Studio Pro"
   - **Visibility:** Public
   - **Source:** GitHub Link → paste
     `https://github.com/rperdiga/mxTerminal/releases/tag/v4.1.2`
   - **Thumbnail:** `marketing/concord-thumbnail-600x420.png`
   - **Screenshots:** up to 10 captures from
     `marketing/screenshots/` (the SCREENSHOTS.md shot list has 11
     candidates; pick the strongest 10 — drop About if you have to
     drop one)
4. Step 2 (Content): paste the long description from
   `marketing/listing.md` "Long description" section.
5. Step 3 (License): Apache 2.0 — link to LICENSE in the repo or
   paste full text if required.
6. Step 4 (Media & Documentation): attach any extra documentation
   PDFs/links (README, DEPLOYING, PASTE).
7. Authors: Ricardo Perdigao first, Kelly Seale second.
8. Compatibility: Studio Pro 11.10+, Windows 10 1809+ (ConPTY
   requirement), .NET 8 runtime.
9. **Save and Exit** to draft. Review once. Then **Submit for
   Review**.
10. Mendix QSM (Quality Standards Module) scan runs automatically.
    Address any findings.

## Step 6 — Post-publication

- [ ] Update `README.md` with the marketplace badge / link
- [ ] Update `DEPLOYING.md` "Consumer path" section to point at the
      marketplace as the recommended install path
- [ ] Announce in the CoE Team channel
- [ ] Schedule a check in 2 weeks for first install metrics + any
      user issues

## Versioning discipline

For every subsequent release:

1. Bump `<Version>` and `<InformationalVersion>` in
   `Terminal.csproj`
2. Add a CHANGELOG entry under the new version heading
3. Build, test, smoke-test
4. Build new `.mxmodule` wrapper (Step 2 — Studio Pro export)
5. Tag and release on GitHub (Step 4)
6. Marketplace: **Publish New Version** on the existing component
   (NOT a new listing — that breaks installed users' upgrade path)

## What can break

- **Component Type is immutable post-publish.** If you pick the
  wrong type at first submission, you have to start a new listing.
  Always pick **Module**.
- **`.mxmodule` ≠ `.mpk`.** The form field labelled "MPK" accepts
  both; Studio Pro's File menu has separate export entries — use
  **Export Add-on Module Package**, NOT Export Module Package.
- **Marketplace dark-mode rendering.** There's an open investigation
  that the published listing renders with broken contrast inside Studio
  Pro's in-IDE Marketplace view (suspected dark-mode CSS bug on the
  marketplace side). Ricardo to confirm before each publish whether
  the new copy renders cleanly in both the web marketplace AND the
  in-IDE marketplace surface.
- **Trust prompt on first install.** Users installing Concord for
  the first time will see a "this extension wants to run native
  code" prompt from Studio Pro. The README's install section
  should set this expectation.
- **MxLabs gate.** Extension publishing is partner-only. MxLabs
  is the partner; submission must go through that publisher
  account, not a personal Mendix account.
