namespace Terminal.Spmcp.Utils;

public class Utils
{
    /// <summary>
    /// Reads a parameter by canonical name, falling back to aliases if the canonical name is absent.
    /// Prevents silent failures when LLMs use slightly wrong parameter names.
    /// Example: GetParam(p, "name", "microflow_name", "microflowName")
    /// </summary>
    public static string? GetParam(System.Text.Json.Nodes.JsonObject? p, string canonical, params string[] aliases)
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
}
