namespace Concord.Host11x.Interop;

using System.Security.Cryptography;
using System.Text;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.DomainModels;
using Mendix.StudioPro.ExtensionsAPI.Model.DataTypes;
using Mendix.StudioPro.ExtensionsAPI.Model.Enumerations;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.Texts;
using Terminal.Interop;

// Disambiguate between Mendix and Core interop enums that share names
using MxAssociationType = Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.AssociationType;
using MxAssociationOwner = Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.AssociationOwner;

/// <summary>
/// Implements IDomainModelHost against the 11.6.2 ExtensionsAPI surface.
/// Bodies ported from MendixDomainModelTools.cs (SPMCP source), with JSON
/// parsing stripped and Mendix modeling work retained.
/// </summary>
public sealed class DomainModelHost11x : IDomainModelHost
{
    private readonly IModel _model;
    private readonly Mendix.StudioPro.ExtensionsAPI.Services.INameValidationService? _nameValidation;

    public DomainModelHost11x(
        IModel model,
        Mendix.StudioPro.ExtensionsAPI.Services.INameValidationService? nameValidation = null)
    {
        _model = model;
        _nameValidation = nameValidation;
    }

    // ── Module-level ─────────────────────────────────────────────────────────

    public ModuleId CreateModule(string moduleName)
    {
        // Check for duplicate
        var existing = _model.Root.GetModules()
            .FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            throw new InvalidOperationException($"Module '{moduleName}' already exists.");

        using var tx = _model.StartTransaction("create module");
        var module = _model.Create<IModule>();
        module.Name = moduleName;
        _model.Root.AddModule(module);
        tx.Commit();

        return new ModuleId(ParseId(module.Id), module.Name);
    }

    public void RenameModule(ModuleId moduleId, string newName)
    {
        var module = ResolveModule(moduleId);
        using var tx = _model.StartTransaction($"Rename module '{module.Name}' to '{newName}'");
        module.Name = newName;
        tx.Commit();
    }

    // ── Entity CRUD ──────────────────────────────────────────────────────────

    public IReadOnlyList<EntityRef> ListEntities(ModuleId moduleId)
    {
        var module = ResolveModule(moduleId);
        return module.DomainModel.GetEntities()
            .Select(e => new EntityRef(GuidFromString($"{module.Name}.{e.Name}"), $"{module.Name}.{e.Name}"))
            .ToList();
    }

    public EntityShape ReadEntity(EntityRef entity)
    {
        var (mxEntity, module) = FindEntityByRef(entity);

        var generalization = mxEntity.Generalization is IGeneralization gen
            ? gen.Generalization?.ToString()
            : null;

        var kind = mxEntity.Generalization is INoGeneralization noGen
            ? (noGen.Persistable ? EntityKind.Persistent : EntityKind.NonPersistent)
            : EntityKind.Persistent; // specialized entities default to persistent

        var attributes = mxEntity.GetAttributes()
            .Select(a => new AttributeRef(GuidFromString($"{module.Name}.{mxEntity.Name}.{a.Name}"), a.Name, MapAttributeKind(a.Type)))
            .ToList();

        var outgoing = new List<AssociationRef>();
        var incoming = new List<AssociationRef>();
        foreach (var ea in mxEntity.GetAssociations(AssociationDirection.Both, null))
        {
            var aref = new AssociationRef(
                GuidFromString(ea.Association.Name),
                ea.Association.Name,
                ea.Parent?.Name ?? "",
                ea.Child?.Name ?? "",
                ea.Association.Type == MxAssociationType.Reference ? Terminal.Interop.AssociationType.Reference : Terminal.Interop.AssociationType.ReferenceSet);

            if (string.Equals(ea.Parent?.Name, mxEntity.Name, StringComparison.Ordinal))
                outgoing.Add(aref);
            else
                incoming.Add(aref);
        }

        var handlers = mxEntity.GetEventHandlers()
            .Select(h => $"{h.Moment} {h.Event} -> {h.Microflow}")
            .ToList();

        var loc = mxEntity.Location;

        return new EntityShape(
            Self: entity,
            ModuleName: module.Name,
            Kind: kind,
            GeneralizationQualifiedName: generalization,
            Documentation: mxEntity.Documentation,
            Attributes: attributes,
            OutgoingAssociations: outgoing,
            IncomingAssociations: incoming,
            EventHandlerDescriptions: handlers,
            X: loc.X,
            Y: loc.Y);
    }

    public EntityRef CreateEntity(CreateEntityRequest request)
    {
        var module = ResolveModuleByName(request.ModuleName);
        if (module.DomainModel == null)
            throw new InvalidOperationException($"Module '{request.ModuleName}' has no domain model.");

        using var tx = _model.StartTransaction("create entity");
        var mxEntity = CreatePersistentEntityCore(module, request.EntityName, request.Attributes, request.Kind);

        if (!string.IsNullOrEmpty(request.Generalization))
        {
            var (parentEntity, _) = FindEntityAcrossModules(request.Generalization, null);
            if (parentEntity == null)
                throw new InvalidOperationException($"Generalization entity '{request.Generalization}' not found.");
            var gen = _model.Create<IGeneralization>();
            gen.Generalization = parentEntity.QualifiedName;
            mxEntity.Entity.Generalization = gen;
        }

        if (!string.IsNullOrEmpty(request.Documentation))
            mxEntity.Entity.Documentation = request.Documentation;

        if (request.X != 0 || request.Y != 0)
            mxEntity.Entity.Location = new Location((int)request.X, (int)request.Y);

        tx.Commit();
        return new EntityRef(GuidFromString($"{module.Name}.{mxEntity.Entity.Name}"), $"{module.Name}.{mxEntity.Entity.Name}");
    }

    public IReadOnlyList<EntityRef> CreateMultipleEntities(IReadOnlyList<CreateEntityRequest> requests)
    {
        var results = new List<EntityRef>();
        using var tx = _model.StartTransaction("create multiple entities");

        foreach (var request in requests)
        {
            var module = ResolveModuleByName(request.ModuleName);
            var existing = module.DomainModel.GetEntities()
                .FirstOrDefault(e => string.Equals(e.Name, request.EntityName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) continue; // skip duplicates

            var created = CreatePersistentEntityCore(module, request.EntityName, request.Attributes, request.Kind);

            if (!string.IsNullOrEmpty(request.Documentation))
                created.Entity.Documentation = request.Documentation;

            if (request.X != 0 || request.Y != 0)
                created.Entity.Location = new Location((int)request.X, (int)request.Y);

            results.Add(new EntityRef(GuidFromString($"{module.Name}.{created.Entity.Name}"), $"{module.Name}.{created.Entity.Name}"));
        }

        tx.Commit();
        return results;
    }

    public IReadOnlyList<EntityRef> CreateDomainModelFromSchema(string moduleName, string schemaJson)
    {
        var module = ResolveModuleByName(moduleName);
        var schema = System.Text.Json.JsonDocument.Parse(schemaJson).RootElement;
        var results = new List<EntityRef>();

        using var tx = _model.StartTransaction("create domain model from schema");

        // Create entities
        if (schema.TryGetProperty("entities", out var entitiesEl) && entitiesEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var entityEl in entitiesEl.EnumerateArray())
            {
                var name = entityEl.TryGetProperty("name", out var nEl) ? nEl.GetString()
                         : entityEl.TryGetProperty("entity_name", out var enEl) ? enEl.GetString()
                         : null;
                if (string.IsNullOrEmpty(name)) continue;

                var existing = module.DomainModel.GetEntities()
                    .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null) continue;

                var kind = EntityKind.Persistent;
                if (entityEl.TryGetProperty("entityType", out var etEl))
                    kind = etEl.GetString()?.ToLowerInvariant() == "non-persistent" ? EntityKind.NonPersistent : EntityKind.Persistent;

                // Parse attributes from JSON
                IReadOnlyList<AttributeSpec>? attrs = null;
                if (entityEl.TryGetProperty("attributes", out var attrsEl) && attrsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var attrList = new List<AttributeSpec>();
                    foreach (var attrEl in attrsEl.EnumerateArray())
                    {
                        var attrName = attrEl.TryGetProperty("name", out var anEl) ? anEl.GetString() : null;
                        var attrType = attrEl.TryGetProperty("type", out var atEl) ? atEl.GetString() : "String";
                        if (string.IsNullOrEmpty(attrName)) continue;
                        attrList.Add(new AttributeSpec(
                            Name: attrName,
                            Kind: ParseAttributeKind(attrType ?? "String"),
                            EnumerationQualifiedName: null,
                            EnumerationValues: null,
                            MaxLength: null,
                            LocalizeDate: null,
                            DefaultValue: attrEl.TryGetProperty("default_value", out var dvEl) ? dvEl.GetString() : null,
                            Documentation: null));
                    }
                    attrs = attrList;
                }

                var created = CreatePersistentEntityCore(module, name, attrs, kind);
                results.Add(new EntityRef(GuidFromString($"{module.Name}.{created.Entity.Name}"), $"{module.Name}.{created.Entity.Name}"));
            }
        }

