namespace Concord.Core.Tests.Fakes;

using Terminal.Interop;

public sealed class FakeDomainModelHost : IDomainModelHost
{
    public ModuleId CreateModule(string moduleName) => throw new NotImplementedException();
    public void RenameModule(ModuleId moduleId, string newName) => throw new NotImplementedException();
    public IReadOnlyList<EntityRef> ListEntities(ModuleId moduleId) => Array.Empty<EntityRef>();
    public EntityShape ReadEntity(EntityRef entity) => throw new NotImplementedException();
    public EntityRef CreateEntity(CreateEntityRequest request) => throw new NotImplementedException();
    public IReadOnlyList<EntityRef> CreateMultipleEntities(IReadOnlyList<CreateEntityRequest> requests) => throw new NotImplementedException();
    public IReadOnlyList<EntityRef> CreateDomainModelFromSchema(string moduleName, string schemaJson) => throw new NotImplementedException();
    public void RenameEntity(EntityRef entity, string newName) => throw new NotImplementedException();
    public void DeleteEntity(EntityRef entity) => throw new NotImplementedException();
    public AttributeRef AddAttribute(EntityRef entity, AttributeSpec spec) => throw new NotImplementedException();
    public void RenameAttribute(EntityRef entity, AttributeRef attribute, string newName) => throw new NotImplementedException();
    public void UpdateAttribute(EntityRef entity, AttributeRef attribute, AttributeSpec newSpec) => throw new NotImplementedException();
    public void DeleteAttribute(EntityRef entity, AttributeRef attribute) => throw new NotImplementedException();
    public void SetCalculatedAttribute(EntityRef entity, AttributeRef attribute, string microflowQualifiedName) => throw new NotImplementedException();
    public void ConfigureSystemAttributes(EntityRef entity, bool? hasCreatedDate = null, bool? hasChangedDate = null, bool? hasOwner = null, bool? hasChangedBy = null, bool? persistable = null) => throw new NotImplementedException();
    public AssociationRef CreateAssociation(CreateAssociationRequest request) => throw new NotImplementedException();
    public IReadOnlyList<AssociationRef> CreateMultipleAssociations(IReadOnlyList<CreateAssociationRequest> requests) => throw new NotImplementedException();
    public void RenameAssociation(AssociationRef association, string newName) => throw new NotImplementedException();
    public void UpdateAssociation(AssociationRef association, AssociationType? newType = null, DeleteBehavior? newParentDeleteBehavior = null, DeleteBehavior? newChildDeleteBehavior = null, AssociationOwner? newOwner = null, string? newDocumentation = null) => throw new NotImplementedException();
    public void DeleteAssociation(AssociationRef association) => throw new NotImplementedException();
    public IReadOnlyList<AssociationQueryItem> QueryAssociations(string? entityQualifiedName = null, string? secondEntityQualifiedName = null, string? moduleName = null, string direction = "both") => throw new NotImplementedException();
    public void SetGeneralization(EntityRef entity, EntityRef parent) => throw new NotImplementedException();
    public void RemoveGeneralization(EntityRef entity) => throw new NotImplementedException();
    public void AddEventHandler(EntityRef entity, EventHandlerSpec handler) => throw new NotImplementedException();
    public IReadOnlyList<EnumerationRef> ListEnumerations(ModuleId moduleId) => Array.Empty<EnumerationRef>();
    public EnumerationRef CreateEnumeration(string moduleName, string name, IReadOnlyList<EnumerationValueSpec> values) => throw new NotImplementedException();
    public void RenameEnumerationValue(string enumerationQualifiedName, string oldValueName, string newValueName) => throw new NotImplementedException();
    public void UpdateEnumeration(EnumerationRef enumeration, IReadOnlyList<EnumerationValueSpec>? addValues = null, IReadOnlyList<string>? removeValues = null, IReadOnlyDictionary<string, string>? renameValues = null) => throw new NotImplementedException();
    public void RenameDocument(DocumentId document, string newName) => throw new NotImplementedException();
    public void SetEntityDocumentation(EntityRef entity, string documentation) => throw new NotImplementedException();
    public void SetAttributeDocumentation(EntityRef entity, AttributeRef attribute, string documentation) => throw new NotImplementedException();
    public void SetAssociationDocumentation(AssociationRef association, string documentation) => throw new NotImplementedException();
    public void SetDomainModelDocumentation(ModuleId moduleId, string documentation) => throw new NotImplementedException();
    public void ArrangeDomainModel(ArrangeDomainModelRequest request) => throw new NotImplementedException();
    public NameValidationResult? ValidateName(string name, bool autoFix = false) => throw new NotImplementedException();
    public CopyResult CopyElement(CopyRequest request) => throw new NotImplementedException();
    public IReadOnlyList<ModelCheckItem> CheckModel(string? moduleName = null) => throw new NotImplementedException();
}
