namespace Terminal.Spmcp.Utils;

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Terminal.Interop;

public class Utils
{
    /// <summary>
    /// Reads a parameter by canonical name, falling back to aliases if the canonical name is absent.
    /// Prevents silent failures when LLMs use slightly wrong parameter names.
    /// Example: GetParam(p, "name", "microflow_name", "microflowName")
    /// </summary>
    public static string? GetParam(JsonObject? p, string canonical, params string[] aliases)
    {
        if (p == null) return null;
        var v = p[canonical]?.ToString();
        if (v != null) return v;
        foreach (var alias in aliases)
        {
            v = p[alias]?.ToString();
            if (v != null) return v;
        }
        return null;
    }

    /// <summary>
    /// Reads an array parameter by canonical name + aliases, accepting BOTH a
    /// real <see cref="JsonArray"/> AND a string-encoded JSON array. Without
    /// per-tool input schemas in <c>tools/list</c>, some MCP clients
    /// (notably Claude Code as of v2.1) conservatively serialize complex
    /// values as JSON strings — so a caller's <c>values: ["a","b"]</c>
    /// reaches us as <c>values: "[\"a\",\"b\"]"</c>. Defensive parsing here
    /// keeps the SPMCP tools usable until per-tool schemas land in alpha.3.
    /// <para>
    /// Returns null when no matching parameter is present, or when the value
    /// is neither a JsonArray nor a parseable JSON-array string. Returns an
    /// empty array (caller should treat the same as null) only if the input
    /// was explicitly an empty array.
    /// </para>
    /// </summary>
    public static JsonArray? GetArrayParam(JsonObject? p, string canonical, params string[] aliases)
    {
        if (p == null) return null;

        var keys = new List<string>(1 + aliases.Length) { canonical };
        keys.AddRange(aliases);

        foreach (var key in keys)
        {
            var node = p[key];
            if (node is null) continue;

            // Direct array — the contract per MCP spec.
            if (node is JsonArray arr) return arr;

            // String-encoded JSON array — defensive fallback for clients that
            // stringify complex args. Only attempt parse when the string
            // looks array-shaped to avoid spurious System.Text.Json work.
            if (node is JsonValue v && v.TryGetValue<string>(out var s))
            {
                var trimmed = s.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    try
                    {
                        var parsed = JsonNode.Parse(trimmed) as JsonArray;
                        if (parsed != null) return parsed;
                    }
                    catch (System.Text.Json.JsonException)
                    {
                        // Not parseable — fall through to next alias.
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Wraps a per-module ExtensionsAPI call that may throw KeyNotFoundException
    /// for ModuleProxy on Studio Pro 10.x system / App-Store modules. Returns the
    /// call result on success; records the skip and returns default(T) on the
    /// known-pattern failure. Non-matching exceptions re-throw so unrelated bugs
    /// aren't silently swallowed.
    /// </summary>
    public static T? TryPerModule<T>(
        ModuleId moduleId,
        Func<T> call,
        List<object> skipped,
        string operation,
        ILogger logger)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (ex is KeyNotFoundException
                                && ex.Message.Contains("ModuleProxy"))
        {
            logger.LogWarning(ex, "{Operation} failed for module {Module}", operation, moduleId.Name);
            skipped.Add(new
            {
                module = moduleId.Name,
                operation,
                error = ex.Message,
                note = "Module's index isn't queryable via this Studio Pro version's extension API (often happens for system / App-Store-imported modules on 10.x). Skipped."
            });
            return default;
        }
    }
}
