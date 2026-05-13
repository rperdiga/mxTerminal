namespace Concord.Core.Tests;

using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Terminal.Interop;
using Terminal.Spmcp.Tools;
using Xunit;

public class SpmcpSmokeTests : IDisposable
{
    public SpmcpSmokeTests()
    {
        HostServices.Reset();
        HostServices.Register(
            app: new Fakes.FakeAppHost(),
            runConfigs: new Fakes.FakeRunConfigsHost(),
            runState: new Fakes.FakeRunStateHost(),
            moduleImport: new Fakes.FakeModuleImportHost(),
            model: new Fakes.FakeModelHost(),
            domainModel: new Fakes.FakeDomainModelHost(),
            pageGeneration: new Fakes.FakePageGenerationHost(),
            navigation: new Fakes.FakeNavigationHost(),
            versionControl: new Fakes.FakeVersionControlHost(),
            untypedModel: new Fakes.FakeUntypedModelHost(),
            microflowAuthoring: new Fakes.FakeMicroflowAuthoringHost());
    }

    public void Dispose() => HostServices.Reset();

    [Fact]
    public async Task MendixAdditionalTools_GetLastError_ReturnsJson()
    {
        var tools = new MendixAdditionalTools(NullLogger<MendixAdditionalTools>.Instance);
        var result = await tools.GetLastError(new JsonObject());
        Assert.NotNull(result);
        var json = result.ToString();
        Assert.NotNull(json);
        // No error has been set — should contain the "no errors" message
        Assert.Contains("No errors recorded", json);
    }

    [Fact]
    public async Task MendixAdditionalTools_ListAvailableTools_ReturnsJson()
    {
        var tools = new MendixAdditionalTools(NullLogger<MendixAdditionalTools>.Instance);
        var result = await tools.ListAvailableTools(new JsonObject());
        Assert.NotNull(result);
        Assert.Contains("list_modules", result.ToString()!);
    }

    [Fact]
    public async Task MendixDomainModelTools_ListModules_HitsModelHost()
    {
        var tools = new MendixDomainModelTools(NullLogger<MendixDomainModelTools>.Instance);
        var result = await tools.ListModules(new JsonObject());
        Assert.NotNull(result);
        // FakeModelHost.ListModules returns one synthetic ModuleId named "TestModule"
        Assert.Contains("TestModule", result);
    }

    [Fact]
    public async Task MendixDomainModelTools_ReadProjectInfo_ReturnsProjectMetadata()
    {
        var tools = new MendixDomainModelTools(NullLogger<MendixDomainModelTools>.Instance);
        var result = await tools.ReadProjectInfo(new JsonObject());
        Assert.NotNull(result);
        Assert.Contains("TestProject", result);
    }
}
