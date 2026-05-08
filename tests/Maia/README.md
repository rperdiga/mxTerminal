# Maia tests

Three layers:

1. **Unit** — `MaiaRouterTests`, `MaiaActionsTests`, `CdpInjectedTransportTests`,
   `CdpChatTransportTests`, `MaiaJsonRpcTests`, `EmbeddedResourceTests`. Run on
   every PR. No Studio Pro needed.

2. **JS agent unit** — TODO: port the prototype's 12 tests for `maia_agent.js`
   via Node subprocess.

3. **Live (`[Trait("Category","MaiaLive")]`)** — `MaiaLiveTests`. Skipped unless
   `CONCORD_MAIA_LIVE=1` is set. Requires:
   - Studio Pro 11.10+ running, single instance.
   - Maia panel visible (click the Maia tab in the right pane).
   - Concord extension loaded (so we share an environment, but the tests
     drive Maia directly via CDP — Concord need not be wired in).

   Run locally: `$env:CONCORD_MAIA_LIVE=1; dotnet test --filter "Category=MaiaLive"`.
