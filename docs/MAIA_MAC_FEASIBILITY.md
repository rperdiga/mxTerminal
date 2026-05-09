# Maia integration on Studio Pro for macOS — feasibility report

## Bottom line

Driving Maia programmatically on macOS via the Windows-style CDP transport is
**not feasible** — WKWebView in a shipping commercial app cannot be inspected
from outside the host process unless Mendix opts in by setting
`isInspectable = true` on the Maia WebView, which Studio Pro almost certainly
does not do in production. The only realistic path is a Tier-3 fallback that
drives Maia at the UI level (osascript / Accessibility), or a feature request
to Mendix to either expose Maia via their public Extensions API or to
`isInspectable = YES` the Maia WebView in a debug build flag.

## WKWebView inspection on Mac

Apple's modern API, `WKWebView.isInspectable` (macOS 13.3+ / iOS 16.4+),
defaults to `false` and **must be flipped per-WebView by the host app**. The
WebKit team's launch post is explicit: "This decision is made for each
individual WKWebView to prevent unintentionally making it enabled for a view
or context you don't intend to be inspectable" (WebKit blog, 2023). Apps
linking against pre-13.3 SDKs follow the older rule: WebViews are inspectable
only when the host process is launched from Xcode (i.e., signed with
`get-task-allow`).

The legacy private `WKPreferences._developerExtrasEnabled` flag is similarly
host-side: it has to be set inside the host before Safari can attach. There
is no Apple-supported way to enable inspection on a WebView in another app
"from the outside."

The only known bypass is a memory patch of `WebInspector.framework`'s
`-[RWIRelayDelegateMac _allowApplication:bundleIdentifier:]` to always return
TRUE (see `zwo/patch_webinspect` on GitHub). This requires (a) **disabling
SIP**, (b) **root**, (c) `task_for_pid` access to Safari, and (d) opcode
offsets that change between macOS versions. Even ignoring that this is
hostile to ship to end users, it's a non-starter for a commercial extension —
asking developers to disable SIP to use a terminal pane is unacceptable.

Wire protocol: WebKit's remote inspector uses a JSON-over-WebSocket protocol
distinct from CDP. Open-source clients exist (`google/ios-webkit-debug-proxy`
for iOS, WebInspectorServer in WebKitGTK / WPE), but `ios-webkit-debug-proxy`
is iOS-only via usbmuxd and does not address third-party Mac apps. There is
no production-quality .NET client for the Web Inspector protocol.

Verdict: **no host opt-in → no inspection.** Mach injection / lldb / dylib
patching all require disabling SIP and shipping kernel-extension-grade
plumbing — out of scope for Concord.

## Maia on Studio Pro Mac

Maia **does run on Studio Pro for Mac** (Studio Pro 11 supports macOS Sonoma
14+; Mendix's release notes through 11.10 list no Mac-exclusion of Maia
features, and the 11.10 release notes mention Maia improvements without
platform carve-outs). The Mendix `refguide/maia-chat/` and
`refguide/mendix-ai-assistance/` pages do not document any platform-specific
behaviour, which strongly suggests Maia's chat panel renders in a WKWebView
on Mac the same way it renders in WebView2 on Windows. There is one
documented Maia-on-Mac issue (the chat panel can stay open even when Maia is
disabled in Preferences), but no public docs, forum posts, or GitHub issues
indicate that Maia is implemented differently on Mac, nor that Studio Pro
exposes a remote-debugging port for the Mac WKWebView.

## Studio Pro Extensions API

Inspection of `Mendix.StudioPro.ExtensionsAPI` 11.6.2's XML doc surface shows
**no Maia-related types**. The Services namespaces expose
`IBackgroundJobService`, `IDomainModelService`, `IExtensionFileService`,
`IHttpClientService`, `ILocalRunConfigurationsService`,
`IMicroflowActivitiesService`, `INavigationManagerService`,
`IPageGenerationService`, `IUntypedModelAccessService`, etc., plus UI
services (`IDialogService`, `IDockingWindowService`,
`INotificationPopupService`). There is no `IMaiaService`, `IAiAssistantService`,
`IChatService`, or anything similar. The only WebView surface
(`Mendix.StudioPro.ExtensionsAPI.UI.WebView.IWebView`) is for the extension's
*own* panels — it cannot reach into the Maia panel. There is no public,
in-process API for talking to Maia today.

## Alternative approaches (ranked)

