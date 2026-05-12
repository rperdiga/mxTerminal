namespace Terminal.Interop;

// ─── Attribute types ────────────────────────────────────────────────────────
// Mirrors Mendix DataType (IAttributeType sub-types), absorbing the
// Mendix.StudioPro.ExtensionsAPI.Model.DataTypes dependency at the boundary.

public enum AttributeKind
{
    String,
    Integer,
    LongType,
    Decimal,
    Boolean,
    DateTime,
    Enumeration,
    AutoNumber,
    HashString,
    Binary,
    Object
}

// ─── Association types ───────────────────────────────────────────────────────
// Mirrors Mendix AssociationType (Reference = one-to-many, ReferenceSet = many-to-many).

public enum AssociationType { Reference, ReferenceSet }

// Mirrors Mendix DeletingBehavior (note: Mendix uses "References" not "Referred").
public enum DeleteBehavior
{
    DeleteMeButKeepReferences,
    DeleteMeIfNoReferences,
    DeleteMeAndReferences
}

public enum AssociationOwner { Default, Both }

// ─── Event handler types ─────────────────────────────────────────────────────
// Each value combines the Mendix ActionMoment (Before/After) + EventType
// (Create/Commit/Delete/RollBack) into a single enum to avoid a separate moment
// parameter at the Interop boundary.

public enum EventHandlerKind
{
    BeforeCreate,
    AfterCreate,
    BeforeCommit,
    AfterCommit,
    BeforeDelete,
    AfterDelete,
    BeforeRollback,
    AfterRollback
}

// ─── Entity persistence kind ─────────────────────────────────────────────────
// Persistent = standard DB-backed; NonPersistent = in-memory only.
// ExternalDatabase and other template-based types may be added in future.
public enum EntityKind { Persistent, NonPersistent }

// ─── Lightweight ref structs (identity + display name only) ─────────────────

public readonly record struct EntityRef(Guid Id, string QualifiedName);
public readonly record struct AttributeRef(Guid Id, string Name, AttributeKind Kind);
public readonly record struct AssociationRef(
    Guid Id,
    string Name,
    string ParentEntityQualifiedName,
    string ChildEntityQualifiedName,
    AssociationType Type);
public readonly record struct EnumerationRef(Guid Id, string QualifiedName);
public readonly record struct AssociationQueryItem(
    string Name,
    string ParentEntityQualifiedName,
    string ChildEntityQualifiedName,
    AssociationType Type);

// ─── Attribute spec ──────────────────────────────────────────────────────────

/// <summary>
/// Full specification for creating or replacing an attribute.
/// <para><c>EnumerationQualifiedName</c> is required when <c>Kind == Enumeration</c>;
/// it may be a qualified name ("Module.EnumName") or a simple name.
/// </para>
/// <para>
/// <c>EnumerationValues</c> is used instead of <c>EnumerationQualifiedName</c> when
/// the caller wants the host to create a new inline enumeration (the host will
/// generate a name from the attribute name if none is provided).
/// </para>
/// </summary>
public record AttributeSpec(
    string Name,
    AttributeKind Kind,
    string? EnumerationQualifiedName,      // when Kind == Enumeration and linking to existing enum
    IReadOnlyList<string>? EnumerationValues, // when Kind == Enumeration and creating inline enum
    int? MaxLength,                         // for String
    bool? LocalizeDate,                     // for DateTime
    string? DefaultValue,
    string? Documentation,
    bool IsSystemAttribute = false);

// ─── Entity shape (read) ─────────────────────────────────────────────────────

/// <summary>
/// Full read-side shape of a single entity from the domain model.
/// </summary>
public record EntityShape(
    EntityRef Self,
    string ModuleName,
    EntityKind Kind,
    string? GeneralizationQualifiedName,
    string? Documentation,
    IReadOnlyList<AttributeRef> Attributes,
    IReadOnlyList<AssociationRef> OutgoingAssociations,
    IReadOnlyList<AssociationRef> IncomingAssociations,
    IReadOnlyList<string> EventHandlerDescriptions,
    double X,
    double Y);

// ─── Enumeration value spec ──────────────────────────────────────────────────

public record EnumerationValueSpec(string Name, string? Caption);

// ─── Create/update request records ──────────────────────────────────────────

public record CreateEntityRequest(
    string ModuleName,
    string EntityName,
    EntityKind Kind,
    string? Generalization,
    IReadOnlyList<AttributeSpec>? Attributes,
    string? Documentation,
    double X = 0,
    double Y = 0);

public record CreateAssociationRequest(
    string ModuleName,
    string Name,
    string ParentEntityQualifiedName,
    string ChildEntityQualifiedName,
    AssociationType Type,
    DeleteBehavior ParentDeleteBehavior,
    DeleteBehavior ChildDeleteBehavior,
    AssociationOwner Owner,
    string? Documentation);

public record EventHandlerSpec(
    EventHandlerKind Kind,
    string MicroflowQualifiedName,
    bool RaiseErrorOnFalse = true,
    bool PassEventObject = true);

