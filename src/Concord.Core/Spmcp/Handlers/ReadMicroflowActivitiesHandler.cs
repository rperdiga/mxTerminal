using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Terminal.Interop;
using Terminal.Spmcp.Core;

namespace Terminal.Spmcp.Handlers
{
    public class ReadMicroflowActivitiesHandler : BaseApiHandler
    {
        public override string Path => "/api/microflow/activities";
        public override string Method => "GET";

        public ReadMicroflowActivitiesHandler() : base() { }

        public override async Task HandleAsync(HttpContext context)
        {
            try
            {
                context.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
                context.Response.Headers.TryAdd("Access-Control-Allow-Methods", "GET, OPTIONS");
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

                var microflowName = context.Request.Query["microflowName"].ToString();
                if (string.IsNullOrEmpty(microflowName) || !microflowName.Contains("."))
                {
                    context.Response.StatusCode = 400;
                    await WriteJsonResponseAsync(context, new
                    {
                        success = false,
                        message = "Microflow name must include the module name (e.g., 'ModuleName.MicroflowName')."
                    });
                    return;
                }

                var microflowAuthoring = HostServices.MicroflowAuthoring;

                if (!microflowAuthoring.IsAvailable)
                {
                    context.Response.StatusCode = 503;
                    await WriteJsonResponseAsync(context, new
                    {
                        success = false,
                        message = "Microflow authoring service is not available on this Studio Pro version."
                    });
                    return;
                }

                var summary = microflowAuthoring.ReadMicroflow(microflowName);
                if (summary == null)
                {
                    context.Response.StatusCode = 404;
                    await WriteJsonResponseAsync(context, new
                    {
                        success = false,
                        message = $"Microflow '{microflowName}' not found."
                    });
                    return;
                }

                var activities = microflowAuthoring.ReadActivities(microflowName);

                var activitiesList = activities.Select(a => new
                {
                    Position = a.Position,
                    Index = a.Position - 1,
                    ActivityType = a.ActivityType,
                    Caption = a.Caption ?? string.Empty,
                    OutputVariable = a.OutputVariable,
                    TargetEntity = a.TargetEntity,
                    TargetMicroflow = a.TargetMicroflow,
                    TargetJavaAction = a.TargetJavaAction,
                    Parameters = a.Parameters
                }).ToList();

                var inputParameters = summary.Parameters.Select(p => new
                {
                    p.Name,
                    DataType = p.TypeQualifiedName,
                    p.IsList,
                    p.Documentation
                }).ToList();

                var outputParameter = new
                {
                    ReturnType = summary.ReturnTypeQualifiedName ?? "Void",
                    ReturnIsList = summary.ReturnIsList
                };

                context.Response.StatusCode = 200;
                await WriteJsonResponseAsync(context, new
                {
                    success = true,
                    data = new
                    {
                        InputParameters = inputParameters,
                        Activities = activitiesList,
                        ActivityCount = activitiesList.Count,
                        OutputParameter = outputParameter,
                        MicroflowName = summary.Name,
                        QualifiedName = summary.QualifiedName
                    }
                });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await WriteJsonResponseAsync(context, new
                {
                    success = false,
                    message = $"Error retrieving microflow details: {ex.Message}",
                    stackTrace = ex.StackTrace
                });
            }
        }
    }
}