        // Create associations
        if (schema.TryGetProperty("associations", out var assocsEl) && assocsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var assocEl in assocsEl.EnumerateArray())
            {
                var assocName = assocEl.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                var parent = assocEl.TryGetProperty("parent", out var pEl) ? pEl.GetString() : null;
                var child = assocEl.TryGetProperty("child", out var cEl) ? cEl.GetString() : null;
                if (string.IsNullOrEmpty(assocName) || string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child)) continue;

                var (parentEntity, _) = FindEntityAcrossModules(parent, null);
                var (childEntity, _) = FindEntityAcrossModules(child, null);
                if (parentEntity == null || childEntity == null) continue;

                var mxAssoc = childEntity.AddAssociation(parentEntity);
                mxAssoc.Name = assocName;
                var typeStr = assocEl.TryGetProperty("type", out var tEl) ? tEl.GetString() : "one-to-many";
                mxAssoc.Type = typeStr?.ToLowerInvariant() == "many-to-many" || typeStr?.ToLowerInvariant() == "referenceset"
                    ? MxAssociationType.ReferenceSet : MxAssociationType.Reference;
            }
        }

        tx.Commit();
        return results;
    }

    public void RenameEntity(EntityRef entity, string newName)
    {
        var (mxEntity, _) = FindEntityByRef(entity);
        using var tx = _model.StartTransaction($"Rename entity '{mxEntity.Name}' to '{newName}'");
        mxEntity.Name = newName;
        tx.Commit();
    }

    public void DeleteEntity(EntityRef entity)
    {
        var (mxEntity, module) = FindEntityByRef(entity);
        using var tx = _model.StartTransaction("Delete Entity");

        // Delete all associations first
        var associations = mxEntity.GetAssociations(AssociationDirection.Both, null).ToList();
        foreach (var ea in associations)
            mxEntity.DeleteAssociation(ea.Association);

        module.DomainModel.RemoveEntity(mxEntity);
        tx.Commit();
    }

    // ── Attribute CRUD ───────────────────────────────────────────────────────

    public AttributeRef AddAttribute(EntityRef entity, AttributeSpec spec)
    {
        var (mxEntity, module) = FindEntityByRef(entity);

        using var tx = _model.StartTransaction("add attribute");
        var mxAttr = _model.Create<IAttribute>();
        mxAttr.Name = spec.Name;
        mxAttr.Type = CreateAttributeTypeFromSpec(spec, module);

        if (!string.IsNullOrEmpty(spec.DefaultValue))
        {
            var stored = _model.Create<IStoredValue>();
            stored.DefaultValue = spec.DefaultValue;
            mxAttr.Value = stored;
        }

        if (!string.IsNullOrEmpty(spec.Documentation))
            mxAttr.Documentation = spec.Documentation;

        mxEntity.AddAttribute(mxAttr);
        tx.Commit();

        return new AttributeRef(GuidFromString($"{mxEntity.Name}.{mxAttr.Name}"), mxAttr.Name, spec.Kind);
    }

    public void RenameAttribute(EntityRef entity, AttributeRef attribute, string newName)
    {
        var (mxEntity, _) = FindEntityByRef(entity);
        var mxAttr = mxEntity.GetAttributes()
            .FirstOrDefault(a => string.Equals(a.Name, attribute.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Attribute '{attribute.Name}' not found on entity '{mxEntity.Name}'.");

        using var tx = _model.StartTransaction($"Rename attribute '{attribute.Name}' to '{newName}'");
        mxAttr.Name = newName;
        tx.Commit();
    }

    public void UpdateAttribute(EntityRef entity, AttributeRef attribute, AttributeSpec newSpec)
    {
        var (mxEntity, module) = FindEntityByRef(entity);
        var mxAttr = mxEntity.GetAttributes()
            .FirstOrDefault(a => string.Equals(a.Name, attribute.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Attribute '{attribute.Name}' not found on entity '{mxEntity.Name}'.");

        using var tx = _model.StartTransaction($"Update attribute '{attribute.Name}' on '{mxEntity.Name}'");

        // Update type
        mxAttr.Type = CreateAttributeTypeFromSpec(newSpec, module);

        // Update max length if string
        if (newSpec.MaxLength.HasValue && mxAttr.Type is IStringAttributeType strType)
            strType.Length = newSpec.MaxLength.Value;

        // Update localize date if datetime
        if (newSpec.LocalizeDate.HasValue && mxAttr.Type is IDateTimeAttributeType dtType)
            dtType.LocalizeDate = newSpec.LocalizeDate.Value;

        // Update default value
        if (newSpec.DefaultValue != null)
        {
            if (mxAttr.Value is IStoredValue stored)
                stored.DefaultValue = newSpec.DefaultValue;
            else
            {
                var newStored = _model.Create<IStoredValue>();
                newStored.DefaultValue = newSpec.DefaultValue;
                mxAttr.Value = newStored;
            }
        }

        if (newSpec.Documentation != null)
            mxAttr.Documentation = newSpec.Documentation;

        tx.Commit();
    }

    public void DeleteAttribute(EntityRef entity, AttributeRef attribute)
    {
        var (mxEntity, _) = FindEntityByRef(entity);
        var mxAttr = mxEntity.GetAttributes()
            .FirstOrDefault(a => string.Equals(a.Name, attribute.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Attribute '{attribute.Name}' not found on entity '{mxEntity.Name}'.");

        using var tx = _model.StartTransaction("Delete Attribute");
        mxEntity.RemoveAttribute(mxAttr);
        tx.Commit();
    }

    public void SetCalculatedAttribute(EntityRef entity, AttributeRef attribute, string microflowQualifiedName)
    {
        var (mxEntity, _) = FindEntityByRef(entity);
        var mxAttr = mxEntity.GetAttributes()
            .FirstOrDefault(a => string.Equals(a.Name, attribute.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Attribute '{attribute.Name}' not found on entity '{mxEntity.Name}'.");

        var microflow = FindMicroflowByQualifiedName(microflowQualifiedName)
            ?? throw new InvalidOperationException($"Microflow '{microflowQualifiedName}' not found.");

        using var tx = _model.StartTransaction("set calculated attribute");
        var calcValue = _model.Create<ICalculatedValue>();
        calcValue.Microflow = microflow.QualifiedName;
        calcValue.PassEntity = true;
        mxAttr.Value = calcValue;
        tx.Commit();
    }

    public void ConfigureSystemAttributes(
        EntityRef entity,
        bool? hasCreatedDate = null,
        bool? hasChangedDate = null,
        bool? hasOwner = null,
        bool? hasChangedBy = null,
        bool? persistable = null)
    {
        var (mxEntity, _) = FindEntityByRef(entity);

        if (mxEntity.Generalization is not INoGeneralization noGen)
            throw new InvalidOperationException($"Entity '{mxEntity.Name}' has a generalization. System attributes can only be configured on root entities.");

        using var tx = _model.StartTransaction("Configure system attributes");

        if (hasCreatedDate.HasValue) noGen.HasCreatedDate = hasCreatedDate.Value;
        if (hasChangedDate.HasValue) noGen.HasChangedDate = hasChangedDate.Value;
        if (hasOwner.HasValue) noGen.HasOwner = hasOwner.Value;
        if (hasChangedBy.HasValue) noGen.HasChangedBy = hasChangedBy.Value;
        if (persistable.HasValue) noGen.Persistable = persistable.Value;

        tx.Commit();
    }

    // ── Association CRUD ─────────────────────────────────────────────────────

    public AssociationRef CreateAssociation(CreateAssociationRequest request)
    {
        var (parentEntity, _) = FindEntityAcrossModules(request.ParentEntityQualifiedName, request.ModuleName)
            is { } pe ? pe : throw new InvalidOperationException($"Parent entity '{request.ParentEntityQualifiedName}' not found.");
        var (childEntity, _) = FindEntityAcrossModules(request.ChildEntityQualifiedName, request.ModuleName)
            is { } ce ? ce : throw new InvalidOperationException($"Child entity '{request.ChildEntityQualifiedName}' not found.");

        if (parentEntity == null)
            throw new InvalidOperationException($"Parent entity '{request.ParentEntityQualifiedName}' not found.");
        if (childEntity == null)
            throw new InvalidOperationException($"Child entity '{request.ChildEntityQualifiedName}' not found.");

        using var tx = _model.StartTransaction("create association");
        var mxAssoc = childEntity.AddAssociation(parentEntity);
        mxAssoc.Name = request.Name;
        mxAssoc.Type = MapAssociationType(request.Type);
        mxAssoc.ParentDeleteBehavior = MapDeleteBehavior(request.ParentDeleteBehavior);
        mxAssoc.ChildDeleteBehavior = MapDeleteBehavior(request.ChildDeleteBehavior);
        mxAssoc.Owner = request.Owner == Terminal.Interop.AssociationOwner.Both ? MxAssociationOwner.Both : MxAssociationOwner.Default;

        if (!string.IsNullOrEmpty(request.Documentation))
            mxAssoc.Documentation = request.Documentation;

        tx.Commit();

        return new AssociationRef(
            GuidFromString(mxAssoc.Name),
            mxAssoc.Name,
            parentEntity.Name,
            childEntity.Name,
            request.Type);
    }

    public IReadOnlyList<AssociationRef> CreateMultipleAssociations(IReadOnlyList<CreateAssociationRequest> requests)
    {
        var results = new List<AssociationRef>();
        using var tx = _model.StartTransaction("create multiple associations");

        foreach (var request in requests)
        {
            var (parentEntity, _) = FindEntityAcrossModules(request.ParentEntityQualifiedName, request.ModuleName);
            var (childEntity, _) = FindEntityAcrossModules(request.ChildEntityQualifiedName, request.ModuleName);

            if (parentEntity == null || childEntity == null) continue;

            var mxAssoc = childEntity.AddAssociation(parentEntity);
            mxAssoc.Name = request.Name;
            mxAssoc.Type = MapAssociationType(request.Type);
            mxAssoc.ParentDeleteBehavior = MapDeleteBehavior(request.ParentDeleteBehavior);
            mxAssoc.ChildDeleteBehavior = MapDeleteBehavior(request.ChildDeleteBehavior);
            mxAssoc.Owner = request.Owner == Terminal.Interop.AssociationOwner.Both ? MxAssociationOwner.Both : MxAssociationOwner.Default;

            if (!string.IsNullOrEmpty(request.Documentation))
                mxAssoc.Documentation = request.Documentation;

            results.Add(new AssociationRef(
                GuidFromString(mxAssoc.Name),
                mxAssoc.Name,
                parentEntity.Name,
                childEntity.Name,
                request.Type));
        }

        tx.Commit();
        return results;
    }

    public void RenameAssociation(AssociationRef association, string newName)
    {
        var mxAssoc = FindAssociationByRef(association)
            ?? throw new InvalidOperationException($"Association '{association.Name}' not found.");

        using var tx = _model.StartTransaction($"Rename association '{association.Name}' to '{newName}'");
        mxAssoc.Name = newName;
        tx.Commit();
    }

    public void UpdateAssociation(
        AssociationRef association,
        Terminal.Interop.AssociationType? newType = null,
        DeleteBehavior? newParentDeleteBehavior = null,
        DeleteBehavior? newChildDeleteBehavior = null,
        Terminal.Interop.AssociationOwner? newOwner = null,
        string? newDocumentation = null)
    {
        var mxAssoc = FindAssociationByRef(association)
            ?? throw new InvalidOperationException($"Association '{association.Name}' not found.");

        using var tx = _model.StartTransaction($"Update association '{association.Name}'");

        if (newType.HasValue)
            mxAssoc.Type = MapAssociationType(newType.Value);
        if (newParentDeleteBehavior.HasValue)
            mxAssoc.ParentDeleteBehavior = MapDeleteBehavior(newParentDeleteBehavior.Value);
        if (newChildDeleteBehavior.HasValue)
            mxAssoc.ChildDeleteBehavior = MapDeleteBehavior(newChildDeleteBehavior.Value);
        if (newOwner.HasValue)
            mxAssoc.Owner = newOwner.Value == Terminal.Interop.AssociationOwner.Both ? MxAssociationOwner.Both : MxAssociationOwner.Default;
        if (newDocumentation != null)
            mxAssoc.Documentation = newDocumentation;

        tx.Commit();
    }

    public void DeleteAssociation(AssociationRef association)
    {
        // Find the entity that owns this association (child entity)
        foreach (var module in _model.Root.GetModules())
        {
            foreach (var entity in module.DomainModel.GetEntities())
            {
                var ea = entity.GetAssociations(AssociationDirection.Both, null)
                    .FirstOrDefault(a => string.Equals(a.Association.Name, association.Name, StringComparison.OrdinalIgnoreCase));
                if (ea != null)
                {
                    using var tx = _model.StartTransaction("Delete Association");
                    entity.DeleteAssociation(ea.Association);
                    tx.Commit();
                    return;
                }
            }
        }
        throw new InvalidOperationException($"Association '{association.Name}' not found.");
    }

    public IReadOnlyList<AssociationQueryItem> QueryAssociations(
        string? entityQualifiedName = null,
        string? secondEntityQualifiedName = null,
        string? moduleName = null,
        string direction = "both")
    {
        var seenAssociations = new HashSet<string>();
        var allAssociations = new List<(IAssociation assoc, string parentName, string childName)>();

        foreach (var mod in _model.Root.GetModules())
        {
            foreach (var entity in mod.DomainModel.GetEntities())
            {
                foreach (var ea in entity.GetAssociations(AssociationDirection.Both))
                {
                    var name = ea.Association.Name;
                    if (seenAssociations.Contains(name)) continue;
                    seenAssociations.Add(name);
                    allAssociations.Add((ea.Association, ea.Parent?.Name ?? "", ea.Child?.Name ?? ""));
                }
            }
        }

        IEnumerable<(IAssociation assoc, string parentName, string childName)> filtered = allAssociations;

        if (!string.IsNullOrEmpty(entityQualifiedName) && !string.IsNullOrEmpty(secondEntityQualifiedName))
        {
            var e1Name = SimpleName(entityQualifiedName);
            var e2Name = SimpleName(secondEntityQualifiedName);
            filtered = filtered.Where(a =>
                (string.Equals(a.parentName, e1Name, StringComparison.OrdinalIgnoreCase) && string.Equals(a.childName, e2Name, StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(a.parentName, e2Name, StringComparison.OrdinalIgnoreCase) && string.Equals(a.childName, e1Name, StringComparison.OrdinalIgnoreCase)));
        }
        else if (!string.IsNullOrEmpty(entityQualifiedName))
        {
            var eName = SimpleName(entityQualifiedName);
            filtered = direction.ToLowerInvariant() switch
            {
                "parent" => filtered.Where(a => string.Equals(a.parentName, eName, StringComparison.OrdinalIgnoreCase)),
                "child" => filtered.Where(a => string.Equals(a.childName, eName, StringComparison.OrdinalIgnoreCase)),
                _ => filtered.Where(a => string.Equals(a.parentName, eName, StringComparison.OrdinalIgnoreCase) || string.Equals(a.childName, eName, StringComparison.OrdinalIgnoreCase))
            };
        }
        else if (!string.IsNullOrEmpty(moduleName))
        {
            // Filter by associations that involve entities in the given module
            var mod = _model.Root.GetModules()
                .FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
            if (mod != null)
            {
                var entityNames = new HashSet<string>(mod.DomainModel.GetEntities().Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
                filtered = filtered.Where(a => entityNames.Contains(a.parentName) || entityNames.Contains(a.childName));
            }
        }

        return filtered.Select(a => new AssociationQueryItem(
            Name: a.assoc.Name,
            ParentEntityQualifiedName: a.parentName,
            ChildEntityQualifiedName: a.childName,
            Type: a.assoc.Type == MxAssociationType.ReferenceSet
                ? Terminal.Interop.AssociationType.ReferenceSet
                : Terminal.Interop.AssociationType.Reference))
            .ToList();
    }

    // ── Generalization ────────────────────────────────────────────────────────

    public void SetGeneralization(EntityRef entity, EntityRef parent)
    {
        var (mxEntity, _) = FindEntityByRef(entity);
        var (parentEntity, _) = FindEntityByRef(parent);

        if (ReferenceEquals(mxEntity, parentEntity))
            throw new InvalidOperationException($"Entity '{mxEntity.Name}' cannot inherit from itself.");

        using var tx = _model.StartTransaction("set entity generalization");
        var generalization = _model.Create<IGeneralization>();
        generalization.Generalization = parentEntity.QualifiedName;
        mxEntity.Generalization = generalization;
        tx.Commit();
    }

    public void RemoveGeneralization(EntityRef entity)
    {
        var (mxEntity, _) = FindEntityByRef(entity);

        if (mxEntity.Generalization is not IGeneralization)
            throw new InvalidOperationException($"Entity '{mxEntity.Name}' does not have a generalization to remove.");

        using var tx = _model.StartTransaction("remove entity generalization");
        var noGen = _model.Create<INoGeneralization>();
        noGen.Persistable = true;
        mxEntity.Generalization = noGen;
        tx.Commit();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    public void AddEventHandler(EntityRef entity, EventHandlerSpec handler)
    {
        var (mxEntity, _) = FindEntityByRef(entity);

        var microflow = FindMicroflowByQualifiedName(handler.MicroflowQualifiedName)
            ?? throw new InvalidOperationException($"Microflow '{handler.MicroflowQualifiedName}' not found.");

        var (moment, eventType) = MapEventHandlerKind(handler.Kind);

        using var tx = _model.StartTransaction("add event handler");
        var mxHandler = _model.Create<IEventHandler>();
        mxHandler.Moment = moment;
        mxHandler.Event = eventType;
        mxHandler.Microflow = microflow.QualifiedName;
        mxHandler.RaiseErrorOnFalse = handler.RaiseErrorOnFalse;
        mxHandler.PassEventObject = handler.PassEventObject;
        mxEntity.AddEventHandler(mxHandler);
        tx.Commit();
    }

    // ── Enumeration documents ─────────────────────────────────────────────────

    public IReadOnlyList<EnumerationRef> ListEnumerations(ModuleId moduleId)
    {
        var module = ResolveModule(moduleId);
        return _model.Root.GetModuleDocuments<IEnumeration>(module)
            .Select(e => new EnumerationRef(ParseId(e.Id), e.QualifiedName?.ToString() ?? $"{module.Name}.{e.Name}"))
            .ToList();
    }

    public EnumerationRef CreateEnumeration(string moduleName, string name, IReadOnlyList<EnumerationValueSpec> values)
    {
        var module = ResolveModuleByName(moduleName);

        var existing = _model.Root.GetModuleDocuments<IEnumeration>(module)
            .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            throw new InvalidOperationException($"Enumeration '{name}' already exists in module '{module.Name}'.");

        using var tx = _model.StartTransaction($"Create enumeration {name}");
        var enumDoc = _model.Create<IEnumeration>();
        enumDoc.Name = name;

        foreach (var valueSpec in values)
        {
            var enumValue = _model.Create<IEnumerationValue>();
            enumValue.Name = valueSpec.Name;
            var captionText = _model.Create<IText>();
            captionText.AddOrUpdateTranslation("en_US", valueSpec.Caption ?? valueSpec.Name);
            enumValue.Caption = captionText;
            enumDoc.AddValue(enumValue);
        }

        module.AddDocument(enumDoc);
        tx.Commit();

        return new EnumerationRef(ParseId(enumDoc.Id), enumDoc.QualifiedName?.ToString() ?? $"{module.Name}.{name}");
    }

    public void RenameEnumerationValue(string enumerationQualifiedName, string oldValueName, string newValueName)
    {
        var enumDoc = FindEnumerationByQualifiedName(enumerationQualifiedName)
            ?? throw new InvalidOperationException($"Enumeration '{enumerationQualifiedName}' not found.");

        var value = enumDoc.GetValues()
            .FirstOrDefault(v => string.Equals(v.Name, oldValueName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Value '{oldValueName}' not found in enumeration '{enumDoc.Name}'.");

        using var tx = _model.StartTransaction($"Rename enumeration value '{oldValueName}' to '{newValueName}' in '{enumDoc.Name}'");
        value.Name = newValueName;
        tx.Commit();
    }

    public void UpdateEnumeration(
        EnumerationRef enumeration,
        IReadOnlyList<EnumerationValueSpec>? addValues = null,
        IReadOnlyList<string>? removeValues = null,
        IReadOnlyDictionary<string, string>? renameValues = null)
    {
        var enumDoc = FindEnumerationByRef(enumeration)
            ?? throw new InvalidOperationException($"Enumeration '{enumeration.QualifiedName}' not found.");

        using var tx = _model.StartTransaction($"Update enumeration '{enumDoc.Name}'");

        if (addValues != null)
        {
            foreach (var spec in addValues)
            {
                if (enumDoc.GetValues().Any(v => string.Equals(v.Name, spec.Name, StringComparison.OrdinalIgnoreCase)))
                    continue; // skip duplicates

                var enumValue = _model.Create<IEnumerationValue>();
                enumValue.Name = spec.Name;
                var captionText = _model.Create<IText>();
                captionText.AddOrUpdateTranslation("en_US", spec.Caption ?? spec.Name);
                enumValue.Caption = captionText;
                enumDoc.AddValue(enumValue);
            }
        }

        if (removeValues != null)
        {
            foreach (var valueName in removeValues)
            {
                var existing = enumDoc.GetValues()
                    .FirstOrDefault(v => string.Equals(v.Name, valueName, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                    enumDoc.RemoveValue(existing);
            }
        }

        if (renameValues != null)
        {
            foreach (var kv in renameValues)
            {
                var val = enumDoc.GetValues()
                    .FirstOrDefault(v => string.Equals(v.Name, kv.Key, StringComparison.OrdinalIgnoreCase));
                if (val != null)
                    val.Name = kv.Value;
            }
        }

        tx.Commit();
    }

    // ── Document-level operations ─────────────────────────────────────────────

    public void RenameDocument(DocumentId document, string newName)
    {
        foreach (var module in _model.Root.GetModules())
        {
            var doc = FindDocumentInFolder(module, document.Value);
            if (doc != null)
            {
                using var tx = _model.StartTransaction($"Rename document '{doc.Name}' to '{newName}'");
                doc.Name = newName;
                tx.Commit();
                return;
            }
        }
        throw new InvalidOperationException($"Document '{document.QualifiedName}' (id={document.Value}) not found.");
    }

    // ── Documentation ─────────────────────────────────────────────────────────

    public void SetEntityDocumentation(EntityRef entity, string documentation)
    {
        var (mxEntity, _) = FindEntityByRef(entity);
        using var tx = _model.StartTransaction($"Set documentation on entity '{mxEntity.Name}'");
        mxEntity.Documentation = documentation;
        tx.Commit();
    }

    public void SetAttributeDocumentation(EntityRef entity, AttributeRef attribute, string documentation)
    {
        var (mxEntity, _) = FindEntityByRef(entity);
        var mxAttr = mxEntity.GetAttributes()
            .FirstOrDefault(a => string.Equals(a.Name, attribute.Name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Attribute '{attribute.Name}' not found on entity '{mxEntity.Name}'.");

        using var tx = _model.StartTransaction($"Set documentation on attribute '{mxAttr.Name}'");
        mxAttr.Documentation = documentation;
        tx.Commit();
    }

    public void SetAssociationDocumentation(AssociationRef association, string documentation)
    {
        var mxAssoc = FindAssociationByRef(association)
            ?? throw new InvalidOperationException($"Association '{association.Name}' not found.");

        using var tx = _model.StartTransaction($"Set documentation on association '{association.Name}'");
        mxAssoc.Documentation = documentation;
        tx.Commit();
    }

    public void SetDomainModelDocumentation(ModuleId moduleId, string documentation)
    {
        var module = ResolveModule(moduleId);
        using var tx = _model.StartTransaction($"Set documentation on domain model of module '{module.Name}'");
        module.DomainModel.Documentation = documentation;
        tx.Commit();
    }

    // ── Layout / arrange ──────────────────────────────────────────────────────

    public void ArrangeDomainModel(ArrangeDomainModelRequest request)
    {
        var module = ResolveModuleByName(request.ModuleName);
        using var tx = _model.StartTransaction("arrange domain model");
        ArrangeDomainModelInternal(module, request.RootEntityName);
        tx.Commit();
    }

    // ── Name validation ───────────────────────────────────────────────────────

    public NameValidationResult? ValidateName(string name, bool autoFix = false)
    {
        if (_nameValidation is null)
            return null;

        var result = _nameValidation.IsNameValid(name);
        string? suggestion = null;
        if (!result.IsValid && autoFix)
            suggestion = _nameValidation.GetValidName(name);

        return new NameValidationResult(result.IsValid, result.IsValid ? null : result.ErrorMessage, suggestion);
    }

    // ── Copy / clone ──────────────────────────────────────────────────────────

    public CopyResult CopyElement(CopyRequest request)
    {
        try
        {
            var sourceModule = !string.IsNullOrEmpty(request.SourceModuleName)
                ? ResolveModuleByName(request.SourceModuleName)
                : _model.Root.GetModules().FirstOrDefault(m => !m.FromAppStore)
                    ?? throw new InvalidOperationException("No user module found.");

            var targetModule = !string.IsNullOrEmpty(request.TargetModuleName)
                ? ResolveModuleByName(request.TargetModuleName)
                : sourceModule;

            using var tx = _model.StartTransaction($"Copy {request.ElementType} '{request.SourceName}' as '{request.NewName}'");

            switch (request.ElementType.ToLowerInvariant())
            {
                case "entity":
                {
                    var source = sourceModule.DomainModel.GetEntities()
                        .FirstOrDefault(e => string.Equals(e.Name, request.SourceName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"Entity '{request.SourceName}' not found in module '{sourceModule.Name}'.");
                    var copy = _model.Copy(source);
                    copy.Name = request.NewName;
                    targetModule.DomainModel.AddEntity(copy);
                    tx.Commit();
                    return new CopyResult(true, $"{targetModule.Name}.{request.NewName}", null);
                }
                case "microflow":
                {
                    var source = sourceModule.GetDocuments().OfType<IMicroflow>()
                        .FirstOrDefault(m => string.Equals(m.Name, request.SourceName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"Microflow '{request.SourceName}' not found in module '{sourceModule.Name}'.");
                    var copy = _model.Copy(source);
                    copy.Name = request.NewName;
                    targetModule.AddDocument(copy);
                    tx.Commit();
                    return new CopyResult(true, $"{targetModule.Name}.{request.NewName}", null);
                }
                case "constant":
                {
                    var source = _model.Root.GetModuleDocuments<Mendix.StudioPro.ExtensionsAPI.Model.Constants.IConstant>(sourceModule)
                        .FirstOrDefault(c => string.Equals(c.Name, request.SourceName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"Constant '{request.SourceName}' not found in module '{sourceModule.Name}'.");
                    var copy = _model.Copy(source);
                    copy.Name = request.NewName;
                    targetModule.AddDocument(copy);
                    tx.Commit();
                    return new CopyResult(true, $"{targetModule.Name}.{request.NewName}", null);
                }
                case "enumeration":
                {
                    var source = _model.Root.GetModuleDocuments<IEnumeration>(sourceModule)
                        .FirstOrDefault(e => string.Equals(e.Name, request.SourceName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"Enumeration '{request.SourceName}' not found in module '{sourceModule.Name}'.");
                    var copy = _model.Copy(source);
                    copy.Name = request.NewName;
                    targetModule.AddDocument(copy);
                    tx.Commit();
                    return new CopyResult(true, $"{targetModule.Name}.{request.NewName}", null);
                }
                default:
                    return new CopyResult(false, null, $"Unknown element type '{request.ElementType}'. Supported: entity, microflow, constant, enumeration.");
            }
        }
        catch (Exception ex)
        {
            return new CopyResult(false, null, ex.Message);
        }
    }

    // ── Model integrity checks ────────────────────────────────────────────────

    public IReadOnlyList<ModelCheckItem> CheckModel(string? moduleName = null)
    {
        var results = new List<ModelCheckItem>();

        var modules = string.IsNullOrWhiteSpace(moduleName)
            ? _model.Root.GetModules().Where(m => !m.FromAppStore).ToList()
            : _model.Root.GetModules()
                .Where(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (!modules.Any())
            throw new InvalidOperationException(moduleName != null ? $"Module '{moduleName}' not found." : "No user modules found.");

        foreach (var module in modules)
        {
            var entities = module.DomainModel?.GetEntities()?.ToList() ?? new List<IEntity>();

            foreach (var entity in entities)
            {
                // Check: entity has no attributes
                var attrs = entity.GetAttributes();
                if (attrs == null || attrs.Count == 0)
                    results.Add(new ModelCheckItem(ModelCheckSeverity.Warning, module.Name, entity.Name, "no_attributes", $"Entity '{entity.Name}' has no attributes defined."));

                // Check: generalization resolves
                if (entity.Generalization is IGeneralization gen)
                {
                    try
                    {
                        var parentEntity = gen.Generalization?.Resolve();
                        if (parentEntity == null)
                            results.Add(new ModelCheckItem(ModelCheckSeverity.Error, module.Name, entity.Name, "broken_generalization", $"Entity '{entity.Name}' has a generalization to '{gen.Generalization}' which cannot be resolved."));
                    }
                    catch
                    {
                        results.Add(new ModelCheckItem(ModelCheckSeverity.Error, module.Name, entity.Name, "broken_generalization", $"Entity '{entity.Name}' has a generalization that cannot be resolved."));
                    }
                }

                // Check: event handlers point to valid microflows
                var handlers = entity.GetEventHandlers();
                if (handlers != null)
                {
                    foreach (var handler in handlers)
                    {
                        if (handler.Microflow == null)
                        {
                            results.Add(new ModelCheckItem(ModelCheckSeverity.Error, module.Name, entity.Name, "missing_event_microflow", $"Event handler on '{entity.Name}' ({handler.Moment} {handler.Event}) has no microflow assigned."));
                        }
                        else
                        {
                            try
                            {
                                var mf = handler.Microflow.Resolve();
                                if (mf == null)
                                    results.Add(new ModelCheckItem(ModelCheckSeverity.Error, module.Name, entity.Name, "broken_event_microflow", $"Event handler on '{entity.Name}' references microflow '{handler.Microflow}' which cannot be resolved."));
                            }
                            catch
                            {
                                results.Add(new ModelCheckItem(ModelCheckSeverity.Error, module.Name, entity.Name, "broken_event_microflow", $"Event handler on '{entity.Name}' references a microflow that cannot be resolved."));
                            }
                        }
                    }
                }

                // Check: calculated attributes point to valid microflows
                if (attrs != null)
                {
                    foreach (var attr in attrs)
                    {
                        if (attr.Value is ICalculatedValue calcVal)
                        {
                            if (calcVal.Microflow == null)
                            {
                                results.Add(new ModelCheckItem(ModelCheckSeverity.Error, module.Name, entity.Name, "missing_calc_microflow", $"Calculated attribute '{attr.Name}' on '{entity.Name}' has no microflow assigned."));
                            }
                            else
                            {
                                try
                                {
                                    var mf = calcVal.Microflow.Resolve();
                                    if (mf == null)
                                        results.Add(new ModelCheckItem(ModelCheckSeverity.Error, module.Name, entity.Name, "broken_calc_microflow", $"Calculated attribute '{attr.Name}' on '{entity.Name}' references microflow '{calcVal.Microflow}' which cannot be resolved."));
                                }
                                catch
                                {
                                    results.Add(new ModelCheckItem(ModelCheckSeverity.Error, module.Name, entity.Name, "broken_calc_microflow", $"Calculated attribute '{attr.Name}' on '{entity.Name}' references a microflow that cannot be resolved."));
                                }
                            }
                        }
                    }
                }
            }
        }

        return results;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static Guid ParseId(string id)
        => Guid.TryParse(id, out var guid) ? guid : GuidFromString(id);

    private static Guid GuidFromString(string s)
    {
        var bytes = new byte[16];
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(s));
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    private IModule ResolveModule(ModuleId moduleId)
    {
        var module = _model.Root.GetModules()
            .FirstOrDefault(m => Guid.TryParse(m.Id, out var g) && g == moduleId.Value)
            ?? _model.Root.GetModules()
            .FirstOrDefault(m => string.Equals(m.Name, moduleId.Name, StringComparison.OrdinalIgnoreCase));
        return module ?? throw new InvalidOperationException($"Module '{moduleId.Name}' (id={moduleId.Value}) not found.");
    }

    private IModule ResolveModuleByName(string moduleName)
    {
        var module = _model.Root.GetModules()
            .FirstOrDefault(m => string.Equals(m.Name, moduleName, StringComparison.OrdinalIgnoreCase));
        return module ?? throw new InvalidOperationException($"Module '{moduleName}' not found.");
    }

    private (IEntity Entity, IModule Module) FindEntityByRef(EntityRef entity)
    {
        // IEntity has no .Id property in ExtensionsAPI; match by qualified name then simple name.
        var simpleName = SimpleName(entity.QualifiedName);
        string? moduleNameHint = entity.QualifiedName.Contains('.')
            ? entity.QualifiedName[..entity.QualifiedName.IndexOf('.')]
            : null;

        foreach (var module in _model.Root.GetModules())
        {
            if (moduleNameHint != null && !string.Equals(module.Name, moduleNameHint, StringComparison.OrdinalIgnoreCase))
                continue;

            var mxEntity = module.DomainModel.GetEntities()
                .FirstOrDefault(e => string.Equals(e.Name, simpleName, StringComparison.OrdinalIgnoreCase));

            if (mxEntity != null)
                return (mxEntity, module);
        }

        // Fallback: search all modules ignoring module hint (handles cross-module refs)
        if (moduleNameHint != null)
        {
            foreach (var module in _model.Root.GetModules())
            {
                var mxEntity = module.DomainModel.GetEntities()
                    .FirstOrDefault(e => string.Equals(e.Name, simpleName, StringComparison.OrdinalIgnoreCase));
                if (mxEntity != null)
                    return (mxEntity, module);
            }
        }

        throw new InvalidOperationException($"Entity '{entity.QualifiedName}' not found.");
    }

    private (IEntity? Entity, IModule? Module) FindEntityAcrossModules(string qualifiedOrSimpleName, string? moduleHint)
    {
        // Parse qualified name
        string? moduleNamePart = null;
        string entityNamePart = qualifiedOrSimpleName;
        if (qualifiedOrSimpleName.Contains('.'))
        {
            var dot = qualifiedOrSimpleName.IndexOf('.');
            moduleNamePart = qualifiedOrSimpleName[..dot];
            entityNamePart = qualifiedOrSimpleName[(dot + 1)..];
        }

        var effectiveModuleName = moduleHint ?? moduleNamePart;

        var modules = effectiveModuleName != null
            ? _model.Root.GetModules().Where(m => string.Equals(m.Name, effectiveModuleName, StringComparison.OrdinalIgnoreCase))
            : _model.Root.GetModules();

        foreach (var module in modules)
        {
            var entity = module.DomainModel.GetEntities()
                .FirstOrDefault(e => string.Equals(e.Name, entityNamePart, StringComparison.OrdinalIgnoreCase));
            if (entity != null)
                return (entity, module);
        }

        return (null, null);
    }

    private IAssociation? FindAssociationByRef(AssociationRef association)
    {
        foreach (var module in _model.Root.GetModules())
        {
            foreach (var entity in module.DomainModel.GetEntities())
            {
                var ea = entity.GetAssociations(AssociationDirection.Both, null)
                    .FirstOrDefault(a => string.Equals(a.Association.Name, association.Name, StringComparison.OrdinalIgnoreCase));
                if (ea != null)
                    return ea.Association;
            }
        }
        return null;
    }

    private IEnumeration? FindEnumerationByRef(EnumerationRef enumRef)
    {
        foreach (var module in _model.Root.GetModules())
        {
            var enumDoc = _model.Root.GetModuleDocuments<IEnumeration>(module)
                .FirstOrDefault(e => Guid.TryParse(e.Id, out var g) && g == enumRef.Id
                    || string.Equals(e.QualifiedName?.ToString(), enumRef.QualifiedName, StringComparison.OrdinalIgnoreCase));
            if (enumDoc != null) return enumDoc;
        }
        return null;
    }

    private IEnumeration? FindEnumerationByQualifiedName(string qualifiedName)
    {
        string? moduleNamePart = null;
        string enumName = qualifiedName;
        if (qualifiedName.Contains('.'))
        {
            var dot = qualifiedName.IndexOf('.');
            moduleNamePart = qualifiedName[..dot];
            enumName = qualifiedName[(dot + 1)..];
        }

        var modules = moduleNamePart != null
            ? _model.Root.GetModules().Where(m => string.Equals(m.Name, moduleNamePart, StringComparison.OrdinalIgnoreCase))
            : _model.Root.GetModules();

        foreach (var module in modules)
        {
            var enumDoc = _model.Root.GetModuleDocuments<IEnumeration>(module)
                .FirstOrDefault(e => string.Equals(e.Name, enumName, StringComparison.OrdinalIgnoreCase));
            if (enumDoc != null) return enumDoc;
        }
        return null;
    }

    private IEnumeration? FindEnumerationByQualifiedNameOrSimple(string name)
    {
        foreach (var module in _model.Root.GetModules())
        {
            var enumDoc = _model.Root.GetModuleDocuments<IEnumeration>(module)
                .FirstOrDefault(e =>
                    string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(e.QualifiedName?.ToString(), name, StringComparison.OrdinalIgnoreCase));
            if (enumDoc != null) return enumDoc;
        }
        return null;
    }

    private IMicroflow? FindMicroflowByQualifiedName(string qualifiedName)
    {
        string? moduleHint = null;
        string mfName = qualifiedName;
        if (qualifiedName.Contains('.'))
        {
            var dot = qualifiedName.IndexOf('.');
            moduleHint = qualifiedName[..dot];
            mfName = qualifiedName[(dot + 1)..];
        }

        var modules = moduleHint != null
            ? _model.Root.GetModules().Where(m => string.Equals(m.Name, moduleHint, StringComparison.OrdinalIgnoreCase))
            : _model.Root.GetModules().Where(m => !m.FromAppStore);

        foreach (var mod in modules)
        {
            var mf = mod.GetDocuments().OfType<IMicroflow>()
                .FirstOrDefault(m => string.Equals(m.Name, mfName, StringComparison.OrdinalIgnoreCase));
            if (mf != null) return mf;
        }

        // Fall back across all modules if module hint didn't find anything
        if (moduleHint != null)
        {
            foreach (var mod in _model.Root.GetModules().Where(m => !m.FromAppStore))
            {
                var mf = mod.GetDocuments().OfType<IMicroflow>()
                    .FirstOrDefault(m => string.Equals(m.Name, mfName, StringComparison.OrdinalIgnoreCase));
                if (mf != null) return mf;
            }
        }

        return null;
    }

    private static IDocument? FindDocumentInFolder(IFolderBase folder, Guid id)
    {
        var doc = folder.GetDocuments().FirstOrDefault(d => ParseId(d.Id) == id);
        if (doc != null) return doc;
        foreach (var subfolder in folder.GetFolders())
        {
            var found = FindDocumentInFolder(subfolder, id);
            if (found != null) return found;
        }
        return null;
    }

    private IAttributeType CreateAttributeTypeFromSpec(AttributeSpec spec, IModule module)
    {
        switch (spec.Kind)
        {
            case AttributeKind.Enumeration:
                if (!string.IsNullOrEmpty(spec.EnumerationQualifiedName))
                {
                    var enumDoc = FindEnumerationByQualifiedNameOrSimple(spec.EnumerationQualifiedName)
                        ?? throw new InvalidOperationException($"Enumeration '{spec.EnumerationQualifiedName}' not found.");
                    var enumAttrType = _model.Create<IEnumerationAttributeType>();
                    enumAttrType.Enumeration = enumDoc.QualifiedName;
                    return enumAttrType;
                }
                else if (spec.EnumerationValues != null && spec.EnumerationValues.Count > 0)
                {
                    return CreateEnumerationAttributeType(spec.Name, spec.EnumerationValues.ToList(), module);
                }
                throw new InvalidOperationException("Enumeration attribute requires EnumerationQualifiedName or EnumerationValues.");
            case AttributeKind.String:
            {
                var strType = _model.Create<IStringAttributeType>();
                if (spec.MaxLength.HasValue) strType.Length = spec.MaxLength.Value;
                return strType;
            }
            case AttributeKind.DateTime:
            {
                var dtType = _model.Create<IDateTimeAttributeType>();
                if (spec.LocalizeDate.HasValue) dtType.LocalizeDate = spec.LocalizeDate.Value;
                return dtType;
            }
            case AttributeKind.Integer: return _model.Create<IIntegerAttributeType>();
            case AttributeKind.LongType: return _model.Create<ILongAttributeType>();
            case AttributeKind.Decimal: return _model.Create<IDecimalAttributeType>();
            case AttributeKind.Boolean: return _model.Create<IBooleanAttributeType>();
            case AttributeKind.AutoNumber: return _model.Create<IAutoNumberAttributeType>();
            case AttributeKind.HashString: return _model.Create<IHashedStringAttributeType>();
            case AttributeKind.Binary: return _model.Create<IBinaryAttributeType>();
            default: return _model.Create<IStringAttributeType>();
        }
    }

    private IEnumerationAttributeType CreateEnumerationAttributeType(string baseName, List<string> values, IModule module)
    {
        var enumDoc = _model.Create<IEnumeration>();
        enumDoc.Name = $"{baseName}Enum";
        foreach (var v in values)
        {
            var enumValue = _model.Create<IEnumerationValue>();
            enumValue.Name = v;
            var caption = _model.Create<IText>();
            caption.AddOrUpdateTranslation("en_US", v);
            enumValue.Caption = caption;
            enumDoc.AddValue(enumValue);
        }
        module.AddDocument(enumDoc);
        var attrType = _model.Create<IEnumerationAttributeType>();
        attrType.Enumeration = enumDoc.QualifiedName;
        return attrType;
    }

    private (IEntity Entity, IModule Module) CreatePersistentEntityCore(IModule module, string entityName, IReadOnlyList<AttributeSpec>? attributes, EntityKind kind)
    {
        var mxEntity = _model.Create<IEntity>();
        mxEntity.Name = entityName;

        if (kind == EntityKind.NonPersistent)
        {
            var noGen = _model.Create<INoGeneralization>();
            noGen.Persistable = false;
            mxEntity.Generalization = noGen;
        }

        module.DomainModel.AddEntity(mxEntity);

        if (attributes != null)
        {
            foreach (var spec in attributes)
            {
                var mxAttr = _model.Create<IAttribute>();
                mxAttr.Name = spec.Name;
                mxAttr.Type = CreateAttributeTypeFromSpec(spec, module);

                if (!string.IsNullOrEmpty(spec.DefaultValue))
                {
                    var stored = _model.Create<IStoredValue>();
                    stored.DefaultValue = spec.DefaultValue;
                    mxAttr.Value = stored;
                }

                if (!string.IsNullOrEmpty(spec.Documentation))
                    mxAttr.Documentation = spec.Documentation;

                mxEntity.AddAttribute(mxAttr);
            }
        }

        return (mxEntity, module);
    }

    private static MxAssociationType MapAssociationType(Terminal.Interop.AssociationType type)
        => type == Terminal.Interop.AssociationType.ReferenceSet
            ? MxAssociationType.ReferenceSet
            : MxAssociationType.Reference;

    private static DeletingBehavior MapDeleteBehavior(DeleteBehavior behavior)
        => behavior switch
        {
            DeleteBehavior.DeleteMeAndReferences => DeletingBehavior.DeleteMeAndReferences,
            DeleteBehavior.DeleteMeIfNoReferences => DeletingBehavior.DeleteMeIfNoReferences,
            _ => DeletingBehavior.DeleteMeButKeepReferences
        };

    private static (ActionMoment Moment, EventType Event) MapEventHandlerKind(EventHandlerKind kind)
        => kind switch
        {
            EventHandlerKind.BeforeCreate => (ActionMoment.Before, EventType.Create),
            EventHandlerKind.AfterCreate => (ActionMoment.After, EventType.Create),
            EventHandlerKind.BeforeCommit => (ActionMoment.Before, EventType.Commit),
            EventHandlerKind.AfterCommit => (ActionMoment.After, EventType.Commit),
            EventHandlerKind.BeforeDelete => (ActionMoment.Before, EventType.Delete),
            EventHandlerKind.AfterDelete => (ActionMoment.After, EventType.Delete),
            EventHandlerKind.BeforeRollback => (ActionMoment.Before, EventType.RollBack),
            EventHandlerKind.AfterRollback => (ActionMoment.After, EventType.RollBack),
            _ => (ActionMoment.After, EventType.Commit)
        };

    private static AttributeKind MapAttributeKind(IAttributeType? type)
        => type switch
        {
            IStringAttributeType => AttributeKind.String,
            IIntegerAttributeType => AttributeKind.Integer,
            ILongAttributeType => AttributeKind.LongType,
            IDecimalAttributeType => AttributeKind.Decimal,
            IBooleanAttributeType => AttributeKind.Boolean,
            IDateTimeAttributeType => AttributeKind.DateTime,
            IEnumerationAttributeType => AttributeKind.Enumeration,
            IAutoNumberAttributeType => AttributeKind.AutoNumber,
            IHashedStringAttributeType => AttributeKind.HashString,
            IBinaryAttributeType => AttributeKind.Binary,
            _ => AttributeKind.String
        };

    private static AttributeKind ParseAttributeKind(string kindStr)
        => kindStr.ToLowerInvariant() switch
        {
            "string" => AttributeKind.String,
            "integer" or "int" => AttributeKind.Integer,
            "long" => AttributeKind.LongType,
            "decimal" => AttributeKind.Decimal,
            "boolean" or "bool" => AttributeKind.Boolean,
            "datetime" => AttributeKind.DateTime,
            "enumeration" => AttributeKind.Enumeration,
            "autonumber" => AttributeKind.AutoNumber,
            "hashstring" or "hashedstring" => AttributeKind.HashString,
            "binary" => AttributeKind.Binary,
            _ => AttributeKind.String
        };

    private static string SimpleName(string qualifiedOrSimple)
    {
        var dot = qualifiedOrSimple.LastIndexOf('.');
        return dot >= 0 ? qualifiedOrSimple[(dot + 1)..] : qualifiedOrSimple;
    }

    // Sugiyama-style layout — adapted from MendixDomainModelTools.ArrangeDomainModelInternal
    private void ArrangeDomainModelInternal(IModule module, string? rootEntityName = null)
    {
        var entities = module.DomainModel.GetEntities().ToList();
        if (entities.Count == 0) return;

        const int ENTITY_WIDTH = 200;
        const int H_GAP = 50;
        const int V_SPACING = 120;
        const int START_X = 50;
        const int START_Y = 50;

        var entityByName = new Dictionary<string, IEntity>();
        foreach (var e in entities)
            entityByName[e.Name] = e;

        var neighbors = new Dictionary<string, HashSet<string>>();
        var edgeSet = new HashSet<(string, string)>();
        foreach (var e in entities)
            neighbors[e.Name] = new HashSet<string>();

        foreach (var entity in entities)
        {
            foreach (var assoc in entity.GetAssociations(AssociationDirection.Both, null))
            {
                try
                {
                    var nameA = assoc.Parent?.Name;
                    var nameB = assoc.Child?.Name;
                    if (nameA == null || nameB == null || nameA == nameB) continue;
                    if (!entityByName.ContainsKey(nameA) || !entityByName.ContainsKey(nameB)) continue;
                    neighbors[nameA].Add(nameB);
                    neighbors[nameB].Add(nameA);
                    var canonical = string.Compare(nameA, nameB, StringComparison.Ordinal) < 0
                        ? (nameA, nameB) : (nameB, nameA);
                    edgeSet.Add(canonical);
                }
                catch { /* skip broken associations */ }
            }
        }

        // BFS layer assignment
        var visited = new HashSet<string>();
        var layerOf = new Dictionary<string, int>();
        var bfsQueue = new Queue<string>();

        // Find component roots using highest-degree heuristic
        var byDegree = entities.OrderByDescending(e => neighbors[e.Name].Count).Select(e => e.Name).ToList();
        foreach (var name in byDegree)
        {
            if (visited.Contains(name) || neighbors[name].Count == 0) continue;
            var component = FloodFill(name, neighbors, visited);

            string root;
            if (!string.IsNullOrEmpty(rootEntityName) && component.Contains(rootEntityName))
                root = rootEntityName;
            else
                root = component.OrderBy(n => neighbors[n].Count).First();

            layerOf[root] = 0;
            bfsQueue.Enqueue(root);
        }

        while (bfsQueue.Count > 0)
        {
            var node = bfsQueue.Dequeue();
            foreach (var nb in neighbors[node])
            {
                if (!layerOf.ContainsKey(nb))
                {
                    layerOf[nb] = layerOf[node] + 1;
                    bfsQueue.Enqueue(nb);
                }
            }
        }

        var maxLayer = layerOf.Values.Any() ? layerOf.Values.Max() : 0;
        var layers = new Dictionary<int, List<string>>();
        for (int i = 0; i <= maxLayer; i++)
            layers[i] = new List<string>();
        foreach (var kvp in layerOf)
            layers[kvp.Value].Add(kvp.Key);

        var orphans = entities.Where(e => !layerOf.ContainsKey(e.Name)).Select(e => e.Name).ToList();

        // Assign positions
        int maxLayerCount = layers.Values.Any() ? layers.Values.Max(l => l.Count) : 1;
        int layerTotalWidth = maxLayerCount * ENTITY_WIDTH + (maxLayerCount - 1) * H_GAP;
        int currentY = START_Y;

        for (int layer = 0; layer <= maxLayer; layer++)
        {
            var layerNodes = layers[layer];
            if (layerNodes.Count == 0) continue;
            int thisLayerWidth = layerNodes.Count * ENTITY_WIDTH + (layerNodes.Count - 1) * H_GAP;
            int offsetX = START_X + (layerTotalWidth - thisLayerWidth) / 2;
            for (int i = 0; i < layerNodes.Count; i++)
            {
                var x = offsetX + i * (ENTITY_WIDTH + H_GAP);
                if (entityByName.TryGetValue(layerNodes[i], out var ent))
                    ent.Location = new Location(x, currentY);
            }
            currentY += 80 + V_SPACING;
        }

        // Orphan placement
        int orphanX = START_X;
        int orphanY = currentY;
        foreach (var orphanName in orphans)
        {
            if (entityByName.TryGetValue(orphanName, out var ent))
                ent.Location = new Location(orphanX, orphanY);
            orphanX += ENTITY_WIDTH + H_GAP;
        }
    }

    private static List<string> FloodFill(string start, Dictionary<string, HashSet<string>> neighbors, HashSet<string> visited)
    {
        var component = new List<string>();
        var queue = new Queue<string>();
        queue.Enqueue(start);
        visited.Add(start);
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            component.Add(n);
            foreach (var nb in neighbors[n])
            {
                if (!visited.Contains(nb))
                {
                    visited.Add(nb);
                    queue.Enqueue(nb);
                }
            }
        }
        return component;
    }
}