/// <summary>
/// Request to auto-arrange entities in a domain model.
/// <para><c>RootEntityName</c> is optional; when supplied the host uses it as
/// the tree root for Sugiyama-style layout.</para>
/// </summary>
public record ArrangeDomainModelRequest(
    string ModuleName,
    string? RootEntityName = null);

public record CopyRequest(
    string ElementType,           // "entity" | "microflow" | "constant" | "enumeration"
    string SourceName,
    string? SourceModuleName,
    string? TargetModuleName,
    string NewName);

public record CopyResult(bool Success, string? TargetQualifiedName, string? Error);

// ─── Validation result ───────────────────────────────────────────────────────

public record NameValidationResult(bool IsValid, string? ErrorMessage, string? SuggestedFix);

// ─── Model-check results ─────────────────────────────────────────────────────

public enum ModelCheckSeverity { Error, Warning, Info }
public record ModelCheckItem(ModelCheckSeverity Severity, string ModuleName, string? EntityName, string Code, string Message);

// ─── Interface ───────────────────────────────────────────────────────────────

/// <summary>
/// Host-side contract for entity / attribute / association / generalization /
/// event-handler / enumeration / arrange / copy / rename / documentation
/// operations on a Mendix domain model.
///
/// <para>All Mendix-specific types are absorbed at this boundary:</para>
/// <list type="bullet">
///   <item><c>Mendix.StudioPro.ExtensionsAPI.Model.Texts.IText</c> → plain <see cref="string"/> documentation.</item>
///   <item><c>INameValidationService</c> → <see cref="ValidateName"/>.</item>
///   <item><c>DataType / IAttributeType</c> sub-types → <see cref="AttributeKind"/> + optional enumeration name.</item>
///   <item><c>DeletingBehavior</c> → <see cref="DeleteBehavior"/>.</item>
///   <item><c>AssociationType</c> → <see cref="AssociationType"/>.</item>
/// </list>
/// </summary>
public interface IDomainModelHost
{
    // ── Module-level ─────────────────────────────────────────────────────────

    /// <summary>Creates a new module with the given name and returns its id.</summary>
    ModuleId CreateModule(string moduleName);

    /// <summary>Renames a module (updates all qualified references in the model).</summary>
    void RenameModule(ModuleId moduleId, string newName);

    // ── Entity CRUD ──────────────────────────────────────────────────────────

    /// <summary>Lists all entities in a module.</summary>
    IReadOnlyList<EntityRef> ListEntities(ModuleId moduleId);

    /// <summary>Returns the full shape of a single entity including attributes, associations,
    /// event handlers, generalization, and layout position.</summary>
    EntityShape ReadEntity(EntityRef entity);

    /// <summary>Creates a single entity; returns a ref to the new entity.</summary>
    EntityRef CreateEntity(CreateEntityRequest request);

    /// <summary>Bulk entity creation within a single transaction.</summary>
    IReadOnlyList<EntityRef> CreateMultipleEntities(IReadOnlyList<CreateEntityRequest> requests);

    /// <summary>Creates entities + associations from a JSON schema object (the
    /// <c>create_domain_model_from_schema</c> tool pattern).</summary>
    IReadOnlyList<EntityRef> CreateDomainModelFromSchema(string moduleName, string schemaJson);

    /// <summary>Renames an entity (all qualified references are updated by the host).</summary>
    void RenameEntity(EntityRef entity, string newName);

    /// <summary>Deletes an entity and its owned associations.</summary>
    void DeleteEntity(EntityRef entity);

    // ── Attribute CRUD ───────────────────────────────────────────────────────

    /// <summary>Adds an attribute to an entity.</summary>
    AttributeRef AddAttribute(EntityRef entity, AttributeSpec spec);

    /// <summary>Renames an attribute (all references updated by the host).</summary>
    void RenameAttribute(EntityRef entity, AttributeRef attribute, string newName);

    /// <summary>Updates one or more properties of an existing attribute.
    /// The host applies only the non-null fields of <paramref name="newSpec"/>.</summary>
    void UpdateAttribute(EntityRef entity, AttributeRef attribute, AttributeSpec newSpec);

    /// <summary>Deletes an attribute.</summary>
    void DeleteAttribute(EntityRef entity, AttributeRef attribute);

    /// <summary>Converts an attribute to a calculated attribute driven by a microflow.</summary>
    void SetCalculatedAttribute(EntityRef entity, AttributeRef attribute, string microflowQualifiedName);

    /// <summary>Configures system-managed attributes on a root entity
    /// (HasCreatedDate, HasChangedDate, HasOwner, HasChangedBy, Persistable).
    /// Only valid on root entities (no generalization).</summary>
    void ConfigureSystemAttributes(
        EntityRef entity,
        bool? hasCreatedDate = null,
        bool? hasChangedDate = null,
        bool? hasOwner = null,
        bool? hasChangedBy = null,
        bool? persistable = null);

