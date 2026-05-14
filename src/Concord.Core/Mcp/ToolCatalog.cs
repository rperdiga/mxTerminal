namespace Terminal.Mcp;

using System.Text.Json.Nodes;
using Terminal;

public sealed class ToolCatalog
{
    private readonly TargetMode _mode;
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ToolFamily> _disabledFamilies = new();
    private readonly object _gate = new();

    public ToolCatalog(TargetMode mode) => _mode = mode;

    public void Register(ITool tool)
    {
        lock (_gate) _tools[tool.Name] = tool;
    }

    public void SetFamilyEnabled(ToolFamily family, bool enabled)
    {
        lock (_gate)
        {
            if (enabled) _disabledFamilies.Remove(family);
            else _disabledFamilies.Add(family);
        }
    }

    public IReadOnlyList<string> ListVisibleNames()
    {
        lock (_gate)
            return _tools.Values
                         .Where(IsVisible)
                         .Select(t => t.Name)
                         .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                         .ToList();
    }

    public IReadOnlyList<ITool> ListVisibleTools()
    {
        lock (_gate) return _tools.Values.Where(IsVisible).ToList();
    }

    public Task<object> InvokeAsync(string name, JsonObject arguments)
    {
        ITool? tool;
        lock (_gate)
        {
            if (!_tools.TryGetValue(name, out tool) || !IsVisible(tool))
                throw new InvalidOperationException(
                    $"Tool '{name}' is not registered or is filtered out for TargetMode={_mode}.");
        }
        return tool.Invoke(arguments);
    }

    private bool IsVisible(ITool tool)
    {
        if (_disabledFamilies.Contains(tool.Family)) return false;
        if (_mode == TargetMode.Studio11x && !Studio11xAllowlist.Contains(tool.Name)) return false;
        return true;
    }
}

public static class ToolCatalogRegistry
{
    public static ToolCatalog? Active { get; set; }
}
