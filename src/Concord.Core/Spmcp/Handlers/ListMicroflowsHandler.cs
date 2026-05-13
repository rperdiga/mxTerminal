using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Terminal.Interop;
using Terminal.Spmcp.Core;

namespace Terminal.Spmcp.Handlers
{
    public class ListMicroflowsHandler : BaseApiHandler
    {
        public override string Path => "/api/microflows/list";
        public override string Method => "POST";

        public ListMicroflowsHandler() : base() { }

        public override async Task HandleAsync(HttpContext context)
        {
            try
            {
                var requestBody = await ReadRequestAsync<ListMicroflowsRequest>(context);
                if (requestBody == null || string.IsNullOrEmpty(requestBody.ModuleName))
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonResponseAsync(context, new { success = false, message = "Module name is required." });
                    return;
                }

                var model = HostServices.Model;
                var microflowAuthoring = HostServices.MicroflowAuthoring;

                var moduleId = model.GetModuleByName(requestBody.ModuleName);
                if (moduleId == null)
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonResponseAsync(context, new { success = false, message = "Module not found." });
                    return;
                }

                var microflows = microflowAuthoring.ListMicroflows(moduleId)
                    .Select(mf => new
                    {
                        mf.Name,
                        QualifiedName = mf.QualifiedName
                    }).ToList();

                await WriteJsonResponseAsync(context, new
                {
                    success = true,
                    message = "Microflows retrieved successfully.",
                    data = microflows
                });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await WriteJsonResponseAsync(context, new
                {
                    success = false,
                    message = $"Error retrieving microflows: {ex.Message}"
                });
            }
        }

        private class ListMicroflowsRequest
        {
            public string? ModuleName { get; set; }
        }
    }
}
