namespace Concord.Core.Tests.Fakes;

using Terminal.Interop;

public sealed class FakePageGenerationHost : IPageGenerationHost
{
    public PageGenerationResult GenerateOverviewPages(PageGenerationRequest request) => throw new NotImplementedException();
    public bool DeleteDocument(DocumentId document) => throw new NotImplementedException();
}
