namespace Concord.Core.Tests;

using FluentAssertions;
using System.Text.Json.Nodes;
using Terminal;
using Terminal.Mcp;
using Xunit;

public class ToolCatalogTests
{
    private static ITool MakeTool(string name, ToolFamily family) =>
        new SimpleTool(name, family, _ => Task.FromResult<object>("{}"));

    [Fact]
    public void RegisteredTools_Visible_When_TargetIs10x()
    {
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        catalog.Register(MakeTool("create_entity", ToolFamily.DomainModel));
        catalog.Register(MakeTool("save_all", ToolFamily.UiActions));
        catalog.ListVisibleNames().Should().BeEquivalentTo("create_entity", "save_all");
    }

    [Fact]
    public void OnStudio11x_OnlyAllowlistedTools_Visible()
    {
        var catalog = new ToolCatalog(TargetMode.Studio11x);
        catalog.Register(MakeTool("create_entity", ToolFamily.DomainModel));        // not on allowlist
        catalog.Register(MakeTool("delete_model_element", ToolFamily.DomainModel)); // on allowlist
        catalog.ListVisibleNames().Should().BeEquivalentTo("delete_model_element");
    }

    [Fact]
    public void FamilyDisabled_RemovesTools_OnBothModes()
    {
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        catalog.Register(MakeTool("create_entity", ToolFamily.DomainModel));
        catalog.Register(MakeTool("save_all", ToolFamily.UiActions));
        catalog.SetFamilyEnabled(ToolFamily.DomainModel, enabled: false);
        catalog.ListVisibleNames().Should().BeEquivalentTo("save_all");
    }

    [Fact]
    public async Task Invoke_DispatchesToRegisteredHandler()
    {
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        catalog.Register(new SimpleTool("echo", ToolFamily.Diagnostics,
            args => Task.FromResult<object>(args.ToJsonString())));
        var result = await catalog.InvokeAsync("echo", new JsonObject { ["msg"] = "hi" });
        ((string)result).Should().Contain("hi");
    }

    private sealed record SimpleTool(string Name, ToolFamily Family, Func<JsonObject, Task<object>> Invoke) : ITool;
}
