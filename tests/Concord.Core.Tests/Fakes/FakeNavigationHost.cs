namespace Concord.Core.Tests.Fakes;

using Terminal.Interop;

public sealed class FakeNavigationHost : INavigationHost
{
    public IReadOnlyList<NavigationProfile> ListProfiles() => throw new NotImplementedException();
    public NavigationProfile? ReadProfile(string profileName) => throw new NotImplementedException();
    public void AddItem(string profileName, NavigationItem item) => throw new NotImplementedException();
    public void AddItems(string profileName, IReadOnlyList<NavigationItem> items) => throw new NotImplementedException();
    public void RemoveItem(string profileName, string caption) => throw new NotImplementedException();
    public void SetItemTarget(string profileName, string caption, string microflowOrPageQualifiedName) => throw new NotImplementedException();
    public void SetItemIcon(string profileName, string caption, string? iconQualifiedName) => throw new NotImplementedException();
}
