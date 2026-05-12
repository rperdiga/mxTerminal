# W2 Discovery Spike — Findings

Date: 2026-05-12
Branch: feat/v5.0.0-w2-mcpx-merge
W1 anchor: v5.0.0-alpha.1 @ 6dbfbf7

## Studio Pro 11.x tools/list snapshot
- Captured: no — deferred (requires running Studio Pro 11.10 with MCP attached)
- STUDIO_PRO_11X_TOOLS_LIST = TBD
- Implications: Phase 5 Task 18 uses spec lines 198-211 as working assumption.
  Allowlist reconciliation is a follow-up before W2 ships.

## MCPExtension subtree source
- Approach: local git init (option a)
- Source ref: local tag `concord-w2-import` at C:\Extensions\MCPExtension
- Commit: 329180c — "import: MCPExtension snapshot for Concord W2 subtree merge"
- Directories confirmed present: Tools/, Handlers/, Utils/, backport-10x/
- (If a remote URL becomes available later, the plan's option b can replace this.)

## Open compile-time blockers expected during Phase 2
- (record findings here as Phase 2 progresses)
