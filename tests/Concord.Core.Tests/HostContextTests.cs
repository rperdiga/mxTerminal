namespace Concord.Core.Tests;

using Xunit;
using Terminal;
using Terminal.Interop;

[Collection("HostServices")]
public class HostContextTests
{
    [Fact]
    public void TargetMode_DefaultsToUninitialized_WhenHostHasNotSetIt()
    {
        HostContext.Reset();
        Assert.Equal(TargetMode.Uninitialized, HostContext.TargetMode);
    }

    [Fact]
    public void TargetMode_ReturnsValueSetByHost()
    {
        HostContext.Reset();
        HostContext.Initialize(TargetMode.Studio11x);
        Assert.Equal(TargetMode.Studio11x, HostContext.TargetMode);
    }

    [Fact]
    public void Initialize_Throws_WhenCalledTwice()
    {
        HostContext.Reset();
        HostContext.Initialize(TargetMode.Studio11x);
        Assert.Throws<InvalidOperationException>(() => HostContext.Initialize(TargetMode.Studio10x));
    }

    [Fact]
    public void Initialize_Throws_WhenCalledWithUninitialized()
    {
        HostContext.Reset();
        Assert.Throws<ArgumentException>(() => HostContext.Initialize(TargetMode.Uninitialized));
    }

    [Fact]
    public void HostServices_ResolvesModelHost_AfterFullRegisterCalled()
    {
        HostContext.Reset();
        HostServices.Reset();

        var fakeModel = new Fakes.FakeModelHost();
        HostServices.Register(
            app: new Fakes.FakeAppHost(),
            runConfigs: new Fakes.FakeRunConfigsHost(),
            runState: new Fakes.FakeRunStateHost(),
            moduleImport: new Fakes.FakeModuleImportHost(),
            model: fakeModel,
            domainModel: new Fakes.FakeDomainModelHost(),
            pageGeneration: new Fakes.FakePageGenerationHost(),
            navigation: new Fakes.FakeNavigationHost(),
            versionControl: new Fakes.FakeVersionControlHost(),
            untypedModel: new Fakes.FakeUntypedModelHost(),
            microflowAuthoring: new Fakes.FakeMicroflowAuthoringHost());

        Assert.Same(fakeModel, HostServices.Model);
    }

    [Fact]
    public void HostServices_Model_ThrowsBeforeRegister()
    {
        HostServices.Reset();
        Assert.Throws<InvalidOperationException>(() => _ = HostServices.Model);
    }

    [Fact]
    public void HostServices_All11Accessors_ResolveAfterFullRegister()
    {
        HostContext.Reset();
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

        Assert.NotNull(HostServices.App);
        Assert.NotNull(HostServices.RunConfigurations);
        Assert.NotNull(HostServices.RunState);
        Assert.NotNull(HostServices.ModuleImport);
        Assert.NotNull(HostServices.Model);
        Assert.NotNull(HostServices.DomainModel);
        Assert.NotNull(HostServices.PageGeneration);
        Assert.NotNull(HostServices.Navigation);
        Assert.NotNull(HostServices.VersionControl);
        Assert.NotNull(HostServices.UntypedModel);
        Assert.NotNull(HostServices.MicroflowAuthoring);
    }

    [Fact]
    public void HostServices_LegacyFourArgRegister_StillWorks_LeavesNewAccessorsUninitialized()
    {
        HostContext.Reset();
        HostServices.Reset();

        HostServices.Register(
            app: new Fakes.FakeAppHost(),
            runConfigs: new Fakes.FakeRunConfigsHost(),
            runState: new Fakes.FakeRunStateHost(),
            moduleImport: new Fakes.FakeModuleImportHost());

        // Old accessors resolve
        Assert.NotNull(HostServices.App);

        // New accessors throw because the legacy overload didn't supply them
        Assert.Throws<InvalidOperationException>(() => _ = HostServices.Model);
    }

    [Fact]
    public void HostServices_RunStateProbe_ThrowsBeforeSetterCalled()
    {
        HostServices.Reset();
        Assert.Throws<InvalidOperationException>(() => _ = HostServices.RunStateProbe);
    }

    [Fact]
    public void HostServices_UiAutomation_ThrowsBeforeSetterCalled()
    {
        HostServices.Reset();
        Assert.Throws<InvalidOperationException>(() => _ = HostServices.UiAutomation);
    }

    [Fact]
    public void HostServices_SetRunStateProbe_ResolvesAfterSet()
    {
        HostServices.Reset();
        var fake = new Fakes.FakeRunStateProbe();
        HostServices.SetRunStateProbe(fake);
        Assert.Same(fake, HostServices.RunStateProbe);
    }

