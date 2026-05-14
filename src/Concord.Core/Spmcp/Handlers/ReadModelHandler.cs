using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Terminal.Interop;
using Terminal.Spmcp.Core;

namespace Terminal.Spmcp.Handlers
{
    public class ReadModelHandler : BaseApiHandler
    {
        public override string Path => "/api/model/read";
        public override string Method => "POST";

        public ReadModelHandler() : base() { }

        public override async Task HandleAsync(HttpContext context)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Received {context.Request.Method} request to {context.Request.Path}");

                if (context.Request.Method != "POST")
                {
                    context.Response.StatusCode = 405;
                    await WriteJsonResponseAsync(context, new
                    {
                        success = false,
                        message = "Method not allowed. Use POST."
                    });
                    return;
                }

                await ExecuteInTransactionAsync(
                    context,
                    "read domain model",
                    async () =>
                    {
                        var model = HostServices.Model;
                        var domainModel = HostServices.DomainModel;

                        var module = model.ListModules().FirstOrDefault(m =>
                            !m.Name.StartsWith("System", StringComparison.OrdinalIgnoreCase) &&
                            !m.Name.StartsWith("AppStore", StringComparison.OrdinalIgnoreCase));

                        if (module == default)
                        {
                            return (
                                success: false,
                                message: "No domain model found.",
                                data: null as object
                            );
                        }

                        var entities = domainModel.ListEntities(module);

                        var modelData = new
                        {
                            ModuleName = module.Name,
                            Entities = entities.Select(entity =>
                            {
                                var shape = domainModel.ReadEntity(entity);
                                var simpleName = entity.QualifiedName.Split('.').Last();
                                return new
                                {
                                    Name = simpleName,
                                    QualifiedName = entity.QualifiedName,
                                    Attributes = GetEntityAttributes(shape),
                                    Associations = GetEntityAssociations(shape)
                                };
                            }).ToList()
                        };

                        return (
                            success: true,
                            message: "Model retrieved successfully",
                            data: modelData as object
                        );
                    });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in ReadModelHandler: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");

                context.Response.StatusCode = 500;
                await WriteJsonResponseAsync(context, new
                {
                    success = false,
                    message = $"Error reading domain model: {ex.Message}",
                    error = "InternalServerError"
                });
            }
        }

        private Dictionary<string, string> GetEntityAttributes(EntityShape shape)
        {
            return shape.Attributes
                .Where(attr => attr.Name != null)
                .ToDictionary(
                    attr => attr.Name,
                    attr =>
                    {
                        if (attr.Kind == AttributeKind.Enumeration)
                            return "Enumeration";
                        return attr.Kind.ToString();
                    }
                );
        }

        private List<AssociationInfo> GetEntityAssociations(EntityShape shape)
        {
            var results = new List<AssociationInfo>();
            var seen = new HashSet<string>();

            foreach (var assoc in shape.OutgoingAssociations.Concat(shape.IncomingAssociations))
            {
                if (!seen.Add(assoc.Name)) continue;

                var mappedType = assoc.Type == AssociationType.Reference ? "one-to-many" : "many-to-many";
                results.Add(new AssociationInfo
                {
                    Name = assoc.Name,
                    Parent = assoc.ParentEntityQualifiedName.Split('.').Last(),
                    Child = assoc.ChildEntityQualifiedName.Split('.').Last(),
                    Type = mappedType
                });
            }

            return results;
        }
    }

    public class AssociationInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Parent { get; set; } = string.Empty;
        public string Child { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }
}
