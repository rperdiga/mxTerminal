namespace Concord.Core.Tests.Fakes;

using Terminal.Interop;

public sealed class FakeMicroflowAuthoringHost : IMicroflowAuthoringHost
{
    public bool IsAvailable => false;
    public IReadOnlyList<MicroflowSummary> ListMicroflows(ModuleId? moduleFilter) => throw new NotImplementedException();
    public MicroflowSummary? ReadMicroflow(string qualifiedName) => throw new NotImplementedException();
    public IReadOnlyList<MicroflowActivitySummary> ReadActivities(string microflowQualifiedName) => throw new NotImplementedException();
    public DocumentId Create(CreateMicroflowRequest request) => throw new NotImplementedException();
    public bool Delete(string microflowQualifiedName) => throw new NotImplementedException();
    public int AddActivity(ActivityInsertion insertion) => throw new NotImplementedException();
    public int InsertBeforeActivity(string microflowQualifiedName, int beforePosition, MicroflowActivitySummary activity) => throw new NotImplementedException();
    public void ModifyActivity(string microflowQualifiedName, int activityPosition, IReadOnlyDictionary<string, string> changes) => throw new NotImplementedException();
    public void DeleteActivity(string microflowQualifiedName, int activityPosition) => throw new NotImplementedException();
    public void SetUrl(string microflowQualifiedName, string? url) => throw new NotImplementedException();
    public void SetAccessLevel(string microflowQualifiedName, MicroflowAccessLevel level) => throw new NotImplementedException();
    public VariableNameCheckResult CheckVariableName(string microflowQualifiedName, string variableName) => throw new NotImplementedException();
    public IReadOnlyList<NanoflowSummary> ListNanoflows(ModuleId? moduleFilter) => throw new NotImplementedException();
    public NanoflowSummary? ReadNanoflow(string qualifiedName) => throw new NotImplementedException();
    public IReadOnlyList<JavaActionDescriptor> ListJavaActions(ModuleId? moduleFilter) => throw new NotImplementedException();
}
