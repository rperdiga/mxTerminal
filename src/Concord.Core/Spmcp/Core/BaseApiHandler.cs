using Microsoft.AspNetCore.Http;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Terminal.Spmcp.Core
{
    /// <summary>
    /// Base class for SPMCP HTTP handlers. Provides CORS, OPTIONS handling,
    /// JSON response writing, and helper wrappers for handler operations.
    /// The original BaseApiHandler took an IModel in its constructor;
    /// the rewritten Core version reads from HostServices on demand so
    /// the base no longer needs Mendix-typed plumbing.
    /// </summary>
    public abstract class BaseApiHandler : IApiHandler
    {
        public abstract string Path { get; }
        public virtual string Method => "POST";

        protected readonly JsonSerializerOptions JsonOptions;

        protected BaseApiHandler()
        {
            JsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };
        }

        public abstract Task HandleAsync(HttpContext context);

        /// <summary>
        /// Standard wrapper: emits CORS headers, handles OPTIONS preflight,
        /// verifies request method, runs the operation, writes a consistent
        /// JSON envelope on success/failure.
        /// </summary>
        protected async Task ExecuteAsync<T>(
            HttpContext context,
            string operationName,
            Func<Task<(bool success, string message, T? data)>> operation)
        {
            try
            {
                AddCorsHeaders(context);

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
                        message = $"Method not allowed. Use {Method}.",
                        error = "MethodNotAllowed"
                    });
                    return;
                }

                var (success, message, data) = await operation();

                context.Response.StatusCode = success ? 200 : 400;
                await WriteJsonResponseAsync(context, new
                {
                    success,
                    message,
                    data,
                    operation = operationName
                });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await WriteJsonResponseAsync(context, new
                {
                    success = false,
                    message = $"{operationName} failed: {ex.Message}",
                    error = ex.GetType().Name,
                    operation = operationName
                });
            }
        }

        /// <summary>
        /// Compatibility wrapper that mirrors the old ExecuteInTransactionAsync signature.
        /// The operation lambda no longer receives an IModel parameter — callers read from
        /// HostServices directly. CORS + OPTIONS + method check are applied before calling
        /// the operation.
        /// </summary>
        protected async Task ExecuteInTransactionAsync(
            HttpContext context,
            string operationName,
            Func<Task<(bool success, string message, object? data)>> operation)
        {
            try
            {
                AddCorsHeaders(context);

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
                        message = $"Method not allowed. Use {Method}.",
                        error = "MethodNotAllowed"
                    });
                    return;
                }

                var (success, message, data) = await operation();

                context.Response.StatusCode = success ? 200 : 400;
                await WriteJsonResponseAsync(context, new
                {
                    success,
                    message,
                    data,
                    operation = operationName
                });
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await WriteJsonResponseAsync(context, new
                {
                    success = false,
                    message = $"{operationName} failed: {ex.Message}",
                    error = ex.GetType().Name,
                    operation = operationName
                });
            }
        }

        /// <summary>
        /// Read the request body as a deserialized payload of T.
        /// Returns default(T) if the body is empty or invalid JSON.
        /// </summary>
        protected async Task<T?> ReadRequestAsync<T>(HttpContext context) where T : class
        {
            try
            {
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                if (string.IsNullOrWhiteSpace(body)) return null;
                return JsonSerializer.Deserialize<T>(body, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Read the request body as a deserialized payload of T.
        /// Alias for ReadRequestAsync — matches the old BaseApiHandler method name.
        /// </summary>
        protected Task<T?> DeserializeRequestBodyAsync<T>(HttpContext context) where T : class
            => ReadRequestAsync<T>(context);

        /// <summary>
        /// Write a JSON response with the configured options.
        /// </summary>
        protected async Task WriteJsonResponseAsync(HttpContext context, object value)
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(value, JsonOptions));
        }

        private static void AddCorsHeaders(HttpContext context)
        {
            context.Response.Headers.TryAdd("Access-Control-Allow-Origin", "*");
            context.Response.Headers.TryAdd("Access-Control-Allow-Methods", "POST, GET, OPTIONS, DELETE");
            context.Response.Headers.TryAdd("Access-Control-Allow-Headers", "Content-Type");
        }
    }
}
