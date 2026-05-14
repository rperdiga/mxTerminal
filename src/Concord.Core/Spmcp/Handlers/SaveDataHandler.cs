using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Terminal.Interop;
using Terminal.Spmcp.Core;

namespace Terminal.Spmcp.Handlers
{
    public class SaveDataHandler : BaseApiHandler
    {
        public override string Path => "/api/data/save";
        public override string Method => "POST";

        public class SaveDataRequest
        {
            public Dictionary<string, JsonElement> data { get; set; } = new();
        }

        public SaveDataHandler() : base() { }

        public override async Task HandleAsync(HttpContext context)
        {
            await ExecuteInTransactionAsync(
                context,
                "save data",
                async () =>
                {
                    var request = await DeserializeRequestBodyAsync<SaveDataRequest>(context);

                    if (request?.data == null)
                    {
                        return (
                            success: false,
                            message: "Invalid request format or empty data. Ensure the request body is a JSON object with a 'data' property containing a dictionary of entity names and their respective data arrays.",
                            data: null as object
                        );
                    }

                    var validationResult = ValidateData(request.data);
                    if (!validationResult.IsValid)
                    {
                        return (
                            success: false,
                            message: validationResult.Message,
                            data: null as object
                        );
                    }

                    await SaveSampleData(request);

                    return (
                        success: true,
                        message: "Data validated and saved successfully",
                        data: request as object
                    );
                });
        }

        private (bool IsValid, string Message) ValidateData(Dictionary<string, JsonElement> data)
        {
            try
            {
                var model = HostServices.Model;
                var domainModel = HostServices.DomainModel;

                var module = model.ListModules().FirstOrDefault(m =>
                    !m.Name.StartsWith("System", StringComparison.OrdinalIgnoreCase) &&
                    !m.Name.StartsWith("AppStore", StringComparison.OrdinalIgnoreCase));

                if (module == default)
                {
                    return (false, "No domain model found.");
                }

                var entities = domainModel.ListEntities(module);

                foreach (var entityData in data)
                {
                    var entityName = entityData.Key.Split('.').Last();
                    var entity = entities.FirstOrDefault(e =>
                        e.QualifiedName.Split('.').Last().Equals(entityName, StringComparison.OrdinalIgnoreCase));

                    if (entity == default)
                    {
                        return (false, $"Entity {entityName} not found in domain model.");
                    }

                    if (entityData.Value.ValueKind != JsonValueKind.Array)
                    {
                        return (false, $"Data for entity {entityName} must be an array.");
                    }

                    var shape = domainModel.ReadEntity(entity);
                    var records = entityData.Value.EnumerateArray();
                    foreach (var record in records)
                    {
                        if (record.ValueKind != JsonValueKind.Object)
                        {
                            return (false, $"Each record in {entityName} must be an object.");
                        }

                        // Warn (not fail) for missing attributes
                        foreach (var attribute in shape.Attributes)
                        {
                            if (!record.TryGetProperty(attribute.Name, out _))
                            {
                                Console.WriteLine($"Warning: Attribute {attribute.Name} missing in {entityName}.");
                            }
                        }

                        // VirtualId required when associations exist
                        var hasAssociations = shape.OutgoingAssociations.Any() || shape.IncomingAssociations.Any();
                        if (hasAssociations)
                        {
                            if (!record.TryGetProperty("VirtualId", out var virtualIdProp) ||
                                virtualIdProp.ValueKind != JsonValueKind.String)
                            {
                                return (false, $"Entity {entityName} requires a 'VirtualId' property for relationships.");
                            }
                        }

                        // Validate association references in the payload
                        foreach (var assoc in shape.OutgoingAssociations.Concat(shape.IncomingAssociations))
                        {
                            if (record.TryGetProperty(assoc.Name, out var assocProp))
                            {
                                if (assocProp.ValueKind != JsonValueKind.Object ||
                                    !assocProp.TryGetProperty("VirtualId", out var assocVirtualId) ||
                                    assocVirtualId.ValueKind != JsonValueKind.String)
                                {
                                    return (false, $"Association {assoc.Name} in {entityName} must be an object with a 'VirtualId'.");
                                }
                            }
                        }
                    }
                }

                return (true, "Validation successful");
            }
            catch (Exception ex)
            {
                return (false, $"Validation error: {ex.Message}");
            }
        }

        private async Task SaveSampleData(object sampleData)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string executingDirectory = System.IO.Path.GetDirectoryName(assembly.Location)
                    ?? throw new InvalidOperationException("Could not determine assembly directory");
                var directory = new DirectoryInfo(executingDirectory);
                string targetDirectory = directory?.Parent?.Parent?.Parent?.FullName
                    ?? throw new InvalidOperationException("Could not determine target directory");

                string filePath = System.IO.Path.Combine(targetDirectory, "resources", "SampleData.json");

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };

                string jsonData = JsonSerializer.Serialize(sampleData, options);
                await File.WriteAllTextAsync(filePath, jsonData);
            }
            catch (Exception ex)
            {
                // Log the error; do not show a UI dialog from Core
                Console.WriteLine($"Error saving sample data: {ex.Message}");
                throw;
            }
        }
    }
}
