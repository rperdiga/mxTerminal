# Concord MCP — argument shapes (reverse-engineered)

> Source of truth for the matrix.jsonc entries. For each tool, derived by
> grepping `MendixDomainModelTools.cs` and `MendixAdditionalTools.cs` for the
> `parameters?["X"]?.ToString()` access pattern and the explicit "X is
> required" guards. Where source allows multiple shapes, the most-likely-
> success shape is documented along with the ambiguity.

## Conventions

- **Required fields** are listed first; **optional** below them with default.
- File-line cite is to the C# method body, not the dispatch table.
- "Notes" captures source-visible quirks (e.g. ToString on a JsonArray; a
  branch for both string and array shape on the same field).

---
