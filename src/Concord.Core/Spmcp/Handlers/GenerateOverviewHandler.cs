using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Terminal.Interop;
using Terminal.Spmcp.Core;

namespace Terminal.Spmcp.Handlers.Schema
{
    public class OverviewPagesRequest
    {
        public List<string> EntityNames { get; set; } = new List<string>();
        public bool GenerateIndexSnippet { get; set; } = true;
    }
}

namespace Terminal.Spmcp.Handlers
{
    public class GenerateOverviewHandler : BaseApiHandler
    {
        public override string Path => "/api/pages/generate-overview";
        public override string Method => "POST";

        public GenerateOverviewHandler() : base() { }

        public override async Task HandleAsync(HttpContext context)
        {
            await ExecuteInTransactionAsync(
                context,
                "generate overview pages",
                async () =>
                {
                    try
                    {
                        var request = await DeserializeRequestBodyAsync<Schema.OverviewPagesRequest>(context);

                        if (request == null || request.EntityNames == null || !request.EntityNames.Any())
                        {
                            return (
                                success: false,
                                message: "Invalid request format or no entity names provided",
                                data: null as object
                            );
                        }

                        var model = HostServices.Model;
                        var domainModel = HostServices.DomainModel;
                        var pageGeneration = HostServices.PageGeneration;

                        // Resolve the first user module
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

                        var allEntities = domainModel.ListEntities(module);

                        // Filter entities to those requested
                        var matchedEntityNames = request.EntityNames
                            .Where(reqName => allEntities.Any(e =>
                                e.QualifiedName.Split('.').Last().Equals(reqName, StringComparison.OrdinalIgnoreCase)))
                            .ToList();

                        if (!matchedEntityNames.Any())
                        {
                            return (
                                success: false,
                                message: "None of the requested entities were found in the domain model",
                                data: null as object
                            );
                        }

                        var pgRequest = new PageGenerationRequest(
                            module.Name,
                            matchedEntityNames,
                            request.GenerateIndexSnippet);

                        var result = pageGeneration.GenerateOverviewPages(pgRequest);

                        if (!result.Success)
                        {
                            return (
                                success: false,
                                message: result.Error ?? "Page generation failed",
                                data: null as object
                            );
                        }

                        return (
                            success: true,
                            message: $"Successfully generated {result.CreatedPageNames.Count} overview pages",
                            data: new
                            {
                                GeneratedPages = result.CreatedPageNames.ToList(),
                                Warnings = result.Warnings.ToList()
                            } as object
                        );
                    }
                    catch (Exception ex)
                    {
                        return (
                            success: false,
                            message: $"Error generating overview pages: {ex.Message}",
                            data: null as object
                        );
                    }
                });
        }
    }
}
