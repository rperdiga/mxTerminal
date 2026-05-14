using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Terminal.Interop;
using Terminal.Spmcp.Utils;

namespace Terminal.Spmcp.Tools
{
    public class MendixDomainModelTools
    {
        private readonly ILogger<MendixDomainModelTools> _logger;

        public MendixDomainModelTools(ILogger<MendixDomainModelTools> logger)
        {
            _logger = logger;
        }

        public async Task<string> ListModules(JsonObject parameters)
        {
            try
            {
                var allModuleIds = HostServices.Model.ListModules();
                var moduleList = allModuleIds
                    .Select(m =>
                    {
                        var entityCount = HostServices.DomainModel.ListEntities(m).Count;
                        return new
                        {
                            name = m.Name,
                            fromAppStore = false, // note: ModuleId does not expose FromAppStore; gap surfaced here
                            entityCount
                        };
                    })
                    .OrderBy(m => m.name)
                    .ToList();

                var result = new
                {
                    success = true,
                    message = $"Found {moduleList.Count} modules",
                    note = "fromAppStore filter not available via IModelHost — all modules shown",
                    modules = moduleList,
                    userModules = moduleList.Select(m => m.name).ToList()
                };

                return JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing modules");
                return JsonSerializer.Serialize(new { error = "Failed to list modules", details = ex.Message });
            }
        }