    [Fact]
    public void HostServices_SetUiAutomation_ResolvesAfterSet()
    {
        HostServices.Reset();
        var fake = new Fakes.FakeUiAutomation();
        HostServices.SetUiAutomation(fake);
        Assert.Same(fake, HostServices.UiAutomation);
    }

    [Fact]
    public void HostServices_Reset_ClearsRunStateProbeAndUiAutomation()
    {
        HostServices.SetRunStateProbe(new Fakes.FakeRunStateProbe());
        HostServices.SetUiAutomation(new Fakes.FakeUiAutomation());
        HostServices.Reset();
        Assert.Throws<InvalidOperationException>(() => _ = HostServices.RunStateProbe);
        Assert.Throws<InvalidOperationException>(() => _ = HostServices.UiAutomation);
    }

    // Pane-scoped model-tier setters (the production registration path).
    // The 11-arg Register overload covers test paths above where fakes can
    // be constructed at registration time; these setters cover production
    // where IModel comes from CurrentApp and is only available after pane Open.

    [Fact]
    public void HostServices_SetModel_ResolvesAfterSet()
    {
        HostServices.Reset();
        var fake = new Fakes.FakeModelHost();
        HostServices.SetModel(fake);
        Assert.Same(fake, HostServices.Model);
    }

    [Fact]
    public void HostServices_SetDomainModel_ResolvesAfterSet()
    {
        HostServices.Reset();
        var fake = new Fakes.FakeDomainModelHost();
        HostServices.SetDomainModel(fake);
        Assert.Same(fake, HostServices.DomainModel);
    }

    [Fact]
    public void HostServices_SetPageGeneration_ResolvesAfterSet()
    {
        HostServices.Reset();
        var fake = new Fakes.FakePageGenerationHost();
        HostServices.SetPageGeneration(fake);
        Assert.Same(fake, HostServices.PageGeneration);
    }

    [Fact]
    public void HostServices_SetNavigation_ResolvesAfterSet()
    {
        HostServices.Reset();
        var fake = new Fakes.FakeNavigationHost();
        HostServices.SetNavigation(fake);
        Assert.Same(fake, HostServices.Navigation);
    }

    [Fact]
    public void HostServices_SetVersionControl_ResolvesAfterSet()
    {
        HostServices.Reset();
        var fake = new Fakes.FakeVersionControlHost();
        HostServices.SetVersionControl(fake);
        Assert.Same(fake, HostServices.VersionControl);
    }

    [Fact]
    public void HostServices_SetUntypedModel_ResolvesAfterSet()
    {
        HostServices.Reset();
        var fake = new Fakes.FakeUntypedModelHost();
        HostServices.SetUntypedModel(fake);
        Assert.Same(fake, HostServices.UntypedModel);
    }

    [Fact]
    public void HostServices_SetMicroflowAuthoring_ResolvesAfterSet()
    {
        HostServices.Reset();
        var fake = new Fakes.FakeMicroflowAuthoringHost();
        HostServices.SetMicroflowAuthoring(fake);
        Assert.Same(fake, HostServices.MicroflowAuthoring);
    }

    [Fact]
    public void HostServices_Reset_ClearsAllModelTierSetters()
    {
        HostServices.SetModel(new Fakes.FakeModelHost());
        HostServices.SetDomainModel(new Fakes.FakeDomainModelHost());
        HostServices.SetPageGeneration(new Fakes.FakePageGenerationHost());
        HostServices.SetNavigation(new Fakes.FakeNavigationHost());
        HostServices.SetVersionControl(new Fakes.FakeVersionControlHost());
        HostServices.SetUntypedModel(new Fakes.FakeUntypedModelHost());
        HostServices.SetMicroflowAuthoring(new Fakes.FakeMicroflowAuthoringHost());

        HostServices.Reset();

        Assert.Throws<InvalidOperationException>(() => _ = HostServices.Model);
        Assert.Throws<InvalidOperationException>(() => _ = HostServices.DomainModel);
        Assert.Throws<InvalidOperationException>(() => _ = HostServices.PageGeneration);
        Assert.Throws<InvalidOperationException>(() => _ = HostServices.Navigation);
        Assert.Throws<InvalidOperationException>(() => _ = HostServices.VersionControl);
        Assert.Throws<InvalidOperationException>(() => _ = HostServices.UntypedModel);
        Assert.Throws<InvalidOperationException>(() => _ = HostServices.MicroflowAuthoring);
    }

    [Fact]
    public void HostServices_SetModel_AcceptsNull_ClearsBinding()
    {
        HostServices.Reset();
        HostServices.SetModel(new Fakes.FakeModelHost());
        HostServices.SetModel(null);
        Assert.Throws<InvalidOperationException>(() => _ = HostServices.Model);
    }
}
