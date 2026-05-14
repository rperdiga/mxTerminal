using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Terminal.Interop;
using Terminal.Spmcp.Core;

namespace Terminal.Spmcp.Handlers
{
    public class AssociationDiagnosticHandler : BaseApiHandler
    {
        public override string Path => "/api/diagnostics/association";
        public override string Method => "POST";

        public AssociationDiagnosticHandler() : base() { }

        public override async Task HandleAsync(HttpContext context)
        {
            try
            {
                context.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
                context.Response.Headers.TryAdd("Access-Control-Allow-Methods", "POST, OPTIONS");
                context.Response.Headers.TryAdd("Access-Control-Allow-Headers", "Content-Type");

                if (context.Request.Method == "OPTIONS")
                {
                    context.Response.StatusCode = 200;
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
                    context.Response.StatusCode = 404;
                    await WriteJsonResponseAsync(context, new
                    {
                        success = false,
                        message = "No domain model found"
                    });
                    return;
                }

                var entities = domainModel.ListEntities(module);
                var entityNames = entities.Select(e => e.QualifiedName.Split('.').Last()).ToList();
                var allAssociations = new List<object>();

                // Query associations for all entities
                var associations = domainModel.QueryAssociations(moduleName: module.Name, direction: "both");
                foreach (var assoc in associations)
                {
                    allAssociations.Add(new
                    {
                        Name = assoc.Name,
                        Parent = assoc.ParentEntityQualifiedName.Split('.').Last(),
                        Child = assoc.ChildEntityQualifiedName.Split('.').Last(),
                        Type = assoc.Type.ToString(),
                        MappedType = assoc.Type == AssociationType.Reference ? "one-to-many" : "many-to-many"
                    });
                }

                // Deduplicate associations (QueryAssociations may return each twice when direction=both)
                var uniqueAssociations = allAssociations
                    .GroupBy(a => ((dynamic)a).Name)
                    .Select(g => g.First())
                    .ToList();

                var associationGuidance = new
                {
                    CommonIssues = new[]
                    {
                        "Entities must exist before creating associations",
                        "Entity names are case sensitive",
                        "Don't use module prefixes in entity names",
                        "Association names must be unique",
                        "For one-to-many associations, parent is the 'one' side, child is the 'many' side"
                    },
                    ProperFormat = new
                    {
                        Name = "Customer_Orders",
                        Parent = "Customer",
                        Child = "Order",
                        Type = "one-to-many"
                    },
                    AvailableEntities = entityNames
                };

                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";

                var responseOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    success = true,
                    message = "Association diagnostic information retrieved",
                    data = new
                    {
                        Entities = entityNames,
                        EntityCount = entityNames.Count,
                        Associations = uniqueAssociations,
                        AssociationCount = uniqueAssociations.Count,
                        Guidance = associationGuidance
                    }
                }, responseOptions));
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await WriteJsonResponseAsync(context, new
                {
                    success = false,
                    message = $"Error in association diagnostic: {ex.Message}"
                });
            }
        }
    }
}
