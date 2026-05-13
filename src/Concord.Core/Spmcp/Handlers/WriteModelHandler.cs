using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Terminal.Interop;
using Terminal.Spmcp.Core;

namespace Terminal.Spmcp.Handlers.Schema
{
    public class DomainModelSchema
    {
        public List<EntitySchema> Entities { get; set; } = new List<EntitySchema>();
        public List<AssociationSchema> Associations { get; set; } = new List<AssociationSchema>();
    }

    public class EntitySchema
    {
        public string Name { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public List<AttributeSchema> Attributes { get; set; } = new List<AttributeSchema>();
    }

    public class AttributeSchema
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public List<string>? EnumerationValues { get; set; }
    }

    public class AssociationSchema
    {
        public string Name { get; set; } = string.Empty;
        public string Parent { get; set; } = string.Empty;
        public string Child { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    public class Association
    {
        public string Name { get; set; } = string.Empty;
        public string Parent { get; set; } = string.Empty;
        public string Child { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}

namespace Terminal.Spmcp.Handlers
{
    public class WriteModelHandler : BaseApiHandler
    {
        private static readonly HashSet<string> UsedNames = new HashSet<string>();

        public override string Path => "/api/model/write";
        public override string Method => "POST";

        public WriteModelHandler() : base() { }

        private static string StripModuleName(string entityName)
            => entityName.Contains(".") ? entityName.Split('.').Last() : entityName;

        private static Terminal.Interop.AssociationType MapAssociationType(string type)
        {
            if (string.IsNullOrEmpty(type)) return Terminal.Interop.AssociationType.Reference;
            return type.ToLowerInvariant().Trim() switch
            {
                "one-to-many" or "reference" => Terminal.Interop.AssociationType.Reference,
                "many-to-many" or "referenceset" => Terminal.Interop.AssociationType.ReferenceSet,
                _ => Terminal.Interop.AssociationType.Reference
            };
        }

        private static AttributeKind MapAttributeKind(string type)
        {
            return type.ToLowerInvariant() switch
            {
                "decimal" => AttributeKind.Decimal,
                "integer" => AttributeKind.Integer,
                "string" => AttributeKind.String,
                "boolean" => AttributeKind.Boolean,
                "datetime" => AttributeKind.DateTime,
                "autonumber" => AttributeKind.AutoNumber,
                "enumeration" => AttributeKind.Enumeration,
                "long" or "longtype" => AttributeKind.LongType,
                "hashstring" => AttributeKind.HashString,
                "binary" => AttributeKind.Binary,
                _ => AttributeKind.String
            };
        }

        private static EntityKind MapEntityKind(string? entityType)
        {
            return entityType?.ToLower() switch
            {
                "non-persistent" => EntityKind.NonPersistent,
                _ => EntityKind.Persistent
            };
        }

        private static string? MapGeneralization(string? entityType)
        {
            return entityType?.ToLower() switch
            {
                "filedocument" => "System.FileDocument",
                "image" => "System.Image",
                _ => null
            };
        }

        private string GetUniqueName(string baseName)
        {
            if (!UsedNames.Contains(baseName))
            {
                UsedNames.Add(baseName);
                return baseName;
            }

            int counter = 1;
            string uniqueName;
            do
            {
                uniqueName = $"{baseName}{counter}";
                counter++;
            } while (UsedNames.Contains(uniqueName));

            UsedNames.Add(uniqueName);
            return uniqueName;
        }

        public override async Task HandleAsync(HttpContext context)
        {
            try
            {
                context.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
                context.Response.Headers.TryAdd("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
                context.Response.Headers.TryAdd("Access-Control-Allow-Headers", "Content-Type");

                if (context.Request.Method == "OPTIONS")
                {
                    context.Response.StatusCode = 200;
                    return;
                }

                if (context.Request.Method != Method)
                {
                    context.Response.StatusCode = 405;
                    await WriteJsonResponseAsync(context, new
                    {
                        success = false,
                        message = $"Method not allowed. Use {Method}."
                    });
                    return;
                }

                var schema = await DeserializeRequestBodyAsync<Schema.DomainModelSchema>(context);
                if (schema == null)
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonResponseAsync(context, new
                    {
                        success = false,
                        message = "Failed to deserialize the request body or request body is empty"
                    });
                    return;
                }

                if ((schema.Entities == null || !schema.Entities.Any()) &&
                    (schema.Associations == null || !schema.Associations.Any()))
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonResponseAsync(context, new
                    {
                        success = false,
                        message = "No entities or associations provided in the schema"
                    });
                    return;
                }

                var model = HostServices.Model;
                var domainModel = HostServices.DomainModel;

                // Resolve the first user module
                var module = model.ListModules().FirstOrDefault(m =>
                    !m.Name.StartsWith("System", StringComparison.OrdinalIgnoreCase) &&
                    !m.Name.StartsWith("AppStore", StringComparison.OrdinalIgnoreCase));

                if (module == default)
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonResponseAsync(context, new
                    {
                        success = false,
                        message = "No domain model found."
                    });
                    return;
                }

                // Validate for duplicates
                var duplicatedEntityNames = schema.Entities
                    .GroupBy(e => StripModuleName(e.Name))
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList();

                if (duplicatedEntityNames.Any())
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonResponseAsync(context, new
                    {
                        success = false,
                        message = $"Duplicated entity names detected: {string.Join(", ", duplicatedEntityNames)}"
                    });
                    return;
                }

                var duplicatedAssociationNames = schema.Associations?
                    .GroupBy(a => StripModuleName(a.Name))
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToList() ?? new List<string>();

                if (duplicatedAssociationNames.Any())
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonResponseAsync(context, new
                    {
                        success = false,
                        message = $"Duplicated association names detected: {string.Join(", ", duplicatedAssociationNames)}"
                    });
                    return;
                }

                // Build entity create requests
                var existingEntities = domainModel.ListEntities(module)
                    .ToDictionary(e => e.QualifiedName.Split('.').Last(), StringComparer.OrdinalIgnoreCase);

                var createdEntities = new Dictionary<string, EntityRef>(StringComparer.OrdinalIgnoreCase);

                // Create entities that don't already exist
                var entityRequests = new List<CreateEntityRequest>();
                foreach (var entity in schema.Entities)
                {
                    var entityName = StripModuleName(entity.Name);
                    if (existingEntities.ContainsKey(entityName))
                    {
                        createdEntities[entityName] = existingEntities[entityName];
                        continue;
                    }

                    var attributes = entity.Attributes?.Select(a => new AttributeSpec(
                        Name: a.Name,
                        Kind: MapAttributeKind(a.Type),
                        EnumerationQualifiedName: null,
                        EnumerationValues: (a.Type.Equals("Enumeration", StringComparison.OrdinalIgnoreCase) && a.EnumerationValues != null)
                            ? a.EnumerationValues
                            : null,
                        MaxLength: null,
                        LocalizeDate: null,
                        DefaultValue: null,
                        Documentation: null
                    )).ToList() ?? new List<AttributeSpec>();

                    entityRequests.Add(new CreateEntityRequest(
                        ModuleName: module.Name,
                        EntityName: GetUniqueName(entityName),
                        Kind: MapEntityKind(entity.EntityType),
                        Generalization: MapGeneralization(entity.EntityType),
                        Attributes: attributes,
                        Documentation: null
                    ));
                }

                if (entityRequests.Any())
                {
                    var newEntityRefs = domainModel.CreateMultipleEntities(entityRequests);
                    foreach (var entityRef in newEntityRefs)
                    {
                        var simpleName = entityRef.QualifiedName.Split('.').Last();
                        createdEntities[simpleName] = entityRef;
                    }
                }

                // Create associations
                var allEntityRefs = createdEntities;
                var assocRequests = new List<CreateAssociationRequest>();

                if (schema.Associations != null)
                {
                    foreach (var association in schema.Associations)
                    {
                        var parentName = StripModuleName(association.Parent);
                        var childName = StripModuleName(association.Child);
                        var assocName = string.IsNullOrEmpty(association.Name)
                            ? $"{parentName}_{childName}"
                            : StripModuleName(association.Name);

                        if (!allEntityRefs.TryGetValue(parentName, out var parentRef))
                        {
                            context.Response.StatusCode = 400;
                            await WriteJsonResponseAsync(context, new
                            {
                                success = false,
                                message = $"Invalid association {assocName}: Parent entity '{parentName}' not found."
                            });
                            return;
                        }

                        if (!allEntityRefs.TryGetValue(childName, out var childRef))
                        {
                            context.Response.StatusCode = 400;
                            await WriteJsonResponseAsync(context, new
                            {
                                success = false,
                                message = $"Invalid association {assocName}: Child entity '{childName}' not found."
                            });
                            return;
                        }

                        assocRequests.Add(new CreateAssociationRequest(
                            ModuleName: module.Name,
                            Name: assocName,
                            ParentEntityQualifiedName: parentRef.QualifiedName,
                            ChildEntityQualifiedName: childRef.QualifiedName,
                            Type: MapAssociationType(association.Type),
                            ParentDeleteBehavior: DeleteBehavior.DeleteMeButKeepReferences,
                            ChildDeleteBehavior: DeleteBehavior.DeleteMeButKeepReferences,
                            Owner: AssociationOwner.Default,
                            Documentation: null
                        ));
                    }
                }

                if (assocRequests.Any())
                {
                    domainModel.CreateMultipleAssociations(assocRequests);
                }

                // Auto-arrange the domain model
                try
                {
                    domainModel.ArrangeDomainModel(new ArrangeDomainModelRequest(module.Name));
                }
                catch
                {
                    // Arrangement is best-effort; don't fail the operation
                }

                // Return updated model data
                var updatedEntities = domainModel.ListEntities(module);
                var modelData = updatedEntities.Select(entityRef =>
                {
                    var shape = domainModel.ReadEntity(entityRef);
                    var simpleName = entityRef.QualifiedName.Split('.').Last();
                    return new
                    {
                        EntityName = entityRef.QualifiedName,
                        Attributes = shape.Attributes.ToDictionary(a => a.Name, a => a.Kind.ToString()),
                        Associations = shape.OutgoingAssociations.Select(a => new
                        {
                            Name = a.Name,
                            Parent = a.ParentEntityQualifiedName.Split('.').Last(),
                            Child = a.ChildEntityQualifiedName.Split('.').Last(),
                            Type = a.Type == Terminal.Interop.AssociationType.Reference ? "one-to-many" : "many-to-many"
                        }).ToList()
                    };
                }).ToList();

                context.Response.StatusCode = 200;
                await WriteJsonResponseAsync(context, new
                {
                    success = true,
                    message = "Model created successfully",
                    data = modelData
                });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 400;
                await WriteJsonResponseAsync(context, new
                {
                    success = false,
                    message = $"Error creating model: {ex.Message}",
                    data = (object?)null
                });
            }
        }
    }
}
