namespace Terminal.Mcp;

using System.Text.Json.Nodes;

public interface ITool
{
    string Name { get; }
    ToolFamily Family { get; }
    Func<JsonObject, Task<object>> Invoke { get; }
}
