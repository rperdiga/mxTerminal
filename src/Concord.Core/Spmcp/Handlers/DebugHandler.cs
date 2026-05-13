using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Terminal.Interop;
using Terminal.Spmcp.Core;

namespace Terminal.Spmcp.Handlers
{
    public class DebugHandler : BaseApiHandler
    {
        public override string Path => "/api/debug";
        public override string Method => "POST";

        public DebugHandler() : base() { }

        public override async Task HandleAsync(HttpContext context)
        {
            context.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
            context.Response.Headers.TryAdd("Access-Control-Allow-Methods", "POST, GET, OPTIONS");
            context.Response.Headers.TryAdd("Access-Control-Allow-Headers", "Content-Type");

            if (context.Request.Method == "OPTIONS")
            {
                context.Response.StatusCode = 200;
                return;
            }

            string requestBody;
            using (var reader = new StreamReader(context.Request.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            System.Diagnostics.Debug.WriteLine($"Debug API request: {requestBody}");

            var model = HostServices.Model;
            var domainModel = HostServices.DomainModel;
            var response = new Dictionary<string, object>();

            // Resolve the first user module
            var modules = model.ListModules();
            var module = modules.FirstOrDefault(m =>
                !m.Name.StartsWith("System", StringComparison.OrdinalIgnoreCase) &&
                !m.Name.StartsWith("AppStore", StringComparison.OrdinalIgnoreCase));

            if (module != default)
            {
                var entities = domainModel.ListEntities(module);

                response["module"] = module.Name;
                response["entityCount"] = entities.Count;
                response["entities"] = entities.Select(e =>
                {
                    var shape = domainModel.ReadEntity(e);
                    var simpleName = e.QualifiedName.Split('.').Last();
                    return new
                    {
                        Name = simpleName,
                        QualifiedName = e.QualifiedName,
                        AttributeCount = shape.Attributes.Count,
                        Attributes = shape.Attributes.Select(a => a.Name).ToList(),
                        LocationX = shape.X,
                        LocationY = shape.Y
                    };
                }).ToList();

                // Collect association information
                var allAssociations = new List<object>();
                var associations = domainModel.QueryAssociations(moduleName: module.Name, direction: "both");
                var seen = new HashSet<string>();
                foreach (var assoc in associations)
                {
                    if (!seen.Add(assoc.Name)) continue;
                    allAssociations.Add(new
                    {
                        Name = assoc.Name,
                        Parent = assoc.ParentEntityQualifiedName.Split('.').Last(),
                        ParentQualifiedName = assoc.ParentEntityQualifiedName,
                        Child = assoc.ChildEntityQualifiedName.Split('.').Last(),
                        ChildQualifiedName = assoc.ChildEntityQualifiedName,
                        Type = assoc.Type.ToString(),
                        MappedType = assoc.Type == AssociationType.Reference ? "one-to-many" : "many-to-many"
                    });
                }
                response["associations"] = allAssociations;
                response["associationCount"] = allAssociations.Count;

                response["troubleshooting"] = new
                {
                    entityNamesList = entities.Select(e => e.QualifiedName.Split('.').Last()).ToList(),
                    associationTips = new[] {
                        "Make sure both entities exist before creating an association",
                        "Use simple names without module prefixes in API calls",
                        "Check that association names are unique"
                    }
                };
            }
            else
            {
                response["error"] = "No domain model found";
            }

            response["examples"] = new
            {
                entityCreation = new
                {
                    simple = new
                    {
                        entity_name = "Customer",
                        attributes = new[]
                        {
                            new { name = "firstName", type = "String" },
                            new { name = "lastName", type = "String" },
                            new { name = "birthDate", type = "DateTime" },
                            new { name = "isActive", type = "Boolean" }
                        }
                    },
                    withEnumeration = new
                    {
                        entity_name = "Product",
                        attributes = new object[]
                        {
                            new { name = "productName", type = "String" },
                            new { name = "price", type = "Decimal" },
                            new
                            {
                                name = "status",
                                type = "Enumeration",
                                enumerationValues = new[] { "Available", "OutOfStock", "Discontinued" }
                            }
                        }
                    }
                },
                associationCreation = new
                {
                    oneToMany = new
                    {
                        name = "Customer_Orders",
                        parent = "Customer",
                        child = "Order",
                        type = "one-to-many"
                    },
                    manyToMany = new
                    {
                        name = "Product_Category",
                        parent = "Product",
                        child = "Category",
                        type = "many-to-many"
                    }
                }
            };

            context.Response.StatusCode = 200;
            context.Response.ContentType = "application/json";

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                success = true,
                message = "Debug information retrieved successfully",
                data = response
            }, options));
        }
    }
}