    // ── Association CRUD ─────────────────────────────────────────────────────

    /// <summary>Creates a single association.</summary>
    AssociationRef CreateAssociation(CreateAssociationRequest request);

    /// <summary>Bulk association creation within a single transaction.</summary>
    IReadOnlyList<AssociationRef> CreateMultipleAssociations(IReadOnlyList<CreateAssociationRequest> requests);

    /// <summary>Renames an association (all references updated by the host).</summary>
    void RenameAssociation(AssociationRef association, string newName);

    /// <summary>Updates one or more properties of an association.</summary>
    void UpdateAssociation(
        AssociationRef association,
        AssociationType? newType = null,
        DeleteBehavior? newParentDeleteBehavior = null,
        DeleteBehavior? newChildDeleteBehavior = null,
        AssociationOwner? newOwner = null,
        string? newDocumentation = null);

    /// <summary>Deletes an association.</summary>
    void DeleteAssociation(AssociationRef association);

    /// <summary>Queries associations, optionally filtered to a specific entity or pair of entities.</summary>
    IReadOnlyList<AssociationQueryItem> QueryAssociations(
        string? entityQualifiedName = null,
        string? secondEntityQualifiedName = null,
        string? moduleName = null,
        string direction = "both");

    // ── Generalization ────────────────────────────────────────────────────────

    /// <summary>Sets an entity's generalization (parent entity).</summary>
    void SetGeneralization(EntityRef entity, EntityRef parent);

    /// <summary>Removes an entity's generalization, making it a root entity.</summary>
    void RemoveGeneralization(EntityRef entity);

    // ── Event handlers ────────────────────────────────────────────────────────

    /// <summary>Adds an event handler to an entity.</summary>
    void AddEventHandler(EntityRef entity, EventHandlerSpec handler);

    // ── Enumeration documents ─────────────────────────────────────────────────

    /// <summary>Lists all enumeration documents in a module.</summary>
    IReadOnlyList<EnumerationRef> ListEnumerations(ModuleId moduleId);

    /// <summary>Creates a new enumeration document with the given values.</summary>
    EnumerationRef CreateEnumeration(string moduleName, string name, IReadOnlyList<EnumerationValueSpec> values);

    /// <summary>Renames an enumeration value within an existing enumeration.</summary>
    void RenameEnumerationValue(string enumerationQualifiedName, string oldValueName, string newValueName);

    /// <summary>Adds, removes, or renames values in an existing enumeration.</summary>
    void UpdateEnumeration(
        EnumerationRef enumeration,
        IReadOnlyList<EnumerationValueSpec>? addValues = null,
        IReadOnlyList<string>? removeValues = null,
        IReadOnlyDictionary<string, string>? renameValues = null);

    // ── Document-level operations ─────────────────────────────────────────────

    /// <summary>Renames any document type (microflow, page, constant, enumeration, etc.).
    /// All by-name references in the model are updated by Studio Pro.</summary>
    void RenameDocument(DocumentId document, string newName);

    // ── Documentation (absorbs Mendix.Model.Texts.IText) ─────────────────────

    /// <summary>Sets the documentation string on an entity.
    /// The host translates the plain string to an IText / string property internally.</summary>
    void SetEntityDocumentation(EntityRef entity, string documentation);

    /// <summary>Sets the documentation string on an attribute.</summary>
    void SetAttributeDocumentation(EntityRef entity, AttributeRef attribute, string documentation);

    /// <summary>Sets the documentation string on an association.</summary>
    void SetAssociationDocumentation(AssociationRef association, string documentation);

    /// <summary>Sets the documentation string on a module's domain model root.</summary>
    void SetDomainModelDocumentation(ModuleId moduleId, string documentation);

    // ── Layout / arrange ──────────────────────────────────────────────────────

    /// <summary>Auto-arranges entity positions in the domain model diagram using
    /// the host's built-in Sugiyama-style layout algorithm.</summary>
    void ArrangeDomainModel(ArrangeDomainModelRequest request);

    // ── Name validation (absorbs INameValidationService) ─────────────────────

    /// <summary>Validates whether a name is legal in Mendix (no reserved words,
    /// valid characters, etc.). Returns a suggestion when the name is invalid.
    /// Returns <c>null</c> when the host's INameValidationService is unavailable.</summary>
    NameValidationResult? ValidateName(string name, bool autoFix = false);

    // ── Copy / clone ──────────────────────────────────────────────────────────

    /// <summary>Copies a model element (entity, microflow, constant, or enumeration)
    /// to a target module under a new name.</summary>
    CopyResult CopyElement(CopyRequest request);

    // ── Model integrity checks ────────────────────────────────────────────────

    /// <summary>Runs a lightweight domain-model consistency check and returns
    /// errors, warnings, and info items.
    /// Pass <c>null</c> to check all non-AppStore modules.</summary>
    IReadOnlyList<ModelCheckItem> CheckModel(string? moduleName = null);
}