1. **AX/osascript Tier-3 transport (most feasible).** Use macOS Accessibility
   to locate Maia's chat input and send-button elements, type via AppleScript
   `keystroke`, and read replies from the AX value tree. Concord already
   pays the Accessibility-permission cost for hotkeys, so the consent flow
   is established. Drawbacks: AX trees over WKWebView content are sparse
   (Apple exposes ARIA roles but not `innerText` of arbitrary `<p>`
   bubbles), so the sentinel-echo trick used in `maia_agent.js` may have
   to be replaced by polling the AX `AXSelectedText` / `AXValue` of the
   chat region. Brittle but plausible. Prototype effort: ~1-2 days.

2. **Lobby Mendix to expose Maia via Extensions API.** Cleanest long-term.
   File a feature request for an `IMaiaService` (or even just an
   `OpenMaiaPrompt(string) → Task<string>`). This is what Concord should
   want regardless — it's the only way Maia integration becomes durable
   instead of fighting generated CSS class names.

3. **Lobby Mendix to set `isInspectable = YES` behind a debug flag on Mac.**
   Equivalent of `--remote-debugging-port` on Windows. If they did this,
   the existing CDP-injected agent could be ported to the Web Inspector
   protocol relatively cheaply (different wire format, same DOM walk).

4. **patch_webinspect-style SIP bypass.** Rejected — unshippable.

5. **HTTP/MCP IPC to Maia.** No evidence Maia exposes one. Studio Pro's
   own MCP server is for Studio Pro UI actions, not for talking to Maia.

## Recommendation

**Punt on Tier-1/Tier-2 (CDP) on Mac. Don't prototype patch_webinspect.**
Two parallel actions:

1. File the Mendix feature request now (`isInspectable` flag *or*
   `IMaiaService`). Keep it minimal so it has a chance of landing.
2. If Maia-on-Mac is a near-term must-have, prototype the AX/osascript
   Tier-3 transport against Studio Pro on Mac. Allocate one day to
   inspect the Maia panel via Accessibility Inspector (Xcode > Open
   Developer Tool > Accessibility Inspector) and confirm the chat
   text/input/send-button are reachable through AX before committing
   to building it. If AX coverage is too thin, the work stops there
   and Mac users get the same "Maia integration: Windows-only" message
   they get today.

Until either lands, keep the existing README copy: Maia integration is
Windows-only.

## Sources

- WebKit blog, *Enabling the Inspection of Web Content in Apps* — https://webkit.org/blog/13936/enabling-the-inspection-of-web-content-in-apps/
- Apple Developer Documentation, `WKWebView.isInspectable` — https://developer.apple.com/documentation/webkit/wkwebview/isinspectable
- Apple Developer Documentation, *Enabling inspecting content in your apps* — https://developer.apple.com/documentation/safari-developer-tools/enabling-inspecting-content-in-your-apps
- Daniel Dallos, *The dark side of WKWebView.isInspectable* — https://danieldallos.com/posts/2023/04/the-dark-side-of-wkwebview.isinspectable/
- `zwo/patch_webinspect` (SIP-disabling memory patch PoC) — https://github.com/zwo/patch_webinspect
- `google/ios-webkit-debug-proxy` (iOS-only WIRP→CDP bridge) — https://github.com/google/ios-webkit-debug-proxy
- WebKit Trac, *Enabling the Web Inspector* / Remote Inspector — https://trac.webkit.org/wiki/WebInspector and https://trac.webkit.org/wiki/RemoteInspectorGTKandWPE
- WebKit bug 154401, *Allow Web Inspector usage for release builds of WKWebView* — https://bugs.webkit.org/show_bug.cgi?id=154401
- Mendix Documentation, *Maia Chat* — https://docs.mendix.com/refguide/maia-chat/
- Mendix Documentation, *Mendix AI Assistance (Maia)* — https://docs.mendix.com/refguide/mendix-ai-assistance/
- Mendix Documentation, *Studio Pro 11.10 release notes* — https://docs.mendix.com/releasenotes/studio-pro/11.10/
- Mendix Documentation, *Using Mendix Studio Pro on a Mac* — https://docs.mendix.com/refguide/using-mendix-studio-pro-on-a-mac/
- Mendix Documentation, *System Requirements* — https://docs.mendix.com/refguide/system-requirements/
- `Mendix.StudioPro.ExtensionsAPI` 11.6.2 NuGet — XML doc surface inspection (no Maia types present), local NuGet cache at `~/.nuget/packages/mendix.studiopro.extensionsapi/11.6.2/lib/net8.0/Mendix.StudioPro.ExtensionsAPI.xml`