        public async Task<string> CreateModule(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters["module_name"]?.ToString();
                if (string.IsNullOrEmpty(moduleName))
                {
                    return JsonSerializer.Serialize(new { error = "module_name is required" });
                }

                // Check for duplicate
                var existing = HostServices.Model.GetModuleByName(moduleName);
                if (existing != null)
                {
                    return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' already exists" });
                }

                var moduleId = HostServices.DomainModel.CreateModule(moduleName);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Module '{moduleName}' created successfully",
                    module = new { name = moduleId.Name, fromAppStore = false, entityCount = 0 }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating module");
                MendixAdditionalTools.SetLastError($"Failed to create module: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to create module: {ex.Message}" });
            }
        }

        public async Task<string> SetEntityGeneralization(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var parentEntityName = parameters["parent_entity"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();
                var parentModuleName = parameters["parent_module"]?.ToString();

                if (string.IsNullOrEmpty(entityName) || string.IsNullOrEmpty(parentEntityName))
                    return JsonSerializer.Serialize(new { error = "entity_name and parent_entity are required" });

                // BUG-015 fix: Prevent self-referencing generalization (name-level check)
                if (entityName.Equals(parentEntityName, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(moduleName) && string.IsNullOrEmpty(parentModuleName) ||
                     (!string.IsNullOrEmpty(moduleName) && moduleName.Equals(parentModuleName, StringComparison.OrdinalIgnoreCase))))
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' cannot inherit from itself (self-referencing generalization)" });

                var entityRef = ResolveEntityRef(entityName, moduleName);
                if (entityRef == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" + (moduleName != null ? $" in module '{moduleName}'" : "") });

                var parentRef = ResolveEntityRef(parentEntityName, parentModuleName);
                if (parentRef == null)
                    return JsonSerializer.Serialize(new { error = $"Parent entity '{parentEntityName}' not found" + (parentModuleName != null ? $" in module '{parentModuleName}'" : "") });

                // BUG-015 fix: identity check via Guid
                if (entityRef.Value.Id == parentRef.Value.Id)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' cannot inherit from itself (self-referencing generalization)" });

                HostServices.DomainModel.SetGeneralization(entityRef.Value, parentRef.Value);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Entity '{entityName}' now inherits from '{parentEntityName}'",
                    entity = entityName,
                    parent = parentEntityName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting entity generalization");
                MendixAdditionalTools.SetLastError($"Failed to set generalization: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to set generalization: {ex.Message}" });
            }
        }

        public async Task<string> RemoveEntityGeneralization(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });

                var entityRef = ResolveEntityRef(entityName, moduleName);
                if (entityRef == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });

                // Verify there is a generalization to remove
                var shape = HostServices.DomainModel.ReadEntity(entityRef.Value);
                if (string.IsNullOrEmpty(shape.GeneralizationQualifiedName))
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' does not have a generalization to remove" });

                HostServices.DomainModel.RemoveGeneralization(entityRef.Value);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Generalization removed from entity '{entityName}'. It is now a root entity.",
                    entity = entityName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing entity generalization");
                MendixAdditionalTools.SetLastError($"Failed to remove generalization: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to remove generalization: {ex.Message}" });
            }
        }

        public async Task<string> AddEventHandler(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var eventStr = parameters["event"]?.ToString();
                var momentStr = parameters["moment"]?.ToString();
                var microflowName = parameters["microflow"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName) || string.IsNullOrEmpty(eventStr) || string.IsNullOrEmpty(momentStr) || string.IsNullOrEmpty(microflowName))
                    return JsonSerializer.Serialize(new { error = "entity_name, event, moment, and microflow are required" });

                var entityRef = ResolveEntityRef(entityName, moduleName);
                if (entityRef == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });

                // Resolve qualified microflow name: accept "Module.Name" or plain "Name"
                string microflowQualifiedName;
                if (microflowName.Contains('.'))
                {
                    microflowQualifiedName = microflowName;
                }
                else
                {
                    // Search across modules to build the qualified name
                    string? found = null;
                    foreach (var modId in HostServices.Model.ListModules())
                    {
                        var docs = HostServices.Model.ListModuleDocuments(modId, "Microflow");
                        var doc = docs.FirstOrDefault(d =>
                        {
                            var dot = d.QualifiedName.LastIndexOf('.');
                            var simpleName = dot >= 0 ? d.QualifiedName.Substring(dot + 1) : d.QualifiedName;
                            return simpleName.Equals(microflowName, StringComparison.OrdinalIgnoreCase);
                        });
                        if (doc.QualifiedName != null)
                        {
                            found = doc.QualifiedName;
                            break;
                        }
                    }
                    if (found == null)
                        return JsonSerializer.Serialize(new { error = $"Microflow '{microflowName}' not found in any module" });
                    microflowQualifiedName = found;
                }

                // Map moment + event → EventHandlerKind
                var kind = MapEventHandlerKind(momentStr, eventStr);
                if (kind == null)
                    return JsonSerializer.Serialize(new { error = $"Invalid moment '{momentStr}' or event '{eventStr}'. moment: before/after; event: create/commit/delete/rollback" });

                bool raiseErrorOnFalse = true;
                if (parameters.ContainsKey("raise_error_on_false"))
                {
                    if (parameters["raise_error_on_false"]?.AsValue().TryGetValue<bool>(out var val) == true)
                        raiseErrorOnFalse = val;
                }

                var spec = new EventHandlerSpec(
                    Kind: kind.Value,
                    MicroflowQualifiedName: microflowQualifiedName,
                    RaiseErrorOnFalse: raiseErrorOnFalse,
                    PassEventObject: true);

                HostServices.DomainModel.AddEventHandler(entityRef.Value, spec);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Event handler added to '{entityName}': {momentStr} {eventStr} → {microflowName}",
                    entity = entityName,
                    moment = momentStr,
                    @event = eventStr,
                    microflow = microflowName,
                    raiseErrorOnFalse = raiseErrorOnFalse
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding event handler");
                MendixAdditionalTools.SetLastError($"Failed to add event handler: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to add event handler: {ex.Message}" });
            }
        }

        /// <summary>
        /// Maps (moment, event) string pair to the interop <see cref="EventHandlerKind"/> enum.
        /// Returns null when either value is unrecognized.
        /// </summary>
        private static EventHandlerKind? MapEventHandlerKind(string moment, string @event)
        {
            var m = moment.ToLowerInvariant().Trim();
            var e = @event.ToLowerInvariant().Trim();
            return (m, e) switch
            {
                ("before", "create")   => EventHandlerKind.BeforeCreate,
                ("after",  "create")   => EventHandlerKind.AfterCreate,
                ("before", "commit")   => EventHandlerKind.BeforeCommit,
                ("after",  "commit")   => EventHandlerKind.AfterCommit,
                ("before", "delete")   => EventHandlerKind.BeforeDelete,
                ("after",  "delete")   => EventHandlerKind.AfterDelete,
                ("before", "rollback") => EventHandlerKind.BeforeRollback,
                ("after",  "rollback") => EventHandlerKind.AfterRollback,
                _ => null
            };
        }

        public async Task<string> AddAttribute(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var attributeName = parameters["attribute_name"]?.ToString();
                var attributeType = parameters["attribute_type"]?.ToString();
                var defaultValue = parameters["default_value"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });
                if (string.IsNullOrEmpty(attributeName))
                    return JsonSerializer.Serialize(new { error = "attribute_name is required" });
                if (string.IsNullOrEmpty(attributeType))
                    return JsonSerializer.Serialize(new { error = "attribute_type is required" });

                var entityRef = ResolveEntityRef(entityName, moduleName);
                if (entityRef == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" + (moduleName != null ? $" in module '{moduleName}'" : "") });

                // Check if attribute already exists
                var shape = HostServices.DomainModel.ReadEntity(entityRef.Value);
                if (shape.Attributes.Any(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase)))
                    return JsonSerializer.Serialize(new { error = $"Attribute '{attributeName}' already exists on entity '{entityName}'" });

                var kind = ParseAttributeKind(attributeType);

                // Resolve enumeration references
                string? enumQualifiedName = null;
                IReadOnlyList<string>? enumValues = null;

                if (kind == AttributeKind.Enumeration)
                {
                    if (attributeType.StartsWith("Enumeration:", StringComparison.OrdinalIgnoreCase))
                    {
                        var enumName = attributeType.Substring("Enumeration:".Length).Trim();
                        var explicitEnumName = parameters["enumeration_name"]?.ToString();
                        if (!string.IsNullOrEmpty(explicitEnumName)) enumName = explicitEnumName;
                        if (string.IsNullOrEmpty(enumName))
                            return JsonSerializer.Serialize(new { error = "Enumeration name must be specified after the colon, e.g. 'Enumeration:OrderStatus'" });
                        // Use the name as-is; the host resolves it. Qualify if needed.
                        enumQualifiedName = enumName;
                    }
                    else
                    {
                        var explicitEnumName = parameters["enumeration_name"]?.ToString();
                        if (!string.IsNullOrEmpty(explicitEnumName))
                        {
                            enumQualifiedName = explicitEnumName;
                        }
                        else
                        {
                            // Same robust-array shape as CreateEnumeration —
                            // accept stringified arrays + parameter-name variants.
                            var valuesNode = Utils.Utils.GetArrayParam(parameters, "enumeration_values", "values", "enum_values", "enumerationValues");
                            var rawValues = valuesNode
                                ?.Select(v => v?.ToString())
                                ?.Where(v => !string.IsNullOrEmpty(v))
                                ?.ToList();
                            if (rawValues == null || rawValues.Count == 0)
                                return JsonSerializer.Serialize(new { error = "Enumeration type requires 'enumeration_values' array (e.g. [\"Draft\",\"Submitted\"]) or 'enumeration_name' to reference an existing enumeration" });
                            enumValues = rawValues!;
                        }
                    }
                }

                int? maxLength = null;
                if (parameters["max_length"]?.AsValue().TryGetValue<int>(out var ml) == true) maxLength = ml;

                bool? localizeDate = null;
                if (parameters["localize_date"]?.AsValue().TryGetValue<bool>(out var ld) == true) localizeDate = ld;

                var spec = new AttributeSpec(
                    Name: attributeName,
                    Kind: kind,
                    EnumerationQualifiedName: enumQualifiedName,
                    EnumerationValues: enumValues,
                    MaxLength: maxLength,
                    LocalizeDate: localizeDate,
                    DefaultValue: defaultValue,
                    Documentation: parameters["documentation"]?.ToString());

                var attrRef = HostServices.DomainModel.AddAttribute(entityRef.Value, spec);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Attribute '{attributeName}' ({attributeType}) added to entity '{entityName}'",
                    entity = entityName,
                    attribute = new
                    {
                        name = attrRef.Name,
                        type = attributeType,
                        defaultValue = defaultValue
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding attribute");
                MendixAdditionalTools.SetLastError($"Failed to add attribute: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to add attribute: {ex.Message}" });
            }
        }

        public async Task<string> SetCalculatedAttribute(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var attributeName = parameters["attribute_name"]?.ToString();
                var microflowName = parameters["microflow"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });
                if (string.IsNullOrEmpty(attributeName))
                    return JsonSerializer.Serialize(new { error = "attribute_name is required" });
                if (string.IsNullOrEmpty(microflowName))
                    return JsonSerializer.Serialize(new { error = "microflow is required" });

                var entityRef = ResolveEntityRef(entityName, moduleName);
                if (entityRef == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" + (moduleName != null ? $" in module '{moduleName}'" : "") });

                var shape = HostServices.DomainModel.ReadEntity(entityRef.Value);
                var attrRef = shape.Attributes.FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                if (attrRef.Name == null)
                    return JsonSerializer.Serialize(new { error = $"Attribute '{attributeName}' not found on entity '{entityName}'" });

                // Resolve qualified microflow name
                string microflowQualifiedName;
                if (microflowName.Contains('.'))
                {
                    microflowQualifiedName = microflowName;
                }
                else
                {
                    string? found = null;
                    foreach (var modId in HostServices.Model.ListModules())
                    {
                        var docs = HostServices.Model.ListModuleDocuments(modId, "Microflow");
                        var doc = docs.FirstOrDefault(d =>
                        {
                            var dot = d.QualifiedName.LastIndexOf('.');
                            var simpleName = dot >= 0 ? d.QualifiedName.Substring(dot + 1) : d.QualifiedName;
                            return simpleName.Equals(microflowName, StringComparison.OrdinalIgnoreCase);
                        });
                        if (doc.QualifiedName != null) { found = doc.QualifiedName; break; }
                    }
                    if (found == null)
                        return JsonSerializer.Serialize(new { error = $"Microflow '{microflowName}' not found" });
                    microflowQualifiedName = found;
                }

                HostServices.DomainModel.SetCalculatedAttribute(entityRef.Value, attrRef, microflowQualifiedName);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Attribute '{attributeName}' on '{entityName}' is now calculated by microflow '{microflowName}'",
                    entity = entityName,
                    attribute = attributeName,
                    microflow = microflowName,
                    passEntity = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting calculated attribute");
                MendixAdditionalTools.SetLastError($"Failed to set calculated attribute: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to set calculated attribute: {ex.Message}" });
            }
        }

        public async Task<string> CheckModel(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();
                var items = HostServices.DomainModel.CheckModel(string.IsNullOrWhiteSpace(moduleName) ? null : moduleName);

                var errors   = items.Where(i => i.Severity == ModelCheckSeverity.Error)
                                    .Select(i => new { module = i.ModuleName, entity = i.EntityName, code = i.Code, message = i.Message })
                                    .ToList<object>();
                var warnings = items.Where(i => i.Severity == ModelCheckSeverity.Warning)
                                    .Select(i => new { module = i.ModuleName, entity = i.EntityName, code = i.Code, message = i.Message })
                                    .ToList<object>();
                var info     = items.Where(i => i.Severity == ModelCheckSeverity.Info)
                                    .Select(i => new { module = i.ModuleName, entity = i.EntityName, code = i.Code, message = i.Message })
                                    .ToList<object>();

                var hasIssues = errors.Any() || warnings.Any();
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    healthy = !errors.Any(),
                    summary = new
                    {
                        totalItems = items.Count,
                        errorCount = errors.Count,
                        warningCount = warnings.Count,
                        infoCount = info.Count
                    },
                    errors = errors.Any() ? errors : null,
                    warnings = warnings.Any() ? warnings : null,
                    info = info.Any() ? info : null,
                    message = !hasIssues ? "Model is healthy. No issues found." : $"Found {errors.Count} error(s) and {warnings.Count} warning(s)."
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking model");
                return JsonSerializer.Serialize(new { error = $"Failed to check model: {ex.Message}" });
            }
        }

        #region Phase 5: Constants + Enumerations

        public Task<string> CreateConstant(JsonObject parameters)
        {
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success = false,
                escalation = "manual",
                message = "CreateConstant: typed IConstant write is not exposed on the Core Interop surface (IModelHost.CreateConstant deferred). Create the constant in Studio Pro directly, or extend IModelHost with a Constant CRUD surface in a follow-up task."
            }));
        }

        public async Task<string> ListConstants(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();

                // Use UntypedModel to get Constants with their properties — single API call
                if (!HostServices.UntypedModel.IsAvailable)
                {
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        error = "IUntypedModelHost is not available on this Studio Pro version (10.x). Constant listing requires Studio Pro 11+."
                    });
                }

                var allConstants = HostServices.UntypedModel.GetUnitsOfType("Constants$Constant");

                var result = new List<object>();
                foreach (var c in allConstants)
                {
                    var qn = c.QualifiedName ?? "";
                    // Filter by module if specified: QualifiedName = "ModuleName.ConstantName"
                    if (!string.IsNullOrEmpty(moduleName))
                    {
                        var dotIdx = qn.IndexOf('.');
                        var cModule = dotIdx >= 0 ? qn.Substring(0, dotIdx) : qn;
                        if (!cModule.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    var defaultValue = HostServices.UntypedModel.ReadUnitProperty(qn, "defaultValue")
                                   ?? HostServices.UntypedModel.ReadUnitProperty(qn, "DefaultValue")
                                   ?? "<not exposed>";
                    var dataType = HostServices.UntypedModel.ReadUnitProperty(qn, "type")
                               ?? HostServices.UntypedModel.ReadUnitProperty(qn, "dataType")
                               ?? "<not exposed>";

                    var dotIdx2 = qn.IndexOf('.');
                    var modName = dotIdx2 >= 0 ? qn.Substring(0, dotIdx2) : "";

                    result.Add(new
                    {
                        name = c.Name,
                        qualifiedName = qn,
                        module = modName,
                        defaultValue,
                        dataType
                    });
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    count = result.Count,
                    constants = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing constants");
                return JsonSerializer.Serialize(new { error = $"Failed to list constants: {ex.Message}" });
            }
        }

        public async Task<string> CreateEnumeration(JsonObject parameters)
        {
            try
            {
                var name = Utils.Utils.GetParam(parameters, "name", "enumeration_name", "enumerationName");
                var moduleName = parameters?["module_name"]?.ToString();
                // Accept both real arrays AND string-encoded JSON arrays —
                // some MCP clients (Claude Code v2.1.x without an input
                // schema) conservatively stringify complex args. Also accept
                // common parameter-name variants since GetParam-style aliases
                // are the established pattern across SPMCP tools.
                var valuesArray = Utils.Utils.GetArrayParam(parameters, "values", "enumeration_values", "enum_values", "enumerationValues");

                if (string.IsNullOrEmpty(name))
                    return JsonSerializer.Serialize(new { error = "Enumeration name is required. Use the 'name' parameter (aliases accepted: 'enumeration_name')." });

                if (valuesArray == null || valuesArray.Count == 0)
                    return JsonSerializer.Serialize(new { error = "At least one value is required for enumeration. Use the 'values' parameter as a JSON array of strings (e.g. [\"Draft\",\"Submitted\"]) or objects with name+caption (e.g. [{\"name\":\"Draft\",\"caption\":\"Draft\"}])." });

                // Resolve module name
                if (string.IsNullOrWhiteSpace(moduleName))
                {
                    var allModules = HostServices.Model.ListModules();
                    if (!allModules.Any())
                        return JsonSerializer.Serialize(new { error = "No modules found in the project" });
                    moduleName = allModules.First().Name;
                }
                else
                {
                    var mid = HostServices.Model.GetModuleByName(moduleName);
                    if (mid == null)
                        return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });
                }

                // Build enumeration value specs
                var valueSpecs = new List<EnumerationValueSpec>();
                var createdValues = new List<object>();

                foreach (var valueNode in valuesArray)
                {
                    string? valueName = null;
                    string? caption = null;

                    if (valueNode is JsonObject valueObj)
                    {
                        valueName = valueObj["name"]?.ToString();
                        caption = valueObj["caption"]?.ToString();
                    }
                    else
                    {
                        valueName = valueNode?.ToString();
                    }

                    if (string.IsNullOrEmpty(valueName)) continue;

                    valueSpecs.Add(new EnumerationValueSpec(valueName, caption));
                    createdValues.Add(new { name = valueName, caption = caption ?? valueName });
                }

                if (valueSpecs.Count == 0)
                    return JsonSerializer.Serialize(new { error = "No valid enumeration values provided" });

                var enumRef = HostServices.DomainModel.CreateEnumeration(moduleName, name, valueSpecs);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Enumeration '{name}' created with {createdValues.Count} values in module '{moduleName}'",
                    enumeration = new
                    {
                        name,
                        qualifiedName = enumRef.QualifiedName,
                        module = moduleName,
                        values = createdValues
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating enumeration");
                return JsonSerializer.Serialize(new { error = $"Failed to create enumeration: {ex.Message}" });
            }
        }

        public async Task<string> ListEnumerations(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();

                var allModuleIds = HostServices.Model.ListModules();
                if (!string.IsNullOrEmpty(moduleName))
                {
                    var filtered = HostServices.Model.GetModuleByName(moduleName);
                    if (filtered == null)
                        return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });
                    allModuleIds = new[] { filtered.Value };
                }

                var result = new List<object>();
                var skipped = new List<object>();
                foreach (var moduleId in allModuleIds)
                {
                    var enumerations = Utils.Utils.TryPerModule(moduleId,
                        () => HostServices.DomainModel.ListEnumerations(moduleId),
                        skipped, "ListEnumerations", _logger);
                    if (enumerations == null) continue;

                    foreach (var enumRef in enumerations)
                    {
                        var dotIdx = enumRef.QualifiedName.IndexOf('.');
                        var simpleName = dotIdx >= 0 ? enumRef.QualifiedName.Substring(dotIdx + 1) : enumRef.QualifiedName;

                        // Enumeration values not exposed by IDomainModelHost — gap documented
                        result.Add(new
                        {
                            name = simpleName,
                            qualifiedName = enumRef.QualifiedName,
                            module = moduleId.Name,
                            note = "Enumeration values not surfaced by IDomainModelHost; use read_domain_model for value detail"
                        });
                    }
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    count = result.Count,
                    enumerations = result,
                    // Only surface skipped[] when there were errors — keeps
                    // the response clean for the success-path users hit ~99%
                    // of the time.
                    skippedModules = skipped.Count == 0 ? null : skipped,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing enumerations");
                return JsonSerializer.Serialize(new { error = $"Failed to list enumerations: {ex.Message}" });
            }
        }

        #endregion

        public async Task<string> ReadProjectInfo(JsonObject parameters)
        {
            try
            {
                var projectInfo = HostServices.Model.GetProjectInfo();
                var allModuleIds = HostServices.Model.ListModules();

                if (!allModuleIds.Any())
                {
                    return JsonSerializer.Serialize(new { error = "No modules found in the project" });
                }

                var moduleInfos = new List<object>();
                var skippedModules = new List<object>();

                // Running totals — accumulated in the loop so we don't need to
                // cast List<object> elements back to their anonymous type.
                int totalEntities = 0, totalAssociations = 0,
                    totalMicroflows = 0, totalConstants = 0, totalEnumerations = 0;

                foreach (var moduleId in allModuleIds)
                {
                    var entityRefs = Utils.Utils.TryPerModule(moduleId,
                        () => HostServices.DomainModel.ListEntities(moduleId),
                        skippedModules, "ListEntities", _logger);
                    if (entityRefs == null) continue;

                    var enumerationRefs = Utils.Utils.TryPerModule(moduleId,
                        () => HostServices.DomainModel.ListEnumerations(moduleId),
                        skippedModules, "ListEnumerations", _logger);
                    if (enumerationRefs == null) continue;

                    var microflowDocs = Utils.Utils.TryPerModule(moduleId,
                        () => HostServices.Model.ListModuleDocuments(moduleId, "Microflow"),
                        skippedModules, "ListModuleDocuments(Microflow)", _logger);
                    if (microflowDocs == null) continue;

                    var constantDocs = Utils.Utils.TryPerModule(moduleId,
                        () => HostServices.Model.ListModuleDocuments(moduleId, "Constant"),
                        skippedModules, "ListModuleDocuments(Constant)", _logger);
                    if (constantDocs == null) continue;

                    // Count associations by reading entity shapes.
                    // Inner try/catch is intentional — it guards against per-entity
                    // shape failures (a different exception class than ModuleProxy).
                    var seenAssocNames = new HashSet<string>();
                    foreach (var entityRef in entityRefs)
                    {
                        try
                        {
                            var shape = HostServices.DomainModel.ReadEntity(entityRef);
                            foreach (var assoc in shape.OutgoingAssociations)
                                seenAssocNames.Add(assoc.Name);
                        }
                        catch { }
                    }

                    var entityCount      = entityRefs.Count;
                    var associationCount = seenAssocNames.Count;
                    var microflowCount   = microflowDocs.Count;
                    var constantCount    = constantDocs.Count;
                    var enumerationCount = enumerationRefs.Count;

                    totalEntities      += entityCount;
                    totalAssociations  += associationCount;
                    totalMicroflows    += microflowCount;
                    totalConstants     += constantCount;
                    totalEnumerations  += enumerationCount;

                    moduleInfos.Add(new
                    {
                        name = moduleId.Name,
                        entityCount,
                        associationCount,
                        microflowCount,
                        constantCount,
                        enumerationCount,
                        entities = entityRefs.Select(e =>
                        {
                            var dotIdx = e.QualifiedName.IndexOf('.');
                            return dotIdx >= 0 ? e.QualifiedName.Substring(dotIdx + 1) : e.QualifiedName;
                        }).ToList()
                    });
                }

                var totals = new
                {
                    modules      = moduleInfos.Count,
                    entities     = totalEntities,
                    associations = totalAssociations,
                    microflows   = totalMicroflows,
                    constants    = totalConstants,
                    enumerations = totalEnumerations
                };

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    projectName = projectInfo.Name,
                    projectDirectory = projectInfo.DirectoryPath,
                    mendixVersion = projectInfo.MendixVersion,
                    appId = projectInfo.AppId,
                    totals,
                    modules = moduleInfos,
                    skippedModules = skippedModules.Count == 0 ? null : skippedModules
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading project info");
                return JsonSerializer.Serialize(new { error = $"Failed to read project info: {ex.Message}" });
            }
        }

        public async Task<string> ReadDomainModel(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();

                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                };

                if (string.IsNullOrWhiteSpace(moduleName))
                {
                    // Return domain models from ALL modules
                    var allModuleIds = HostServices.Model.ListModules();
                    if (!allModuleIds.Any())
                    {
                        return JsonSerializer.Serialize(new { error = "No modules found" });
                    }

                    var allModuleData = allModuleIds.Select(moduleId =>
                    {
                        var entityRefs = HostServices.DomainModel.ListEntities(moduleId);
                        return new
                        {
                            ModuleName = moduleId.Name,
                            Entities = entityRefs.Select(entityRef => BuildEntitySummary(entityRef)).ToList()
                        };
                    }).ToList();

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        message = $"Domain models retrieved from {allModuleData.Count} modules",
                        data = allModuleData,
                        status = "success"
                    }, options);
                }

                var moduleIdOpt = HostServices.Model.GetModuleByName(moduleName);
                if (moduleIdOpt == null)
                {
                    return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });
                }

                var targetModuleId = moduleIdOpt.Value;
                var entities = HostServices.DomainModel.ListEntities(targetModuleId);

                var modelData = new
                {
                    ModuleName = targetModuleId.Name,
                    Entities = entities.Select(entityRef => BuildEntitySummary(entityRef)).ToList()
                };

                var result = new
                {
                    success = true,
                    message = "Model retrieved successfully",
                    data = modelData,
                    status = "success"
                };

                return JsonSerializer.Serialize(result, options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading domain model");
                return JsonSerializer.Serialize(new { error = "Failed to read domain model", details = ex.Message });
            }
        }

        /// <summary>
        /// Reads an EntityRef via HostServices.DomainModel.ReadEntity and projects it to
        /// the anonymous summary shape used by ReadDomainModel.
        /// </summary>
        private object BuildEntitySummary(EntityRef entityRef)
        {
            var shape = HostServices.DomainModel.ReadEntity(entityRef);
            var dotIdx = entityRef.QualifiedName.IndexOf('.');
            var simpleName = dotIdx >= 0 ? entityRef.QualifiedName.Substring(dotIdx + 1) : entityRef.QualifiedName;

            return new
            {
                Name = simpleName,
                QualifiedName = entityRef.QualifiedName,
                Kind = shape.Kind.ToString(),
                Generalization = shape.GeneralizationQualifiedName != null
                    ? (object)new { hasGeneralization = true, parent = shape.GeneralizationQualifiedName }
                    : null,
                Documentation = shape.Documentation,
                Attributes = shape.Attributes.Select(a => new
                {
                    name = a.Name,
                    type = a.Kind.ToString(),
                    note = "MaxLength/defaultValue/EnumerationQualifiedName not surfaced by AttributeRef"
                }).ToList(),
                Associations = shape.OutgoingAssociations.Concat(shape.IncomingAssociations)
                    .Select(a => new
                    {
                        name = a.Name,
                        parent = a.ParentEntityQualifiedName,
                        child = a.ChildEntityQualifiedName,
                        type = a.Type == AssociationType.ReferenceSet ? "many-to-many" : "one-to-many"
                    }).ToList(),
                EventHandlers = shape.EventHandlerDescriptions.Count > 0
                    ? shape.EventHandlerDescriptions
                    : null
            };
        }

        public async Task<string> CreateEntity(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "Entity name is required" });

                var moduleName = parameters["module_name"]?.ToString();

                // Resolve module
                ModuleId moduleId;
                if (!string.IsNullOrWhiteSpace(moduleName))
                {
                    var mid = HostServices.Model.GetModuleByName(moduleName);
                    if (mid == null)
                        return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });
                    moduleId = mid.Value;
                }
                else
                {
                    var allModules = HostServices.Model.ListModules();
                    if (!allModules.Any())
                        return JsonSerializer.Serialize(new { error = "No modules found in the project" });
                    moduleId = allModules.First();
                }

                // Extract entityType / persistable
                bool persistable = true;
                if (parameters.ContainsKey("persistable"))
                    parameters["persistable"]?.AsValue().TryGetValue<bool>(out persistable);

                string entityType = "persistent";
                if (parameters.ContainsKey("entityType"))
                    entityType = parameters["entityType"]?.ToString() ?? "persistent";
                else if (!persistable)
                    entityType = "non-persistent";

                var kind = entityType.Equals("non-persistent", StringComparison.OrdinalIgnoreCase)
                    ? EntityKind.NonPersistent
                    : EntityKind.Persistent;

                // Build attribute specs from JSON — accept stringified arrays + name variants.
                var attributeSpecs = BuildAttributeSpecs(Utils.Utils.GetArrayParam(parameters, "attributes", "attribute_list", "attrs"));

                var generalization = parameters["generalization"]?.ToString();
                var documentation = parameters["documentation"]?.ToString();

                var request = new CreateEntityRequest(
                    ModuleName: moduleId.Name,
                    EntityName: entityName,
                    Kind: kind,
                    Generalization: generalization,
                    Attributes: attributeSpecs,
                    Documentation: documentation);

                var entityRef = HostServices.DomainModel.CreateEntity(request);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Entity '{entityName}' created successfully as {entityType}",
                    entity = new
                    {
                        name = entityName,
                        qualifiedName = entityRef.QualifiedName,
                        persistable,
                        entityType,
                        attributes = attributeSpecs?.Select(a => new { name = a.Name, type = a.Kind.ToString() }).ToArray()
                            ?? Array.Empty<object>()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating entity");
                MendixAdditionalTools.SetLastError($"Failed to create entity: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to create entity: {ex.Message}" });
            }
        }

        public async Task<string> CreateAssociation(JsonObject parameters)
        {
            try
            {
                var name = Utils.Utils.GetParam(parameters, "name", "association_name", "associationName");
                var parent = Utils.Utils.GetParam(parameters, "parent", "parent_entity", "parentEntity", "from_entity");
                var child = Utils.Utils.GetParam(parameters, "child", "child_entity", "childEntity", "to_entity");
                var type = parameters["type"]?.ToString() ?? "one-to-many";

                _logger.LogInformation($"CreateAssociation called with: name='{name}', parent='{parent}', child='{child}', type='{type}'");

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child))
                {
                    return JsonSerializer.Serialize(new {
                        error = "Missing required parameters for association creation",
                        message = "To create an association, you must provide: name, parent, and child parameters",
                        required_parameters = new {
                            name = new { type = "string", description = "Name of the association (e.g., 'Customer_Orders')", required = true },
                            parent = new { type = "string", description = "Name of the parent entity (e.g., 'Customer')", required = true },
                            child = new { type = "string", description = "Name of the child entity (e.g., 'Order')", required = true },
                            type = new { type = "string", description = "Type of association ('one-to-many' or 'many-to-many')", required = false, @default = "one-to-many" }
                        },
                        example_usage = new {
                            tool_name = "create_association",
                            parameters = new {
                                name = "Customer_Orders",
                                parent = "Customer",
                                child = "Order",
                                type = "one-to-many"
                            }
                        },
                        guidance = "Make sure both parent and child entities exist before creating an association."
                    });
                }

                // Cross-module support: resolve entities from specified or any module
                var parentModuleName = parameters["parent_module"]?.ToString();
                var childModuleName = parameters["child_module"]?.ToString();
                var defaultModuleName = parameters["module_name"]?.ToString();
                parentModuleName ??= defaultModuleName;
                childModuleName ??= defaultModuleName;

                var parentRef = ResolveEntityRef(parent, parentModuleName);
                if (parentRef == null)
                    return JsonSerializer.Serialize(new { error = $"Parent entity '{parent}' not found" + (parentModuleName != null ? $" in module '{parentModuleName}'" : " in any module") });

                var childRef = ResolveEntityRef(child, childModuleName);
                if (childRef == null)
                    return JsonSerializer.Serialize(new { error = $"Child entity '{child}' not found" + (childModuleName != null ? $" in module '{childModuleName}'" : " in any module") });

                var assocModuleName = defaultModuleName ?? parentRef.Value.QualifiedName.Split('.')[0];

                var parentDeleteBehavior = MapDeleteBehavior(parameters["parent_delete_behavior"]?.ToString());
                var childDeleteBehavior = MapDeleteBehavior(parameters["child_delete_behavior"]?.ToString());
                var ownerStr = parameters["owner"]?.ToString();
                var owner = (!string.IsNullOrEmpty(ownerStr) && ownerStr.ToLowerInvariant().Trim() == "both")
                    ? AssociationOwner.Both
                    : AssociationOwner.Default;

                var request = new CreateAssociationRequest(
                    ModuleName: assocModuleName,
                    Name: name,
                    ParentEntityQualifiedName: parentRef.Value.QualifiedName,
                    ChildEntityQualifiedName: childRef.Value.QualifiedName,
                    Type: MapAssociationType(type),
                    ParentDeleteBehavior: parentDeleteBehavior,
                    ChildDeleteBehavior: childDeleteBehavior,
                    Owner: owner,
                    Documentation: parameters["documentation"]?.ToString());

                var assocRef = HostServices.DomainModel.CreateAssociation(request);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Association '{name}' created successfully",
                    association = new
                    {
                        name = assocRef.Name,
                        parent = assocRef.ParentEntityQualifiedName,
                        child = assocRef.ChildEntityQualifiedName,
                        type = assocRef.Type == AssociationType.ReferenceSet ? "many-to-many" : "one-to-many",
                        parentDeleteBehavior = FormatDeleteBehavior(parentDeleteBehavior),
                        childDeleteBehavior = FormatDeleteBehavior(childDeleteBehavior),
                        owner = owner.ToString()
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating association");
                MendixAdditionalTools.SetLastError($"Failed to create association: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to create association: {ex.Message}" });
            }
        }

        public async Task<string> CreateMultipleEntities(JsonObject parameters)
        {
            try
            {
                var entitiesArray = Utils.Utils.GetArrayParam(parameters, "entities", "entity_list", "entityList");
                if (entitiesArray == null)
                    return JsonSerializer.Serialize(new { error = "Entities array is required. Use the 'entities' parameter as a JSON array of entity-spec objects." });

                // Extract global persistable parameter (default to true for backward compatibility)
                bool globalPersistable = true;
                if (parameters.ContainsKey("persistable"))
                    parameters["persistable"]?.AsValue().TryGetValue<bool>(out globalPersistable);

                string globalEntityType = globalPersistable ? "persistent" : "non-persistent";

                // Resolve default module
                var globalModuleName = parameters["module_name"]?.ToString();
                ModuleId defaultModuleId;
                if (!string.IsNullOrWhiteSpace(globalModuleName))
                {
                    var mid = HostServices.Model.GetModuleByName(globalModuleName);
                    if (mid == null)
                        return JsonSerializer.Serialize(new { error = $"Module '{globalModuleName}' not found" });
                    defaultModuleId = mid.Value;
                }
                else
                {
                    var allModules = HostServices.Model.ListModules();
                    if (!allModules.Any())
                        return JsonSerializer.Serialize(new { error = "No modules found in the project" });
                    defaultModuleId = allModules.First();
                }

                // Build requests
                var requests = new List<CreateEntityRequest>();
                var requestMeta = new List<(string entityName, string entityType, bool persistable)>();

                foreach (var entityNode in entitiesArray)
                {
                    var entityObj = entityNode?.AsObject();
                    if (entityObj == null) continue;

                    var entityName = entityObj["entity_name"]?.ToString() ?? entityObj["name"]?.ToString();
                    if (string.IsNullOrEmpty(entityName)) continue;

                    // Per-entity module override
                    var entityModuleName = entityObj["module_name"]?.ToString();
                    ModuleId moduleId = defaultModuleId;
                    if (!string.IsNullOrWhiteSpace(entityModuleName))
                    {
                        var mid = HostServices.Model.GetModuleByName(entityModuleName);
                        if (mid != null) moduleId = mid.Value;
                    }

                    // Per-entity entityType
                    string entityType = globalEntityType;
                    bool persistable = globalPersistable;
                    if (entityObj.ContainsKey("entityType"))
                    {
                        entityType = entityObj["entityType"]?.ToString() ?? globalEntityType;
                        persistable = !entityType.Equals("non-persistent", StringComparison.OrdinalIgnoreCase);
                    }

                    var kind = entityType.Equals("non-persistent", StringComparison.OrdinalIgnoreCase)
                        ? EntityKind.NonPersistent
                        : EntityKind.Persistent;

                    var attrSpecs = BuildAttributeSpecs(entityObj["attributes"]?.AsArray());

                    requests.Add(new CreateEntityRequest(
                        ModuleName: moduleId.Name,
                        EntityName: entityName,
                        Kind: kind,
                        Generalization: entityObj["generalization"]?.ToString(),
                        Attributes: attrSpecs,
                        Documentation: entityObj["documentation"]?.ToString()));

                    requestMeta.Add((entityName, entityType, persistable));
                }

                var createdRefs = HostServices.DomainModel.CreateMultipleEntities(requests);

                var createdEntities = createdRefs.Select((r, i) => new
                {
                    name = i < requestMeta.Count ? requestMeta[i].entityName : r.QualifiedName,
                    qualifiedName = r.QualifiedName,
                    persistable = i < requestMeta.Count ? requestMeta[i].persistable : globalPersistable,
                    entityType = i < requestMeta.Count ? requestMeta[i].entityType : globalEntityType
                }).ToList();

                // Trigger arrange via host
                bool arranged = false;
                try
                {
                    HostServices.DomainModel.ArrangeDomainModel(new ArrangeDomainModelRequest(defaultModuleId.Name));
                    arranged = true;
                }
                catch (Exception layoutEx)
                {
                    _logger.LogWarning(layoutEx, "Auto-arrange after bulk creation failed (non-fatal)");
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Successfully created {createdEntities.Count} entities",
                    entities = createdEntities,
                    auto_arranged = arranged
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating multiple entities");
                return JsonSerializer.Serialize(new { error = $"Failed to create entities: {ex.Message}" });
            }
        }

        public async Task<string> CreateMultipleAssociations(JsonObject parameters)
        {
            try
            {
                    var associationsArray = Utils.Utils.GetArrayParam(parameters, "associations", "association_list", "associationList", "assocs");

                    if (associationsArray == null)
                    {
                        return JsonSerializer.Serialize(new {
                            error = "Missing required 'associations' array parameter. Provide a JSON array of association-spec objects.",
                            message = "To create multiple associations, you must provide an 'associations' array containing association objects",
                            required_parameters = new {
                                associations = new {
                                    type = "array",
                                    description = "Array of association objects to create",
                                    required = true,
                                    item_schema = new {
                                        name = new { type = "string", description = "Name of the association", required = true },
                                        parent = new { type = "string", description = "Name of the parent entity", required = true },
                                        child = new { type = "string", description = "Name of the child entity", required = true },
                                        type = new { type = "string", description = "Type of association", required = false, @default = "one-to-many" }
                                    }
                                }
                            },
                            example_usage = new {
                                tool_name = "create_multiple_associations",
                                parameters = new {
                                    associations = new[] {
                                        new {
                                            name = "Customer_Orders",
                                            parent = "Customer",
                                            child = "Order", 
                                            type = "one-to-many"
                                        }
                                    }
                                }
                            },
                            available_entities = new string[] { "Customer", "Order" },
                            guidance = "Each association object must have name, parent, and child properties. Ensure all referenced entities exist before creating associations."
                        });
                    }

                // Build requests
                var defaultModuleName = parameters["module_name"]?.ToString();
                var requests = new List<CreateAssociationRequest>();

                foreach (var assocNode in associationsArray)
                {
                    var assocObj = assocNode?.AsObject();
                    if (assocObj == null) continue;

                    var name = assocObj["name"]?.ToString();
                    var parent = assocObj["parent"]?.ToString();
                    var child = assocObj["child"]?.ToString();
                    var type = assocObj["type"]?.ToString() ?? "one-to-many";

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(child))
                        continue; // Skip invalid associations

                    var parentModuleName = assocObj["parent_module"]?.ToString() ?? defaultModuleName;
                    var childModuleName = assocObj["child_module"]?.ToString() ?? defaultModuleName;

                    var parentRef = ResolveEntityRef(parent, parentModuleName);
                    var childRef = ResolveEntityRef(child, childModuleName);

                    if (parentRef == null || childRef == null)
                        continue; // Skip if entities don't exist

                    var assocModuleName = defaultModuleName ?? parentRef.Value.QualifiedName.Split('.')[0];

                    var ownerStr = assocObj["owner"]?.ToString();
                    var owner = (!string.IsNullOrEmpty(ownerStr) && ownerStr.ToLowerInvariant().Trim() == "both")
                        ? AssociationOwner.Both
                        : AssociationOwner.Default;

                    requests.Add(new CreateAssociationRequest(
                        ModuleName: assocModuleName,
                        Name: name,
                        ParentEntityQualifiedName: parentRef.Value.QualifiedName,
                        ChildEntityQualifiedName: childRef.Value.QualifiedName,
                        Type: MapAssociationType(type),
                        ParentDeleteBehavior: MapDeleteBehavior(assocObj["parent_delete_behavior"]?.ToString()),
                        ChildDeleteBehavior: MapDeleteBehavior(assocObj["child_delete_behavior"]?.ToString()),
                        Owner: owner,
                        Documentation: assocObj["documentation"]?.ToString()));
                }

                var createdRefs = HostServices.DomainModel.CreateMultipleAssociations(requests);

                var createdAssociations = createdRefs.Select(r => new
                {
                    name = r.Name,
                    parent = r.ParentEntityQualifiedName,
                    child = r.ChildEntityQualifiedName,
                    type = r.Type == AssociationType.ReferenceSet ? "many-to-many" : "one-to-many"
                }).ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Successfully created {createdAssociations.Count} associations",
                    associations = createdAssociations
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating multiple associations");
                return JsonSerializer.Serialize(new { error = $"Failed to create associations: {ex.Message}" });
            }
        }

        public async Task<string> CreateDomainModelFromSchema(JsonObject parameters)
        {
            try
            {
                var schema = parameters["schema"]?.AsObject();
                if (schema == null)
                    return JsonSerializer.Serialize(new { error = "Schema object is required" });

                var moduleName = parameters["module_name"]?.ToString();
                if (string.IsNullOrWhiteSpace(moduleName))
                {
                    // Fall back to first module
                    var allModules = HostServices.Model.ListModules();
                    if (!allModules.Any())
                        return JsonSerializer.Serialize(new { error = "No modules found in the project" });
                    moduleName = allModules.First().Name;
                }
                else
                {
                    var mid = HostServices.Model.GetModuleByName(moduleName);
                    if (mid == null)
                        return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });
                }

                // Pass the schema JSON verbatim to the host — it owns validation
                var schemaJson = schema.ToJsonString();
                var createdRefs = HostServices.DomainModel.CreateDomainModelFromSchema(moduleName, schemaJson);

                // Trigger arrange via host
                bool arranged = false;
                try
                {
                    HostServices.DomainModel.ArrangeDomainModel(new ArrangeDomainModelRequest(moduleName));
                    arranged = true;
                }
                catch (Exception layoutEx)
                {
                    _logger.LogWarning(layoutEx, "Auto-arrange after schema creation failed (non-fatal)");
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Successfully created domain model with {createdRefs.Count} entities",
                    entities = createdRefs.Select(r => new { qualifiedName = r.QualifiedName }).ToList(),
                    auto_arranged = arranged
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating domain model from schema");
                return JsonSerializer.Serialize(new { error = $"Failed to create domain model: {ex.Message}" });
            }
        }

        public async Task<string> DeleteModelElement(JsonObject parameters)
        {
            try
            {
                var elementType = parameters["element_type"]?.ToString();
                var elementName = parameters["element_name"]?.ToString();
                var entityName = parameters["entity_name"]?.ToString() ?? elementName;
                var attributeName = parameters["attribute_name"]?.ToString();
                var associationName = parameters["association_name"]?.ToString();
                var documentName = parameters["document_name"]?.ToString() ?? elementName ?? entityName;
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(elementType))
                    return JsonSerializer.Serialize(new { error = "Element type is required" });

                switch (elementType.ToLower())
                {
                    case "entity":
                    {
                        if (string.IsNullOrEmpty(entityName))
                            return JsonSerializer.Serialize(new { error = "entity_name is required for entity deletion" });
                        var entityRef = ResolveEntityRef(entityName, moduleName);
                        if (entityRef == null)
                            return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });
                        HostServices.DomainModel.DeleteEntity(entityRef.Value);
                        _logger.LogInformation($"Deleted entity '{entityRef.Value.QualifiedName}'");
                        return JsonSerializer.Serialize(new { success = true, message = $"Entity '{entityRef.Value.QualifiedName}' and its owned associations deleted successfully" });
                    }

                    case "attribute":
                    {
                        if (string.IsNullOrEmpty(entityName) || string.IsNullOrEmpty(attributeName))
                            return JsonSerializer.Serialize(new { error = "entity_name and attribute_name are required for attribute deletion" });
                        var entityRef = ResolveEntityRef(entityName, moduleName);
                        if (entityRef == null)
                            return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });
                        var shape = HostServices.DomainModel.ReadEntity(entityRef.Value);
                        var attrRef = shape.Attributes.FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                        if (attrRef.Name == null)
                            return JsonSerializer.Serialize(new { error = $"Attribute '{attributeName}' not found on entity '{entityRef.Value.QualifiedName}'" });
                        HostServices.DomainModel.DeleteAttribute(entityRef.Value, attrRef);
                        _logger.LogInformation($"Deleted attribute '{attributeName}' from entity '{entityRef.Value.QualifiedName}'");
                        return JsonSerializer.Serialize(new { success = true, message = $"Attribute '{attributeName}' deleted successfully from entity '{entityRef.Value.QualifiedName}'" });
                    }

                    case "association":
                    {
                        if (string.IsNullOrEmpty(associationName))
                            return JsonSerializer.Serialize(new { error = "association_name is required for association deletion" });
                        var (assocRef, foundModuleName) = ResolveAssociationRef(associationName, moduleName);
                        if (assocRef == null)
                            return JsonSerializer.Serialize(new { error = $"Association '{associationName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });
                        HostServices.DomainModel.DeleteAssociation(assocRef.Value);
                        _logger.LogInformation($"Deleted association '{associationName}'");
                        return JsonSerializer.Serialize(new { success = true, message = $"Association '{associationName}' deleted successfully" });
                    }

                    case "module":
                        return JsonSerializer.Serialize(new
                        {
                            success = false,
                            escalation = "manual",
                            message = "Module deletion is not exposed on the Core Interop surface (IDomainModelHost.DeleteModule deferred). Delete the module in Studio Pro directly."
                        });

                    case "microflow":
                        return JsonSerializer.Serialize(new
                        {
                            error = "delete_model_element does not support microflow deletion. Use the delete_document tool instead.",
                            suggestion = "Call delete_document with: document_name='" + (documentName ?? "<microflow_name>") + "'" + (moduleName != null ? $", module_name='{moduleName}'" : "") + ", document_type='microflow'",
                            example = new { tool = "delete_document", document_name = documentName ?? "<microflow_name>", module_name = moduleName ?? "<module_name>", document_type = "microflow" }
                        });

                    case "nanoflow":
                        return JsonSerializer.Serialize(new
                        {
                            error = "Nanoflow deletion is not supported by the Extensions API.",
                            suggestion = "Nanoflows cannot be deleted programmatically via MCP tools. Delete the nanoflow manually in Studio Pro."
                        });

                    case "constant":
                        return JsonSerializer.Serialize(new
                        {
                            success = false,
                            escalation = "manual",
                            message = "Constant deletion is not exposed on the Core Interop surface. Delete the constant in Studio Pro directly, or use the delete_document tool if available."
                        });

                    case "enumeration":
                        return JsonSerializer.Serialize(new
                        {
                            success = false,
                            escalation = "manual",
                            message = "Enumeration deletion is not exposed on the Core Interop surface. Delete the enumeration in Studio Pro directly."
                        });

                    default:
                        return JsonSerializer.Serialize(new { error = $"Unknown deletion type: {elementType}. Supported: entity, attribute, association, microflow, module, constant, enumeration" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting model element");
                MendixAdditionalTools.SetLastError($"Failed to delete element: {ex.Message}", ex);
                return JsonSerializer.Serialize(new { error = $"Failed to delete element: {ex.Message}" });
            }
        }

        public async Task<string> DiagnoseAssociations(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();

                // Resolve the module(s) to inspect
                IReadOnlyList<ModuleId> moduleIds;
                if (!string.IsNullOrWhiteSpace(moduleName))
                {
                    var mid = HostServices.Model.GetModuleByName(moduleName);
                    if (mid == null)
                        return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });
                    moduleIds = new[] { mid.Value };
                }
                else
                {
                    moduleIds = HostServices.Model.ListModules();
                }

                var entityNames = new List<string>();
                var allAssociations = new List<object>();
                var seenAssocNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var moduleId in moduleIds)
                {
                    var entityRefs = HostServices.DomainModel.ListEntities(moduleId);
                    foreach (var entityRef in entityRefs)
                    {
                        entityNames.Add(entityRef.QualifiedName);
                        var shape = HostServices.DomainModel.ReadEntity(entityRef);

                        foreach (var assocRef in shape.OutgoingAssociations)
                        {
                            if (seenAssocNames.Add(assocRef.Name))
                            {
                                allAssociations.Add(new
                                {
                                    Name = assocRef.Name,
                                    Parent = assocRef.ParentEntityQualifiedName,
                                    Child = assocRef.ChildEntityQualifiedName,
                                    Type = assocRef.Type == AssociationType.ReferenceSet ? "ReferenceSet" : "Reference",
                                    MappedType = assocRef.Type == AssociationType.ReferenceSet ? "many-to-many" : "one-to-many"
                                });
                            }
                        }
                    }
                }

                var result = new
                {
                    entities = entityNames,
                    entityCount = entityNames.Count,
                    associations = allAssociations,
                    associationCount = allAssociations.Count,
                    status = "Domain model diagnosed successfully",
                    guidance = new
                    {
                        commonIssues = new[]
                        {
                            "Entities must exist before creating associations",
                            "Entity names are case sensitive",
                            "Don't use module prefixes in entity names",
                            "Association names must be unique",
                            "For one-to-many associations, parent is the 'one' side, child is the 'many' side"
                        },
                        properFormat = new
                        {
                            Name = "Customer_Orders",
                            Parent = "Customer",
                            Child = "Order",
                            Type = "one-to-many"
                        }
                    }
                };

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error diagnosing associations");
                return JsonSerializer.Serialize(new { error = "Failed to diagnose associations", details = ex.Message });
            }
        }

        public async Task<string> GetLastError(JsonObject parameters)
        {
            return JsonSerializer.Serialize(new { error = "GetLastError not implemented yet" });
        }

        public async Task<string> ListAvailableTools(JsonObject parameters)
        {
            var tools = new[]
            {
                "list_modules",
                "create_module",
                "read_domain_model",
                "read_project_info",
                "create_entity",
                "create_multiple_entities",
                "create_association",
                "create_multiple_associations",
                "create_domain_model_from_schema",
                "delete_model_element",
                "diagnose_associations",
                "set_entity_generalization",
                "remove_entity_generalization",
                "add_event_handler",
                "add_attribute",
                "set_calculated_attribute",
                "create_constant",
                "list_constants",
                "create_enumeration",
                "list_enumerations",
                "save_data",
                "generate_overview_pages",
                "list_microflows",
                "read_microflow_details",
                "create_microflow",
                "create_microflow_activities",
                "check_model",
                "check_project_errors",
                "get_studio_pro_logs",
                "get_last_error",
                "list_available_tools",
                "debug_info",
                "configure_system_attributes",
                "manage_folders",
                "validate_name",
                "copy_model_element",
                "list_java_actions",
                "read_runtime_settings",
                "set_runtime_settings",
                "read_configurations",
                "set_configuration",
                "read_version_control",
                "set_microflow_url",
                "list_rules",
                "exclude_document",
                "read_security_info",
                "read_entity_access_rules",
                "read_microflow_security",
                "audit_security",
                "read_nanoflow_details",
                "list_nanoflows",
                "list_scheduled_events",
                "list_rest_services",
                "query_model_elements",
                "rename_entity",
                "rename_attribute",
                "rename_association",
                "rename_document",
                "rename_module",
                "rename_enumeration_value",
                "update_attribute",
                "update_association",
                "update_constant",
                "update_enumeration",
                "set_documentation",
                "query_associations",
                "manage_navigation",
                "check_variable_name",
                "modify_microflow_activity",
                "insert_before_activity",
                "list_pages",
                "read_page_details",
                "list_workflows",
                "read_workflow_details",
                "delete_document",
                "sync_filesystem",
                "update_microflow",
                "read_attribute_details",
                "configure_constant_values",
                "generate_sample_data",
                "read_sample_data",
                "setup_data_import",
                "arrange_domain_model"
            };

            return JsonSerializer.Serialize(new { tools = tools, status = "success" });
        }

        /// <summary>
        /// Get dynamically available entity types based on template availability.
        /// Checks for template entities in the AIExtension module via HostServices.DomainModel.
        /// </summary>
        /// <returns>List of supported entity types in current project</returns>
        public List<string> GetAvailableEntityTypes()
        {
            var availableTypes = new List<string>
            {
                "persistent" // Always available
            };

            try
            {
                var aiExtensionModuleId = HostServices.Model.GetModuleByName("AIExtension");
                if (aiExtensionModuleId != null)
                {
                    var entityRefs = HostServices.DomainModel.ListEntities(aiExtensionModuleId.Value);
                    var entityNames = new HashSet<string>(
                        entityRefs.Select(e =>
                        {
                            var dotIdx = e.QualifiedName.IndexOf('.');
                            return dotIdx >= 0 ? e.QualifiedName.Substring(dotIdx + 1) : e.QualifiedName;
                        }),
                        StringComparer.OrdinalIgnoreCase);

                    if (entityNames.Contains("NPE") || entityNames.Contains("non-persistent"))
                        availableTypes.Add("non-persistent");
                    if (entityNames.Contains("FileDocument"))
                        availableTypes.Add("filedocument");
                    if (entityNames.Contains("Image"))
                        availableTypes.Add("image");
                    if (entityNames.Contains("StoreCreatedDate"))
                        availableTypes.Add("storecreateddate");
                    if (entityNames.Contains("StoreChangeDate"))
                        availableTypes.Add("storechangedate");
                    if (entityNames.Contains("StoreCreatedChangeDate"))
                        availableTypes.Add("storecreatedchangedate");
                    if (entityNames.Contains("StoreOwner"))
                        availableTypes.Add("storeowner");
                    if (entityNames.Contains("StoreChangeBy"))
                        availableTypes.Add("storechangeby");
                }
                else
                {
                    // AIExtension module absent — non-persistent entities are generally always available
                    availableTypes.Add("non-persistent");
                }

                _logger.LogInformation($"Available entity types: {string.Join(", ", availableTypes)}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking template availability, falling back to basic types");
                if (!availableTypes.Contains("non-persistent"))
                    availableTypes.Add("non-persistent");
            }

            return availableTypes;
        }

        /// <summary>
        /// Get detailed information about available entity types including descriptions
        /// </summary>
        /// <returns>Dictionary with entity type details</returns>
        public Dictionary<string, object> GetEntityTypeInfo()
        {
            var availableTypes = GetAvailableEntityTypes();
            var allDescriptions = new Dictionary<string, string>
            {
                { "persistent", "Standard entity stored in database (always available)" },
                { "non-persistent", "Session entity not stored in database (uses NPE template)" },
                { "filedocument", "Entity inheriting from System.FileDocument for file storage" },
                { "image", "Entity inheriting from System.Image for image storage" },
                { "storecreateddate", "Entity with automatic creation date tracking" },
                { "storechangedate", "Entity with automatic modification date tracking" },
                { "storecreatedchangedate", "Entity with both creation and modification date tracking" },
                { "storeowner", "Entity with automatic owner (creator) tracking" },
                { "storechangeby", "Entity with automatic last modifier tracking" }
            };

            var result = new Dictionary<string, object>
            {
                { "availableTypes", availableTypes },
                { "descriptions", availableTypes.ToDictionary(type => type, type => allDescriptions[type]) },
                { "unavailableTypes", allDescriptions.Keys.Except(availableTypes).ToList() },
                { "templateInstructions", "Unavailable types require corresponding templates in AIExtension module" }
            };

            return result;
        }

        #region HostServices Interop Helpers

        /// <summary>
        /// Resolves an entity by simple name (or "Module.Entity" qualified name) to an EntityRef
        /// using HostServices.DomainModel.ListEntities. Returns null if not found.
        /// </summary>
        private EntityRef? ResolveEntityRef(string entityName, string? moduleName)
        {
            // Handle qualified names like "ModuleName.EntityName"
            if (entityName.Contains('.') && string.IsNullOrWhiteSpace(moduleName))
            {
                var parts = entityName.Split('.', 2);
                moduleName = parts[0];
                entityName = parts[1];
            }

            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                var mid = HostServices.Model.GetModuleByName(moduleName);
                if (mid == null) return null;
                return HostServices.DomainModel.ListEntities(mid.Value)
                    .FirstOrDefault(e =>
                    {
                        var dot = e.QualifiedName.IndexOf('.');
                        var simpleName = dot >= 0 ? e.QualifiedName.Substring(dot + 1) : e.QualifiedName;
                        return simpleName.Equals(entityName, StringComparison.OrdinalIgnoreCase);
                    }) is EntityRef r && r.Id != Guid.Empty ? r : (EntityRef?)null;
            }

            // Search all modules
            foreach (var modId in HostServices.Model.ListModules())
            {
                foreach (var eRef in HostServices.DomainModel.ListEntities(modId))
                {
                    var dot = eRef.QualifiedName.IndexOf('.');
                    var simpleName = dot >= 0 ? eRef.QualifiedName.Substring(dot + 1) : eRef.QualifiedName;
                    if (simpleName.Equals(entityName, StringComparison.OrdinalIgnoreCase))
                        return eRef;
                }
            }
            return null;
        }

        /// <summary>
        /// Builds a list of AttributeSpec from a JSON array of attribute objects.
        /// Returns null (not an empty list) when the array is null or empty, matching
        /// CreateEntityRequest's nullable Attributes field.
        /// </summary>
        private IReadOnlyList<AttributeSpec>? BuildAttributeSpecs(JsonArray? attributesArray)
        {
            if (attributesArray == null || attributesArray.Count == 0)
                return null;

            var specs = new List<AttributeSpec>();
            foreach (var attrNode in attributesArray)
            {
                var attrObj = attrNode?.AsObject();
                if (attrObj == null) continue;

                var attrName = attrObj["name"]?.ToString();
                var attrType = attrObj["type"]?.ToString();
                if (string.IsNullOrEmpty(attrName) || string.IsNullOrEmpty(attrType)) continue;

                var kind = ParseAttributeKind(attrType);

                string? enumQualifiedName = null;
                IReadOnlyList<string>? enumValues = null;

                if (kind == AttributeKind.Enumeration)
                {
                    if (attrType.StartsWith("Enumeration:", StringComparison.OrdinalIgnoreCase))
                        enumQualifiedName = attrType.Substring("Enumeration:".Length).Trim();
                    enumQualifiedName ??= attrObj["enumeration_name"]?.ToString();
                    if (enumQualifiedName == null)
                    {
                        enumValues = attrObj["enumerationValues"]?.AsArray()
                            ?.Select(v => v?.ToString())
                            ?.Where(v => !string.IsNullOrEmpty(v))
                            ?.ToList()!;
                    }
                }

                int? maxLength = null;
                if (attrObj["max_length"]?.AsValue().TryGetValue<int>(out var ml) == true) maxLength = ml;

                bool? localizeDate = null;
                if (attrObj["localize_date"]?.AsValue().TryGetValue<bool>(out var ld) == true) localizeDate = ld;

                specs.Add(new AttributeSpec(
                    Name: attrName,
                    Kind: kind,
                    EnumerationQualifiedName: enumQualifiedName,
                    EnumerationValues: enumValues,
                    MaxLength: maxLength,
                    LocalizeDate: localizeDate,
                    DefaultValue: attrObj["default_value"]?.ToString(),
                    Documentation: attrObj["documentation"]?.ToString()));
            }

            return specs.Count > 0 ? specs : null;
        }

        private static AttributeKind ParseAttributeKind(string attrType)
        {
            var normalized = attrType.ToLowerInvariant().Trim();
            if (normalized.StartsWith("enumeration")) return AttributeKind.Enumeration;
            return normalized switch
            {
                "decimal" => AttributeKind.Decimal,
                "integer" or "int" => AttributeKind.Integer,
                "long" => AttributeKind.LongType,
                "string" => AttributeKind.String,
                "boolean" or "bool" => AttributeKind.Boolean,
                "datetime" => AttributeKind.DateTime,
                "autonumber" => AttributeKind.AutoNumber,
                "binary" => AttributeKind.Binary,
                "hashedstring" or "hashstring" => AttributeKind.HashString,
                _ => AttributeKind.String
            };
        }

        /// <summary>Maps a JSON string to the interop DeleteBehavior enum.</summary>
        private static DeleteBehavior MapDeleteBehavior(string? behavior)
        {
            if (string.IsNullOrEmpty(behavior))
                return DeleteBehavior.DeleteMeButKeepReferences;

            return behavior.ToLowerInvariant().Trim() switch
            {
                "delete_me_and_references" or "cascade" or "delete_me_too" or "delete_referencing"
                    => DeleteBehavior.DeleteMeAndReferences,
                "delete_me_if_no_references" or "prevent" or "keep_if_referenced"
                    => DeleteBehavior.DeleteMeIfNoReferences,
                _ => DeleteBehavior.DeleteMeButKeepReferences
            };
        }

        /// <summary>Formats the interop DeleteBehavior enum to a JSON-friendly string.</summary>
        private static string FormatDeleteBehavior(DeleteBehavior behavior)
        {
            return behavior switch
            {
                DeleteBehavior.DeleteMeAndReferences => "delete_me_and_references",
                DeleteBehavior.DeleteMeIfNoReferences => "delete_me_if_no_references",
                _ => "delete_me_but_keep_references"
            };
        }

        /// <summary>
        /// Resolves an <see cref="AssociationRef"/> by name (and optional module hint) via
        /// HostServices.DomainModel. Searches all outgoing associations across entities.
        /// Returns null when not found.
        /// </summary>
        private static AssociationType MapAssociationType(string type)
        {
            if (string.IsNullOrEmpty(type))
                return AssociationType.Reference;

            return type.ToLowerInvariant().Trim() switch
            {
                "one-to-many" or "reference" => AssociationType.Reference,
                "many-to-many" or "referenceset" or "reference_set" => AssociationType.ReferenceSet,
                _ => AssociationType.Reference
            };
        }

        private (AssociationRef? Ref, string? ModuleName) ResolveAssociationRef(string associationName, string? moduleName)
        {
            var moduleIds = string.IsNullOrEmpty(moduleName)
                ? HostServices.Model.ListModules()
                : HostServices.Model.GetModuleByName(moduleName) is ModuleId mid
                    ? new[] { mid }
                    : Array.Empty<ModuleId>();

            foreach (var modId in moduleIds)
            {
                foreach (var entityRef in HostServices.DomainModel.ListEntities(modId))
                {
                    var shape = HostServices.DomainModel.ReadEntity(entityRef);
                    var assoc = shape.OutgoingAssociations
                        .FirstOrDefault(a => a.Name.Equals(associationName, StringComparison.OrdinalIgnoreCase));
                    if (assoc.Name != null)
                        return (assoc, modId.Name);
                }
            }
            return (null, null);
        }

        #endregion

        #region Smart Domain Model Layout

        public async Task<string> ArrangeDomainModel(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters["module_name"]?.ToString();
                var rootEntity = parameters["root_entity"]?.ToString();
                if (string.IsNullOrEmpty(moduleName))
                    return JsonSerializer.Serialize(new { success = false, error = "module_name is required" });

                var request = new ArrangeDomainModelRequest(moduleName, rootEntity);
                HostServices.DomainModel.ArrangeDomainModel(request);

                _logger.LogInformation($"Arranged domain model for module '{moduleName}'");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Domain model for module '{moduleName}' arranged successfully",
                    module = moduleName,
                    rootEntity
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error arranging domain model");
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }


        #endregion


        #region Phase 9: Entity Configuration & Module Organization

        public async Task<string> ConfigureSystemAttributes(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });

                var moduleName = parameters["module_name"]?.ToString();
                var entityRef = ResolveEntityRef(entityName, moduleName);
                if (entityRef == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });

                // Read shape to verify it's a root entity (no generalization)
                var shape = HostServices.DomainModel.ReadEntity(entityRef.Value);
                if (!string.IsNullOrEmpty(shape.GeneralizationQualifiedName))
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' has a generalization (inherits from another entity). System attributes can only be configured on root entities." });

                bool? hasCreatedDate = null;
                bool? hasChangedDate = null;
                bool? hasOwner = null;
                bool? hasChangedBy = null;
                bool? persistable = null;

                bool changed = false;
                if (parameters.ContainsKey("has_created_date"))
                {
                    hasCreatedDate = parameters["has_created_date"]?.GetValue<bool>() ?? false;
                    changed = true;
                }
                if (parameters.ContainsKey("has_changed_date"))
                {
                    hasChangedDate = parameters["has_changed_date"]?.GetValue<bool>() ?? false;
                    changed = true;
                }
                if (parameters.ContainsKey("has_owner"))
                {
                    hasOwner = parameters["has_owner"]?.GetValue<bool>() ?? false;
                    changed = true;
                }
                if (parameters.ContainsKey("has_changed_by"))
                {
                    hasChangedBy = parameters["has_changed_by"]?.GetValue<bool>() ?? false;
                    changed = true;
                }
                if (parameters.ContainsKey("persistable"))
                {
                    persistable = parameters["persistable"]?.GetValue<bool>() ?? true;
                    changed = true;
                }

                if (!changed)
                    return JsonSerializer.Serialize(new { error = "No system attribute parameters provided. Use has_created_date, has_changed_date, has_owner, has_changed_by, or persistable." });

                HostServices.DomainModel.ConfigureSystemAttributes(
                    entityRef.Value,
                    hasCreatedDate: hasCreatedDate,
                    hasChangedDate: hasChangedDate,
                    hasOwner: hasOwner,
                    hasChangedBy: hasChangedBy,
                    persistable: persistable);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    entity = entityName,
                    hasCreatedDate,
                    hasChangedDate,
                    hasOwner,
                    hasChangedBy,
                    persistable
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error configuring system attributes");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ManageFolders(JsonObject parameters)
        {
            try
            {
                var action = parameters["action"]?.ToString()?.ToLowerInvariant();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(action))
                    return JsonSerializer.Serialize(new { error = "action is required: 'list', 'create', or 'move_document'" });

                if (string.IsNullOrEmpty(moduleName))
                    return JsonSerializer.Serialize(new { error = "module_name is required" });

                var moduleId = HostServices.Model.GetModuleByName(moduleName);
                if (moduleId == null)
                    return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });

                switch (action)
                {
                    case "list":
                    {
                        var folders = HostServices.Model.ListFolders(moduleId.Value);
                        var folderList = folders.Select(f => new { path = f.Path }).ToList();
                        return JsonSerializer.Serialize(new { success = true, module = moduleName, folders = folderList, count = folderList.Count });
                    }

                    case "create":
                    {
                        var folderName = parameters["folder_name"]?.ToString();
                        if (string.IsNullOrEmpty(folderName))
                            return JsonSerializer.Serialize(new { error = "folder_name is required for 'create' action" });

                        var parentFolderPath = parameters["parent_folder"]?.ToString() ?? "";

                        var newFolderId = HostServices.Model.CreateFolder(moduleId.Value, parentFolderPath, folderName);
                        if (newFolderId == null)
                            return JsonSerializer.Serialize(new { error = $"Failed to create folder '{folderName}'" + (!string.IsNullOrEmpty(parentFolderPath) ? $" under '{parentFolderPath}'" : "") });

                        return JsonSerializer.Serialize(new { success = true, folder = folderName, path = newFolderId.Value.Path, module = moduleName, parent = parentFolderPath == "" ? "(root)" : parentFolderPath });
                    }

                    case "move_document":
                    {
                        var documentQualifiedName = parameters["document_name"]?.ToString();
                        var targetFolderPath = parameters["target_folder"]?.ToString();
                        if (string.IsNullOrEmpty(documentQualifiedName))
                            return JsonSerializer.Serialize(new { error = "document_name is required for 'move_document' action (use qualified name 'Module.DocumentName')" });

                        // Resolve document
                        var qualifiedName = documentQualifiedName.Contains('.')
                            ? documentQualifiedName
                            : $"{moduleName}.{documentQualifiedName}";
                        var docId = HostServices.Model.GetDocumentByQualifiedName(qualifiedName);
                        if (docId == null)
                            return JsonSerializer.Serialize(new { error = $"Document '{qualifiedName}' not found" });

                        // Resolve target folder (null = move to module root)
                        FolderId? targetFolder = null;
                        if (!string.IsNullOrEmpty(targetFolderPath))
                        {
                            var folders = HostServices.Model.ListFolders(moduleId.Value);
                            targetFolder = folders.FirstOrDefault(f => f.Path.Equals(targetFolderPath, StringComparison.OrdinalIgnoreCase)) is FolderId fid && fid.Value != Guid.Empty
                                ? fid
                                : (FolderId?)null;
                            if (targetFolder == null)
                                return JsonSerializer.Serialize(new { error = $"Target folder '{targetFolderPath}' not found in module '{moduleName}'" });
                        }

                        var moved = HostServices.Model.MoveDocument(docId.Value, targetFolder);
                        if (!moved)
                            return JsonSerializer.Serialize(new { error = $"Failed to move document '{qualifiedName}'" });

                        return JsonSerializer.Serialize(new { success = true, document = qualifiedName, movedTo = targetFolderPath ?? "(module root)" });
                    }

                    default:
                        return JsonSerializer.Serialize(new { error = $"Unknown action '{action}'. Use 'list', 'create', or 'move_document'." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error managing folders");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ValidateName(JsonObject parameters)
        {
            try
            {
                var name = parameters["name"]?.ToString();
                if (string.IsNullOrEmpty(name))
                    return JsonSerializer.Serialize(new { error = "name is required" });

                var autoFix = parameters["auto_fix"]?.GetValue<bool>() ?? false;
                var result = HostServices.DomainModel.ValidateName(name, autoFix);

                if (result == null)
                    return JsonSerializer.Serialize(new { error = "INameValidationService is not available on the active host" });

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    name,
                    isValid = result.IsValid,
                    errorMessage = result.IsValid ? null : result.ErrorMessage,
                    fixedName = result.SuggestedFix
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating name");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> CopyModelElement(JsonObject parameters)
        {
            try
            {
                var elementType = parameters["element_type"]?.ToString()?.ToLowerInvariant();
                var sourceName = parameters["source_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();
                var sourceModuleName = parameters["source_module"]?.ToString();
                var targetModuleName = parameters["target_module"]?.ToString();

                if (string.IsNullOrEmpty(elementType))
                    return JsonSerializer.Serialize(new { error = "element_type is required: 'entity', 'microflow', 'constant', 'enumeration'" });
                if (string.IsNullOrEmpty(sourceName))
                    return JsonSerializer.Serialize(new { error = "source_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                // BUG-013 fix: default target to source when not supplied
                var effectiveTargetModule = !string.IsNullOrWhiteSpace(targetModuleName) ? targetModuleName : sourceModuleName;

                var request = new CopyRequest(
                    ElementType: elementType,
                    SourceName: sourceName,
                    SourceModuleName: sourceModuleName,
                    TargetModuleName: effectiveTargetModule,
                    NewName: newName);

                var result = HostServices.DomainModel.CopyElement(request);

                if (!result.Success)
                    return JsonSerializer.Serialize(new { error = result.Error ?? $"Failed to copy {elementType} '{sourceName}'" });

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    elementType,
                    source = sourceName,
                    copy = newName,
                    targetQualifiedName = result.TargetQualifiedName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error copying model element");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 13: Rename & Refactor

        public async Task<string> RenameEntity(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                // Validate new name via HostServices
                var validation = HostServices.DomainModel.ValidateName(newName);
                if (validation != null && !validation.IsValid)
                    return JsonSerializer.Serialize(new { error = $"Invalid name '{newName}': {validation.ErrorMessage}" });

                var entityRef = ResolveEntityRef(entityName, moduleName);
                if (entityRef == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var oldQualifiedName = entityRef.Value.QualifiedName;
                HostServices.DomainModel.RenameEntity(entityRef.Value, newName);

                var newModuleName = oldQualifiedName.Contains('.') ? oldQualifiedName.Split('.')[0] : moduleName ?? "";
                _logger.LogInformation($"Renamed entity '{entityName}' to '{newName}'");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Entity renamed from '{entityName}' to '{newName}'",
                    module = newModuleName,
                    oldName = entityName,
                    newName,
                    qualifiedName = $"{newModuleName}.{newName}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming entity");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> RenameAttribute(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var attributeName = parameters["attribute_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });
                if (string.IsNullOrEmpty(attributeName))
                    return JsonSerializer.Serialize(new { error = "attribute_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                var validation = HostServices.DomainModel.ValidateName(newName);
                if (validation != null && !validation.IsValid)
                    return JsonSerializer.Serialize(new { error = $"Invalid name '{newName}': {validation.ErrorMessage}" });

                var entityRef = ResolveEntityRef(entityName, moduleName);
                if (entityRef == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var shape = HostServices.DomainModel.ReadEntity(entityRef.Value);
                var attrRef = shape.Attributes.FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                if (attrRef.Name == null)
                    return JsonSerializer.Serialize(new { error = $"Attribute '{attributeName}' not found on entity '{entityRef.Value.QualifiedName}'" });

                HostServices.DomainModel.RenameAttribute(entityRef.Value, attrRef, newName);

                _logger.LogInformation($"Renamed attribute '{attributeName}' to '{newName}' on entity '{entityRef.Value.QualifiedName}'");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Attribute renamed from '{attributeName}' to '{newName}' on entity '{entityRef.Value.QualifiedName}'",
                    entity = entityRef.Value.QualifiedName,
                    oldName = attributeName,
                    newName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming attribute");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> RenameAssociation(JsonObject parameters)
        {
            try
            {
                var associationName = parameters["association_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(associationName))
                    return JsonSerializer.Serialize(new { error = "association_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                var validation = HostServices.DomainModel.ValidateName(newName);
                if (validation != null && !validation.IsValid)
                    return JsonSerializer.Serialize(new { error = $"Invalid name '{newName}': {validation.ErrorMessage}" });

                var (assocRef, foundModuleName) = ResolveAssociationRef(associationName, moduleName);
                if (assocRef == null)
                    return JsonSerializer.Serialize(new { error = $"Association '{associationName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                HostServices.DomainModel.RenameAssociation(assocRef.Value, newName);

                _logger.LogInformation($"Renamed association '{associationName}' to '{newName}'");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Association renamed from '{associationName}' to '{newName}'",
                    module = foundModuleName,
                    oldName = associationName,
                    newName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming association");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> RenameDocument(JsonObject parameters)
        {
            try
            {
                var documentName = parameters["document_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();
                var documentType = parameters["document_type"]?.ToString()?.ToLowerInvariant();

                if (string.IsNullOrEmpty(documentName))
                    return JsonSerializer.Serialize(new { error = "document_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                var validation = HostServices.DomainModel.ValidateName(newName);
                if (validation != null && !validation.IsValid)
                    return JsonSerializer.Serialize(new { error = $"Invalid name '{newName}': {validation.ErrorMessage}" });

                // Normalize: handle qualified name (Module.DocumentName)
                string? effectiveModule = moduleName;
                string effectiveDocName = documentName;
                if (documentName.Contains('.') && effectiveModule == null)
                {
                    var parts = documentName.Split('.', 2);
                    effectiveModule = parts[0];
                    effectiveDocName = parts[1];
                }

                // Build qualified name for lookup
                DocumentId? docId = null;
                string? resolvedModule = null;

                if (!string.IsNullOrEmpty(effectiveModule))
                {
                    // Try direct qualified lookup first
                    var qualifiedName = $"{effectiveModule}.{effectiveDocName}";
                    docId = HostServices.Model.GetDocumentByQualifiedName(qualifiedName);
                    resolvedModule = effectiveModule;
                }

                if (docId == null)
                {
                    // Search across modules via ListAllDocuments
                    var typeFilter = documentType switch
                    {
                        "microflow" => "microflow",
                        "constant" => "constant",
                        "enumeration" => "enumeration",
                        _ => null
                    };
                    var allDocs = HostServices.Model.ListAllDocuments(typeFilter);
                    var match = allDocs.FirstOrDefault(d =>
                    {
                        var parts = d.QualifiedName.Split('.', 2);
                        var docLocalName = parts.Length == 2 ? parts[1] : d.QualifiedName;
                        var docMod = parts.Length == 2 ? parts[0] : null;
                        bool nameMatch = docLocalName.Equals(effectiveDocName, StringComparison.OrdinalIgnoreCase);
                        bool moduleMatch = effectiveModule == null || (docMod != null && docMod.Equals(effectiveModule, StringComparison.OrdinalIgnoreCase));
                        return nameMatch && moduleMatch;
                    });
                    if (match.QualifiedName != null)
                    {
                        docId = match;
                        var mp = match.QualifiedName.Split('.', 2);
                        resolvedModule = mp.Length == 2 ? mp[0] : effectiveModule;
                    }
                }

                if (docId == null)
                    return JsonSerializer.Serialize(new { error = $"Document '{documentName}'{(documentType != null ? $" (type: {documentType})" : "")} not found{(effectiveModule != null ? $" in module '{effectiveModule}'" : "")}" });

                HostServices.DomainModel.RenameDocument(docId.Value, newName);

                _logger.LogInformation($"Renamed document '{documentName}' to '{newName}'");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Document renamed from '{effectiveDocName}' to '{newName}' (all by-name references updated)",
                    module = resolvedModule,
                    oldName = effectiveDocName,
                    newName,
                    qualifiedName = $"{resolvedModule}.{newName}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming document");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> RenameModule(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters["module_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();

                if (string.IsNullOrEmpty(moduleName))
                    return JsonSerializer.Serialize(new { error = "module_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                var validation = HostServices.DomainModel.ValidateName(newName);
                if (validation != null && !validation.IsValid)
                    return JsonSerializer.Serialize(new { error = $"Invalid name '{newName}': {validation.ErrorMessage}" });

                var moduleId = HostServices.Model.GetModuleByName(moduleName);
                if (moduleId == null)
                    return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });

                HostServices.DomainModel.RenameModule(moduleId.Value, newName);

                _logger.LogInformation($"Renamed module '{moduleName}' to '{newName}' (all qualified references updated)");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Module renamed from '{moduleName}' to '{newName}' (all qualified references updated)",
                    oldName = moduleName,
                    newName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming module");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> RenameEnumerationValue(JsonObject parameters)
        {
            try
            {
                var enumerationName = parameters["enumeration_name"]?.ToString();
                var valueName = parameters["value_name"]?.ToString();
                var newName = parameters["new_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(enumerationName))
                    return JsonSerializer.Serialize(new { error = "enumeration_name is required" });
                if (string.IsNullOrEmpty(valueName))
                    return JsonSerializer.Serialize(new { error = "value_name is required" });
                if (string.IsNullOrEmpty(newName))
                    return JsonSerializer.Serialize(new { error = "new_name is required" });

                var validation = HostServices.DomainModel.ValidateName(newName);
                if (validation != null && !validation.IsValid)
                    return JsonSerializer.Serialize(new { error = $"Invalid name '{newName}': {validation.ErrorMessage}" });

                // Build the qualified enumeration name for the host call
                string qualifiedEnumName;
                if (enumerationName.Contains('.'))
                {
                    qualifiedEnumName = enumerationName;
                }
                else if (!string.IsNullOrEmpty(moduleName))
                {
                    qualifiedEnumName = $"{moduleName}.{enumerationName}";
                }
                else
                {
                    // Search all modules to find the enumeration's qualified name
                    var allDocs = HostServices.Model.ListAllDocuments("enumeration");
                    var match = allDocs.FirstOrDefault(d =>
                    {
                        var parts = d.QualifiedName.Split('.', 2);
                        var localName = parts.Length == 2 ? parts[1] : d.QualifiedName;
                        return localName.Equals(enumerationName, StringComparison.OrdinalIgnoreCase);
                    });
                    if (match.QualifiedName == null)
                        return JsonSerializer.Serialize(new { error = $"Enumeration '{enumerationName}' not found" });
                    qualifiedEnumName = match.QualifiedName;
                }

                HostServices.DomainModel.RenameEnumerationValue(qualifiedEnumName, valueName, newName);

                _logger.LogInformation($"Renamed enumeration value '{valueName}' to '{newName}' in '{qualifiedEnumName}'");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Enumeration value renamed from '{valueName}' to '{newName}' in '{qualifiedEnumName}'",
                    enumeration = qualifiedEnumName,
                    oldName = valueName,
                    newName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming enumeration value");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 14: Modify Existing Elements

        public async Task<string> UpdateAttribute(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var attributeName = parameters["attribute_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(entityName))
                    return JsonSerializer.Serialize(new { error = "entity_name is required" });
                if (string.IsNullOrEmpty(attributeName))
                    return JsonSerializer.Serialize(new { error = "attribute_name is required" });

                var entityRef = ResolveEntityRef(entityName, moduleName);
                if (entityRef == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var shape = HostServices.DomainModel.ReadEntity(entityRef.Value);
                var attrRef = shape.Attributes.FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                if (attrRef.Name == null)
                    return JsonSerializer.Serialize(new { error = $"Attribute '{attributeName}' not found on entity '{entityName}'" });

                var changes = new List<string>();

                // Determine new type if specified
                var newTypeStr = parameters["type"]?.ToString();
                AttributeKind? newKind = null;
                string? enumQualifiedName = null;
                if (!string.IsNullOrEmpty(newTypeStr))
                {
                    if (newTypeStr.StartsWith("enumeration:", StringComparison.OrdinalIgnoreCase))
                    {
                        newKind = AttributeKind.Enumeration;
                        enumQualifiedName = newTypeStr.Substring("enumeration:".Length).Trim();
                        changes.Add($"type → Enumeration:{enumQualifiedName}");
                    }
                    else
                    {
                        newKind = ParseAttributeKind(newTypeStr);
                        changes.Add($"type → {newTypeStr}");
                    }
                }

                int? maxLength = null;
                if (parameters["max_length"] != null)
                {
                    maxLength = parameters["max_length"]!.GetValue<int>();
                    changes.Add($"max_length → {maxLength}");
                }

                bool? localizeDate = null;
                if (parameters["localize_date"] != null)
                {
                    localizeDate = parameters["localize_date"]!.GetValue<bool>();
                    changes.Add($"localize_date → {localizeDate}");
                }

                var defaultValue = parameters["default_value"]?.ToString();
                if (defaultValue != null)
                    changes.Add($"default_value → '{defaultValue}'");

                var documentation = parameters["documentation"]?.ToString();
                if (documentation != null)
                    changes.Add("documentation updated");

                if (changes.Count == 0)
                    return JsonSerializer.Serialize(new { error = "No changes specified. Provide at least one of: type, max_length, localize_date, default_value, documentation" });

                // Build a newSpec using the current attribute as baseline, overlaying changes
                var newSpec = new AttributeSpec(
                    Name: attrRef.Name,
                    Kind: newKind ?? attrRef.Kind,
                    EnumerationQualifiedName: enumQualifiedName,
                    EnumerationValues: null,
                    MaxLength: maxLength,
                    LocalizeDate: localizeDate,
                    DefaultValue: defaultValue,
                    Documentation: documentation);

                HostServices.DomainModel.UpdateAttribute(entityRef.Value, attrRef, newSpec);

                _logger.LogInformation($"Updated attribute '{attributeName}' on '{entityName}': {string.Join(", ", changes)}");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Attribute '{attributeName}' updated on entity '{entityName}'",
                    entity = entityName,
                    attribute = attributeName,
                    module = shape.ModuleName,
                    changes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating attribute");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> UpdateAssociation(JsonObject parameters)
        {
            try
            {
                var associationName = parameters["association_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(associationName))
                    return JsonSerializer.Serialize(new { error = "association_name is required" });

                var (assocRef, foundModuleName) = ResolveAssociationRef(associationName, moduleName);
                if (assocRef == null)
                    return JsonSerializer.Serialize(new { error = $"Association '{associationName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var changes = new List<string>();

                // Parse optional fields
                AssociationType? newType = null;
                var typeStr = parameters["type"]?.ToString()?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(typeStr))
                {
                    newType = typeStr switch
                    {
                        "reference" or "one-to-many" or "1:n" => AssociationType.Reference,
                        "referenceset" or "reference_set" or "many-to-many" or "n:m" => AssociationType.ReferenceSet,
                        _ => null
                    };
                    if (newType == null)
                        return JsonSerializer.Serialize(new { error = $"Invalid type '{typeStr}'. Use 'reference' or 'referenceset'." });
                    changes.Add($"type → {typeStr}");
                }

                AssociationOwner? newOwner = null;
                var ownerStr = parameters["owner"]?.ToString()?.ToLowerInvariant();
                if (!string.IsNullOrEmpty(ownerStr))
                {
                    newOwner = ownerStr switch
                    {
                        "default" or "parent" or "one" => AssociationOwner.Default,
                        "both" => AssociationOwner.Both,
                        _ => null
                    };
                    if (newOwner == null)
                        return JsonSerializer.Serialize(new { error = $"Invalid owner '{ownerStr}'. Use 'default' (one owner) or 'both'." });
                    changes.Add($"owner → {ownerStr}");
                }

                DeleteBehavior? newParentDeleteBehavior = null;
                var parentDeleteStr = parameters["parent_delete_behavior"]?.ToString();
                if (!string.IsNullOrEmpty(parentDeleteStr))
                {
                    newParentDeleteBehavior = MapDeleteBehavior(parentDeleteStr);
                    changes.Add($"parent_delete_behavior → {parentDeleteStr}");
                }

                DeleteBehavior? newChildDeleteBehavior = null;
                var childDeleteStr = parameters["child_delete_behavior"]?.ToString();
                if (!string.IsNullOrEmpty(childDeleteStr))
                {
                    newChildDeleteBehavior = MapDeleteBehavior(childDeleteStr);
                    changes.Add($"child_delete_behavior → {childDeleteStr}");
                }

                var newDocumentation = parameters["documentation"]?.ToString();
                if (newDocumentation != null)
                    changes.Add("documentation updated");

                if (changes.Count == 0)
                    return JsonSerializer.Serialize(new { error = "No changes specified. Provide at least one of: owner, type, parent_delete_behavior, child_delete_behavior, documentation" });

                HostServices.DomainModel.UpdateAssociation(
                    assocRef.Value,
                    newType: newType,
                    newParentDeleteBehavior: newParentDeleteBehavior,
                    newChildDeleteBehavior: newChildDeleteBehavior,
                    newOwner: newOwner,
                    newDocumentation: newDocumentation);

                _logger.LogInformation($"Updated association '{associationName}': {string.Join(", ", changes)}");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Association '{associationName}' updated",
                    association = associationName,
                    module = foundModuleName,
                    changes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating association");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public Task<string> UpdateConstant(JsonObject parameters)
        {
            // escalation:manual — typed IConstant write is not exposed on the Core Interop surface.
            // IModelHost / IDomainModelHost have no constant-update method.
            // Edit the constant value in Studio Pro directly.
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success = false,
                escalation = "manual",
                message = "UpdateConstant requires typed IConstant write which is not exposed on Core Interop; edit in Studio Pro."
            }));
        }

        public async Task<string> UpdateEnumeration(JsonObject parameters)
        {
            try
            {
                var enumerationName = parameters["enumeration_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(enumerationName))
                    return JsonSerializer.Serialize(new { error = "enumeration_name is required" });

                // Handle qualified name
                if (enumerationName.Contains('.') && moduleName == null)
                {
                    var parts = enumerationName.Split('.', 2);
                    moduleName = parts[0];
                    enumerationName = parts[1];
                }

                // Resolve the EnumerationRef via HostServices
                EnumerationRef? foundEnumRef = null;
                ModuleId foundModuleId = default;
                var moduleIds = string.IsNullOrEmpty(moduleName)
                    ? HostServices.Model.ListModules()
                    : HostServices.Model.GetModuleByName(moduleName) is ModuleId mid
                        ? new[] { mid }
                        : Array.Empty<ModuleId>();

                foreach (var modId in moduleIds)
                {
                    var candidate = HostServices.DomainModel.ListEnumerations(modId)
                        .FirstOrDefault(e =>
                        {
                            var dot = e.QualifiedName.LastIndexOf('.');
                            var simpleName = dot >= 0 ? e.QualifiedName.Substring(dot + 1) : e.QualifiedName;
                            return simpleName.Equals(enumerationName, StringComparison.OrdinalIgnoreCase);
                        });
                    if (candidate.QualifiedName != null) { foundEnumRef = candidate; foundModuleId = modId; break; }
                }

                if (foundEnumRef == null)
                    return JsonSerializer.Serialize(new { error = $"Enumeration '{enumerationName}' not found{(moduleName != null ? $" in module '{moduleName}'" : "")}" });

                var changes = new List<string>();

                // Parse add_values
                List<EnumerationValueSpec>? addValues = null;
                var addValuesNode = parameters["add_values"];
                if (addValuesNode is JsonArray addArray && addArray.Count > 0)
                {
                    addValues = addArray
                        .Select(v => v?.ToString())
                        .Where(v => !string.IsNullOrEmpty(v))
                        .Select(v => new EnumerationValueSpec(v!, null))
                        .ToList();
                    foreach (var v in addValues) changes.Add($"added '{v.Name}'");
                }

                // Parse remove_values
                List<string>? removeValues = null;
                var removeValuesNode = parameters["remove_values"];
                if (removeValuesNode is JsonArray removeArray && removeArray.Count > 0)
                {
                    removeValues = removeArray
                        .Select(v => v?.ToString())
                        .Where(v => !string.IsNullOrEmpty(v))
                        .ToList()!;
                    foreach (var v in removeValues) changes.Add($"removed '{v}'");
                }

                // Parse rename_values: { "OldName": "NewName" }
                Dictionary<string, string>? renameValues = null;
                var renameNode = parameters["rename_values"]?.AsObject();
                if (renameNode != null)
                {
                    renameValues = new Dictionary<string, string>();
                    foreach (var kv in renameNode)
                    {
                        if (kv.Value?.ToString() is string newValName)
                        {
                            renameValues[kv.Key] = newValName;
                            changes.Add($"renamed '{kv.Key}' → '{newValName}'");
                        }
                    }
                }

                if (changes.Count == 0)
                    return JsonSerializer.Serialize(new { error = "No changes specified. Provide at least one of: add_values, remove_values, rename_values" });

                var skipped = new List<object>();
                var updated = Utils.Utils.TryPerModule<bool>(
                    foundModuleId,
                    () =>
                    {
                        HostServices.DomainModel.UpdateEnumeration(
                            foundEnumRef.Value,
                            addValues: addValues,
                            removeValues: removeValues,
                            renameValues: renameValues);
                        return true;
                    },
                    skipped, "UpdateEnumeration", _logger);

                if (updated != true)
                {
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        error = $"Module '{foundModuleId.Name}' is not queryable on this Studio Pro version; ModuleProxy not registered. Try a different module or edit the enumeration via the Studio Pro UI.",
                        details = skipped,
                    });
                }

                _logger.LogInformation($"Updated enumeration '{enumerationName}': {string.Join(", ", changes)}");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Enumeration '{enumerationName}' updated",
                    enumeration = enumerationName,
                    changes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating enumeration");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> SetDocumentation(JsonObject parameters)
        {
            try
            {
                var elementType = parameters["element_type"]?.ToString()?.ToLowerInvariant();
                var elementName = parameters["element_name"]?.ToString();
                var documentation = parameters["documentation"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(elementType))
                    return JsonSerializer.Serialize(new { error = "element_type is required: 'entity', 'attribute', 'association', 'domain_model'" });
                if (string.IsNullOrEmpty(elementName) && elementType != "domain_model")
                    return JsonSerializer.Serialize(new { error = "element_name is required" });
                if (documentation == null)
                    return JsonSerializer.Serialize(new { error = "documentation is required (use empty string to clear)" });

                switch (elementType)
                {
                    case "entity":
                    {
                        var entityRef = ResolveEntityRef(elementName!, moduleName);
                        if (entityRef == null)
                            return JsonSerializer.Serialize(new { error = $"Entity '{elementName}' not found" });
                        var shape = HostServices.DomainModel.ReadEntity(entityRef.Value);
                        HostServices.DomainModel.SetEntityDocumentation(entityRef.Value, documentation);
                        return JsonSerializer.Serialize(new { success = true, message = $"Documentation set on entity '{elementName}'", elementType, elementName, module = shape.ModuleName });
                    }
                    case "attribute":
                    {
                        var entityName = parameters["entity_name"]?.ToString() ?? elementName!;
                        var attrName = parameters["attribute_name"]?.ToString();
                        if (string.IsNullOrEmpty(attrName))
                        {
                            if (elementName!.Contains('.'))
                            {
                                var parts = elementName.Split('.', 2);
                                entityName = parts[0];
                                attrName = parts[1];
                            }
                            else
                            {
                                return JsonSerializer.Serialize(new { error = "attribute_name is required for attribute documentation (or use element_name as 'Entity.Attribute')" });
                            }
                        }
                        var entityRef = ResolveEntityRef(entityName, moduleName);
                        if (entityRef == null)
                            return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });
                        var shape = HostServices.DomainModel.ReadEntity(entityRef.Value);
                        var attrRef = shape.Attributes.FirstOrDefault(a => a.Name.Equals(attrName, StringComparison.OrdinalIgnoreCase));
                        if (attrRef.Name == null)
                            return JsonSerializer.Serialize(new { error = $"Attribute '{attrName}' not found on entity '{entityName}'" });
                        HostServices.DomainModel.SetAttributeDocumentation(entityRef.Value, attrRef, documentation);
                        return JsonSerializer.Serialize(new { success = true, message = $"Documentation set on attribute '{attrName}' of entity '{entityName}'", elementType, entity = entityName, attribute = attrName, module = shape.ModuleName });
                    }
                    case "association":
                    {
                        var (assocRef, foundModuleName) = ResolveAssociationRef(elementName!, moduleName);
                        if (assocRef == null)
                            return JsonSerializer.Serialize(new { error = $"Association '{elementName}' not found" });
                        HostServices.DomainModel.SetAssociationDocumentation(assocRef.Value, documentation);
                        return JsonSerializer.Serialize(new { success = true, message = $"Documentation set on association '{elementName}'", elementType, association = elementName, module = foundModuleName });
                    }
                    case "domain_model":
                    {
                        if (string.IsNullOrEmpty(moduleName))
                            return JsonSerializer.Serialize(new { error = "module_name is required for domain_model documentation" });
                        var moduleId = HostServices.Model.GetModuleByName(moduleName);
                        if (moduleId == null)
                            return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });
                        HostServices.DomainModel.SetDomainModelDocumentation(moduleId.Value, documentation);
                        return JsonSerializer.Serialize(new { success = true, message = $"Documentation set on domain model of module '{moduleName}'", elementType, module = moduleName });
                    }
                    default:
                        return JsonSerializer.Serialize(new { error = $"Unknown element_type '{elementType}'. Supported: entity, attribute, association, domain_model" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting documentation");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 15: Cross-Module Association Queries

        public async Task<string> QueryAssociations(JsonObject parameters)
        {
            try
            {
                var entityName = parameters["entity_name"]?.ToString();
                var secondEntity = parameters["second_entity"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();
                var direction = parameters["direction"]?.ToString()?.ToLowerInvariant() ?? "both";

                // Resolve qualified names for the entity filter arguments
                string? entityQn = null;
                if (!string.IsNullOrEmpty(entityName))
                {
                    entityQn = ResolveEntityQualifiedName(entityName, moduleName);
                    if (entityQn == null)
                        return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });
                }

                string? secondEntityQn = null;
                if (!string.IsNullOrEmpty(secondEntity))
                {
                    secondEntityQn = ResolveEntityQualifiedName(secondEntity, null);
                    if (secondEntityQn == null)
                        return JsonSerializer.Serialize(new { error = $"Entity '{secondEntity}' not found" });
                }

                // Delegate to IDomainModelHost.QueryAssociations — it handles all filter combinations
                var items = HostServices.DomainModel.QueryAssociations(
                    entityQualifiedName: entityQn,
                    secondEntityQualifiedName: secondEntityQn,
                    moduleName: moduleName,
                    direction: direction);

                var associations = items.Select(a => new
                {
                    name = a.Name,
                    parent = a.ParentEntityQualifiedName,
                    child = a.ChildEntityQualifiedName,
                    type = a.Type == AssociationType.ReferenceSet ? "many-to-many" : "one-to-many"
                }).ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    count = associations.Count,
                    query = new { entityName, secondEntity, moduleName, direction },
                    associations
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying associations");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Resolves a simple entity name (or already-qualified name) to its fully qualified name
        /// by searching across all modules (optionally scoped to a specific module).
        /// Returns null if not found.
        /// </summary>
        private string? ResolveEntityQualifiedName(string entityName, string? preferredModule)
        {
            // Already qualified?
            if (entityName.Contains('.'))
                return entityName;

            var allModuleIds = HostServices.Model.ListModules();
            if (!string.IsNullOrEmpty(preferredModule))
            {
                var preferred = HostServices.Model.GetModuleByName(preferredModule);
                if (preferred != null)
                {
                    var hit = HostServices.DomainModel.ListEntities(preferred.Value)
                        .FirstOrDefault(e => e.QualifiedName.EndsWith("." + entityName, StringComparison.OrdinalIgnoreCase));
                    if (hit.QualifiedName != null)
                        return hit.QualifiedName;
                }
            }

            foreach (var moduleId in allModuleIds)
            {
                var hit = HostServices.DomainModel.ListEntities(moduleId)
                    .FirstOrDefault(e => e.QualifiedName.EndsWith("." + entityName, StringComparison.OrdinalIgnoreCase));
                if (hit.QualifiedName != null)
                    return hit.QualifiedName;
            }

            return null;
        }

        #endregion
    }

    public class Association
    {
        public string Name { get; set; }
        public string Parent { get; set; }
        public string Child { get; set; }
        public string Type { get; set; }
        public string ParentDeleteBehavior { get; set; }
        public string ChildDeleteBehavior { get; set; }
        public string Owner { get; set; }
    }
}
