namespace Concord.Core.Tests.Fakes;

using Terminal.Interop;

public sealed class FakeUntypedModelHost : IUntypedModelHost
{
    public bool IsAvailable => false;
    public IReadOnlyList<UntypedUnitDescriptor> GetUnitsOfType(string typeString) => throw new NotImplementedException();
    public string ReadUnitPropertiesAsJson(string qualifiedName) => throw new NotImplementedException();
    public string? ReadUnitProperty(string qualifiedName, string propertyName) => throw new NotImplementedException();
}
