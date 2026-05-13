using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Terminal.Interop;
using Terminal.Spmcp.Utils;

namespace Terminal.Spmcp.Tools
{
    public class MendixAdditionalTools
    {
        private readonly ILogger<MendixAdditionalTools> _logger;
        private readonly string? _projectDirectory;
        private static string? _lastError;
        private static Exception? _lastException;

        public MendixAdditionalTools(ILogger<MendixAdditionalTools> logger, string? projectDirectory = null)
        {
            _logger = logger;
            _projectDirectory = projectDirectory;
        }

        private string GetDebugLogPath()
        {
            try
            {
                // Use the project directory if available
                if (!string.IsNullOrEmpty(_projectDirectory))
                {
                    string resourcesDir = System.IO.Path.Combine(_projectDirectory, "resources");
                    
                    if (!System.IO.Directory.Exists(resourcesDir))
                    {
                        System.IO.Directory.CreateDirectory(resourcesDir);
                    }
                    
                    return System.IO.Path.Combine(resourcesDir, "mcp_debug.log");
                }
                
                // Fallback to current directory if no project found
                return System.IO.Path.Combine(Environment.CurrentDirectory, "mcp_debug.log");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting debug log path, using fallback");
                return System.IO.Path.Combine(Environment.CurrentDirectory, "mcp_debug.log");
            }
        }

        private string GetAISampleImportLogPath()
        {
            try
            {
                // Use the project directory if available
                if (!string.IsNullOrEmpty(_projectDirectory))
                {
                    string resourcesDir = System.IO.Path.Combine(_projectDirectory, "resources");
                    
                    if (!System.IO.Directory.Exists(resourcesDir))
                    {
                        System.IO.Directory.CreateDirectory(resourcesDir);
                    }
                    
                    return System.IO.Path.Combine(resourcesDir, "AI_Sample_Import.log");
                }
                
                // Fallback to current directory if no project found
                return System.IO.Path.Combine(Environment.CurrentDirectory, "AI_Sample_Import.log");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting AI sample import log path, using fallback");
                return System.IO.Path.Combine(Environment.CurrentDirectory, "AI_Sample_Import.log");
            }
        }

        public static void SetLastError(string error, Exception? exception = null)
        {
            _lastError = error;
            _lastException = exception;
        }

    public async Task<object> SaveData(JsonObject arguments)
    {
        try
        {
            var dataProperty = arguments["data"]?.AsObject();
            if (dataProperty == null)
            {
                var requestedModuleName = arguments["module_name"]?.ToString();
                ModuleId? currentModuleId = string.IsNullOrWhiteSpace(requestedModuleName)
                    ? HostServices.Model.ListModules().FirstOrDefault(m => !string.IsNullOrEmpty(m.Name))
                    : HostServices.Model.GetModuleByName(requestedModuleName);
                if (currentModuleId == null)
                {
                    var error = string.IsNullOrWhiteSpace(requestedModuleName) ? "No module found in SaveData." : $"Module '{requestedModuleName}' not found.";
                    _logger.LogError(error);
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error, success = false });
                }
                var moduleName = currentModuleId?.Name ?? "MyFirstModule";
                    
                var emptyDataError = "Invalid request format or empty data. The save_data tool is used to generate sample data for Mendix domain models.";
                SetLastError(emptyDataError);
                return JsonSerializer.Serialize(new { 
                    error = emptyDataError,
                        message = "The save_data tool requires a 'data' property with entity data in the specified format.",
                        required_format = new {
                            data = new {
                                CustomerEntity = new[] {
                                    new {
                                        VirtualId = "CUST001",
                                        FirstName = "John",
                                        LastName = "Doe",
                                        Email = "john.doe@example.com"
                                    }
                                },
                                OrderEntity = new[] {
                                    new {
                                        VirtualId = "ORD001",
                                        OrderDate = "2023-11-01T10:30:00Z",
                                        TotalAmount = 99.99,
                                        Customer = new {
                                            VirtualId = "CUST001"
                                        }
                                    }
                                }
                            }
                        },
                        format_notes = new {
                            entity_naming = $"Use '{moduleName}.EntityName' format for entity keys (e.g., '{moduleName}.Customer')",
                            virtual_id = "Include a unique VirtualId for each record to establish relationships",
                            relationships = "Reference related entities using their VirtualId in nested objects",
                            dates = "Use ISO 8601 format for dates (YYYY-MM-DDTHH:MM:SSZ)"
                        },
                        purpose = "This tool generates realistic sample data for testing and development purposes.",
                        success = false
                    });
                }

                var saveModuleName = arguments["module_name"]?.ToString();
                ModuleId? moduleId = string.IsNullOrWhiteSpace(saveModuleName)
                    ? HostServices.Model.ListModules().FirstOrDefault(m => !string.IsNullOrEmpty(m.Name))
                    : HostServices.Model.GetModuleByName(saveModuleName);
                if (moduleId == null)
                {
                    var error = string.IsNullOrWhiteSpace(saveModuleName) ? "No module found in SaveData." : $"Module '{saveModuleName}' not found.";
                    _logger.LogError(error);
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error, success = false });
                }

                // Validate the data structure
                var validationResult = ValidateDataStructure(dataProperty, moduleId.Value);
                if (!validationResult.IsValid)
                {
                    SetLastError(validationResult.Message);
                    var errorResponse = new Dictionary<string, object>
                    {
                        ["error"] = validationResult.Message,
                        ["success"] = false
                    };
                    if (validationResult.Details != null) errorResponse["details"] = validationResult.Details;
                    if (validationResult.Warnings.Any()) errorResponse["warnings"] = validationResult.Warnings;
                    return JsonSerializer.Serialize(errorResponse);
                }

                // Save the data to a JSON file
                var saveResult = await SaveDataToFile(dataProperty);
                if (!saveResult.Success)
                {
                    SetLastError(saveResult.ErrorMessage ?? "Unknown error occurred while saving data");
                    return JsonSerializer.Serialize(new { 
                        error = saveResult.ErrorMessage,
                        success = false
                    });
                }

                var successResponse = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["message"] = "Data validated and saved successfully",
                    ["file_path"] = saveResult.FilePath!,
                    ["entities_processed"] = validationResult.EntitiesProcessed
                };
                if (validationResult.Warnings.Any())
                    successResponse["warnings"] = validationResult.Warnings;
                return JsonSerializer.Serialize(successResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving data");
                SetLastError("Error saving data", ex);
                return JsonSerializer.Serialize(new { 
                    error = ex.Message,
                    success = false
                });
            }
        }

    public async Task<object> GenerateSampleData(JsonObject arguments)
    {
        try
        {
            // Resolve modules via HostServices — support both module_name (string) and module_names (array)
            var modules = new List<ModuleId>();
            if (arguments.ContainsKey("module_names") && arguments["module_names"]?.GetValueKind() == JsonValueKind.Array)
            {
                foreach (var mn in arguments["module_names"]!.AsArray())
                {
                    if (mn?.GetValueKind() == JsonValueKind.String)
                    {
                        var found = HostServices.Model.GetModuleByName(mn.GetValue<string>());
                        if (found.HasValue) modules.Add(found.Value);
                    }
                }
            }
            if (!modules.Any())
            {
                var moduleName = arguments.ContainsKey("module_name") ? arguments["module_name"]?.ToString() : null;
                if (!string.IsNullOrWhiteSpace(moduleName))
                {
                    var found = HostServices.Model.GetModuleByName(moduleName);
                    if (found.HasValue) modules.Add(found.Value);
                }
                else
                {
                    // Fallback: use all non-AppStore modules (AppStore filter not available on ModuleId;
                    // include all modules as a safe approximation — Task 14+ can refine via GetProjectInfo).
                    var allModules = HostServices.Model.ListModules();
                    if (allModules.Count > 0) modules.Add(allModules[0]);
                }
            }
            if (!modules.Any())
            {
                return JsonSerializer.Serialize(new { error = "No valid modules found", success = false });
            }

            var recordsPerEntity = 5;
            if (arguments.ContainsKey("records_per_entity"))
            {
                var rpeNode = arguments["records_per_entity"];
                if (rpeNode != null && rpeNode.GetValueKind() == JsonValueKind.Number)
                    recordsPerEntity = Math.Clamp(rpeNode.GetValue<int>(), 1, 50);
            }

            // Optional entity filter (by simple entity name, matched against EntityRef.QualifiedName suffix)
            var entityFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (arguments.ContainsKey("entity_names") && arguments["entity_names"]?.GetValueKind() == JsonValueKind.Array)
            {
                foreach (var en in arguments["entity_names"]!.AsArray())
                {
                    if (en?.GetValueKind() == JsonValueKind.String)
                        entityFilter.Add(en.GetValue<string>());
                }
            }

            // Optional seed for reproducibility
            Random rng;
            if (arguments.ContainsKey("seed") && arguments["seed"]?.GetValueKind() == JsonValueKind.Number)
                rng = new Random(arguments["seed"]!.GetValue<int>());
            else
                rng = new Random();

            // Gather all entity shapes across modules (with module reference)
            var allEntityModulePairs = new List<(EntityShape Entity, ModuleId Module)>();
            foreach (var mod in modules)
            {
                var entityRefs = HostServices.DomainModel.ListEntities(mod);
                foreach (var entityRef in entityRefs)
                {
                    // EntityRef.QualifiedName is "ModuleName.EntityName"
                    var simpleName = entityRef.QualifiedName.Contains('.')
                        ? entityRef.QualifiedName.Split('.', 2)[1]
                        : entityRef.QualifiedName;
                    if (!entityFilter.Any()
                        || entityFilter.Contains(simpleName)
                        || entityFilter.Contains(entityRef.QualifiedName))
                    {
                        var shape = HostServices.DomainModel.ReadEntity(entityRef);
                        allEntityModulePairs.Add((shape, mod));
                    }
                }
            }

            if (!allEntityModulePairs.Any())
            {
                return JsonSerializer.Serialize(new { success = true, message = "No entities found to generate data for", data = new { }, stats = new { entities = 0, total_records = 0 } });
            }

            // Topological sort across all modules
            var sortedPairs = TopologicalSortEntitiesMultiModule(allEntityModulePairs);

            // Build _metadata section
            var metadataObj = BuildMetadata(sortedPairs, modules);

            // Generate data records
            // entityVirtualIds keyed by qualified name (Module.Entity)
            var entityVirtualIds = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var dataObject = new JsonObject();
            int totalRecords = 0;

            foreach (var (entity, mod) in sortedPairs)
            {
                var qualifiedName = entity.Self.QualifiedName;
                var records = GenerateEntityRecordsV2(entity, mod, recordsPerEntity, rng, entityVirtualIds);
                var jsonArray = new JsonArray();
                foreach (var record in records)
                    jsonArray.Add(record);
                dataObject[qualifiedName] = jsonArray;
                totalRecords += records.Count;
            }

            // Build the full v2 root object
            var rootObject = new JsonObject
            {
                ["_metadata"] = metadataObj,
                ["data"] = dataObject
            };

            // Save to file
            var saveResult = await SaveRootJsonToFile(rootObject);
            var filePath = saveResult.Success ? saveResult.FilePath : null;

            object? importSetup = null;
            var autoSetup = true;
            if (arguments.ContainsKey("auto_setup") && arguments["auto_setup"]?.GetValue<bool>() == false)
                autoSetup = false;

            if (autoSetup)
            {
                try
                {
                    var targetModuleName = modules.First().Name;
                    importSetup = SetupDataImportInternal(targetModuleName, "ASu_LoadSampleData", false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[generate_sample_data] Auto-setup failed (data was still generated successfully)");
                    importSetup = new { success = false, error = $"Auto-setup failed: {ex.Message}" };
                }
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                message = $"Generated v2 sample data for {sortedPairs.Count} entities ({totalRecords} total records) across {modules.Count} module(s)",
                file_path = filePath,
                format_version = 2,
                data = JsonSerializer.Deserialize<object>(rootObject.ToJsonString()),
                stats = new { entities = sortedPairs.Count, total_records = totalRecords, modules = modules.Select(m => m.Name).ToList() },
                import_setup = importSetup
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating sample data");
            return JsonSerializer.Serialize(new { error = ex.Message, success = false });
        }
    }

    public async Task<object> ReadSampleData(JsonObject arguments)
    {
        try
        {
            var filePath = arguments.ContainsKey("file_path") && arguments["file_path"]?.GetValueKind() == JsonValueKind.String
                ? arguments["file_path"]!.GetValue<string>()
                : GetSampleDataFilePath();

            if (string.IsNullOrEmpty(filePath))
                return JsonSerializer.Serialize(new { error = "Could not determine sample data file path", success = false });

            if (!File.Exists(filePath))
                return JsonSerializer.Serialize(new { error = $"No sample data file found at '{filePath}'", success = false });

            var content = await File.ReadAllTextAsync(filePath);
            var fileInfo = new FileInfo(filePath);

            // Parse to validate JSON
            object? parsedData;
            try { parsedData = JsonSerializer.Deserialize<object>(content); }
            catch { parsedData = content; }

            return JsonSerializer.Serialize(new
            {
                success = true,
                file_path = filePath,
                file_size_bytes = fileInfo.Length,
                data = parsedData
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading sample data");
            return JsonSerializer.Serialize(new { error = ex.Message, success = false });
        }
    }

    public async Task<object> GenerateOverviewPages(JsonObject arguments)
    {
        await Task.CompletedTask;
        try
        {
            var entityNamesArray = arguments["entity_names"]?.AsArray();
            var generateIndexSnippet = arguments["generate_index_snippet"]?.GetValue<bool>() ?? true;

            if (entityNamesArray == null || !entityNamesArray.Any())
            {
                return JsonSerializer.Serialize(new {
                    error = "Invalid request format or no entity names provided",
                    success = false
                });
            }

            var entityNames = entityNamesArray
                .Select(node => node?.ToString())
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .ToList();

            if (!entityNames.Any())
            {
                return JsonSerializer.Serialize(new {
                    error = "No valid entity names provided",
                    success = false
                });
            }

            var overviewModuleName = arguments["module_name"]?.ToString();
            if (string.IsNullOrWhiteSpace(overviewModuleName))
            {
                var error = "module_name is required for GenerateOverviewPages.";
                _logger.LogError(error);
                SetLastError(error);
                return JsonSerializer.Serialize(new { error, success = false });
            }

            var request = new PageGenerationRequest(overviewModuleName, entityNames, generateIndexSnippet);
            var result = HostServices.PageGeneration.GenerateOverviewPages(request);

            if (!result.Success)
            {
                var error = result.Error ?? "GenerateOverviewPages failed.";
                _logger.LogError(error);
                SetLastError(error);
                return JsonSerializer.Serialize(new { error, success = false, warnings = result.Warnings });
            }

            return JsonSerializer.Serialize(new {
                success = true,
                message = $"Successfully generated {result.CreatedPageNames.Count} overview pages",
                generated_pages = result.CreatedPageNames,
                created_pages = result.CreatedPageNames,
                warnings = result.Warnings.Any() ? result.Warnings : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating overview pages");
            SetLastError("Error generating overview pages", ex);
            return JsonSerializer.Serialize(new {
                error = ex.Message,
                success = false
            });
        }
    }

    public async Task<object> ListMicroflows(JsonObject arguments)
    {
        try
        {
            var moduleName = arguments["module_name"]?.ToString();

            ModuleId? moduleFilter = null;
            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                moduleFilter = HostServices.Model.GetModuleByName(moduleName);
                if (moduleFilter == null)
                {
                    var error = $"Module '{moduleName}' not found.";
                    _logger.LogError(error);
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }
            }

            var microflowSummaries = HostServices.MicroflowAuthoring.ListMicroflows(moduleFilter);

            var microflows = microflowSummaries
                .Select(mf => new
                {
                    name = mf.Name,
                    module = mf.Module,
                    qualifiedName = mf.QualifiedName,
                    parameterCount = mf.Parameters.Count,
                    activityCount = mf.ActivityCount,
                    returnType = mf.ReturnTypeQualifiedName,
                    returnIsList = mf.ReturnIsList
                }).ToArray();

            return JsonSerializer.Serialize(new { microflows = microflows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing microflows");
            SetLastError("Error listing microflows", ex);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private string FormatDataType(DataType? dt)
    {
        if (dt == null) return "Void";
        return dt switch
        {
            ListType lt => $"List of {lt.EntityName?.FullName ?? "Unknown"}",
            ObjectType ot => $"Object of {ot.EntityName?.FullName ?? "Unknown"}",
            EnumerationType et => $"Enumeration {et.EnumerationName?.FullName ?? "Unknown"}",
            _ => dt.ToString()
        };
    }


    public async Task<object> ReadMicroflowDetails(JsonObject arguments)
    {
        try
        {
            var microflowName = arguments["microflow_name"]?.ToString();
            if (string.IsNullOrEmpty(microflowName))
            {
                var error = "Microflow name is required";
                SetLastError(error);
                return JsonSerializer.Serialize(new { error });
            }

            // Build qualified name: if the argument already contains '.' treat it as
            // fully-qualified; otherwise combine with module_name if supplied.
            var moduleName = arguments["module_name"]?.ToString();
            string qualifiedName;
            if (microflowName.Contains('.'))
            {
                qualifiedName = microflowName;
            }
            else if (!string.IsNullOrWhiteSpace(moduleName))
            {
                qualifiedName = $"{moduleName}.{microflowName}";
            }
            else
            {
                qualifiedName = microflowName;
            }

            var summary = HostServices.MicroflowAuthoring.ReadMicroflow(qualifiedName);
            if (summary == null)
            {
                var error = $"Microflow '{qualifiedName}' not found.";
                SetLastError(error);
                return JsonSerializer.Serialize(new { error });
            }

            // --- Parameters (from MicroflowSummary.Parameters) ---
            var parametersInfo = summary.Parameters.Select(p => new
            {
                name = p.Name,
                type = p.TypeQualifiedName,
                isList = p.IsList,
                documentation = p.Documentation
            }).ToList<object>();

            // --- Activities (from ReadActivities) ---
            IReadOnlyList<MicroflowActivitySummary> activitySummaries;
            try
            {
                activitySummaries = HostServices.MicroflowAuthoring.ReadActivities(summary.QualifiedName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not retrieve activities for microflow '{QualifiedName}'", summary.QualifiedName);
                activitySummaries = Array.Empty<MicroflowActivitySummary>();
            }

            var activitiesInfo = activitySummaries.Select(a => (object)new
            {
                position = a.Position,
                actionType = a.ActivityType,
                caption = a.Caption,
                outputVariable = a.OutputVariable,
                targetEntity = a.TargetEntity,
                targetMicroflow = a.TargetMicroflow,
                targetJavaAction = a.TargetJavaAction,
                parameters = a.Parameters
            }).ToList();

            var microflowInfo = new
            {
                name = summary.Name,
                qualifiedName = summary.QualifiedName,
                module = summary.Module,
                documentation = summary.Documentation,
                accessLevel = summary.AccessLevel.ToString(),
                returnType = summary.ReturnTypeQualifiedName,
                returnIsList = summary.ReturnIsList,
                parameterCount = parametersInfo.Count,
                parameters = parametersInfo,
                activityCount = activitiesInfo.Count,
                activities = activitiesInfo
            };

            return JsonSerializer.Serialize(new { success = true, microflow = microflowInfo });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading microflow details");
            SetLastError("Error reading microflow details", ex);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

        public async Task<object> GetLastError(JsonObject arguments)
        {
            try
            {
                if (string.IsNullOrEmpty(_lastError))
                {
                    return JsonSerializer.Serialize(new { 
                        message = "No errors recorded",
                        last_error = (string?)null
                    });
                }

                return JsonSerializer.Serialize(new { 
                    message = "Last error retrieved",
                    last_error = _lastError,
                    details = _lastException?.Message,
                    stack_trace = _lastException?.StackTrace,
                    timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting last error");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<object> GetStudioProLogs(JsonObject arguments)
        {
            try
            {
                var level = arguments?["level"]?.ToString()?.ToUpperInvariant() ?? "ERROR";
                var lastNMinutes = 30;
                if (arguments?["last_minutes"] != null && int.TryParse(arguments["last_minutes"]?.ToString(), out var mins))
                    lastNMinutes = mins;

                var logEntries = new List<object>();

                // Read Studio Pro log file
                var studioProLogPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Mendix", "log", "11.5.0", "log.txt");

                if (System.IO.File.Exists(studioProLogPath))
                {
                    var cutoff = DateTime.Now.AddMinutes(-lastNMinutes);
                    // Read with sharing since Studio Pro has this file open
                    using var fs = new System.IO.FileStream(studioProLogPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                    using var reader = new System.IO.StreamReader(fs);
                    string? line;
                    var multiLineBuffer = new System.Text.StringBuilder();
                    string? currentTimestamp = null;
                    string? currentLevel = null;
                    string? currentSource = null;

                    while ((line = reader.ReadLine()) != null)
                    {
                        // Parse log line: "2026-02-20 01:18:03.0029 INFO Mendix.Something Message here"
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)\s+(INFO|WARN|ERROR|DEBUG)\s+(\S+)\s+(.*)$");
                        if (match.Success)
                        {
                            // Flush previous entry
                            if (currentTimestamp != null && ShouldIncludeLogEntry(currentLevel, level))
                            {
                                if (DateTime.TryParse(currentTimestamp, out var ts) && ts >= cutoff)
                                {
                                    logEntries.Add(new { timestamp = currentTimestamp, level = currentLevel, source = currentSource, message = multiLineBuffer.ToString().TrimEnd() });
                                }
                            }

                            currentTimestamp = match.Groups[1].Value;
                            currentLevel = match.Groups[2].Value;
                            currentSource = match.Groups[3].Value;
                            multiLineBuffer.Clear();
                            multiLineBuffer.AppendLine(match.Groups[4].Value);
                        }
                        else if (currentTimestamp != null)
                        {
                            // Continuation line (stack trace, etc.)
                            multiLineBuffer.AppendLine(line);
                        }
                    }

                    // Flush last entry
                    if (currentTimestamp != null && ShouldIncludeLogEntry(currentLevel, level))
                    {
                        if (DateTime.TryParse(currentTimestamp, out var ts) && ts >= cutoff)
                        {
                            logEntries.Add(new { timestamp = currentTimestamp, level = currentLevel, source = currentSource, message = multiLineBuffer.ToString().TrimEnd() });
                        }
                    }
                }

                // Also read our MCP debug log for extension-specific errors
                var mcpErrors = new List<object>();
                var mcpLogPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "..", "..", "Mendix Projects", "Sample", "resources", "mcp_debug.log");

                // Try project directory path first
                try
                {
                    var projectDir = HostServices.Model.GetProjectInfo().DirectoryPath;
                    if (!string.IsNullOrEmpty(projectDir))
                    {
                        mcpLogPath = System.IO.Path.Combine(projectDir, "resources", "mcp_debug.log");
                    }
                }
                catch { /* ignore */ }

                if (System.IO.File.Exists(mcpLogPath))
                {
                    var cutoff = DateTime.Now.AddMinutes(-lastNMinutes);
                    using var fs = new System.IO.FileStream(mcpLogPath, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                    using var reader = new System.IO.StreamReader(fs);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("exception", StringComparison.OrdinalIgnoreCase) || line.Contains("fail", StringComparison.OrdinalIgnoreCase))
                        {
                            var tsMatch = System.Text.RegularExpressions.Regex.Match(line, @"\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)\]");
                            if (tsMatch.Success && DateTime.TryParse(tsMatch.Groups[1].Value, out var ts) && ts >= cutoff)
                            {
                                mcpErrors.Add(new { timestamp = tsMatch.Groups[1].Value, source = "MCP Extension", message = line });
                            }
                        }
                    }
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    studioProLogPath = studioProLogPath,
                    filter = new { level = level, lastMinutes = lastNMinutes },
                    studioProEntries = logEntries.Count > 0 ? logEntries : null,
                    mcpExtensionErrors = mcpErrors.Count > 0 ? mcpErrors : null,
                    summary = new
                    {
                        studioProLogCount = logEntries.Count,
                        mcpErrorCount = mcpErrors.Count,
                        totalIssues = logEntries.Count + mcpErrors.Count
                    },
                    message = (logEntries.Count + mcpErrors.Count) == 0
                        ? $"No {level} entries found in the last {lastNMinutes} minutes."
                        : $"Found {logEntries.Count} Studio Pro log entries and {mcpErrors.Count} MCP extension errors."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading Studio Pro logs");
                return JsonSerializer.Serialize(new { error = $"Failed to read logs: {ex.Message}" });
            }
        }

        private static bool ShouldIncludeLogEntry(string? entryLevel, string filterLevel)
        {
            if (string.IsNullOrEmpty(entryLevel)) return false;
            return filterLevel switch
            {
                "ERROR" => entryLevel == "ERROR",
                "WARN" => entryLevel == "ERROR" || entryLevel == "WARN",
                "INFO" => entryLevel == "ERROR" || entryLevel == "WARN" || entryLevel == "INFO",
                "ALL" => true,
                _ => entryLevel == "ERROR"
            };
        }

        /// <summary>
        /// Returns an escalation response indicating that project consistency checks are not
        /// exposed via the Core Interop surface. Deferred until IConsistencyHost is introduced
        /// (Phase 3 spike notes). Use 'Project > Check All Errors' in Studio Pro directly.
        /// </summary>
        public async Task<object> CheckProjectErrors(JsonObject arguments)
        {
            await Task.CompletedTask;
            return JsonSerializer.Serialize(new
            {
                success = false,
                escalation = "manual",
                message = "Project consistency check is not exposed via the Core Interop surface. Run 'Project > Check All Errors' in Studio Pro."
            });
        }

        public async Task<object> ListAvailableTools(JsonObject arguments)
        {
            try
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

                return JsonSerializer.Serialize(new { available_tools = tools });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing available tools");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

    public async Task<object> DebugInfo(JsonObject arguments)
    {
        await Task.CompletedTask;
        try
        {
            // Helper: probe a HostServices accessor; returns true if it resolves, false if not registered.
            bool TryAccess(Func<object?> f)
            {
                try { _ = f(); return true; }
                catch (InvalidOperationException) { return false; }
            }

            // Service availability map
            var services = new Dictionary<string, bool>
            {
                ["App"]                 = TryAccess(() => HostServices.App),
                ["RunConfigurations"]   = TryAccess(() => HostServices.RunConfigurations),
                ["RunState"]            = TryAccess(() => HostServices.RunState),
                ["ModuleImport"]        = TryAccess(() => HostServices.ModuleImport),
                ["Model"]               = TryAccess(() => HostServices.Model),
                ["DomainModel"]         = TryAccess(() => HostServices.DomainModel),
                ["PageGeneration"]      = TryAccess(() => HostServices.PageGeneration),
                ["Navigation"]          = TryAccess(() => HostServices.Navigation),
                ["VersionControl"]      = TryAccess(() => HostServices.VersionControl),
                ["UntypedModel"]        = TryAccess(() => HostServices.UntypedModel),
                ["MicroflowAuthoring"]  = TryAccess(() => HostServices.MicroflowAuthoring),
            };

            // Project info (only if Model is available)
            object? projectInfo = null;
            int? moduleCount = null;
            int? documentCount = null;

            if (services["Model"])
            {
                try
                {
                    var info = HostServices.Model.GetProjectInfo();
                    projectInfo = new
                    {
                        name = info.Name,
                        directoryPath = info.DirectoryPath,
                        mendixVersion = info.MendixVersion,
                        appId = info.AppId
                    };

                    var modules = HostServices.Model.ListModules();
                    moduleCount = modules.Count;
                    documentCount = HostServices.Model.ListAllDocuments().Count;
                }
                catch (Exception ex)
                {
                    projectInfo = new { error = ex.Message };
                }
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                message = "Debug information retrieved successfully",
                data = new
                {
                    hostServices = services,
                    projectInfo,
                    moduleCount,
                    documentCount
                },
                timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving debug info");
            SetLastError("Error retrieving debug info", ex);
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }


        public async Task<object> CreateMicroflow(JsonObject arguments)
        {
            await Task.CompletedTask;
            try
            {
                if (!HostServices.MicroflowAuthoring.IsAvailable)
                {
                    var error = "IMicroflowService is not available in the current environment.";
                    SetLastError(error);
                    _logger.LogError("[create_microflow] MicroflowAuthoring host reports IsAvailable=false.");
                    return JsonSerializer.Serialize(new { error });
                }

                var microflowName = Utils.Utils.GetParam(arguments, "name", "microflow_name", "microflowName");
                if (string.IsNullOrWhiteSpace(microflowName))
                {
                    var error = "Microflow name is required. Use the 'name' parameter (aliases accepted: 'microflow_name').";
                    SetLastError(error);
                    _logger.LogError("[create_microflow] Microflow name is missing in arguments.");
                    return JsonSerializer.Serialize(new { error });
                }

                var mfModuleName = arguments["module_name"]?.ToString();
                if (string.IsNullOrWhiteSpace(mfModuleName))
                {
                    var error = "module_name is required for create_microflow.";
                    SetLastError(error);
                    _logger.LogError("[create_microflow] module_name is missing in arguments.");
                    return JsonSerializer.Serialize(new { error });
                }

                // Resolve access level
                var accessLevelStr = arguments["access_level"]?.ToString() ?? "CheckPerOperation";
                if (!Enum.TryParse<MicroflowAccessLevel>(accessLevelStr, ignoreCase: true, out var accessLevel))
                    accessLevel = MicroflowAccessLevel.CheckPerOperation;

                // Resolve return type info
                var returnTypeStr = arguments["returnType"]?.ToString() ?? arguments["return_type"]?.ToString();
                var returnEntityStr = arguments["returnEntity"]?.ToString() ?? arguments["return_entity"]?.ToString();
                bool returnIsList = false;
                string? returnTypeQualifiedName = null;

                if (!string.IsNullOrWhiteSpace(returnTypeStr) &&
                    !returnTypeStr.Trim().Equals("void", StringComparison.OrdinalIgnoreCase))
                {
                    var normalizedReturn = returnTypeStr.Trim().ToLowerInvariant();
                    if (normalizedReturn == "list" && !string.IsNullOrWhiteSpace(returnEntityStr))
                    {
                        returnTypeQualifiedName = returnEntityStr;
                        returnIsList = true;
                    }
                    else if (normalizedReturn == "object" && !string.IsNullOrWhiteSpace(returnEntityStr))
                    {
                        returnTypeQualifiedName = returnEntityStr;
                        returnIsList = false;
                    }
                    else
                    {
                        // Scalar type (String, Integer, Boolean, etc.) — pass as-is
                        returnTypeQualifiedName = returnTypeStr.Trim();
                        returnIsList = false;
                    }
                }

                // Resolve parameters
                var parametersArray = arguments["parameters"]?.AsArray();
                var mfParams = new List<MicroflowParameter>();
                if (parametersArray != null)
                {
                    foreach (var param in parametersArray)
                    {
                        var paramObj = param?.AsObject();
                        if (paramObj == null)
                        {
                            _logger.LogError("[create_microflow] Parameter object is null in parameters array.");
                            continue;
                        }
                        var paramName = paramObj["name"]?.ToString();
                        var paramTypeStr = paramObj["type"]?.ToString();
                        var paramEntityStr = paramObj["entity"]?.ToString();
                        var paramDocStr = paramObj["documentation"]?.ToString();
                        var paramIsList = paramObj["is_list"]?.GetValue<bool>() ?? false;

                        if (string.IsNullOrWhiteSpace(paramName) || string.IsNullOrWhiteSpace(paramTypeStr))
                        {
                            _logger.LogError($"[create_microflow] Parameter missing name or type: {paramObj}");
                            continue;
                        }

                        // For object/list types, use entity as the qualified name
                        var normalizedParamType = paramTypeStr.Trim().ToLowerInvariant();
                        string typeQualifiedName;
                        bool isList;
                        if ((normalizedParamType == "object" || normalizedParamType == "list") &&
                            !string.IsNullOrWhiteSpace(paramEntityStr))
                        {
                            typeQualifiedName = paramEntityStr;
                            isList = normalizedParamType == "list" || paramIsList;
                        }
                        else
                        {
                            typeQualifiedName = paramTypeStr.Trim();
                            isList = paramIsList;
                        }

                        mfParams.Add(new MicroflowParameter(paramName, typeQualifiedName, isList, paramDocStr));
                    }
                }

                var documentation = arguments["documentation"]?.ToString();
                var folderPath = arguments["folder_path"]?.ToString() ?? arguments["folderPath"]?.ToString();

                var createRequest = new CreateMicroflowRequest(
                    ModuleName: mfModuleName,
                    Name: microflowName,
                    Parameters: mfParams,
                    ReturnTypeQualifiedName: returnTypeQualifiedName,
                    ReturnIsList: returnIsList,
                    AccessLevel: accessLevel,
                    Documentation: documentation,
                    FolderPath: folderPath);

                _logger.LogInformation($"[create_microflow] Calling HostServices.MicroflowAuthoring.Create: module={mfModuleName}, name={microflowName}, paramCount={mfParams.Count}");

                var documentId = HostServices.MicroflowAuthoring.Create(createRequest);

                _logger.LogInformation($"[create_microflow] Created microflow '{documentId.QualifiedName}' (guid={documentId.Value})");

                return JsonSerializer.Serialize(new {
                    success = true,
                    message = $"Microflow '{microflowName}' created successfully in module '{mfModuleName}'.",
                    document_id = documentId.Value,
                    qualified_name = documentId.QualifiedName,
                    microflow = new {
                        name = microflowName,
                        qualifiedName = documentId.QualifiedName,
                        module = mfModuleName,
                        returnType = returnTypeQualifiedName ?? "void",
                        parameterCount = mfParams.Count
                    }
                });
            }
            catch (Exception ex)
            {
                SetLastError($"Error in create_microflow: {ex.Message}", ex);
                _logger.LogError(ex, "[create_microflow] Unhandled exception");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Gets default expression strings for different data types
        /// </summary>
        private string GetDefaultExpressionForDataType(Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType dataType)
        {
            return dataType switch
            {
                var dt when dt == Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.String => "''",
                var dt when dt == Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.Integer => "0",
                var dt when dt == Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.Decimal => "0.0",
                var dt when dt == Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.Boolean => "false",
                var dt when dt == Mendix.StudioPro.ExtensionsAPI.Model.DataTypes.DataType.DateTime => "dateTime(1900)",
                _ => "empty"
            };
        }

        /// <summary>
        /// Normalizes Mendix expression strings by replacing double quotes with single quotes.
        /// Mendix expressions use single-quoted string literals ('Hello'). AI agents frequently
        /// pass double-quoted strings ("Hello") which cause CE0117 parse errors.
        /// Double quotes are never valid in Mendix expression syntax, so this replacement is safe.
        /// </summary>
        private static string NormalizeMendixExpression(string expression)
        {
            if (string.IsNullOrEmpty(expression))
                return expression;
            return expression.Replace('"', '\'');
        }

        /// <summary>
        /// Normalizes reduce expressions by qualifying $currentObject/Attribute paths with the entity's qualified name.
        /// Mendix requires entity-qualified attribute access: $currentObject/Module.Entity/Attribute
        /// </summary>
        private string NormalizeReduceExpression(string expr, string listVariable, JsonObject? activityData)
        {
            if (string.IsNullOrEmpty(expr)) return expr;

            // Try to resolve entity from context
            var entityName = activityData?["entity"]?.ToString() ??
                            activityData?["entity_name"]?.ToString() ??
                            activityData?["entityName"]?.ToString();
            if (string.IsNullOrEmpty(entityName))
            {
                _logger.LogWarning($"NormalizeReduceExpression: No entity context provided for list '{listVariable}', cannot qualify attribute paths");
                return expr;
            }

            var (entity, _) = Utils.Utils.FindEntityAcrossModules(_model, entityName);
            if (entity == null)
            {
                _logger.LogWarning($"NormalizeReduceExpression: Entity '{entityName}' not found, cannot qualify attribute paths");
                return expr;
            }

            // Replace $currentObject/AttrName with $currentObject/Module.Entity/AttrName
            // Only replace if not already qualified (path segment doesn't contain a dot)
            var normalized = Regex.Replace(expr, @"\$currentObject/([A-Za-z_]\w*)", match =>
            {
                var attrName = match.Groups[1].Value;
                if (attrName.Contains('.')) return match.Value; // Already qualified
                return $"$currentObject/{entity.QualifiedName}/{attrName}";
            });

            if (normalized != expr)
                _logger.LogInformation($"NormalizeReduceExpression: '{expr}' → '{normalized}'");

            return normalized;
        }

        public async Task<object> CreateMicroflowActivity(JsonObject arguments)
        {
            await Task.CompletedTask;
            try
            {
                if (!HostServices.MicroflowAuthoring.IsAvailable)
                {
                    var error = "MicroflowAuthoring host is not available in the current environment.";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                _logger.LogInformation("=== CreateMicroflowActivity (HostServices) ===");

                var microflowName = arguments["microflow_name"]?.ToString();
                var activityType = arguments["activity_type"]?.ToString();
                var activityData = arguments["activity_config"]?.AsObject();

                // BUG-005 fix: If no nested activity_config, use arguments itself as config (flat format)
                if (activityData == null && activityType != null)
                {
                    activityData = new JsonObject();
                    foreach (var prop in arguments)
                    {
                        if (prop.Key != "activity_type" && prop.Key != "microflow_name" &&
                            prop.Key != "module_name" && prop.Key != "insert_position" &&
                            prop.Key != "insert_after_activity_index")
                        {
                            activityData[prop.Key] = prop.Value?.DeepClone();
                        }
                    }
                }

                // Parse positioning parameters
                int? insertPosition = null;
                if (arguments.TryGetPropertyValue("insert_position", out var positionValue))
                {
                    if (positionValue != null && int.TryParse(positionValue.ToString(), out int pos))
                        insertPosition = pos;
                }
                // Alternative parameter name for backward compatibility
                if (!insertPosition.HasValue && arguments.TryGetPropertyValue("insert_after_activity_index", out var indexValue))
                {
                    if (indexValue != null && int.TryParse(indexValue.ToString(), out int idx))
                        insertPosition = idx + 1; // Convert from "after index" to position
                }

                _logger.LogInformation($"microflowName='{microflowName}', activityType='{activityType}', insertPosition={insertPosition}");

                if (string.IsNullOrWhiteSpace(microflowName))
                {
                    var error = "Microflow name is required.";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                if (string.IsNullOrWhiteSpace(activityType))
                {
                    // BUG-004 fix: Check if user used 'type' instead of 'activity_type'
                    var possibleType = arguments["type"]?.ToString();
                    var error = !string.IsNullOrWhiteSpace(possibleType)
                        ? $"Activity type is required. Did you mean 'activity_type' instead of 'type'? Found type='{possibleType}'. Use 'activity_type' as the field name."
                        : "Activity type is required. Use the 'activity_type' field to specify the type (e.g., 'create_object', 'retrieve_from_database', 'microflow_call').";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                // Resolve the fully-qualified microflow name (Module.MicroflowName)
                var actModuleName = arguments["module_name"]?.ToString();
                string qualifiedMfName;
                if (!string.IsNullOrWhiteSpace(actModuleName))
                {
                    qualifiedMfName = $"{actModuleName}.{microflowName}";
                }
                else if (microflowName.Contains('.'))
                {
                    qualifiedMfName = microflowName;
                    actModuleName = microflowName.Split('.')[0];
                }
                else
                {
                    // Try to find in any module
                    var allMfs = HostServices.MicroflowAuthoring.ListMicroflows(null);
                    var found = allMfs.FirstOrDefault(m => m.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));
                    if (found == null)
                    {
                        var error = $"Microflow '{microflowName}' not found. Specify module_name to disambiguate.";
                        SetLastError(error);
                        return JsonSerializer.Serialize(new { error });
                    }
                    qualifiedMfName = found.QualifiedName;
                    actModuleName = found.Module;
                }

                // Validate microflow exists
                var mfSummary = HostServices.MicroflowAuthoring.ReadMicroflow(qualifiedMfName);
                if (mfSummary == null)
                {
                    var error = $"Microflow '{qualifiedMfName}' not found.";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                // Validate activity type (preserve supported-types vocabulary)
                var normalizedType = activityType.ToLowerInvariant();
                var supportedTypes = new[]
                {
                    "create_object", "create_variable", "create",
                    "microflow_call", "call_microflow",
                    "change_variable", "change_value",
                    "retrieve", "retrieve_from_database", "retrieve_database", "database_retrieve",
                    "retrieve_by_association", "association_retrieve",
                    "commit_object", "commit_objects", "commit",
                    "rollback_object", "rollback",
                    "delete_object", "delete",
                    "create_list", "new_list",
                    "change_list", "modify_list",
                    "sort_list", "filter_list",
                    "find_in_list", "find_list_item",
                    "aggregate_list", "list_aggregate",
                    "java_action_call", "call_java_action",
                    "change_attribute", "change_association", "change_object",
                    "union_lists", "union",
                    "subtract_lists", "subtract",
                    "intersect_lists", "intersect",
                    "contains_in_list", "contains",
                    "head_of_list", "head",
                    "tail_of_list", "tail",
                    "reduce_list", "reduce",
                    "log", "log_message"
                };

                if (!supportedTypes.Contains(normalizedType))
                {
                    var error = $"Unsupported activity type: '{activityType}'. " +
                               $"Supported types: {string.Join(", ", supportedTypes.Distinct())}. " +
                               $"Note: For object changes, use 'change_object' (auto-detects), 'change_attribute' (for attributes), or 'change_association' (for references).";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error, supportedTypes });
                }

                // Build the parameters dictionary from activityData
                var parameters = new Dictionary<string, string>();
                if (activityData != null)
                {
                    foreach (var kv in activityData)
                    {
                        if (kv.Value != null)
                            parameters[kv.Key] = kv.Value.ToString() ?? "";
                    }
                }

                // Extract well-known summary fields from parameters
                var caption = activityData?["caption"]?.ToString();
                var outputVariable = activityData?["output_variable"]?.ToString()
                    ?? activityData?["outputVariable"]?.ToString()
                    ?? activityData?["variable_name"]?.ToString()
                    ?? activityData?["variableName"]?.ToString();
                var targetEntity = activityData?["entity"]?.ToString()
                    ?? activityData?["entity_name"]?.ToString()
                    ?? activityData?["entityName"]?.ToString();
                var targetMicroflow = activityData?["microflow"]?.ToString()
                    ?? activityData?["microflow_name"]?.ToString()
                    ?? activityData?["calledMicroflow"]?.ToString();
                var targetJavaAction = activityData?["java_action"]?.ToString()
                    ?? activityData?["javaAction"]?.ToString();

                var activitySummary = new MicroflowActivitySummary(
                    Position: insertPosition ?? 1,
                    ActivityType: normalizedType,
                    Caption: caption,
                    OutputVariable: outputVariable,
                    TargetEntity: targetEntity,
                    TargetMicroflow: targetMicroflow,
                    TargetJavaAction: targetJavaAction,
                    Parameters: parameters);

                var insertion = new ActivityInsertion(
                    MicroflowQualifiedName: qualifiedMfName,
                    InsertPosition: insertPosition,
                    Activity: activitySummary);

                _logger.LogInformation($"[create_microflow_activity] Calling HostServices.MicroflowAuthoring.AddActivity: mf={qualifiedMfName}, type={normalizedType}, position={insertPosition}");

                int actualPosition = HostServices.MicroflowAuthoring.AddActivity(insertion);

                _logger.LogInformation($"[create_microflow_activity] Inserted at position {actualPosition}");

                return JsonSerializer.Serialize(new {
                    success = true,
                    message = $"Activity of type '{activityType}' added to microflow '{microflowName}' successfully.",
                    position = actualPosition,
                    microflow = qualifiedMfName,
                    activity = new {
                        type = activityType,
                        microflow = microflowName,
                        module = actModuleName,
                        insertPosition = actualPosition
                    }
                });
            }
            catch (Exception ex)
            {
                SetLastError($"Error creating microflow activity: {ex.Message}", ex);
                _logger.LogError(ex, "Error in CreateMicroflowActivity");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #region Helper Methods

        private (bool IsValid, string Message, string? Details, int EntitiesProcessed, List<string> Warnings) ValidateDataStructure(JsonObject data, ModuleId moduleId)
        {
            try
            {
                int entitiesProcessed = 0;
                var validationIssues = new List<string>();
                var warnings = new List<string>();

                // Load entity refs for this module via IDomainModelHost
                var entityRefs = HostServices.DomainModel.ListEntities(moduleId);

                foreach (var entityData in data)
                {
                    // Extract entity name (handle both "ModuleName.EntityName" and "ModuleName_EntityName" formats)
                    var entityKey = entityData.Key;
                    var entityName = entityKey.Contains(".") ? entityKey.Split('.').Last() :
                                    entityKey.Contains("_") ? entityKey.Split('_').Last() : entityKey;

                    var entityRef = entityRefs
                        .FirstOrDefault(e => e.QualifiedName.Split('.').Last()
                            .Equals(entityName, StringComparison.OrdinalIgnoreCase));

                    if (entityRef.Equals(default(EntityRef)))
                    {
                        validationIssues.Add($"Entity '{entityName}' not found in domain model");
                        continue;
                    }

                    if (entityData.Value?.GetValueKind() != JsonValueKind.Array)
                    {
                        validationIssues.Add($"Data for entity '{entityName}' must be an array");
                        continue;
                    }

                    var records = entityData.Value.AsArray();
                    var recordIndex = 0;

                    // Read full entity shape for attribute and association metadata
                    var entityShape = HostServices.DomainModel.ReadEntity(entityRef);
                    var allAssociations = entityShape.OutgoingAssociations.Concat(entityShape.IncomingAssociations).ToList();
                    var knownAttrNames = entityShape.Attributes.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var knownAssocNames = allAssociations.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var knownRelatedEntityNames = allAssociations.Select(a =>
                    {
                        var parentName = a.ParentEntityQualifiedName.Split('.').Last();
                        var childName = a.ChildEntityQualifiedName.Split('.').Last();
                        return parentName.Equals(entityName, StringComparison.OrdinalIgnoreCase) ? childName : parentName;
                    }).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    foreach (var recordNode in records)
                    {
                        recordIndex++;
                        if (recordNode?.GetValueKind() != JsonValueKind.Object)
                        {
                            validationIssues.Add($"Record {recordIndex} in '{entityName}' must be an object");
                            continue;
                        }

                        var record = recordNode.AsObject();

                        // Check for required VirtualId if entity has associations
                        if (allAssociations.Any())
                        {
                            if (!record.ContainsKey("VirtualId") || record["VirtualId"]?.GetValueKind() != JsonValueKind.String)
                            {
                                validationIssues.Add($"Record {recordIndex} in '{entityName}' requires a 'VirtualId' property for relationships");
                                continue;
                            }
                        }

                        // Validate association references - look for both association names and entity names as relationship attributes
                        foreach (var association in allAssociations)
                        {
                            var assocName = association.Name;
                            var parentName = association.ParentEntityQualifiedName.Split('.').Last();
                            var childName = association.ChildEntityQualifiedName.Split('.').Last();
                            var relatedEntityName = parentName.Equals(entityName, StringComparison.OrdinalIgnoreCase) ? childName : parentName;

                            var relationshipKey = record.ContainsKey(relatedEntityName) ? relatedEntityName :
                                                 record.ContainsKey(assocName) ? assocName : null;

                            if (relationshipKey != null)
                            {
                                var assocValue = record[relationshipKey];
                                if (assocValue?.GetValueKind() == JsonValueKind.Object)
                                {
                                    var assocObj = assocValue.AsObject();
                                    if (!assocObj.ContainsKey("VirtualId") || assocObj["VirtualId"]?.GetValueKind() != JsonValueKind.String)
                                    {
                                        validationIssues.Add($"Relationship '{relationshipKey}' in record {recordIndex} of '{entityName}' must have a 'VirtualId' property. Format: {{ \"VirtualId\": \"UNIQUE_ID\" }}");
                                    }
                                }
                                else if (assocValue?.GetValueKind() != JsonValueKind.Null)
                                {
                                    validationIssues.Add($"Relationship '{relationshipKey}' in record {recordIndex} of '{entityName}' must be an object with VirtualId or null");
                                }
                            }
                        }

                        // Attribute type validation (warnings — non-blocking)
                        // Note: string max-length and enum-value checks are deferred pending
                        // IDomainModelHost extension for MaxLength and enumeration value lists
                        // (Task 14+ API gap: AttributeRef lacks MaxLength; enum values need separate IDomainModelHost query).
                        foreach (var attr in entityShape.Attributes)
                        {
                            if (!record.ContainsKey(attr.Name)) continue;
                            var value = record[attr.Name];
                            if (value == null || value.GetValueKind() == JsonValueKind.Null) continue;

                            try
                            {
                                if (attr.Kind == AttributeKind.DateTime)
                                {
                                    if (value.GetValueKind() == JsonValueKind.String)
                                    {
                                        if (!DateTime.TryParse(value.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out _))
                                            warnings.Add($"DateTime value for '{attr.Name}' in '{entityName}' record {recordIndex} is not valid ISO 8601");
                                    }
                                }
                                else if (attr.Kind == AttributeKind.Integer || attr.Kind == AttributeKind.LongType || attr.Kind == AttributeKind.AutoNumber)
                                {
                                    if (value.GetValueKind() != JsonValueKind.Number)
                                        warnings.Add($"Value for integer attribute '{attr.Name}' in '{entityName}' record {recordIndex} is not a number");
                                }
                                else if (attr.Kind == AttributeKind.Decimal)
                                {
                                    if (value.GetValueKind() != JsonValueKind.Number)
                                        warnings.Add($"Value for decimal attribute '{attr.Name}' in '{entityName}' record {recordIndex} is not a number");
                                }
                                else if (attr.Kind == AttributeKind.Boolean)
                                {
                                    if (value.GetValueKind() != JsonValueKind.True && value.GetValueKind() != JsonValueKind.False)
                                        warnings.Add($"Value for boolean attribute '{attr.Name}' in '{entityName}' record {recordIndex} is not a boolean");
                                }
                            }
                            catch { /* Skip validation errors for individual attributes */ }
                        }

                        // Warn on unrecognized attribute names
                        foreach (var prop in record)
                        {
                            if (prop.Key == "VirtualId") continue;
                            if (!knownAttrNames.Contains(prop.Key) && !knownAssocNames.Contains(prop.Key) && !knownRelatedEntityNames.Contains(prop.Key))
                                warnings.Add($"Unrecognized attribute '{prop.Key}' in '{entityName}' record {recordIndex}");
                        }
                    }

                    entitiesProcessed++;
                }

                if (validationIssues.Any())
                {
                    return (false, "Data validation failed", string.Join("; ", validationIssues), entitiesProcessed, warnings);
                }

                return (true, "Validation successful", null, entitiesProcessed, warnings);
            }
            catch (Exception ex)
            {
                return (false, $"Validation error: {ex.Message}", ex.StackTrace, 0, new List<string>());
            }
        }

        private string? GetSampleDataFilePath()
        {
            string? targetDirectory = null;
            if (!string.IsNullOrEmpty(_projectDirectory))
            {
                targetDirectory = _projectDirectory;
            }
            else
            {
                var assembly = Assembly.GetExecutingAssembly();
                var executingDirectory = Path.GetDirectoryName(assembly.Location);
                if (!string.IsNullOrEmpty(executingDirectory))
                {
                    var directory = new DirectoryInfo(executingDirectory);
                    targetDirectory = directory?.Parent?.Parent?.Parent?.FullName;
                }
            }
            if (string.IsNullOrEmpty(targetDirectory)) return null;
            return Path.Combine(targetDirectory, "resources", "SampleData.json");
        }

        private async Task<(bool Success, string? ErrorMessage, string? FilePath)> SaveDataToFile(JsonObject data)
        {
            var root = new JsonObject { ["data"] = data };
            return await SaveRootJsonToFile(root);
        }

        private async Task<(bool Success, string? ErrorMessage, string? FilePath)> SaveRootJsonToFile(JsonObject rootObject)
        {
            try
            {
                var filePath = GetSampleDataFilePath();
                if (string.IsNullOrEmpty(filePath))
                    return (false, "Could not determine sample data file path", null);

                var resourcesDir = Path.GetDirectoryName(filePath)!;
                if (!Directory.Exists(resourcesDir))
                    Directory.CreateDirectory(resourcesDir);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };

                var jsonData = rootObject.ToJsonString(options);
                await File.WriteAllTextAsync(filePath, jsonData);

                return (true, null, filePath);
            }
            catch (Exception ex)
            {
                return (false, $"Error saving data to file: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Standalone tool: wire up the sample data import pipeline (microflow + After Startup).
        /// </summary>
        public async Task<string> SetupDataImport(JsonObject arguments)
        {
            await Task.CompletedTask;
            try
            {
                var moduleName = arguments["module_name"]?.ToString();
                var microflowName = arguments["microflow_name"]?.ToString() ?? "ASu_LoadSampleData";
                var forceAfterStartup = arguments.ContainsKey("force_after_startup") && arguments["force_after_startup"]?.GetValue<bool>() == true;

                var result = SetupDataImportInternal(moduleName, microflowName, forceAfterStartup);
                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[setup_data_import] Unhandled exception");
                return JsonSerializer.Serialize(new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Core bootstrap logic: checks for Java action, creates microflow, wires After Startup.
        /// Used by both setup_data_import (standalone) and generate_sample_data (auto-setup).
        /// Returns a structured object (not serialized) so callers can embed it.
        /// </summary>
        private object SetupDataImportInternal(string? moduleName, string microflowName, bool forceAfterStartup)
        {
            var stepsCompleted = new List<string>();

            // --- STEP 1: Resolve target module name ---
            string resolvedModuleName;
            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                resolvedModuleName = moduleName;
            }
            else
            {
                var firstModule = HostServices.Model.ListModules().FirstOrDefault(m => !string.IsNullOrEmpty(m.Name));
                if (firstModule == null)
                    return new { success = false, error = "No module found. Specify module_name.", steps_completed = stepsCompleted };
                resolvedModuleName = firstModule.Name;
            }
            var qualifiedMfName = $"{resolvedModuleName}.{microflowName}";

            // --- STEP 2: Check Java action existence via HostServices.MicroflowAuthoring.ListJavaActions ---
            string? javaActionQualifiedName = null;
            try
            {
                // Search all modules via the Interop boundary
                var allJavaActions = HostServices.MicroflowAuthoring.ListJavaActions(null);
                foreach (var ja in allJavaActions)
                {
                    // DocumentId.QualifiedName carries "Module.ActionName"
                    if (ja.Document.QualifiedName?.EndsWith(".InsertDataFromJSON", StringComparison.OrdinalIgnoreCase) == true
                        || ja.Document.QualifiedName == "InsertDataFromJSON")
                    {
                        javaActionQualifiedName = ja.Document.QualifiedName;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[setup_data_import] Error searching for Java action via HostServices");
            }

            // Fallback: try untyped model query
            if (javaActionQualifiedName == null)
            {
                try
                {
                    var untypedRoot = GetUntypedModelRoot();
                    if (untypedRoot != null)
                    {
                        var elements = GetUnitsWithFallback(untypedRoot, "JavaActions$JavaAction");
                        foreach (var elem in elements)
                        {
                            try
                            {
                                if (elem.Name == "InsertDataFromJSON")
                                {
                                    javaActionQualifiedName = elem.QualifiedName ?? "SPMCP.InsertDataFromJSON";
                                    break;
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[setup_data_import] Untyped model fallback also failed");
                }
            }

            if (javaActionQualifiedName == null)
            {
                return new
                {
                    success = false,
                    error = "Java action 'InsertDataFromJSON' not found in model. The SPMCP marketplace module must be installed.",
                    hint = "Add the SPMCP module to your project, which provides the InsertDataFromJSON Java action for loading sample data at startup.",
                    steps_completed = stepsCompleted
                };
            }
            stepsCompleted.Add($"Found Java action: {javaActionQualifiedName}");

            // --- STEP 3: Check/create the After Startup microflow via HostServices.MicroflowAuthoring ---
            bool microflowCreated = false;
            var existingMfSummary = HostServices.MicroflowAuthoring.ReadMicroflow(qualifiedMfName);

            if (existingMfSummary == null)
            {
                try
                {
                    var createRequest = new CreateMicroflowRequest(
                        ModuleName: resolvedModuleName,
                        Name: microflowName,
                        Parameters: Array.Empty<MicroflowParameter>(),
                        ReturnTypeQualifiedName: "Boolean",
                        ReturnIsList: false,
                        AccessLevel: MicroflowAccessLevel.AllowAll,
                        Documentation: "Loads sample data from JSON file at application startup.",
                        FolderPath: null);

                    _logger.LogInformation($"[setup_data_import] Creating microflow '{qualifiedMfName}' via HostServices.MicroflowAuthoring.Create");
                    HostServices.MicroflowAuthoring.Create(createRequest);
                    microflowCreated = true;
                    stepsCompleted.Add($"Created microflow: {qualifiedMfName} (Boolean, returns true)");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[setup_data_import] Failed to create microflow");
                    return new { success = false, error = $"Failed to create microflow: {ex.Message}", steps_completed = stepsCompleted };
                }
            }
            else
            {
                stepsCompleted.Add($"Microflow already exists: {qualifiedMfName}");
            }

            // --- STEP 4: Check if microflow already has InsertDataFromJSON call ---
            bool activityAlreadyExists = false;
            bool activityCreated = false;
            try
            {
                var activities = HostServices.MicroflowAuthoring.ReadActivities(qualifiedMfName);
                foreach (var act in activities)
                {
                    if (act.ActivityType == "java_action_call"
                        && (act.TargetJavaAction?.Contains("InsertDataFromJSON", StringComparison.OrdinalIgnoreCase) == true
                            || act.Parameters.TryGetValue("java_action", out var jaParam) && jaParam.Contains("InsertDataFromJSON", StringComparison.OrdinalIgnoreCase)))
                    {
                        activityAlreadyExists = true;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[setup_data_import] Error checking microflow activities");
            }

            if (!activityAlreadyExists)
            {
                try
                {
                    var javaCallParams = new Dictionary<string, string>
                    {
                        ["java_action"] = javaActionQualifiedName,
                        ["use_return_variable"] = "false"
                    };
                    var javaCallActivity = new MicroflowActivitySummary(
                        Position: 1,
                        ActivityType: "java_action_call",
                        Caption: "Insert data from JSON",
                        OutputVariable: null,
                        TargetEntity: null,
                        TargetMicroflow: null,
                        TargetJavaAction: javaActionQualifiedName,
                        Parameters: javaCallParams);
                    var insertion = new ActivityInsertion(qualifiedMfName, 1, javaCallActivity);

                    _logger.LogInformation($"[setup_data_import] Adding java_action_call activity to '{qualifiedMfName}'");
                    HostServices.MicroflowAuthoring.AddActivity(insertion);
                    activityCreated = true;
                    stepsCompleted.Add($"Added Java action call: {javaActionQualifiedName}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[setup_data_import] Failed to add Java action call");
                    return new { success = false, error = $"Failed to add Java action call: {ex.Message}", steps_completed = stepsCompleted };
                }
            }
            else
            {
                stepsCompleted.Add("Java action call already exists in microflow");
            }

            // --- STEP 5: After Startup configuration ---
            // TODO(W2): No Interop equivalent for IRuntimeSettings.AfterStartupMicroflow yet.
            // GetSettingsPart<IRuntimeSettings>() requires _model.Root (IModel) which is absorbed at the host boundary.
            // After Startup wiring must be completed manually in Studio Pro or via the set_runtime_settings tool.
            string? afterStartupWarning =
                $"After Startup configuration requires manual action: set the After Startup microflow to '{qualifiedMfName}' " +
                $"in Studio Pro (App Settings → Runtime) or call the set_runtime_settings tool with after_startup_microflow='{qualifiedMfName}'.";
            stepsCompleted.Add("After Startup: escalation:manual (no Interop equivalent for IRuntimeSettings yet)");

            _logger.LogWarning("[setup_data_import] After Startup step skipped — escalation:manual (Task 14 gap: IRuntimeSettings not yet exposed via HostServices)");

            return new
            {
                success = true,
                message = "Sample data import pipeline configured (After Startup requires manual step — see warning)",
                java_action = new { found = true, qualified_name = javaActionQualifiedName },
                microflow = new { name = qualifiedMfName, created = microflowCreated, already_existed = !microflowCreated },
                java_action_call = new { added = activityCreated, already_existed = activityAlreadyExists },
                after_startup = new
                {
                    set_to = (string?)null,
                    changed = false,
                    warning = afterStartupWarning,
                    escalation = "manual"
                },
                steps_completed = stepsCompleted
            };
        }

        #region Sample Data Generation Helpers

        // --- Metadata Builder ---

        private JsonObject BuildMetadata(List<(EntityShape Entity, ModuleId Module)> entityPairs, List<ModuleId> modules)
        {
            var metadata = new JsonObject
            {
                ["version"] = 2,
                ["generated_by"] = "MCP Extension v74",
                ["generated_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["modules"] = new JsonArray(modules.Select(m => (JsonNode)JsonValue.Create(m.Name)!).ToArray())
            };

            // Build entity metadata (enum attributes)
            // NOTE: AttributeRef carries Kind but not the full enumeration document name.
            // Enum qualified name is unavailable at the Interop boundary via AttributeRef alone
            // (Task 14+ gap: would need IDomainModelHost.ReadEnumeration or similar).
            // We record the attribute as Enum type but omit enum_name.
            var entitiesMeta = new JsonObject();
            foreach (var (entity, mod) in entityPairs)
            {
                var qualifiedName = entity.Self.QualifiedName;
                var attrsMeta = new JsonObject();
                bool hasEnumAttrs = false;

                foreach (var attr in entity.Attributes)
                {
                    if (attr.Kind == AttributeKind.Enumeration)
                    {
                        attrsMeta[attr.Name] = new JsonObject
                        {
                            ["type"] = "Enum",
                            // enum_name not available via AttributeRef; omitted (Task 14+ gap).
                        };
                        hasEnumAttrs = true;
                    }
                }

                if (hasEnumAttrs)
                {
                    entitiesMeta[qualifiedName] = new JsonObject { ["attributes"] = attrsMeta };
                }
            }
            metadata["entities"] = entitiesMeta;

            // Build association metadata using EntityShape.OutgoingAssociations
            var assocsMeta = new JsonObject();
            var seenAssocs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (entity, mod) in entityPairs)
            {
                try
                {
                    foreach (var assoc in entity.OutgoingAssociations)
                    {
                        var qualifiedAssocName = $"{mod.Name}.{assoc.Name}";
                        if (seenAssocs.Contains(qualifiedAssocName)) continue;
                        seenAssocs.Add(qualifiedAssocName);

                        assocsMeta[qualifiedAssocName] = new JsonObject
                        {
                            ["parent"] = assoc.ParentEntityQualifiedName,
                            ["child"] = assoc.ChildEntityQualifiedName,
                            ["type"] = assoc.Type.ToString()
                        };
                    }
                }
                catch { /* Skip entity association errors */ }
            }
            metadata["associations"] = assocsMeta;

            return metadata;
        }

        // --- Topological Sort (Multi-Module) ---

        private List<(EntityShape Entity, ModuleId Module)> TopologicalSortEntitiesMultiModule(List<(EntityShape Entity, ModuleId Module)> entityPairs)
        {
            // Build set of all qualified names present in this batch
            var qualifiedNames = entityPairs.Select(p => p.Entity.Self.QualifiedName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var adjacency = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var (entity, mod) in entityPairs)
            {
                var qn = entity.Self.QualifiedName;
                inDegree[qn] = 0;
                adjacency[qn] = new List<string>();
            }

            foreach (var (entity, mod) in entityPairs)
            {
                var ownerQn = entity.Self.QualifiedName;
                try
                {
                    // Use OutgoingAssociations from EntityShape (parent → child edges)
                    foreach (var assoc in entity.OutgoingAssociations)
                    {
                        // ownerQn is the parent; assoc.ChildEntityQualifiedName is the child
                        var childQn = assoc.ChildEntityQualifiedName;

                        if (childQn != null && qualifiedNames.Contains(childQn) && !childQn.Equals(ownerQn, StringComparison.OrdinalIgnoreCase))
                        {
                            adjacency[childQn].Add(ownerQn);
                            inDegree[ownerQn] = inDegree.GetValueOrDefault(ownerQn) + 1;
                        }
                    }
                }
                catch { /* Skip */ }
            }

            // Kahn's algorithm
            var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            var sorted = new List<(EntityShape, ModuleId)>();
            var sortedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (queue.Count > 0)
            {
                var qn = queue.Dequeue();
                var pair = entityPairs.FirstOrDefault(p => p.Entity.Self.QualifiedName.Equals(qn, StringComparison.OrdinalIgnoreCase));
                if (pair.Entity != null)
                {
                    sorted.Add(pair);
                    sortedNames.Add(qn);
                }

                foreach (var dependent in adjacency.GetValueOrDefault(qn, new List<string>()))
                {
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0) queue.Enqueue(dependent);
                }
            }

            // Append remaining (cycles)
            foreach (var pair in entityPairs)
            {
                var qn = pair.Entity.Self.QualifiedName;
                if (!sortedNames.Contains(qn)) sorted.Add(pair);
            }

            return sorted;
        }

        // --- Record Generation (V2 format) ---

        private List<JsonObject> GenerateEntityRecordsV2(EntityShape entity, ModuleId module, int count, Random rng, Dictionary<string, List<string>> entityVirtualIds)
        {
            var records = new List<JsonObject>();
            var virtualIds = new List<string>();
            var qualifiedName = entity.Self.QualifiedName;
            // Simple name = part after the dot (e.g. "Customer" from "MyModule.Customer")
            var simpleName = qualifiedName.Contains('.')
                ? qualifiedName.Split('.', 2)[1]
                : qualifiedName;

            for (int i = 1; i <= count; i++)
            {
                var record = new JsonObject();
                var virtualId = $"{simpleName}_{i}";
                record["VirtualId"] = virtualId;
                virtualIds.Add(virtualId);

                // Generate attribute values using AttributeRef (no Mendix IAttribute needed)
                foreach (var attr in entity.Attributes)
                {
                    try
                    {
                        var value = GenerateAttributeValue(attr, simpleName, i, rng);
                        if (value != null)
                            record[attr.Name] = value;
                    }
                    catch { /* Skip */ }
                }

                // Generate association references in _associations format
                // Use OutgoingAssociations from EntityShape (entity is parent/owner)
                try
                {
                    var associationsObj = new JsonObject();
                    bool hasAssociations = false;

                    foreach (var assoc in entity.OutgoingAssociations)
                    {
                        var qualifiedAssocName = $"{module.Name}.{assoc.Name}";
                        // ChildEntityQualifiedName is "ChildModule.ChildEntity"
                        var childQualifiedName = assoc.ChildEntityQualifiedName;
                        var childSimpleName = childQualifiedName.Contains('.')
                            ? childQualifiedName.Split('.', 2)[1]
                            : childQualifiedName;

                        // Look for child VirtualIds by simple name or qualified name
                        List<string>? targetIds = null;
                        if (entityVirtualIds.ContainsKey(childSimpleName))
                            targetIds = entityVirtualIds[childSimpleName];
                        else if (entityVirtualIds.ContainsKey(childQualifiedName))
                            targetIds = entityVirtualIds[childQualifiedName];
                        else
                        {
                            var qualifiedChild = entityVirtualIds.Keys
                                .FirstOrDefault(k => k.EndsWith($".{childSimpleName}", StringComparison.OrdinalIgnoreCase));
                            if (qualifiedChild != null)
                                targetIds = entityVirtualIds[qualifiedChild];
                        }

                        if (targetIds != null && targetIds.Any())
                        {
                            var pickedId = targetIds[rng.Next(targetIds.Count)];
                            associationsObj[qualifiedAssocName] = new JsonObject { ["VirtualId"] = pickedId };
                            hasAssociations = true;
                        }
                    }

                    if (hasAssociations)
                        record["_associations"] = associationsObj;
                }
                catch { /* Skip association errors */ }

                records.Add(record);
            }

            // Store VirtualIds keyed by both simple name and qualified name
            entityVirtualIds[simpleName] = virtualIds;
            entityVirtualIds[qualifiedName] = virtualIds;
            return records;
        }

        // --- Attribute Value Generation ---

        private JsonNode? GenerateAttributeValue(AttributeRef attr, string entityName, int recordIndex, Random rng)
        {
            // Map AttributeKind (from Interop) to generated values.
            // AutoNumber and Binary are not generated — omit them.
            switch (attr.Kind)
            {
                case AttributeKind.AutoNumber:
                case AttributeKind.Binary:
                    return null;

                case AttributeKind.String:
                case AttributeKind.HashString:
                    if (attr.Kind == AttributeKind.HashString)
                        return JsonValue.Create("Password123!");
                    // String: maxLength is not on AttributeRef (gap — Task 14+); use 200 as safe default.
                    return JsonValue.Create(GenerateContextualString(attr.Name, entityName, 200, recordIndex, rng));

                case AttributeKind.Integer:
                {
                    var name = attr.Name.ToLowerInvariant();
                    if (name.Contains("rating") || name.Contains("score") || name.Contains("stars"))
                        return JsonValue.Create(rng.Next(1, 6)); // 1-5
                    if (name.Contains("sortorder") || name.Contains("sort_order") || name.Contains("priority") || name.Contains("rank") || name.Contains("order") || name.Contains("position") || name.Contains("index"))
                        return JsonValue.Create(recordIndex * 10); // 10, 20, 30...
                    if (name.Contains("age")) return JsonValue.Create(rng.Next(18, 81));
                    if (name.Contains("count") || name.Contains("quantity") || name.Contains("stock"))
                        return JsonValue.Create(rng.Next(1, 51)); // 1-50
                    if (name.Contains("year")) return JsonValue.Create(rng.Next(2020, 2027));
                    return JsonValue.Create(rng.Next(1, 101)); // 1-100 default
                }

                case AttributeKind.LongType:
                    return JsonValue.Create((long)rng.Next(1000, 100001));

                case AttributeKind.Decimal:
                {
                    var name = attr.Name.ToLowerInvariant();
                    if (name.Contains("price") || name.Contains("amount") || name.Contains("cost") || name.Contains("total") || name.Contains("balance") || name.Contains("fee"))
                        return JsonValue.Create(Math.Round(rng.NextDouble() * 499.98 + 0.01, 2)); // $0.01-$500
                    if (name.Contains("rate") || name.Contains("percentage") || name.Contains("percent") || name.Contains("discount"))
                        return JsonValue.Create(Math.Round(rng.NextDouble() * 100, 2));
                    if (name.Contains("weight") || name.Contains("length") || name.Contains("width") || name.Contains("height"))
                        return JsonValue.Create(Math.Round(rng.NextDouble() * 99.9 + 0.1, 2));
                    return JsonValue.Create(Math.Round(rng.NextDouble() * 999.98 + 0.01, 2));
                }

                case AttributeKind.Boolean:
                    return JsonValue.Create(rng.Next(2) == 1);

                case AttributeKind.DateTime:
                {
                    var daysAgo = rng.Next(0, 366);
                    var date = DateTime.UtcNow.AddDays(-daysAgo).AddHours(rng.Next(0, 24)).AddMinutes(rng.Next(0, 60));
                    return JsonValue.Create(date.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                }

                case AttributeKind.Enumeration:
                    // Enumeration values are not accessible via AttributeRef alone.
                    // Full enum value list requires IDomainModelHost.ListEnumerations + ReadEnumeration
                    // (Task 14+ gap). Return "Unknown" as a safe placeholder.
                    return JsonValue.Create("Unknown");

                case AttributeKind.Object:
                    return null;

                default:
                    return JsonValue.Create($"Sample_{attr.Name}_{recordIndex}");
            }
        }

        // --- Contextual String Data ---

        private static readonly string[] _firstNames = { "Alice", "Bob", "Charlie", "Diana", "Edward", "Fiona", "George", "Hannah", "Ivan", "Julia" };
        private static readonly string[] _lastNames = { "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez" };
        private static readonly string[] _cities = { "Amsterdam", "Rotterdam", "Utrecht", "Berlin", "London", "Paris", "Brussels", "Munich", "Madrid", "Vienna" };
        private static readonly string[] _countries = { "Netherlands", "Germany", "Belgium", "France", "United Kingdom", "Spain", "Austria", "Italy", "Switzerland", "Sweden" };
        private static readonly string[] _streets = { "Main Street", "Oak Avenue", "Elm Road", "Park Lane", "High Street", "Church Road", "Mill Lane", "Station Road", "Victoria Road", "King Street" };
        private static readonly string[] _titles = { "Project Alpha", "Quarterly Report", "System Update", "Customer Review", "Sales Analysis", "Product Launch", "Team Meeting", "Budget Plan", "Risk Assessment", "Process Improvement" };
        private static readonly string[] _productNames = { "Widget Pro", "Smart Sensor", "Power Bank X", "Eco Filter", "TurboCharge", "DataSync Hub", "AeroShield", "FlexMount", "CrystalView", "QuickDrive" };
        private static readonly string[] _categoryNames = { "Electronics", "Home & Garden", "Sports & Outdoors", "Books & Media", "Health & Beauty", "Automotive", "Toys & Games", "Office Supplies", "Food & Beverage", "Clothing" };
        private static readonly string[] _companyNames = { "Acme Corp", "TechVista", "GreenLeaf", "NorthStar", "BluePeak", "SilverLine", "RedWave", "GoldCore", "DeepRoot", "BrightPath" };
        private static readonly string[] _skuPrefixes = { "SKU", "PROD", "ITEM", "ART", "REF" };

        private string GenerateContextualString(string attributeName, string entityName, int maxLength, int recordIndex, Random rng)
        {
            var name = attributeName.ToLowerInvariant();
            var entName = entityName.ToLowerInvariant();
            string result;

            if (name.Contains("firstname") || name.Contains("first_name"))
                result = _firstNames[rng.Next(_firstNames.Length)];
            else if (name.Contains("lastname") || name.Contains("last_name") || name.Contains("surname"))
                result = _lastNames[rng.Next(_lastNames.Length)];
            else if (name.Contains("email"))
                result = $"{_firstNames[rng.Next(_firstNames.Length)].ToLower()}.{_lastNames[rng.Next(_lastNames.Length)].ToLower()}@example.com";
            else if (name.Contains("phone") || name.Contains("telephone") || name.Contains("mobile"))
                result = $"+1-555-{rng.Next(100, 999)}-{rng.Next(1000, 9999)}";
            else if (name.Contains("address") || name.Contains("street"))
                result = $"{rng.Next(1, 9999)} {_streets[rng.Next(_streets.Length)]}";
            else if (name.Contains("city"))
                result = _cities[rng.Next(_cities.Length)];
            else if (name.Contains("country"))
                result = _countries[rng.Next(_countries.Length)];
            else if (name.Contains("description") || name.Contains("notes") || name.Contains("comment") || name.Contains("remark"))
                result = $"Sample {attributeName.ToLower()} for record {recordIndex}.";
            else if (name.Contains("url") || name.Contains("website") || name.Contains("link"))
                result = $"https://www.example.com/{attributeName.ToLower()}/{recordIndex}";
            else if (name.Contains("sku"))
                result = $"{_skuPrefixes[rng.Next(_skuPrefixes.Length)]}-{rng.Next(10000, 99999)}";
            else if (name.Contains("code") || name.Contains("reference") || name.Contains("number"))
                result = $"REF-{rng.Next(10000, 99999)}";
            else if (name.Contains("title") || name.Contains("subject"))
                result = _titles[rng.Next(_titles.Length)];
            else if (name.Contains("company"))
                result = _companyNames[rng.Next(_companyNames.Length)];
            else if (name.Contains("name"))
            {
                // Context-aware name generation based on entity type
                if (entName.Contains("product")) result = _productNames[rng.Next(_productNames.Length)];
                else if (entName.Contains("category")) result = _categoryNames[rng.Next(_categoryNames.Length)];
                else if (entName.Contains("company") || entName.Contains("organization") || entName.Contains("supplier") || entName.Contains("vendor"))
                    result = _companyNames[rng.Next(_companyNames.Length)];
                else
                    result = $"{_firstNames[rng.Next(_firstNames.Length)]} {_lastNames[rng.Next(_lastNames.Length)]}";
            }
            else if (name.Contains("status"))
                result = new[] { "Active", "Pending", "Completed", "Cancelled" }[rng.Next(4)];
            else if (name.Contains("type") || name.Contains("category"))
                result = new[] { "TypeA", "TypeB", "TypeC" }[rng.Next(3)];
            else
                result = $"Sample_{attributeName}_{recordIndex}";

            if (maxLength > 0 && result.Length > maxLength)
                result = result.Substring(0, maxLength);

            return result;
        }

        #endregion

        /// <summary>
        /// Determines the optimal range (first/all) based on XPath constraint and variable naming patterns.
        /// </summary>
        /// <param name="xpath">XPath constraint string</param>
        /// <param name="outputVariable">Output variable name</param>
        /// <returns>Recommended range: "first" or "all"</returns>
        private string DetermineOptimalRange(string? xpath, string outputVariable)
        {
            try
            {
                // Default to "all" for safety
                string recommendedRange = "all";

                // Analyze XPath patterns that typically indicate single record lookup
                if (!string.IsNullOrEmpty(xpath))
                {
                    var xpathLower = xpath.ToLowerInvariant();
                    
                    // Look for ID-based constraints which typically return single records
                    if (xpathLower.Contains("id =") || 
                        xpathLower.Contains("id=") ||
                        xpathLower.Contains("[id =") ||
                        xpathLower.Contains("[id="))
                    {
                        recommendedRange = "first";
                        _logger.LogInformation($"Detected ID-based XPath constraint: '{xpath}' - recommending 'first' range");
                    }
                    // Look for unique key constraints
                    else if (xpathLower.Contains("email =") || 
                             xpathLower.Contains("email=") ||
                             xpathLower.Contains("username =") ||
                             xpathLower.Contains("username=") ||
                             xpathLower.Contains("code =") ||
                             xpathLower.Contains("code="))
                    {
                        recommendedRange = "first";
                        _logger.LogInformation($"Detected unique key constraint: '{xpath}' - recommending 'first' range");
                    }
                }

                // Analyze variable naming patterns
                var variableLower = outputVariable.ToLowerInvariant();
                if (variableLower.StartsWith("retrieved") && !variableLower.Contains("list") && !variableLower.Contains("collection"))
                {
                    // Variable names like "RetrievedCustomer" suggest single object
                    if (!variableLower.EndsWith("s") && !variableLower.Contains("objects"))
                    {
                        recommendedRange = "first";
                        _logger.LogInformation($"Variable name '{outputVariable}' suggests single object - recommending 'first' range");
                    }
                }

                _logger.LogInformation($"Determined optimal range: '{recommendedRange}' (XPath: '{xpath}', Variable: '{outputVariable}')");
                return recommendedRange;
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error in DetermineOptimalRange, defaulting to 'all': {ex.Message}");
                return "all";
            }
        }

        #region Sequential Activity Creation

        public async Task<object> CreateMicroflowActivitiesSequence(JsonObject arguments)
        {
            try
            {
                if (!HostServices.MicroflowAuthoring.IsAvailable)
                {
                    var error = "MicroflowAuthoring host is not available in the current environment.";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                _logger.LogInformation("=== CreateMicroflowActivitiesSequence (HostServices) ===");
                _logger.LogInformation($"Raw arguments received: {arguments?.ToJsonString()}");

                var microflowName = arguments["microflow_name"]?.ToString();
                var activitiesArray = arguments["activities"]?.AsArray();

                _logger.LogInformation($"Extracted microflowName: '{microflowName}'");
                _logger.LogInformation($"Extracted activities count: {activitiesArray?.Count ?? 0}");

                if (string.IsNullOrWhiteSpace(microflowName))
                {
                    var error = "Microflow name is required.";
                    _logger.LogError($"ERROR: {error}");
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                if (activitiesArray == null || activitiesArray.Count == 0)
                {
                    var error = "Activities array is required and must contain at least one activity.";
                    _logger.LogError($"ERROR: {error}");
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                // Resolve qualified microflow name
                var seqModuleName = arguments["module_name"]?.ToString();
                string qualifiedMfName;
                if (!string.IsNullOrWhiteSpace(seqModuleName))
                {
                    qualifiedMfName = $"{seqModuleName}.{microflowName}";
                }
                else if (microflowName.Contains('.'))
                {
                    qualifiedMfName = microflowName;
                    seqModuleName = microflowName.Split('.')[0];
                }
                else
                {
                    var allMfs = HostServices.MicroflowAuthoring.ListMicroflows(null);
                    var found = allMfs.FirstOrDefault(m => m.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));
                    if (found == null)
                    {
                        var error = $"Microflow '{microflowName}' not found. Specify module_name to disambiguate.";
                        SetLastError(error);
                        return JsonSerializer.Serialize(new { error });
                    }
                    qualifiedMfName = found.QualifiedName;
                    seqModuleName = found.Module;
                }

                // Validate microflow exists
                var mfSummary = HostServices.MicroflowAuthoring.ReadMicroflow(qualifiedMfName);
                if (mfSummary == null)
                {
                    var error = $"Microflow '{qualifiedMfName}' not found.";
                    SetLastError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                var activityResults = new List<object>();
                // Variable name tracking for propagation across activities
                var variableNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Debug logging to file
                var debugLogPath = GetDebugLogPath();
                await File.AppendAllTextAsync(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] VARIABLE TRACKING: Starting variable tracking for {activitiesArray.Count} activities\n");

                // Collect insertions in order; insert them in reverse so the first activity ends up first
                // (each insertion uses position=1 / "after start", so reverse order achieves correct sequence)
                var insertions = new List<(int index, string activityType, ActivityInsertion insertion)>();

                for (int i = 0; i < activitiesArray.Count; i++)
                {
                    var activityDef = activitiesArray[i]?.AsObject();
                    if (activityDef == null)
                    {
                        _logger.LogWarning($"Skipping null activity at index {i}");
                        continue;
                    }

                    var activityType = activityDef["activity_type"]?.ToString();
                    var activityConfig = activityDef["activity_config"]?.AsObject();

                    // BUG-005 fix: If no nested activity_config, use the activity definition itself as config
                    if (activityConfig == null && activityType != null)
                    {
                        activityConfig = new JsonObject();
                        foreach (var prop in activityDef)
                        {
                            if (prop.Key != "activity_type")
                                activityConfig[prop.Key] = prop.Value?.DeepClone();
                        }
                    }

                    _logger.LogInformation($"Processing activity {i + 1}: type='{activityType}'");
                    await File.AppendAllTextAsync(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] VARIABLE TRACKING: Processing activity {i + 1}: type='{activityType}'\n");

                    if (string.IsNullOrWhiteSpace(activityType))
                    {
                        // BUG-004 fix: Check for common misname 'type' instead of 'activity_type'
                        var possibleType = activityDef["type"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(possibleType))
                        {
                            _logger.LogWarning($"Activity at index {i} uses 'type' instead of 'activity_type'. Auto-correcting to '{possibleType}'.");
                            activityType = possibleType;
                        }
                        else
                        {
                            _logger.LogWarning($"Skipping activity at index {i} - no activity type specified.");
                            activityResults.Add(new { index = i + 1, type = (string?)null, status = "skipped", error = "No 'activity_type' field found. Use 'activity_type' (not 'type') to specify the activity." });
                            continue;
                        }
                    }

                    // BUG-019 fix: rebuild activityConfig if it was null when type was also null
                    if (activityConfig == null && activityType != null)
                    {
                        activityConfig = new JsonObject();
                        foreach (var prop in activityDef)
                        {
                            if (prop.Key != "activity_type" && prop.Key != "type")
                                activityConfig[prop.Key] = prop.Value?.DeepClone();
                        }
                    }

                    // Apply variable name substitutions
                    await File.AppendAllTextAsync(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] VARIABLE TRACKING: Applying substitutions with {variableNameMap.Count} mappings\n");
                    var processedConfig = ApplyVariableNameSubstitutions(activityConfig, variableNameMap);
                    await File.AppendAllTextAsync(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] VARIABLE TRACKING: Processed config: {processedConfig?.ToJsonString()}\n");

                    // Build MicroflowActivitySummary from the config
                    var normalizedType = activityType.ToLowerInvariant();
                    var parameters = new Dictionary<string, string>();
                    if (processedConfig != null)
                    {
                        foreach (var kv in processedConfig)
                        {
                            if (kv.Value != null)
                                parameters[kv.Key] = kv.Value.ToString() ?? "";
                        }
                    }

                    var caption = processedConfig?["caption"]?.ToString();
                    var outputVariable = processedConfig?["output_variable"]?.ToString()
                        ?? processedConfig?["outputVariable"]?.ToString()
                        ?? processedConfig?["variable_name"]?.ToString()
                        ?? processedConfig?["variableName"]?.ToString();
                    var targetEntity = processedConfig?["entity"]?.ToString()
                        ?? processedConfig?["entity_name"]?.ToString()
                        ?? processedConfig?["entityName"]?.ToString();
                    var targetMicroflow = processedConfig?["microflow"]?.ToString()
                        ?? processedConfig?["microflow_name"]?.ToString()
                        ?? processedConfig?["calledMicroflow"]?.ToString();
                    var targetJavaAction = processedConfig?["java_action"]?.ToString()
                        ?? processedConfig?["javaAction"]?.ToString();

                    var activitySummary = new MicroflowActivitySummary(
                        Position: 1,
                        ActivityType: normalizedType,
                        Caption: caption,
                        OutputVariable: outputVariable,
                        TargetEntity: targetEntity,
                        TargetMicroflow: targetMicroflow,
                        TargetJavaAction: targetJavaAction,
                        Parameters: parameters);

                    insertions.Add((i, activityType, new ActivityInsertion(qualifiedMfName, 1, activitySummary)));

                    // Track variable names for subsequent activities
                    TrackVariableNames(activityType, processedConfig, variableNameMap);
                    await File.AppendAllTextAsync(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] VARIABLE TRACKING: Variable map now has {variableNameMap.Count} entries\n");
                }

                if (insertions.Count == 0)
                {
                    var specificError = _lastError;
                    var error = !string.IsNullOrEmpty(specificError)
                        ? $"No activities were successfully prepared. Last error: {specificError}"
                        : "No activities were successfully prepared.";
                    if (string.IsNullOrEmpty(specificError))
                        SetLastError(error);
                    return JsonSerializer.Serialize(new { error, activityResults });
                }

                // Insert in reverse order so first activity ends up at position 1
                _logger.LogInformation($"Inserting {insertions.Count} activities via HostServices.MicroflowAuthoring.AddActivity (reverse order)");
                for (int j = insertions.Count - 1; j >= 0; j--)
                {
                    var (origIndex, actType, insertion) = insertions[j];
                    try
                    {
                        int actualPos = HostServices.MicroflowAuthoring.AddActivity(insertion);
                        activityResults.Add(new { index = origIndex + 1, type = actType, status = "inserted", position = actualPos });
                        _logger.LogInformation($"Inserted activity {origIndex + 1} (type='{actType}') at position {actualPos}");
                    }
                    catch (Exception insertEx)
                    {
                        _logger.LogError(insertEx, $"Failed to insert activity {origIndex + 1} (type='{actType}')");
                        activityResults.Add(new { index = origIndex + 1, type = actType, status = "failed", error = insertEx.Message });
                    }
                }

                int insertedCount = activityResults.Count(r =>
                {
                    var rObj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(System.Text.Json.JsonSerializer.Serialize(r));
                    return rObj.TryGetProperty("status", out var s) && s.GetString() == "inserted";
                });

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Successfully inserted {insertedCount} of {insertions.Count} activities into microflow '{microflowName}'",
                    microflow = qualifiedMfName,
                    module = seqModuleName,
                    activitiesCreated = insertedCount,
                    activities = activityResults
                });
            }
            catch (Exception ex)
            {
                SetLastError($"Error creating microflow activities sequence: {ex.Message}", ex);
                _logger.LogError(ex, "Error in CreateMicroflowActivitiesSequence");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Variable Name Tracking and Substitution

        /// <summary>
        /// Applies variable name substitutions to activity configuration based on tracked variables
        /// </summary>
        private JsonObject? ApplyVariableNameSubstitutions(JsonObject? activityConfig, Dictionary<string, string> variableNameMap)
        {
            if (activityConfig == null || variableNameMap.Count == 0)
            {
                var debugLogPath = GetDebugLogPath();
                File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: Early return - activityConfig null: {activityConfig == null}, variableNameMap count: {variableNameMap.Count}\n");
                return activityConfig;
            }

            try
            {
                var debugLogPath = GetDebugLogPath();
                File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: Starting substitutions\n");
                
                // Create a deep copy of the configuration to avoid modifying the original
                var configJson = activityConfig.ToJsonString();
                var processedConfig = JsonNode.Parse(configJson)?.AsObject();
                
                if (processedConfig == null)
                    return activityConfig;

                // Common variable name fields that might need substitution
                var variableFields = new[] 
                { 
                    "variable", "variableName", "variable_name", "inputVariable", "input_variable",
                    "objectVariable", "object_variable", "listVariable", "list_variable",
                    "sourceVariable", "source_variable", "targetVariable", "target_variable",
                    "object", "objects", "commit_objects", "variables"
                };

                _logger.LogInformation($"Applying variable substitutions with {variableNameMap.Count} mappings: {string.Join(", ", variableNameMap.Select(kvp => $"{kvp.Key}→{kvp.Value}"))}");

                foreach (var field in variableFields)
                {
                    if (processedConfig.ContainsKey(field))
                    {
                        var fieldValue = processedConfig[field];
                        File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: Found field '{field}' with value kind: {fieldValue?.GetValueKind()}\n");
                        
                        // Handle string fields
                        if (fieldValue?.GetValueKind() == JsonValueKind.String)
                        {
                            var currentValue = fieldValue.ToString();
                            File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: String field '{field}' has value '{currentValue}'\n");
                            
                            // Handle both plain variable names and $-prefixed variables
                            string lookupKey = currentValue;
                            if (currentValue.StartsWith("$"))
                            {
                                lookupKey = currentValue.Substring(1); // Remove $ prefix for lookup
                                File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: Found $-prefixed variable, lookup key: '{lookupKey}'\n");
                            }
                            
                            if (!string.IsNullOrEmpty(lookupKey) && variableNameMap.ContainsKey(lookupKey))
                            {
                                var actualVariableName = variableNameMap[lookupKey];
                                // For delete activities, we want just the variable name without $
                                processedConfig[field] = actualVariableName;
                                File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: ✅ Substituted variable '{currentValue}' with actual name '{actualVariableName}' in field '{field}'\n");
                                _logger.LogInformation($"Substituted variable '{currentValue}' with actual name '{actualVariableName}' in field '{field}'");
                            }
                        }
                        // Handle array fields (like "objects" in commit activities)
                        else if (fieldValue?.GetValueKind() == JsonValueKind.Array)
                        {
                            File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: Array field '{field}' has {fieldValue.AsArray().Count} elements\n");
                            var arrayValue = fieldValue.AsArray();
                            for (int i = 0; i < arrayValue.Count; i++)
                            {
                                var currentValue = arrayValue[i]?.ToString();
                                File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: Array element [{i}] = '{currentValue}'\n");
                                
                                if (!string.IsNullOrEmpty(currentValue))
                                {
                                    // Handle both plain variable names and $-prefixed variables
                                    string lookupKey = currentValue;
                                    if (currentValue.StartsWith("$"))
                                    {
                                        lookupKey = currentValue.Substring(1); // Remove $ prefix for lookup
                                    }
                                    
                                    if (variableNameMap.ContainsKey(lookupKey))
                                    {
                                        var actualVariableName = variableNameMap[lookupKey];
                                        // For activities that expect variable names, use just the name without $
                                        arrayValue[i] = actualVariableName;
                                        File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] APPLY_SUBSTITUTIONS: ✅ Substituted array variable '{currentValue}' with actual name '{actualVariableName}' in field '{field}[{i}]'\n");
                                        _logger.LogInformation($"Substituted array variable '{currentValue}' with actual name '{actualVariableName}' in field '{field}[{i}]'");
                                    }
                                }
                            }
                        }
                    }
                }

                return processedConfig;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error applying variable name substitutions, using original config");
                return activityConfig;
            }
        }

        /// <summary>
        /// Tracks variable names created by activities for future reference
        /// </summary>
        private void TrackVariableNames(string activityType, JsonObject? activityConfig, Dictionary<string, string> variableNameMap)
        {
            if (activityConfig == null)
                return;

            try
            {
                var debugLogPath = GetDebugLogPath();
                File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] TRACK_VARIABLES: Processing activity type '{activityType}'\n");
                
                string? logicalName = null;
                string? actualName = null;

                switch (activityType.ToLowerInvariant())
                {
                    case "retrieve_from_database":
                    case "retrieve_database":
                    case "database_retrieve":
                        File.AppendAllText(debugLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] TRACK_VARIABLES: Processing retrieve activity\n");
                        // For retrieve activities, track the mapping
                        logicalName = activityConfig["variable_name"]?.ToString();
                        
                        // Get the actual variable name that was used/created
                        actualName = activityConfig["outputVariable"]?.ToString() ?? 
                                   activityConfig["output"]?.ToString() ?? 
                                   activityConfig["output_variable"]?.ToString();
                        
                        // If no explicit output variable was specified, use the entity-based name  
                        if (string.IsNullOrEmpty(actualName))
                        {
                            var entityName = activityConfig["entityName"]?.ToString() ?? 
                                           activityConfig["entity"]?.ToString();
                            if (!string.IsNullOrEmpty(entityName))
                            {
                                var simpleEntityName = entityName.Contains('.') ? entityName.Split('.').Last() : entityName;
                                actualName = $"Retrieved{simpleEntityName}";
                            }
                            else
                            {
                                actualName = "RetrievedObjects";
                            }
                        }
                        break;

                    case "create_variable":
                    case "create_object":
                    case "create":
                        // For create activities
                        logicalName = activityConfig["variable_name"]?.ToString() ?? 
                                    activityConfig["variableName"]?.ToString();
                        actualName = logicalName; // Create activities typically use the specified name
                        break;

                    case "retrieve_by_association":
                    case "association_retrieve":
                        // For association retrieve activities
                        logicalName = activityConfig["variable_name"]?.ToString();
                        actualName = activityConfig["outputVariable"]?.ToString() ?? 
                                   activityConfig["output"]?.ToString() ?? 
                                   "AssociatedObjects";
                        break;

                    case "microflow_call":
                    case "call_microflow":
                        // For microflow calls that might return objects
                        logicalName = activityConfig["return_variable"]?.ToString() ?? 
                                    activityConfig["returnVariable"]?.ToString();
                        actualName = logicalName; // Microflow calls typically use the specified return variable name
                        break;
                }

                // Only track if we have both logical and actual names
                if (!string.IsNullOrEmpty(logicalName) && !string.IsNullOrEmpty(actualName))
                {
                    if (!logicalName.Equals(actualName, StringComparison.OrdinalIgnoreCase))
                    {
                        variableNameMap[logicalName] = actualName;
                        _logger.LogInformation($"Tracking variable mapping: '{logicalName}' -> '{actualName}'");
                    }
                    
                    // Also add self-mapping for the actual variable name for direct $-prefixed references
                    variableNameMap[actualName] = actualName;
                }
                    
                // For retrieve activities, also track entity-based logical names (e.g., "Customer" -> "RetrievedCustomer")
                if (activityType.ToLowerInvariant().Contains("retrieve") && !string.IsNullOrEmpty(actualName))
                {
                    var entityName = activityConfig["entityName"]?.ToString() ?? 
                                   activityConfig["entity"]?.ToString();
                    
                    if (!string.IsNullOrEmpty(entityName))
                    {
                        // Extract simple entity name (e.g., "MyFirstModule.Customer" -> "Customer")
                        var simpleEntityName = entityName.Contains('.') ? entityName.Split('.').Last() : entityName;
                        
                        if (!simpleEntityName.Equals(actualName, StringComparison.OrdinalIgnoreCase))
                        {
                            variableNameMap[simpleEntityName] = actualName;
                            _logger.LogInformation($"Tracking entity-based variable mapping: '{simpleEntityName}' -> '{actualName}'");
                        }
                        
                        // Also add self-mapping for the actual variable name for direct $-prefixed references
                        variableNameMap[actualName] = actualName;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Error tracking variable names for activity type '{activityType}'");
            }
        }

        #endregion

        #region Phase 9: Java Actions

        public async Task<string> ListJavaActions(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();
                ModuleId? moduleFilter = null;
                if (!string.IsNullOrEmpty(moduleName))
                {
                    var moduleId = HostServices.Model.GetModuleByName(moduleName);
                    if (moduleId == null)
                        return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });
                    moduleFilter = moduleId;
                }

                var javaActions = HostServices.MicroflowAuthoring.ListJavaActions(moduleFilter);
                var result = javaActions.Select(jad => new
                {
                    name = jad.Document.QualifiedName,
                    qualifiedName = jad.Document.QualifiedName,
                    module = jad.Module,
                    parameterCount = jad.ParameterNames.Count,
                    parameters = jad.ParameterNames.Select(p => new { name = p }).ToList<object>()
                }).ToList<object>();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    totalJavaActions = result.Count,
                    javaActions = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing Java actions");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 10: Project Settings & Runtime Configuration

        private IProjectSettings? GetProjectSettings()
        {
            var project = _model.Root as IProject;
            return project?.GetProjectDocuments().OfType<IProjectSettings>().FirstOrDefault();
        }

        private T? GetSettingsPart<T>() where T : class, IProjectSettingsPart
        {
            var settings = GetProjectSettings();
            return settings?.GetSettingsParts().OfType<T>().FirstOrDefault();
        }

        public async Task<string> ReadRuntimeSettings(JsonObject parameters)
        {
            try
            {
                var settings = HostServices.Model.ReadRuntimeSettings();
                var result = settings.Select(s => new
                {
                    key = s.Key,
                    value = s.Value,
                    description = s.Description
                }).ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    totalSettings = result.Count,
                    settings = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading runtime settings");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> SetRuntimeSettings(JsonObject parameters)
        {
            try
            {
                var key = parameters["key"]?.ToString();
                var value = parameters["value"]?.ToString();

                // Support batch: array of { key, value } pairs
                if (parameters["settings"] is JsonArray settingsArr)
                {
                    var results = new List<object>();
                    foreach (var item in settingsArr)
                    {
                        var k = item?["key"]?.ToString() ?? "";
                        var v = item?["value"]?.ToString();
                        var success = HostServices.Model.WriteRuntimeSetting(k, v);
                        results.Add(new { key = k, value = v, success });
                    }
                    return JsonSerializer.Serialize(new { success = true, results });
                }

                if (string.IsNullOrEmpty(key))
                    return JsonSerializer.Serialize(new { error = "key is required (or provide a settings array)" });

                var writeSuccess = HostServices.Model.WriteRuntimeSetting(key, value);
                return JsonSerializer.Serialize(new { success = writeSuccess, key, value });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting runtime settings");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ReadConfigurations(JsonObject parameters)
        {
            try
            {
                var configs = HostServices.Model.ReadConfigurations();
                var configName = parameters?["configuration_name"]?.ToString();

                var filtered = string.IsNullOrEmpty(configName)
                    ? configs
                    : configs.Where(c => c.Name.Equals(configName, StringComparison.OrdinalIgnoreCase)).ToList();

                // Note: IsActive, DatabaseType, DatabaseConnectionString are returned as false/null
                // by the typed API — this is expected per Phase 3 spike findings.
                var result = filtered.Select(c => new
                {
                    name = c.Name,
                    is_active = c.IsActive,
                    database_type = c.DatabaseType,
                    database_connection_string = c.DatabaseConnectionString,
                    custom_settings = c.CustomSettings
                }).ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    totalConfigurations = result.Count,
                    configurations = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading configurations");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> SetConfiguration(JsonObject parameters)
        {
            try
            {
                var configName = parameters["configuration_name"]?.ToString();
                if (string.IsNullOrEmpty(configName))
                    return JsonSerializer.Serialize(new { error = "configuration_name is required" });

                var setSuccess = HostServices.Model.SetActiveConfiguration(configName);
                if (!setSuccess)
                {
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        escalation = "manual",
                        message = "SetActiveConfiguration is not exposed in Core; toggle the configuration in Studio Pro."
                    });
                }

                return JsonSerializer.Serialize(new { success = true, configuration_name = configName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting configuration");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ReadVersionControl(JsonObject parameters)
        {
            try
            {
                if (!HostServices.VersionControl.IsAvailable)
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        available = false,
                        message = "Version control service not available on this Studio Pro version."
                    });

                var info = HostServices.VersionControl.Read();
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    available = true,
                    is_version_controlled = info.IsVersionControlled,
                    branch = info.BranchName,
                    commit_id = info.CommitId,
                    commit_author = info.CommitAuthor,
                    commit_date = info.CommitDate,
                    commit_message = info.CommitMessage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading version control info");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 11: Advanced Microflow Operations

        public async Task<string> SetMicroflowUrl(JsonObject parameters)
        {
            await Task.CompletedTask;
            try
            {
                var microflowName = parameters["microflow_name"]?.ToString();
                if (string.IsNullOrEmpty(microflowName))
                    return JsonSerializer.Serialize(new { error = "microflow_name is required" });

                var moduleName = parameters?["module_name"]?.ToString();

                // Build qualified name or search by unqualified name
                string qualifiedName;
                if (microflowName.Contains('.'))
                {
                    qualifiedName = microflowName;
                }
                else if (!string.IsNullOrWhiteSpace(moduleName))
                {
                    qualifiedName = $"{moduleName}.{microflowName}";
                }
                else
                {
                    // Search all microflows by unqualified name
                    var all = HostServices.MicroflowAuthoring.ListMicroflows(null);
                    var match = all.FirstOrDefault(m => m.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));
                    if (match == null)
                        return JsonSerializer.Serialize(new { error = $"Microflow '{microflowName}' not found" });
                    qualifiedName = match.QualifiedName;
                }

                // Verify existence
                var summary = HostServices.MicroflowAuthoring.ReadMicroflow(qualifiedName);
                if (summary == null)
                    return JsonSerializer.Serialize(new { error = $"Microflow '{qualifiedName}' not found" });

                if (parameters.ContainsKey("url"))
                {
                    var url = parameters["url"]?.ToString();
                    HostServices.MicroflowAuthoring.SetUrl(qualifiedName, url);

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        microflow = qualifiedName,
                        url,
                        message = string.IsNullOrEmpty(url) ? "URL cleared" : $"URL set to '{url}'"
                    });
                }
                else
                {
                    // Read-only: just return current info (url not accessible from summary)
                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        microflow = qualifiedName,
                        message = "Provide 'url' parameter to set the URL"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting microflow URL");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public Task<string> ListRules(JsonObject parameters)
        {
            // IRule (Mendix business/validation rules) is not exposed on the typed Interop
            // surface (IDomainModelHost, IModelHost, etc.). Surfacing it requires either a
            // new typed interface or untyped-model traversal via IModelRoot.GetModuleDocuments<IRule>,
            // which is not available through IUntypedModelHost. Deferred to a future release.
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success = false,
                escalation = "manual",
                message = "ListRules: Mendix IRule documents are not exposed on the typed Interop surface; " +
                          "route via IModelRoot.GetModuleDocuments<IRule> is deferred to a future release. " +
                          "Use Studio Pro's Project Explorer to inspect business/validation rules directly."
            }));
        }

        public async Task<string> ExcludeDocument(JsonObject parameters)
        {
            await Task.CompletedTask;
            try
            {
                var documentName = parameters["document_name"]?.ToString();
                if (string.IsNullOrEmpty(documentName))
                    return JsonSerializer.Serialize(new { error = "document_name is required" });

                var moduleName = parameters?["module_name"]?.ToString();
                var excluded = parameters?["excluded"]?.GetValue<bool>() ?? true;

                // Build qualified name: Module.Document or search all modules
                string qualifiedName;
                if (documentName.Contains('.'))
                {
                    qualifiedName = documentName;
                }
                else if (!string.IsNullOrWhiteSpace(moduleName))
                {
                    qualifiedName = $"{moduleName}.{documentName}";
                }
                else
                {
                    // Search all modules for the document by unqualified name
                    var allModules = HostServices.Model.ListModules();
                    DocumentId? found = null;
                    foreach (var mod in allModules)
                    {
                        var docs = HostServices.Model.ListModuleDocuments(mod);
                        var match = docs.FirstOrDefault(d =>
                            d.QualifiedName.EndsWith("." + documentName, StringComparison.OrdinalIgnoreCase) ||
                            d.QualifiedName.Equals(documentName, StringComparison.OrdinalIgnoreCase));
                        if (match.Value != Guid.Empty)
                        {
                            found = match;
                            break;
                        }
                    }
                    if (found == null)
                        return JsonSerializer.Serialize(new { error = $"Document '{documentName}' not found" });
                    qualifiedName = found.Value.QualifiedName;
                }

                var documentId = HostServices.Model.GetDocumentByQualifiedName(qualifiedName);
                if (documentId == null)
                    return JsonSerializer.Serialize(new { error = $"Document '{qualifiedName}' not found" });

                var result = HostServices.Model.SetDocumentExcluded(documentId.Value, excluded);

                return JsonSerializer.Serialize(new
                {
                    success = result,
                    document = documentId.Value.QualifiedName,
                    excluded
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error excluding document");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 12: Untyped Model Introspection

        private IModelRoot? GetUntypedModelRoot()
        {
            return _untypedModelService?.GetUntypedModel(_model);
        }

        private List<IModelUnit> GetUnitsWithFallback(IModelRoot root, string typeString)
        {
            // Try with $ separator first, then . separator
            var units = root.GetUnitsOfType(typeString)?.ToList() ?? new List<IModelUnit>();
            if (units.Count == 0 && typeString.Contains("$"))
            {
                units = root.GetUnitsOfType(typeString.Replace("$", "."))?.ToList() ?? new List<IModelUnit>();
            }
            return units;
        }

        private object SerializeModelUnit(IModelUnit unit, bool includeProperties = false, int maxProperties = 20)
        {
            var result = new Dictionary<string, object?>();
            result["name"] = unit.Name;
            result["qualifiedName"] = unit.QualifiedName;
            result["type"] = unit.Type;

            if (includeProperties)
            {
                var props = unit.GetProperties().Take(maxProperties).Select(p =>
                {
                    object? val = null;
                    try
                    {
                        if (p.IsList)
                        {
                            var values = p.GetValues();
                            val = values?.Take(5).Select(v => v?.ToString()).ToList();
                        }
                        else
                        {
                            val = p.Value?.ToString();
                        }
                    }
                    catch { val = "<error reading value>"; }

                    return new { name = p.Name, type = p.Type.ToString(), isList = p.IsList, value = val };
                }).ToList();

                result["properties"] = props;
            }

            return result;
        }

        public Task<string> ReadSecurityInfo(JsonObject parameters)
        {
            // Mendix module security (IModuleSecurity) and project security (IProjectSecurity)
            // are accessible only through IUntypedModelAccessService.GetUntypedModel → IModelRoot,
            // which requires IModelRoot.GetUnitsOfType + IModelUnit.GetElementsOfType traversal.
            // IUntypedModelHost exposes only GetUnitsOfType (returns UntypedUnitDescriptor with no
            // sub-element traversal). Full security introspection is not achievable through the
            // current typed Interop surface. Deferred to a future release.
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success = false,
                escalation = "manual",
                message = "ReadSecurityInfo: Mendix project/module security (IModuleSecurity, IProjectSecurity) " +
                          "requires IModelRoot sub-element traversal that is not exposed on the typed Interop surface. " +
                          "Read security settings in Studio Pro's Security dialog directly."
            }));
        }

        /// <summary>Helper to safely read a property value from an untyped model element</summary>
        private object? ReadPropValue(IModelStructure element, string propertyName)
        {
            try
            {
                var prop = element.GetProperty(propertyName);
                if (prop == null) return null;
                if (prop.IsList)
                {
                    var values = prop.GetValues();
                    return values?.Select(v => v?.ToString()).ToList();
                }
                return prop.Value?.ToString();
            }
            catch { return null; }
        }

        #endregion

        #region Phase 23: Security Introspection

        public Task<string> ReadEntityAccessRules(JsonObject parameters)
        {
            // Entity access rules (IEntityAccessRule / DomainModels$AccessRule) require
            // IModelRoot.GetUnitsOfType → IModelUnit.GetElementsOfType sub-traversal,
            // which is not exposed through IUntypedModelHost (only flat GetUnitsOfType is
            // available). IDomainModelHost does not surface IEntity.AccessRules. Deferred.
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success = false,
                escalation = "manual",
                message = "ReadEntityAccessRules: Entity access rules (DomainModels$AccessRule) require " +
                          "IModelUnit sub-element traversal that is not exposed on the typed Interop surface. " +
                          "Inspect entity access rules in Studio Pro's Domain Model editor directly."
            }));
        }

        public Task<string> ReadMicroflowSecurity(JsonObject parameters)
        {
            // Microflow allowed-roles (IMicroflow.AllowedRoles / allowedModuleRoles property)
            // requires IModelUnit property traversal via IModelRoot, which is not exposed through
            // IUntypedModelHost. IMicroflowAuthoringHost.ReadMicroflow returns AccessLevel (enum)
            // but not the specific role list. Full role-level security introspection is deferred.
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success = false,
                escalation = "manual",
                message = "ReadMicroflowSecurity: Microflow allowed-roles (allowedModuleRoles) requires " +
                          "IModelUnit property traversal not exposed on the typed Interop surface. " +
                          "IMicroflowAuthoringHost.ReadMicroflow exposes AccessLevel enum only. " +
                          "Inspect microflow security in Studio Pro's Microflow Properties dialog directly."
            }));
        }

        public Task<string> AuditSecurity(JsonObject parameters)
        {
            // Full security audit requires IModelRoot sub-element traversal across
            // Security$ProjectSecurity, Security$ModuleSecurity, DomainModels$AccessRule, and
            // Microflows$Microflow.allowedModuleRoles — none of which are accessible through
            // IUntypedModelHost (flat GetUnitsOfType only) or the other typed Interop surfaces.
            // Deferred to a future release that surfaces a richer untyped-model traversal API.
            return Task.FromResult(JsonSerializer.Serialize(new
            {
                success = false,
                escalation = "manual",
                message = "AuditSecurity: Full security audit (project security level, entity access rules, " +
                          "orphaned module roles) requires IModelRoot sub-element traversal not exposed on " +
                          "the typed Interop surface. Run the security audit in Studio Pro's Security Overview directly."
            }));
        }

        #endregion

        #region Phase 24: Nanoflow Introspection

        /// <summary>Maps untyped model DataType type strings to friendly names</summary>
        private string MapReturnType(IModelUnit unit)
        {
            try
            {
                var rtProp = unit.GetProperty("microflowReturnType");
                if (rtProp == null) return "Unknown";

                var rtVal = rtProp.Value;
                if (rtVal == null) return "Unknown";

                // rtVal is an IModelStructure child element — get its Type
                if (rtVal is IModelStructure rtElement)
                {
                    var typeName = rtElement.Type ?? "";
                    return typeName switch
                    {
                        "DataTypes$VoidType" => "Void",
                        "DataTypes$BooleanType" => "Boolean",
                        "DataTypes$StringType" => "String",
                        "DataTypes$IntegerType" => "Integer",
                        "DataTypes$LongType" => "Long",
                        "DataTypes$DecimalType" => "Decimal",
                        "DataTypes$FloatType" => "Float",
                        "DataTypes$DateTimeType" => "DateTime",
                        "DataTypes$BinaryType" => "Binary",
                        "DataTypes$ObjectType" => ExtractEntityFromDataType(rtElement, "Object"),
                        "DataTypes$ListType" => ExtractEntityFromDataType(rtElement, "List"),
                        "DataTypes$EnumerationType" => ExtractEnumFromDataType(rtElement),
                        _ => typeName.Contains("$") ? typeName.Split('$').Last() : typeName
                    };
                }

                return rtVal.ToString() ?? "Unknown";
            }
            catch { return "Unknown"; }
        }

        private string ExtractEntityFromDataType(IModelStructure rtElement, string prefix)
        {
            try
            {
                var entityProp = rtElement.GetProperty("entity") ?? rtElement.GetProperty("entityRef");
                if (entityProp != null)
                {
                    var val = entityProp.Value?.ToString();
                    if (!string.IsNullOrEmpty(val))
                        return $"{prefix}<{val}>";
                }
            }
            catch { }
            return prefix;
        }

        private string ExtractEnumFromDataType(IModelStructure rtElement)
        {
            try
            {
                var enumProp = rtElement.GetProperty("enumeration") ?? rtElement.GetProperty("enumerationRef");
                if (enumProp != null)
                {
                    var val = enumProp.Value?.ToString();
                    if (!string.IsNullOrEmpty(val))
                        return $"Enum<{val}>";
                }
            }
            catch { }
            return "Enumeration";
        }

        /// <summary>Counts activities in a nanoflow/microflow objectCollection</summary>
        private (int activityCount, int flowCount, int paramCount) CountFlowElements(IModelUnit unit)
        {
            int activityCount = 0, flowCount = 0, paramCount = 0;
            try
            {
                // Count flows
                var flowsProp = unit.GetProperty("flows");
                if (flowsProp != null && flowsProp.IsList)
                    flowCount = flowsProp.GetValues()?.Count() ?? 0;

                // Count activities from objectCollection
                var objCollProp = unit.GetProperty("objectCollection");
                if (objCollProp?.Value is IModelStructure objColl)
                {
                    var objects = objColl.GetProperty("objects");
                    if (objects != null && objects.IsList)
                    {
                        var vals = objects.GetValues();
                        if (vals != null)
                        {
                            foreach (var v in vals)
                            {
                                if (v is IModelStructure obj)
                                {
                                    var typeName = obj.Type ?? "";
                                    if (typeName.Contains("ParameterObject"))
                                        paramCount++;
                                    else if (!typeName.Contains("StartEvent") && !typeName.Contains("EndEvent"))
                                        activityCount++;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return (activityCount, flowCount, paramCount);
        }

        public async Task<string> ReadNanoflowDetails(JsonObject parameters)
        {
            try
            {
                var nanoflowName = parameters?["nanoflow_name"]?.ToString();
                var moduleName = parameters?["module_name"]?.ToString();

                if (string.IsNullOrEmpty(nanoflowName))
                    return JsonSerializer.Serialize(new { error = "nanoflow_name is required" });

                // Build qualified name
                string qualifiedName;
                if (nanoflowName.Contains("."))
                {
                    qualifiedName = nanoflowName;
                }
                else if (!string.IsNullOrWhiteSpace(moduleName))
                {
                    qualifiedName = $"{moduleName}.{nanoflowName}";
                }
                else
                {
                    qualifiedName = nanoflowName;
                }

                var summary = HostServices.MicroflowAuthoring.ReadNanoflow(qualifiedName);
                if (summary == null)
                    return JsonSerializer.Serialize(new { success = false, error = $"Nanoflow '{qualifiedName}' not found" });

                // Parameters from NanoflowSummary.Parameters
                var parameterList = summary.Parameters.Select(p => new
                {
                    name = p.Name,
                    type = p.TypeQualifiedName,
                    isList = p.IsList,
                    documentation = p.Documentation
                }).ToList<object>();

                // Nanoflow activity detail is not available via the typed API.
                // Return an empty list with an explanatory note per Task 13 gap guidance.
                var activityList = new List<object>();

                var result = new Dictionary<string, object?>
                {
                    ["success"] = true,
                    ["name"] = summary.Name,
                    ["qualifiedName"] = summary.QualifiedName,
                    ["module"] = summary.Module,
                    ["type"] = "Nanoflow",
                    ["documentation"] = summary.Documentation,
                    ["returnType"] = summary.ReturnTypeQualifiedName,
                    ["parameterCount"] = parameterList.Count,
                    ["parameters"] = parameterList,
                    ["activityCount"] = summary.ActivityCount,
                    ["activities"] = activityList,
                    ["activitiesNote"] = "Nanoflow activity details not available via typed API",
                    ["allowedRoleCount"] = summary.AllowedRoleCount
                };

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading nanoflow details");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        public async Task<string> ListNanoflows(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters?["module_name"]?.ToString();

                ModuleId? moduleFilter = null;
                if (!string.IsNullOrWhiteSpace(moduleName))
                {
                    moduleFilter = HostServices.Model.GetModuleByName(moduleName);
                    if (moduleFilter == null)
                        return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found." });
                }

                var nanoflowSummaries = HostServices.MicroflowAuthoring.ListNanoflows(moduleFilter);

                var result = nanoflowSummaries.Select(nf => (object)new
                {
                    name = nf.Name,
                    qualifiedName = nf.QualifiedName,
                    module = nf.Module,
                    documentation = nf.Documentation,
                    returnType = nf.ReturnTypeQualifiedName,
                    parameterCount = nf.Parameters.Count,
                    activityCount = nf.ActivityCount,
                    allowedRoleCount = nf.AllowedRoleCount
                }).ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    totalNanoflows = result.Count,
                    nanoflows = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing nanoflows");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ListScheduledEvents(JsonObject parameters)
        {
            try
            {
                var root = GetUntypedModelRoot();
                if (root == null)
                    return JsonSerializer.Serialize(new { error = "IUntypedModelAccessService is not available" });

                var moduleName = parameters?["module_name"]?.ToString();
                var events = GetUnitsWithFallback(root, "ScheduledEvents$ScheduledEvent");

                var result = events
                    .Where(e => string.IsNullOrEmpty(moduleName) ||
                                (e.QualifiedName?.Contains(moduleName, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Select(e =>
                    {
                        var enabled = e.GetProperty("Enabled")?.Value?.ToString();
                        var interval = e.GetProperty("Interval")?.Value?.ToString();
                        var intervalType = e.GetProperty("IntervalType")?.Value?.ToString();
                        var startOffset = e.GetProperty("StartOffset")?.Value?.ToString();

                        return new
                        {
                            name = e.Name,
                            qualifiedName = e.QualifiedName,
                            module = e.QualifiedName?.Split('.').FirstOrDefault(),
                            enabled,
                            interval,
                            intervalType,
                            startOffset
                        };
                    })
                    .ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    totalScheduledEvents = result.Count,
                    scheduledEvents = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing scheduled events");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ListRestServices(JsonObject parameters)
        {
            try
            {
                var root = GetUntypedModelRoot();
                if (root == null)
                    return JsonSerializer.Serialize(new { error = "IUntypedModelAccessService is not available" });

                var moduleName = parameters?["module_name"]?.ToString();
                var services = GetUnitsWithFallback(root, "Rest$PublishedRestService");

                var result = services
                    .Where(s => string.IsNullOrEmpty(moduleName) ||
                                (s.QualifiedName?.Contains(moduleName, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Select(s =>
                    {
                        var path = s.GetProperty("Path")?.Value?.ToString();
                        var version = s.GetProperty("Version")?.Value?.ToString();
                        var authentication = s.GetProperty("AuthenticationType")?.Value?.ToString();

                        var resources = s.GetElementsOfType("Rest$PublishedRestServiceResource")
                            .Select(r => new
                            {
                                name = r.Name,
                                type = r.Type
                            })
                            .ToList();

                        return new
                        {
                            name = s.Name,
                            qualifiedName = s.QualifiedName,
                            module = s.QualifiedName?.Split('.').FirstOrDefault(),
                            path,
                            version,
                            authentication,
                            resourceCount = resources.Count,
                            resources
                        };
                    })
                    .ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    totalRestServices = result.Count,
                    restServices = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing REST services");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> QueryModelElements(JsonObject parameters)
        {
            try
            {
                var root = GetUntypedModelRoot();
                if (root == null)
                    return JsonSerializer.Serialize(new { error = "IUntypedModelAccessService is not available" });

                var typeName = parameters["type_name"]?.ToString();
                if (string.IsNullOrEmpty(typeName))
                    return JsonSerializer.Serialize(new { error = "type_name is required (e.g. 'Navigation$NavigationProfile', 'Microflows$Nanoflow')" });

                var moduleName = parameters?["module_name"]?.ToString();
                var includeProperties = parameters?["include_properties"]?.GetValue<bool>() ?? false;
                var maxResults = parameters?["max_results"]?.GetValue<int>() ?? 50;

                var units = GetUnitsWithFallback(root, typeName);

                // BUG-014 fix: For embedded types (Entity, Association, Attribute), use the typed API
                // since GetUnitsOfType only works for top-level document units
                var normalizedType = typeName.ToLowerInvariant();
                if (units.Count == 0 && (normalizedType.Contains("entity") || normalizedType.Contains("association") || normalizedType.Contains("attribute")))
                {
                    var embeddedResults = new List<object>();
                    var modules = string.IsNullOrEmpty(moduleName)
                        ? Utils.Utils.GetAllNonAppStoreModules(_model).ToList()
                        : new List<IModule> { Utils.Utils.GetModuleByName(_model, moduleName)! }.Where(m => m != null).ToList();

                    foreach (var mod in modules)
                    {
                        if (mod?.DomainModel == null) continue;

                        if (normalizedType.Contains("entity") && !normalizedType.Contains("association"))
                        {
                            foreach (var entity in mod.DomainModel.GetEntities().Take(maxResults - embeddedResults.Count))
                            {
                                var entityInfo = new Dictionary<string, object?>
                                {
                                    ["name"] = entity.Name,
                                    ["qualifiedName"] = entity.QualifiedName?.ToString(),
                                    ["module"] = mod.Name,
                                    ["type"] = "DomainModels$Entity"
                                };
                                if (includeProperties)
                                {
                                    entityInfo["attributes"] = entity.GetAttributes().Select(a => new { name = a.Name, type = a.Type?.GetType().Name }).ToList();
                                    entityInfo["attributeCount"] = entity.GetAttributes().Count();
                                    entityInfo["associationCount"] = entity.GetAssociations(AssociationDirection.Both, null).Count();
                                }
                                embeddedResults.Add(entityInfo);
                                if (embeddedResults.Count >= maxResults) break;
                            }
                        }
                        else if (normalizedType.Contains("association"))
                        {
                            foreach (var entity in mod.DomainModel.GetEntities())
                            {
                                foreach (var assoc in entity.GetAssociations(AssociationDirection.Both, null).Take(maxResults - embeddedResults.Count))
                                {
                                    var assocInfo = new Dictionary<string, object?>
                                    {
                                        ["name"] = assoc.Association?.Name,
                                        ["qualifiedName"] = assoc.Association?.Name != null ? $"{mod.Name}.{assoc.Association.Name}" : null,
                                        ["module"] = mod.Name,
                                        ["type"] = "DomainModels$Association",
                                        ["parent"] = assoc.Parent?.Name,
                                        ["child"] = assoc.Child?.Name
                                    };
                                    embeddedResults.Add(assocInfo);
                                    if (embeddedResults.Count >= maxResults) break;
                                }
                                if (embeddedResults.Count >= maxResults) break;
                            }
                        }
                        if (embeddedResults.Count >= maxResults) break;
                    }

                    return JsonSerializer.Serialize(new
                    {
                        success = true,
                        typeName,
                        totalFound = embeddedResults.Count,
                        returned = embeddedResults.Count,
                        elements = embeddedResults,
                        note = "Results obtained via typed domain model API (embedded elements are not accessible via GetUnitsOfType)"
                    });
                }

                var filtered = units
                    .Where(u => string.IsNullOrEmpty(moduleName) ||
                                (u.QualifiedName?.Contains(moduleName, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Take(maxResults)
                    .Select(u => SerializeModelUnit(u, includeProperties))
                    .ToList();

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    typeName,
                    totalFound = units.Count,
                    returned = filtered.Count,
                    elements = filtered
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying model elements");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #region Phase 15: Navigation Management

        public async Task<string> ManageNavigation(JsonObject parameters)
        {
            await Task.CompletedTask;
            try
            {
                // INavigationHost exposes AddItems (append-only via PopulateWebNavigationWith).
                // List/remove/modify operations are not exposed on the typed API surface —
                // those paths surface escalation:manual.
                var action = parameters["action"]?.ToString()?.ToLowerInvariant() ?? "add";

                if (action is "list" or "remove" or "set_icon" or "set_target")
                {
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        escalation = "manual",
                        action,
                        message = $"Navigation action '{action}' is not available on the typed INavigationHost API. " +
                                  "Only 'add' (append items to the Responsive profile) is supported. " +
                                  "Use Studio Pro UI to list, remove, or modify existing navigation items."
                    });
                }

                // Default / "add" path — build NavigationItem list and call AddItems.
                var pagesNode = parameters["pages"];
                if (pagesNode is not JsonArray pagesArray || pagesArray.Count == 0)
                    return JsonSerializer.Serialize(new { error = "pages is required: array of {caption, page_name, module_name}" });

                var navItems = new List<NavigationItem>();
                var addedSummary = new List<object>();

                foreach (var item in pagesArray)
                {
                    if (item is not JsonObject pageObj)
                        return JsonSerializer.Serialize(new { error = "Each page entry must be an object with caption, page_name, and module_name" });

                    var caption = pageObj["caption"]?.ToString();
                    var pageName = pageObj["page_name"]?.ToString();
                    var moduleName = pageObj["module_name"]?.ToString();

                    if (string.IsNullOrEmpty(caption))
                        return JsonSerializer.Serialize(new { error = "caption is required for each page entry" });
                    if (string.IsNullOrEmpty(pageName))
                        return JsonSerializer.Serialize(new { error = "page_name is required for each page entry" });
                    if (string.IsNullOrEmpty(moduleName))
                        return JsonSerializer.Serialize(new { error = "module_name is required for each page entry" });

                    var documentQualifiedName = $"{moduleName}.{pageName}";
                    var iconQualifiedName = pageObj["icon"]?.ToString();

                    navItems.Add(new NavigationItem(caption, documentQualifiedName, iconQualifiedName, null));
                    addedSummary.Add(new { caption, page = pageName, qualifiedName = documentQualifiedName });
                }

                var profileName = parameters["profile"]?.ToString() ?? "Responsive";
                HostServices.Navigation.AddItems(profileName, navItems);

                _logger.LogInformation($"Added {navItems.Count} item(s) to '{profileName}' navigation via HostServices.Navigation");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    action = "add",
                    profile = profileName,
                    message = $"Added {navItems.Count} page(s) to {profileName} navigation",
                    pages = addedSummary
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error managing navigation");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private Mendix.StudioPro.ExtensionsAPI.Model.Pages.IPage? FindPageRecursive(IFolderBase parent, string pageName)
        {
            foreach (var folder in parent.GetFolders())
            {
                var page = folder.GetDocuments().OfType<Mendix.StudioPro.ExtensionsAPI.Model.Pages.IPage>()
                    .FirstOrDefault(p => p.Name.Equals(pageName, StringComparison.OrdinalIgnoreCase));
                if (page != null) return page;
                var found = FindPageRecursive(folder, pageName);
                if (found != null) return found;
            }
            return null;
        }

        #endregion

        #region Phase 16: Microflow Manipulation

        public async Task<string> CheckVariableName(JsonObject parameters)
        {
            await Task.CompletedTask;
            try
            {
                var microflowName = parameters["microflow_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();
                var variableName = parameters["variable_name"]?.ToString();

                if (string.IsNullOrEmpty(microflowName))
                    return JsonSerializer.Serialize(new { error = "microflow_name is required" });
                if (string.IsNullOrEmpty(variableName))
                    return JsonSerializer.Serialize(new { error = "variable_name is required" });

                // Build qualified name
                string qualifiedName;
                if (microflowName.Contains('.'))
                {
                    qualifiedName = microflowName;
                }
                else if (!string.IsNullOrEmpty(moduleName))
                {
                    qualifiedName = $"{moduleName}.{microflowName}";
                }
                else
                {
                    var all = HostServices.MicroflowAuthoring.ListMicroflows(null);
                    var match = all.FirstOrDefault(m => m.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));
                    if (match == null)
                        return JsonSerializer.Serialize(new { error = $"Microflow '{microflowName}' not found. Specify module_name to disambiguate." });
                    qualifiedName = match.QualifiedName;
                }

                var checkResult = HostServices.MicroflowAuthoring.CheckVariableName(qualifiedName, variableName!);

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    microflow = qualifiedName,
                    variableName,
                    in_use = checkResult.InUse,
                    suggested_alternative = checkResult.SuggestedAlternative,
                    existing_variables = checkResult.ExistingVariables.ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking variable name");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ModifyMicroflowActivity(JsonObject parameters)
        {
            await Task.CompletedTask;
            try
            {
                var microflowName = parameters["microflow_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();
                var positionNode = parameters["position"];

                if (string.IsNullOrEmpty(microflowName))
                    return JsonSerializer.Serialize(new { error = "microflow_name is required" });
                if (positionNode == null)
                    return JsonSerializer.Serialize(new { error = "position is required (1-based index)" });

                int position = positionNode.GetValue<int>();

                // Build qualified name
                string qualifiedName;
                if (microflowName!.Contains('.'))
                {
                    qualifiedName = microflowName;
                }
                else if (!string.IsNullOrEmpty(moduleName))
                {
                    qualifiedName = $"{moduleName}.{microflowName}";
                }
                else
                {
                    var all = HostServices.MicroflowAuthoring.ListMicroflows(null);
                    var match = all.FirstOrDefault(m => m.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));
                    if (match == null)
                        return JsonSerializer.Serialize(new { error = $"Microflow '{microflowName}' not found. Specify module_name to disambiguate." });
                    qualifiedName = match.QualifiedName;
                }

                // Validate position against existing activities
                var existingActivities = HostServices.MicroflowAuthoring.ReadActivities(qualifiedName);
                if (position < 1 || position > existingActivities.Count)
                    return JsonSerializer.Serialize(new { error = $"Invalid position {position}. Microflow has {existingActivities.Count} action activities (1-{existingActivities.Count})" });

                // Build changes dictionary from all non-reserved JSON keys
                var changesDict = new Dictionary<string, string>();
                foreach (var kv in parameters)
                {
                    if (kv.Key is "microflow_name" or "module_name" or "position")
                        continue;
                    if (kv.Value != null)
                        changesDict[kv.Key] = kv.Value.ToString() ?? "";
                }

                if (changesDict.Count == 0)
                    return JsonSerializer.Serialize(new { error = "No modifiable properties were supplied. Provide at least one property to change (e.g. caption, disabled, output_variable, commit, refresh_in_client)." });

                HostServices.MicroflowAuthoring.ModifyActivity(qualifiedName, position, changesDict);

                _logger.LogInformation($"Modified activity at position {position} in {qualifiedName} via HostServices.MicroflowAuthoring");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    microflow = qualifiedName,
                    position,
                    changes = changesDict.Keys.ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error modifying microflow activity");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> InsertBeforeActivity(JsonObject parameters)
        {
            await Task.CompletedTask;
            try
            {
                var microflowName = parameters["microflow_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();
                var positionNode = parameters["before_position"];
                var activityData = parameters["activity"] as JsonObject;

                if (string.IsNullOrEmpty(microflowName))
                    return JsonSerializer.Serialize(new { error = "microflow_name is required" });
                if (positionNode == null)
                    return JsonSerializer.Serialize(new { error = "before_position is required (1-based index of the activity to insert before)" });
                if (activityData == null)
                    return JsonSerializer.Serialize(new { error = "activity is required (same format as add_microflow_activity)" });

                int beforePosition = positionNode.GetValue<int>();

                // Build qualified name
                string qualifiedName;
                if (microflowName!.Contains('.'))
                {
                    qualifiedName = microflowName;
                }
                else if (!string.IsNullOrEmpty(moduleName))
                {
                    qualifiedName = $"{moduleName}.{microflowName}";
                }
                else
                {
                    var all = HostServices.MicroflowAuthoring.ListMicroflows(null);
                    var match = all.FirstOrDefault(m => m.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));
                    if (match == null)
                        return JsonSerializer.Serialize(new { error = $"Microflow '{microflowName}' not found. Specify module_name to disambiguate." });
                    qualifiedName = match.QualifiedName;
                }

                // Validate before_position
                var existingActivities = HostServices.MicroflowAuthoring.ReadActivities(qualifiedName);
                if (beforePosition < 1 || beforePosition > existingActivities.Count)
                    return JsonSerializer.Serialize(new { error = $"Invalid before_position {beforePosition}. Microflow has {existingActivities.Count} action activities (1-{existingActivities.Count})" });

                var activityType = activityData["type"]?.ToString()?.ToLowerInvariant();
                if (string.IsNullOrEmpty(activityType))
                    return JsonSerializer.Serialize(new { error = "activity.type is required (create_object, change_object, retrieve, commit, rollback, delete, create_list, etc.)" });

                // Build the activity parameters dictionary
                var activityParams = new Dictionary<string, string>();
                foreach (var kv in activityData)
                {
                    if (kv.Key != "type" && kv.Value != null)
                        activityParams[kv.Key] = kv.Value.ToString() ?? "";
                }

                // Extract well-known fields for the MicroflowActivitySummary record
                var caption = activityData["caption"]?.ToString();
                var outputVariable = activityData["output_variable"]?.ToString()
                    ?? activityData["outputVariable"]?.ToString()
                    ?? activityData["variable_name"]?.ToString();
                var targetEntity = activityData["entity"]?.ToString()
                    ?? activityData["entity_name"]?.ToString()
                    ?? activityData["entityName"]?.ToString();
                var targetMicroflow = activityData["microflow"]?.ToString()
                    ?? activityData["microflow_name"]?.ToString()
                    ?? activityData["calledMicroflow"]?.ToString();
                var targetJavaAction = activityData["java_action"]?.ToString()
                    ?? activityData["javaAction"]?.ToString();

                var activitySummary = new MicroflowActivitySummary(
                    Position: 0,  // host overwrites with actual inserted position
                    ActivityType: activityType!,
                    Caption: caption,
                    OutputVariable: outputVariable,
                    TargetEntity: targetEntity,
                    TargetMicroflow: targetMicroflow,
                    TargetJavaAction: targetJavaAction,
                    Parameters: activityParams);

                int insertedPosition = HostServices.MicroflowAuthoring.InsertBeforeActivity(qualifiedName, beforePosition, activitySummary);

                _logger.LogInformation($"Inserted {activityType} activity before position {beforePosition} in {qualifiedName} via HostServices.MicroflowAuthoring (actual position: {insertedPosition})");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    microflow = qualifiedName,
                    insertedBefore = beforePosition,
                    position = insertedPosition,
                    activityType,
                    message = $"Inserted {activityType} activity before position {beforePosition}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inserting activity before position");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 17: Page & Document Management

        public async Task<string> ListPages(JsonObject parameters)
        {
            try
            {
                var moduleName = parameters["module_name"]?.ToString();
                var includeExcluded = parameters["include_excluded"]?.GetValue<bool>() ?? false;

                var modules = _model.Root.GetModules();
                var results = new List<object>();

                IEnumerable<IModule> targetModules;
                if (!string.IsNullOrEmpty(moduleName))
                {
                    var module = modules.FirstOrDefault(m => m.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
                    if (module == null)
                        return JsonSerializer.Serialize(new { error = $"Module '{moduleName}' not found" });
                    targetModules = new[] { module };
                }
                else
                {
                    targetModules = modules;
                }

                // Get untyped model root for enriched info
                var untypedRoot = GetUntypedModelRoot();

                foreach (var module in targetModules)
                {
                    var pages = new List<Mendix.StudioPro.ExtensionsAPI.Model.Pages.IPage>();

                    // Get pages from root level
                    pages.AddRange(module.GetDocuments().OfType<Mendix.StudioPro.ExtensionsAPI.Model.Pages.IPage>());

                    // Get pages from subfolders recursively
                    CollectPagesRecursive(module, pages);

                    foreach (var page in pages)
                    {
                        if (!includeExcluded && page.Excluded)
                            continue;

                        var qualifiedName = $"{module.Name}.{page.Name}";
                        var pageInfo = new Dictionary<string, object?>
                        {
                            ["name"] = page.Name,
                            ["module"] = module.Name,
                            ["qualifiedName"] = qualifiedName,
                            ["excluded"] = page.Excluded
                        };

                        // Enrich with untyped model data if available
                        if (untypedRoot != null)
                        {
                            var (widgetCount, hasParameters, layoutName, documentation) = GetPageUntypedInfo(untypedRoot, qualifiedName);
                            pageInfo["widgetCount"] = widgetCount;
                            pageInfo["hasParameters"] = hasParameters;
                            if (layoutName != null)
                                pageInfo["layout"] = layoutName;
                            if (documentation != null)
                                pageInfo["documentation"] = documentation;
                        }

                        results.Add(pageInfo);
                    }
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    count = results.Count,
                    moduleName = moduleName ?? "(all)",
                    pages = results
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing pages");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private void CollectPagesRecursive(IFolderBase parent, List<Mendix.StudioPro.ExtensionsAPI.Model.Pages.IPage> pages)
        {
            foreach (var folder in parent.GetFolders())
            {
                pages.AddRange(folder.GetDocuments().OfType<Mendix.StudioPro.ExtensionsAPI.Model.Pages.IPage>());
                CollectPagesRecursive(folder, pages);
            }
        }

        public async Task<string> DeleteDocument(JsonObject parameters)
        {
            await Task.CompletedTask;
            try
            {
                var documentName = parameters["document_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();
                var documentType = parameters["document_type"]?.ToString()?.ToLowerInvariant();

                if (string.IsNullOrEmpty(documentName))
                    return JsonSerializer.Serialize(new { error = "document_name is required" });
                if (string.IsNullOrEmpty(moduleName))
                    return JsonSerializer.Serialize(new { error = "module_name is required" });

                // Build qualified name and resolve via IModelHost
                var qualifiedName = $"{moduleName}.{documentName}";
                var documentId = HostServices.Model.GetDocumentByQualifiedName(qualifiedName);
                if (documentId == null)
                {
                    var error = $"Document '{documentName}' not found in module '{moduleName}'";
                    _logger.LogError(error);
                    return JsonSerializer.Serialize(new { error });
                }

                var deleted = HostServices.PageGeneration.DeleteDocument(documentId.Value);
                if (!deleted)
                {
                    var error = $"Failed to delete document '{documentName}' from module '{moduleName}'.";
                    _logger.LogError(error);
                    return JsonSerializer.Serialize(new { error, success = false });
                }

                _logger.LogInformation($"Deleted document '{qualifiedName}'");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = $"Deleted '{documentName}' from module '{moduleName}'",
                    deletedDocument = documentName,
                    qualified_name = qualifiedName,
                    module = moduleName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting document");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private (IDocument? doc, IFolderBase? parent) FindDocumentWithParent(IFolderBase parent, string documentName)
        {
            foreach (var folder in parent.GetFolders())
            {
                var doc = folder.GetDocuments().FirstOrDefault(d => d.Name.Equals(documentName, StringComparison.OrdinalIgnoreCase));
                if (doc != null) return (doc, folder);
                var result = FindDocumentWithParent(folder, documentName);
                if (result.doc != null) return result;
            }
            return (null, null);
        }

        public async Task<string> SyncFilesystem(JsonObject parameters)
        {
            try
            {
                // IAppService is a UI service — try to get it from service provider
                var appService = _serviceProvider?.GetService<IAppService>();

                if (appService == null)
                    return JsonSerializer.Serialize(new { error = "IAppService is not available. This service may not be accessible from extensions." });

                appService.SynchronizeWithFileSystem(_model);

                _logger.LogInformation("Synchronized model with file system");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    message = "Synchronized model with file system. JavaScript actions, widgets, and other file-based changes have been imported."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error synchronizing with file system");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 18: Quality of Life Improvements

        public async Task<string> UpdateMicroflow(JsonObject parameters)
        {
            await Task.CompletedTask;
            try
            {
                var microflowName = parameters["microflow_name"]?.ToString();
                var moduleName = parameters["module_name"]?.ToString();

                if (string.IsNullOrEmpty(microflowName))
                    return JsonSerializer.Serialize(new { error = "microflow_name is required" });

                // Build qualified name and resolve native microflow (needed for return_type / return_variable_name
                // which are not exposed on IMicroflowAuthoringHost — escalation:manual for typed API).
                if (microflowName!.Contains('.') && string.IsNullOrEmpty(moduleName))
                {
                    var parts = microflowName.Split('.', 2);
                    moduleName = parts[0];
                    microflowName = parts[1];
                }

                var module = Utils.Utils.ResolveModule(_model, moduleName);
                if (module == null)
                    return JsonSerializer.Serialize(new { error = $"Module '{moduleName ?? "(default)"}' not found" });

                var microflow = module.GetDocuments().OfType<IMicroflow>()
                    .FirstOrDefault(mf => mf.Name.Equals(microflowName, StringComparison.OrdinalIgnoreCase));
                if (microflow == null)
                    return JsonSerializer.Serialize(new { error = $"Microflow '{microflowName}' not found in module '{module.Name}'" });

                var qualifiedName = $"{module.Name}.{microflow.Name}";
                var changes = new List<string>();
                var warnings = new List<string>();

                // --- URL path: route through HostServices.MicroflowAuthoring.SetUrl ---
                var url = parameters["url"]?.ToString();
                if (url != null)
                {
                    HostServices.MicroflowAuthoring.SetUrl(qualifiedName, url);
                    changes.Add($"url = '{url}'");
                }

                // --- Return type + return variable name: no typed host method exists (escalation:manual).
                //     Retain direct IMendix-model mutation so functionality is not regressed. ---
                var returnTypeStr = parameters["return_type"]?.ToString()?.ToLowerInvariant();
                var returnVarName = parameters["return_variable_name"]?.ToString();

                bool needsNativeTransaction = !string.IsNullOrEmpty(returnTypeStr) || returnVarName != null;
                if (needsNativeTransaction)
                {
                    using var transaction = _model.StartTransaction($"Update microflow '{microflowName}'");

                    if (!string.IsNullOrEmpty(returnTypeStr))
                    {
                        DataType? newReturnType = returnTypeStr switch
                        {
                            "void" or "nothing" => DataType.Void,
                            "boolean" or "bool" => DataType.Boolean,
                            "string" => DataType.String,
                            "integer" or "int" => DataType.Integer,
                            "decimal" => DataType.Decimal,
                            "float" => DataType.Float,
                            "datetime" or "date" => DataType.DateTime,
                            _ => null
                        };

                        // Handle entity types: "Object:Module.Entity" or "List:Module.Entity"
                        if (newReturnType == null && returnTypeStr.StartsWith("object:"))
                        {
                            var entityQualifiedName = returnTypeStr.Substring(7);
                            var (entity, _) = Utils.Utils.FindEntityAcrossModules(_model, entityQualifiedName, null);
                            if (entity != null)
                                newReturnType = DataType.Object(entity.QualifiedName);
                            else
                            {
                                transaction.Rollback();
                                return JsonSerializer.Serialize(new { error = $"Entity '{entityQualifiedName}' not found for return type" });
                            }
                        }
                        else if (newReturnType == null && returnTypeStr.StartsWith("list:"))
                        {
                            var entityQualifiedName = returnTypeStr.Substring(5);
                            var (entity, _) = Utils.Utils.FindEntityAcrossModules(_model, entityQualifiedName, null);
                            if (entity != null)
                                newReturnType = DataType.List(entity.QualifiedName);
                            else
                            {
                                transaction.Rollback();
                                return JsonSerializer.Serialize(new { error = $"Entity '{entityQualifiedName}' not found for return type" });
                            }
                        }

                        if (newReturnType == null)
                        {
                            transaction.Rollback();
                            return JsonSerializer.Serialize(new { error = $"Unknown return type '{returnTypeStr}'. Supported: void, boolean, string, integer, decimal, float, datetime, object:Module.Entity, list:Module.Entity" });
                        }

                        microflow.ReturnType = newReturnType;
                        changes.Add($"returnType = {FormatDataType(newReturnType)}");

                        // BUG-024: Extensions API explicitly excludes "flows" from IMicroflowBase,
                        // and the untyped model API is read-only — no way to update end event expression.
                        if (newReturnType != DataType.Void)
                        {
                            var defaultExpr = GetDefaultExpressionForDataType(newReturnType);
                            warnings.Add($"IMPORTANT: Return type changed to {FormatDataType(newReturnType)} but the end event " +
                                $"expression was NOT updated (Mendix Extensions API limitation — end events are inaccessible). " +
                                $"This WILL cause error CE0117 in check_project_errors. " +
                                $"Workaround: delete this microflow (delete_document) and recreate it with the correct return type " +
                                $"using create_microflow with returnType={FormatDataType(newReturnType)}. " +
                                $"Expected end event expression: '{defaultExpr}'.");
                        }
                        else
                        {
                            warnings.Add($"Return type changed to Void but the end event expression was NOT cleared " +
                                $"(Mendix Extensions API limitation). This may cause error CE0117. " +
                                $"Workaround: delete this microflow (delete_document) and recreate it as Void.");
                        }
                    }

                    if (returnVarName != null)
                    {
                        microflow.ReturnVariableName = returnVarName;
                        changes.Add($"returnVariableName = '{returnVarName}'");
                    }

                    transaction.Commit();
                }

                if (changes.Count == 0)
                    return JsonSerializer.Serialize(new { error = "No modifiable properties supplied. Provide return_type, return_variable_name, or url." });

                _logger.LogInformation($"Updated microflow {qualifiedName}: {string.Join(", ", changes)}");
                var result = new Dictionary<string, object>
                {
                    ["success"] = true,
                    ["microflow"] = qualifiedName,
                    ["changes"] = changes
                };
                if (warnings.Count > 0)
                    result["warnings"] = warnings;

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating microflow");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ReadAttributeDetails(JsonObject parameters)
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

                var (entity, foundModule) = Utils.Utils.FindEntityAcrossModules(_model, entityName, moduleName);
                if (entity == null)
                    return JsonSerializer.Serialize(new { error = $"Entity '{entityName}' not found" });

                var attribute = entity.GetAttributes()
                    .FirstOrDefault(a => a.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                if (attribute == null)
                    return JsonSerializer.Serialize(new { error = $"Attribute '{attributeName}' not found on entity '{entityName}'" });

                var result = new Dictionary<string, object?>
                {
                    ["name"] = attribute.Name,
                    ["qualifiedName"] = attribute.QualifiedName?.FullName,
                    ["entity"] = entityName,
                    ["module"] = foundModule?.Name,
                    ["documentation"] = string.IsNullOrEmpty(attribute.Documentation) ? null : attribute.Documentation
                };

                // Type details
                var attrType = attribute.Type;
                if (attrType is IStringAttributeType stringType)
                {
                    result["type"] = "String";
                    result["maxLength"] = stringType.Length;
                }
                else if (attrType is IDateTimeAttributeType dateType)
                {
                    result["type"] = "DateTime";
                    result["localizeDate"] = dateType.LocalizeDate;
                }
                else if (attrType is IEnumerationAttributeType enumType)
                {
                    result["type"] = "Enumeration";
                    result["enumeration"] = enumType.Enumeration?.FullName;
                }
                else if (attrType is IBooleanAttributeType)
                    result["type"] = "Boolean";
                else if (attrType is IIntegerAttributeType)
                    result["type"] = "Integer";
                else if (attrType is ILongAttributeType)
                    result["type"] = "Long";
                else if (attrType is IAutoNumberAttributeType)
                    result["type"] = "AutoNumber";
                else if (attrType is IDecimalAttributeType)
                    result["type"] = "Decimal";
                else if (attrType is IBinaryAttributeType)
                    result["type"] = "Binary";
                else if (attrType is IHashedStringAttributeType)
                    result["type"] = "HashedString";
                else
                    result["type"] = attrType?.GetType().Name ?? "Unknown";

                // Value details (stored vs calculated)
                var value = attribute.Value;
                if (value is IStoredValue storedValue)
                {
                    result["valueType"] = "stored";
                    result["defaultValue"] = string.IsNullOrEmpty(storedValue.DefaultValue) ? null : storedValue.DefaultValue;
                }
                else if (value is ICalculatedValue calculatedValue)
                {
                    result["valueType"] = "calculated";
                    result["calculatedMicroflow"] = calculatedValue.Microflow?.FullName;
                    result["passEntity"] = calculatedValue.PassEntity;
                }

                return JsonSerializer.Serialize(new { success = true, attribute = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading attribute details");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ConfigureConstantValues(JsonObject parameters)
        {
            try
            {
                var configName = parameters["configuration_name"]?.ToString();
                var constantName = parameters["constant_name"]?.ToString();
                var constantModule = parameters["module_name"]?.ToString();
                var newValue = parameters["value"]?.ToString();

                // Support qualified names like "Module.ConstantName"
                if (!string.IsNullOrEmpty(constantName) && constantName.Contains('.') && string.IsNullOrEmpty(constantModule))
                {
                    var parts = constantName.Split('.', 2);
                    constantModule = parts[0];
                    constantName = parts[1];
                }

                if (string.IsNullOrEmpty(configName))
                    return JsonSerializer.Serialize(new { error = "configuration_name is required (e.g. 'Development', 'Production')" });
                if (string.IsNullOrEmpty(constantName))
                    return JsonSerializer.Serialize(new { error = "constant_name is required" });
                if (newValue == null)
                    return JsonSerializer.Serialize(new { error = "value is required" });

                // Navigate to project settings -> configurations
                var project = _model.Root as IProject;
                if (project == null)
                    return JsonSerializer.Serialize(new { error = "Cannot access project root" });

                var projectSettings = project.GetProjectDocuments()
                    .OfType<Mendix.StudioPro.ExtensionsAPI.Model.Settings.IProjectSettings>()
                    .FirstOrDefault();
                if (projectSettings == null)
                    return JsonSerializer.Serialize(new { error = "Cannot access project settings" });

                var configSettings = projectSettings.GetSettingsParts()
                    .OfType<Mendix.StudioPro.ExtensionsAPI.Model.Settings.IConfigurationSettings>()
                    .FirstOrDefault();
                if (configSettings == null)
                    return JsonSerializer.Serialize(new { error = "Cannot access configuration settings" });

                var configurations = configSettings.GetConfigurations();
                var config = configurations.FirstOrDefault(c => c.Name.Equals(configName, StringComparison.OrdinalIgnoreCase));
                if (config == null)
                {
                    var availConfigs = configurations.Select(c => c.Name).ToList();
                    return JsonSerializer.Serialize(new { error = $"Configuration '{configName}' not found. Available: {string.Join(", ", availConfigs)}" });
                }

                // Find the constant
                IConstant? constant = null;
                foreach (var module in _model.Root.GetModules())
                {
                    if (!string.IsNullOrEmpty(constantModule) && !module.Name.Equals(constantModule, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var doc = module.GetDocuments().OfType<IConstant>()
                        .FirstOrDefault(c => c.Name.Equals(constantName, StringComparison.OrdinalIgnoreCase));
                    if (doc != null) { constant = doc; break; }
                }
                if (constant == null)
                    return JsonSerializer.Serialize(new { error = $"Constant '{constantName}' not found" });

                // Check if there's an existing constant value override
                var existingCV = config.GetConstantValues()
                    .FirstOrDefault(cv => cv.Constant?.FullName == constant.QualifiedName?.FullName);

                using var transaction = _model.StartTransaction($"Set constant value for '{constantName}' in '{configName}'");

                if (existingCV != null)
                {
                    // Update existing
                    if (existingCV.SharedOrPrivateValue is ISharedValue existingShared)
                    {
                        existingShared.Value = newValue;
                    }
                    else
                    {
                        // Replace with a shared value
                        var sharedValue = _model.Create<ISharedValue>();
                        sharedValue.Value = newValue;
                        existingCV.SharedOrPrivateValue = sharedValue;
                    }
                }
                else
                {
                    // Create new constant value
                    var constantValue = _model.Create<IConstantValue>();
                    constantValue.Constant = constant.QualifiedName;
                    var sharedValue = _model.Create<ISharedValue>();
                    sharedValue.Value = newValue;
                    constantValue.SharedOrPrivateValue = sharedValue;
                    config.AddConstantValue(constantValue);
                }

                transaction.Commit();

                _logger.LogInformation($"Set constant '{constantName}' = '{newValue}' in configuration '{configName}'");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    configuration = configName,
                    constant = constant.QualifiedName?.FullName ?? constantName,
                    value = newValue,
                    message = $"Set constant '{constantName}' = '{newValue}' in configuration '{configName}'"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error configuring constant values");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        #endregion

        #region Phase 25: Page Introspection

        public async Task<string> ReadPageDetails(JsonObject parameters)
        {
            try
            {
                var root = GetUntypedModelRoot();
                if (root == null)
                    return JsonSerializer.Serialize(new { error = "IUntypedModelAccessService is not available" });

                var pageName = parameters?["page_name"]?.ToString();
                var moduleName = parameters?["module_name"]?.ToString();
                var maxDepth = parameters?["max_depth"]?.GetValue<int>() ?? 3;
                maxDepth = Math.Clamp(maxDepth, 1, 5);

                if (string.IsNullOrEmpty(pageName))
                    return JsonSerializer.Serialize(new { error = "page_name is required" });

                // Parse qualified name
                string? targetModule = moduleName;
                string targetName = pageName;
                if (pageName.Contains("."))
                {
                    var parts = pageName.Split('.', 2);
                    targetModule = parts[0];
                    targetName = parts[1];
                }

                var pages = GetUnitsWithFallback(root, "Pages$Page");
                IModelUnit? found = null;

                foreach (var pg in pages)
                {
                    var pgName = pg.Name ?? "";
                    var pgQualified = pg.QualifiedName ?? "";

                    if (!string.IsNullOrEmpty(targetModule))
                    {
                        if (!pgQualified.StartsWith(targetModule + ".", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    if (pgName.Equals(targetName, StringComparison.OrdinalIgnoreCase) ||
                        pgQualified.Equals(pageName, StringComparison.OrdinalIgnoreCase))
                    {
                        found = pg;
                        break;
                    }
                }

                if (found == null)
                    return JsonSerializer.Serialize(new { success = false, error = $"Page '{pageName}' not found" });

                var result = new Dictionary<string, object?>();
                result["success"] = true;
                result["name"] = found.Name;
                result["qualifiedName"] = found.QualifiedName;
                result["module"] = found.QualifiedName?.Split('.').FirstOrDefault();
                result["type"] = "Page";

                // Basic properties
                result["documentation"] = ReadPropValue(found, "documentation");
                result["excluded"] = ReadPropValue(found, "excluded");
                result["exportLevel"] = ReadPropValue(found, "exportLevel");
                result["markAsUsed"] = ReadPropValue(found, "markAsUsed");
                result["title"] = ReadPropValue(found, "title");
                result["url"] = ReadPropValue(found, "url");
                result["popupCloseAction"] = ReadPropValue(found, "popupCloseAction");
                result["popupResizable"] = ReadPropValue(found, "popupResizable");

                // Layout info
                try
                {
                    var layoutCallProp = found.GetProperty("layoutCall");
                    if (layoutCallProp?.Value is IModelStructure layoutCall)
                    {
                        var layoutRef = ReadPropValue(layoutCall, "layout");
                        result["layout"] = layoutRef;
                    }
                    else
                    {
                        result["layout"] = null;
                    }
                }
                catch { result["layout"] = null; }

                // Page parameters
                var parameterList = new List<object>();
                try
                {
                    var paramsProp = found.GetProperty("parameters");
                    if (paramsProp != null && paramsProp.IsList)
                    {
                        var vals = paramsProp.GetValues();
                        if (vals != null)
                        {
                            foreach (var v in vals)
                            {
                                if (v is not IModelStructure param) continue;
                                var pName = param.Name ?? ReadPropValue(param, "name")?.ToString();
                                var pType = "Unknown";
                                try
                                {
                                    var ptProp = param.GetProperty("parameterType");
                                    if (ptProp?.Value is IModelStructure ptEl)
                                    {
                                        pType = SimplifyDataType(ptEl);
                                    }
                                }
                                catch { }
                                parameterList.Add(new { name = pName, type = pType });
                            }
                        }
                    }
                }
                catch { }
                result["parameters"] = parameterList;
                result["parameterCount"] = parameterList.Count;

                // Flat element analysis — GetElements() returns ALL descendants,
                // so we call it ONCE on the page unit and classify the flat list
                var widgetTypeCounts = new Dictionary<string, int>();
                var meaningfulWidgets = new List<object>();

                try
                {
                    var allElements = found.GetElements();
                    if (allElements != null)
                    {
                        foreach (var el in allElements)
                        {
                            var rawType = el.Type ?? "";
                            var simplifiedType = SimplifyWidgetType(rawType);

                            // Skip noise types for both counting and listing
                            if (IsPageNoiseType(rawType))
                                continue;

                            widgetTypeCounts[simplifiedType] = widgetTypeCounts.GetValueOrDefault(simplifiedType) + 1;

                            // Build detail for meaningful/interesting widget types
                            if (IsInterestingWidget(rawType))
                            {
                                var widgetInfo = new Dictionary<string, object?>();
                                widgetInfo["type"] = simplifiedType;

                                var elName = el.Name;
                                if (!string.IsNullOrEmpty(elName))
                                    widgetInfo["name"] = elName;

                                // Extract data source info for data-bound widgets
                                if (rawType.Contains("DataView") || rawType.Contains("ListView") || rawType.Contains("DataGrid") || rawType.Contains("TemplateGrid"))
                                {
                                    ExtractDataSourceInfo(el, widgetInfo);
                                }

                                // Extract action info for buttons
                                if (rawType.Contains("Button"))
                                {
                                    try
                                    {
                                        var actionProp = el.GetProperty("action");
                                        if (actionProp?.Value is IModelStructure actionEl)
                                        {
                                            widgetInfo["actionType"] = SimplifyWidgetType(actionEl.Type ?? "");
                                        }
                                    }
                                    catch { }
                                }

                                // Extract attribute reference for input widgets
                                if (rawType.Contains("TextBox") || rawType.Contains("DatePicker") || rawType.Contains("CheckBox") ||
                                    rawType.Contains("DropDown") || rawType.Contains("TextArea") || rawType.Contains("NumericInput") ||
                                    rawType.Contains("RadioButtons") || rawType.Contains("ReferenceSelector"))
                                {
                                    var attrRef = ReadPropValue(el, "attributeRef");
                                    if (attrRef != null)
                                        widgetInfo["attributeRef"] = attrRef;
                                }

                                meaningfulWidgets.Add(widgetInfo);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    result["widgetError"] = ex.Message;
                }

                result["widgets"] = meaningfulWidgets;
                result["widgetTypeSummary"] = widgetTypeCounts;
                result["totalWidgets"] = widgetTypeCounts.Values.Sum();

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading page details");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private void ExtractDataSourceInfo(IModelStructure element, Dictionary<string, object?> node)
        {
            try
            {
                var dsProp = element.GetProperty("dataSource");
                if (dsProp?.Value is IModelStructure ds)
                {
                    node["dataSourceType"] = SimplifyWidgetType(ds.Type ?? "");
                    var entityRef = ReadPropValue(ds, "entityRef");
                    if (entityRef != null)
                        node["entity"] = entityRef;
                    var entityPath = ReadPropValue(ds, "entityPath");
                    if (entityPath != null)
                        node["entityPath"] = entityPath;
                    var mfRef = ReadPropValue(ds, "microflow");
                    if (mfRef != null)
                        node["dataSourceMicroflow"] = mfRef;
                    var nfRef = ReadPropValue(ds, "nanoflow");
                    if (nfRef != null)
                        node["dataSourceNanoflow"] = nfRef;
                }
            }
            catch { }
        }

        private string SimplifyWidgetType(string rawType)
        {
            if (rawType.Contains("$"))
            {
                var parts = rawType.Split('$', 2);
                return parts.Length > 1 ? parts[1] : rawType;
            }
            if (rawType.Contains("."))
            {
                var parts = rawType.Split('.', 2);
                return parts.Length > 1 ? parts[1] : rawType;
            }
            return rawType;
        }

        private bool IsPageNoiseType(string rawType)
        {
            // Internal widget framework types that are not user-facing
            return rawType.Contains("$Text") ||
                   rawType.Contains("$Translation") ||
                   rawType.Contains("$Appearance") ||
                   rawType.Contains("$DesignPropertyValue") ||
                   rawType.Contains("$OptionDesignPropertyValue") ||
                   rawType.Contains("WidgetPropertyType") ||
                   rawType.Contains("WidgetValueType") ||
                   rawType.Contains("WidgetObjectType") ||
                   rawType.Contains("WidgetEnumerationValue") ||
                   rawType.Contains("WidgetReturnType") ||
                   rawType.Contains("WidgetTranslation") ||
                   rawType.Contains("WidgetObject$") ||
                   rawType.Contains("$WidgetProperty") ||
                   rawType.Contains("$WidgetValue") ||
                   rawType.Contains("$NoClientAction") ||
                   rawType.Contains("$ClientTemplate") ||
                   rawType.Contains("CustomWidgetType") ||
                   rawType.Contains("$IconCollectionIcon") ||
                   rawType.Contains("$PageSettings") ||
                   rawType.Contains("$DirectEntityRef") ||
                   rawType.Contains("LayoutCallArgument") ||
                   rawType.Contains("WebLayoutContent") ||
                   rawType.Contains("NativeLayoutContent") ||
                   rawType.Contains("$AttributeRef") ||
                   rawType.Contains("$GridSortBar") ||
                   rawType.Contains("ScrollContainerRegion");
        }

        private bool IsInterestingWidget(string rawType)
        {
            // User-facing widgets worth showing details for
            return rawType.Contains("DataView") ||
                   rawType.Contains("ListView") ||
                   rawType.Contains("DataGrid") ||
                   rawType.Contains("TemplateGrid") ||
                   rawType.Contains("LayoutGrid") && !rawType.Contains("Row") && !rawType.Contains("Column") ||
                   rawType.Contains("TabContainer") ||
                   rawType.Contains("ActionButton") ||
                   rawType.Contains("LinkButton") ||
                   rawType.Contains("DynamicText") ||
                   rawType.Contains("$CustomWidget") && !rawType.Contains("Type") && !rawType.Contains("XPath") ||
                   rawType.Contains("GroupBox") ||
                   rawType.Contains("ScrollContainer") && !rawType.Contains("Region") ||
                   rawType.Contains("ReferenceSelector") ||
                   rawType.Contains("TextBox") ||
                   rawType.Contains("DatePicker") ||
                   rawType.Contains("DropDown") ||
                   rawType.Contains("CheckBox") ||
                   rawType.Contains("TextArea") ||
                   rawType.Contains("RadioButtons") ||
                   rawType.Contains("NumericInput") ||
                   rawType.Contains("DivContainer") ||
                   rawType.Contains("SnippetCall");
        }

        private string SimplifyDataType(IModelStructure typeElement)
        {
            var type = typeElement.Type ?? "";
            return type switch
            {
                "DataTypes$ObjectType" => ExtractEntityFromDataType(typeElement, "Object"),
                "DataTypes$ListType" => ExtractEntityFromDataType(typeElement, "List"),
                "DataTypes$BooleanType" => "Boolean",
                "DataTypes$StringType" => "String",
                "DataTypes$IntegerType" => "Integer",
                "DataTypes$DecimalType" => "Decimal",
                "DataTypes$DateTimeType" => "DateTime",
                "DataTypes$EnumerationType" => ExtractEnumFromDataType(typeElement),
                "DataTypes$VoidType" => "Void",
                _ => type.Split('$').LastOrDefault() ?? "Unknown"
            };
        }

        // Helper to get page summary for list_pages enhancement
        private (int widgetCount, bool hasParameters, string? layoutName, string? documentation) GetPageUntypedInfo(IModelRoot root, string qualifiedName)
        {
            int widgetCount = 0;
            bool hasParams = false;
            string? layoutName = null;
            string? doc = null;

            try
            {
                var pages = GetUnitsWithFallback(root, "Pages$Page");
                var found = pages.FirstOrDefault(p =>
                    (p.QualifiedName ?? "").Equals(qualifiedName, StringComparison.OrdinalIgnoreCase));

                if (found == null) return (0, false, null, null);

                // Documentation
                var docVal = ReadPropValue(found, "documentation")?.ToString();
                if (!string.IsNullOrEmpty(docVal))
                    doc = docVal.Length > 100 ? docVal.Substring(0, 100) + "..." : docVal;

                // Parameters
                try
                {
                    var paramsProp = found.GetProperty("parameters");
                    if (paramsProp != null && paramsProp.IsList)
                    {
                        var vals = paramsProp.GetValues();
                        hasParams = vals != null && vals.Any();
                    }
                }
                catch { }

                // Layout
                try
                {
                    var layoutCallProp = found.GetProperty("layoutCall");
                    if (layoutCallProp?.Value is IModelStructure layoutCall)
                    {
                        layoutName = ReadPropValue(layoutCall, "layout")?.ToString();
                    }
                }
                catch { }

                // Widget count — count non-noise elements from flat GetElements() list
                try
                {
                    var allElements = found.GetElements();
                    if (allElements != null)
                    {
                        widgetCount = allElements.Count(el => !IsPageNoiseType(el.Type ?? ""));
                    }
                }
                catch { }
            }
            catch { }

            return (widgetCount, hasParams, layoutName, doc);
        }

        #endregion

        #region Phase 26: Workflow Introspection

        public async Task<string> ListWorkflows(JsonObject parameters)
        {
            try
            {
                var root = GetUntypedModelRoot();
                if (root == null)
                    return JsonSerializer.Serialize(new { error = "IUntypedModelAccessService is not available" });

                var moduleName = parameters?["module_name"]?.ToString();
                var workflows = GetUnitsWithFallback(root, "Workflows$Workflow");

                var results = new List<object>();
                foreach (var wf in workflows)
                {
                    var qName = wf.QualifiedName ?? "";
                    var wfModule = qName.Contains('.') ? qName.Split('.').First() : "";

                    if (!string.IsNullOrEmpty(moduleName) &&
                        !wfModule.Equals(moduleName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var info = new Dictionary<string, object?>();
                    info["name"] = wf.Name;
                    info["qualifiedName"] = qName;
                    info["module"] = wfModule;

                    // Context entity
                    try
                    {
                        var ctxProp = wf.GetProperty("contextEntity");
                        if (ctxProp != null)
                            info["contextEntity"] = ctxProp.Value?.ToString();
                    }
                    catch { }

                    // Documentation (truncated)
                    var doc = ReadPropValue(wf, "documentation")?.ToString();
                    if (!string.IsNullOrEmpty(doc))
                        info["documentation"] = doc.Length > 100 ? doc.Substring(0, 100) + "..." : doc;

                    // Activity count from elements
                    try
                    {
                        var elements = wf.GetElements();
                        if (elements != null)
                        {
                            int actCount = 0;
                            foreach (var el in elements)
                            {
                                var t = el.Type ?? "";
                                if (IsWorkflowActivityType(t))
                                    actCount++;
                            }
                            info["activityCount"] = actCount;
                        }
                    }
                    catch { }

                    results.Add(info);
                }

                return JsonSerializer.Serialize(new
                {
                    success = true,
                    count = results.Count,
                    moduleName = moduleName ?? "(all)",
                    workflows = results
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing workflows");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        public async Task<string> ReadWorkflowDetails(JsonObject parameters)
        {
            try
            {
                var root = GetUntypedModelRoot();
                if (root == null)
                    return JsonSerializer.Serialize(new { error = "IUntypedModelAccessService is not available" });

                var workflowName = parameters?["workflow_name"]?.ToString();
                var moduleName = parameters?["module_name"]?.ToString();

                if (string.IsNullOrEmpty(workflowName))
                    return JsonSerializer.Serialize(new { error = "workflow_name is required" });

                // Parse qualified name
                string? targetModule = moduleName;
                string targetName = workflowName;
                if (workflowName.Contains("."))
                {
                    var parts = workflowName.Split('.', 2);
                    targetModule = parts[0];
                    targetName = parts[1];
                }

                var workflows = GetUnitsWithFallback(root, "Workflows$Workflow");
                IModelUnit? found = null;

                foreach (var wf in workflows)
                {
                    var wfName = wf.Name ?? "";
                    var wfQualified = wf.QualifiedName ?? "";

                    if (!string.IsNullOrEmpty(targetModule))
                    {
                        if (!wfQualified.StartsWith(targetModule + ".", StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    if (wfName.Equals(targetName, StringComparison.OrdinalIgnoreCase) ||
                        wfQualified.Equals(workflowName, StringComparison.OrdinalIgnoreCase))
                    {
                        found = wf;
                        break;
                    }
                }

                if (found == null)
                    return JsonSerializer.Serialize(new { success = false, error = $"Workflow '{workflowName}' not found" });

                var result = new Dictionary<string, object?>();
                result["success"] = true;
                result["name"] = found.Name;
                result["qualifiedName"] = found.QualifiedName;
                result["module"] = found.QualifiedName?.Split('.').FirstOrDefault();
                result["type"] = "Workflow";

                // Basic properties
                result["documentation"] = ReadPropValue(found, "documentation");
                result["excluded"] = ReadPropValue(found, "excluded");
                result["exportLevel"] = ReadPropValue(found, "exportLevel");
                result["markAsUsed"] = ReadPropValue(found, "markAsUsed");

                // Context entity
                try
                {
                    var ctxProp = found.GetProperty("contextEntity");
                    if (ctxProp != null)
                        result["contextEntity"] = ctxProp.Value?.ToString();
                }
                catch { }

                // Admin/overview pages
                result["adminPage"] = ReadPropValue(found, "adminPage");
                result["overviewPage"] = ReadPropValue(found, "overviewPage");
                result["workflowType"] = ReadPropValue(found, "workflowType");
                result["dueDate"] = ReadPropValue(found, "dueDate");

                // Security — allowedModuleRoles
                try
                {
                    var rolesProp = found.GetProperty("allowedModuleRoles");
                    if (rolesProp != null && rolesProp.IsList)
                    {
                        var roleValues = rolesProp.GetValues()?.Select(v => v?.ToString()).Where(v => v != null).ToList();
                        result["allowedModuleRoles"] = roleValues;
                        result["allowedRoleCount"] = roleValues?.Count ?? 0;
                    }
                    else
                    {
                        result["allowedModuleRoles"] = new List<string>();
                        result["allowedRoleCount"] = 0;
                    }
                }
                catch
                {
                    result["allowedModuleRoles"] = new List<string>();
                    result["allowedRoleCount"] = 0;
                }

                // Elements — flat list analysis (same pattern as read_page_details)
                var activityTypeCounts = new Dictionary<string, int>();
                var activities = new List<object>();
                int flowCount = 0;

                try
                {
                    var allElements = found.GetElements();
                    if (allElements != null)
                    {
                        foreach (var el in allElements)
                        {
                            var rawType = el.Type ?? "";
                            var simplifiedType = SimplifyWidgetType(rawType);

                            // Skip noise/internal types
                            if (IsWorkflowNoiseType(rawType))
                                continue;

                            // Count flows separately
                            if (rawType.Contains("Flow") || rawType.Contains("SequenceFlow"))
                            {
                                flowCount++;
                                continue;
                            }

                            // Count activity types
                            activityTypeCounts[simplifiedType] = activityTypeCounts.GetValueOrDefault(simplifiedType) + 1;

                            // Build detail for interesting activity types
                            if (IsWorkflowActivityType(rawType))
                            {
                                var actInfo = new Dictionary<string, object?>();
                                actInfo["type"] = simplifiedType;

                                var elName = el.Name;
                                if (!string.IsNullOrEmpty(elName))
                                    actInfo["name"] = elName;

                                // Read common activity properties
                                var caption = ReadPropValue(el, "caption");
                                if (caption != null)
                                    actInfo["caption"] = caption;

                                var taskPage = ReadPropValue(el, "taskPage");
                                if (taskPage != null)
                                    actInfo["taskPage"] = taskPage;

                                var microflow = ReadPropValue(el, "microflow");
                                if (microflow != null)
                                    actInfo["microflow"] = microflow;

                                var documentation = ReadPropValue(el, "documentation");
                                if (documentation != null && documentation.ToString() != "")
                                    actInfo["documentation"] = documentation;

                                // Outcomes for user tasks
                                try
                                {
                                    var outcomesProp = el.GetProperty("outcomes");
                                    if (outcomesProp != null && outcomesProp.IsList)
                                    {
                                        var outcomeVals = outcomesProp.GetValues();
                                        if (outcomeVals != null)
                                        {
                                            var outcomeNames = new List<string>();
                                            foreach (var o in outcomeVals)
                                            {
                                                if (o is IModelStructure os)
                                                {
                                                    var oName = os.Name ?? ReadPropValue(os, "name")?.ToString() ?? ReadPropValue(os, "caption")?.ToString();
                                                    if (oName != null) outcomeNames.Add(oName);
                                                }
                                            }
                                            if (outcomeNames.Count > 0)
                                                actInfo["outcomes"] = outcomeNames;
                                        }
                                    }
                                }
                                catch { }

                                activities.Add(actInfo);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    result["elementError"] = ex.Message;
                }

                result["activities"] = activities;
                result["activityCount"] = activities.Count;
                result["activityTypeSummary"] = activityTypeCounts;
                result["flowCount"] = flowCount;

                // Also dump all property names for discovery (helps refine in future)
                try
                {
                    var props = found.GetProperties();
                    if (props != null)
                    {
                        result["availableProperties"] = props.Select(p => new
                        {
                            name = p.Name,
                            type = p.Type.ToString(),
                            isList = p.IsList
                        }).ToList();
                    }
                }
                catch { }

                return JsonSerializer.Serialize(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading workflow details");
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        private bool IsWorkflowActivityType(string rawType)
        {
            return rawType.Contains("UserTask") ||
                   rawType.Contains("SystemActivity") ||
                   rawType.Contains("CallMicroflow") ||
                   rawType.Contains("CallWorkflow") ||
                   rawType.Contains("Decision") || rawType.Contains("ExclusiveSplit") ||
                   rawType.Contains("ParallelSplit") ||
                   rawType.Contains("JumpActivity") || rawType.Contains("Jump") ||
                   rawType.Contains("EndActivity") || rawType.Contains("EndEvent") ||
                   rawType.Contains("StartActivity") || rawType.Contains("StartEvent") ||
                   rawType.Contains("Boundary") ||
                   rawType.Contains("Timer") ||
                   rawType.Contains("MultiUserTask") ||
                   rawType.Contains("ScriptTask");
        }

        private bool IsWorkflowNoiseType(string rawType)
        {
            // Internal/structural types to skip from counting
            return rawType.Contains("$Text") ||
                   rawType.Contains("$Translation") ||
                   rawType.Contains("$Appearance") ||
                   rawType.Contains("DesignPropertyValue") ||
                   rawType.Contains("OptionDesignPropertyValue") ||
                   rawType.Contains("MicroflowParameterMapping") ||
                   rawType.Contains("$Annotation");
        }

        #endregion

        #region Phase 27: Project Pattern Analysis

        public async Task<string> AnalyzeProjectPatterns(JsonObject parameters)
        {
            try
            {
                var scopeModuleName = parameters["module_name"]?.ToString();
                var saveSkill = parameters["save_skill"]?.ToString()?.ToLowerInvariant() != "false";
                var customSkillPath = parameters["skill_file_path"]?.ToString();

                var project = _model.Root as Mendix.StudioPro.ExtensionsAPI.Model.Projects.IProject;
                var projectName = project?.Name ?? System.IO.Path.GetFileName(_projectDirectory ?? "UnknownProject");

                // Gather user modules to analyze
                var allModules = _model.Root.GetModules()
                    .Where(m => m != null && !m.FromAppStore)
                    .ToList();

                if (!string.IsNullOrEmpty(scopeModuleName))
                    allModules = allModules.Where(m => m.Name.Equals(scopeModuleName, StringComparison.OrdinalIgnoreCase)).ToList();

                if (allModules.Count == 0)
                    return JsonSerializer.Serialize(new { error = $"No user modules found{(string.IsNullOrEmpty(scopeModuleName) ? "" : $" matching '{scopeModuleName}'")}. Available: {Utils.Utils.ListUserModules(_model)}" });

                // ── Accumulators ──────────────────────────────────────────────
                var entityNames = new List<string>();
                var attributeNames = new List<string>();
                var attrTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var baseEntityCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var commonAttrNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int assocOneToMany = 0, assocManyToMany = 0;
                var deleteBehaviorCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int entitiesWithCreatedDate = 0, entitiesWithChangedDate = 0;
                int totalEventHandlers = 0;
                var eventHandlerTypes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                var microflowPrefixCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ACT_"] = 0, ["SUB_"] = 0, ["BCO_"] = 0, ["ACO_"] = 0,
                    ["RUL_"] = 0, ["IVK_"] = 0, ["DS_"] = 0, ["none"] = 0
                };
                var pageSuffixCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["_Overview"] = 0, ["_NewEdit"] = 0, ["_Detail"] = 0, ["_Edit"] = 0,
                    ["_New"] = 0, ["_Select"] = 0, ["_Popup"] = 0, ["other"] = 0
                };

                var moduleStats = new List<object>();
                int totalEntities = 0, totalMicroflows = 0, totalPages = 0, totalAssociations = 0;

                // Track which associations have already been counted (avoid double-count from both-direction traversal)
                var seenAssociations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // ── Per-module analysis ───────────────────────────────────────
                foreach (var module in allModules)
                {
                    int modEntities = 0, modMicroflows = 0, modPages = 0, modAssociations = 0;

                    // Entities + attributes + associations
                    var entities = module.DomainModel?.GetEntities().ToList() ?? new List<Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.IEntity>();
                    modEntities = entities.Count;
                    totalEntities += modEntities;

                    foreach (var entity in entities)
                    {
                        entityNames.Add(entity.Name);

                        // Base entity / generalization
                        var genParent = (entity.Generalization as Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.IGeneralization)?.Generalization?.FullName ?? "System.Object";
                        if (!baseEntityCounts.ContainsKey(genParent)) baseEntityCounts[genParent] = 0;
                        baseEntityCounts[genParent]++;

                        // Attributes
                        var attrs = entity.GetAttributes().ToList();
                        bool hasCreatedDate = false, hasChangedDate = false;

                        foreach (var attr in attrs)
                        {
                            attributeNames.Add(attr.Name);

                            // Track common audit fields
                            var attrNameLower = attr.Name.ToLowerInvariant();
                            if (!commonAttrNames.ContainsKey(attr.Name)) commonAttrNames[attr.Name] = 0;
                            commonAttrNames[attr.Name]++;
                            if (attrNameLower == "createddate" || attrNameLower == "createdby") hasCreatedDate = true;
                            if (attrNameLower == "changeddate" || attrNameLower == "lastchanged" || attrNameLower == "modifieddate") hasChangedDate = true;

                            // Attribute type distribution
                            var typeName = SimplifyAttributeTypeName(attr.Type?.GetType().Name ?? "Unknown");
                            if (!attrTypeCounts.ContainsKey(typeName)) attrTypeCounts[typeName] = 0;
                            attrTypeCounts[typeName]++;
                        }

                        if (hasCreatedDate) entitiesWithCreatedDate++;
                        if (hasChangedDate) entitiesWithChangedDate++;

                        // Associations (count each once)
                        var associations = entity.GetAssociations(Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.AssociationDirection.Both, null).ToList();
                        foreach (var assocInfo in associations)
                        {
                            var assocKey = $"{module.Name}.{assocInfo.Association.Name}";
                            if (seenAssociations.Contains(assocKey)) continue;
                            seenAssociations.Add(assocKey);
                            modAssociations++;
                            totalAssociations++;

                            if (assocInfo.Association.Type == Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.AssociationType.Reference)
                                assocOneToMany++;
                            else
                                assocManyToMany++;

                            // Delete behaviors
                            var pb = assocInfo.Association.ParentDeleteBehavior.ToString();
                            var cb = assocInfo.Association.ChildDeleteBehavior.ToString();
                            if (!deleteBehaviorCounts.ContainsKey(pb)) deleteBehaviorCounts[pb] = 0;
                            deleteBehaviorCounts[pb]++;
                            if (!deleteBehaviorCounts.ContainsKey(cb)) deleteBehaviorCounts[cb] = 0;
                            deleteBehaviorCounts[cb]++;
                        }

                        // Event handlers
                        var handlers = entity.GetEventHandlers().ToList();
                        totalEventHandlers += handlers.Count;
                        foreach (var h in handlers)
                        {
                            var key = $"{h.Moment}_{h.Event}";
                            if (!eventHandlerTypes.ContainsKey(key)) eventHandlerTypes[key] = 0;
                            eventHandlerTypes[key]++;
                        }
                    }

                    // Microflows (names only — fast)
                    var microflows = module.GetDocuments().OfType<Mendix.StudioPro.ExtensionsAPI.Model.Microflows.IMicroflow>().ToList();
                    modMicroflows = microflows.Count;
                    totalMicroflows += modMicroflows;

                    foreach (var mf in microflows)
                    {
                        var name = mf.Name ?? "";
                        var matched = false;
                        foreach (var prefix in new[] { "ACT_", "SUB_", "BCO_", "ACO_", "RUL_", "IVK_", "DS_" })
                        {
                            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            {
                                microflowPrefixCounts[prefix]++;
                                matched = true;
                                break;
                            }
                        }
                        if (!matched) microflowPrefixCounts["none"]++;
                    }

                    // Pages (names only — fast)
                    var pages = module.GetDocuments()
                        .OfType<Mendix.StudioPro.ExtensionsAPI.Model.Pages.IPage>()
                        .ToList();
                    modPages = pages.Count;
                    totalPages += modPages;

                    foreach (var page in pages)
                    {
                        var name = page.Name ?? "";
                        var matched = false;
                        foreach (var suffix in new[] { "_Overview", "_NewEdit", "_Detail", "_Edit", "_New", "_Select", "_Popup" })
                        {
                            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                            {
                                pageSuffixCounts[suffix]++;
                                matched = true;
                                break;
                            }
                        }
                        if (!matched) pageSuffixCounts["other"]++;
                    }

                    moduleStats.Add(new
                    {
                        name = module.Name,
                        entities = modEntities,
                        associations = modAssociations,
                        microflows = modMicroflows,
                        pages = modPages
                    });
                }

                // ── Naming convention analysis ────────────────────────────────
                int entityPascalCount = entityNames.Count(n => IsPascalCase(n));
                int attrPascalCount = attributeNames.Count(n => IsPascalCase(n));
                double entityPascalPct = entityNames.Count > 0 ? entityPascalCount * 100.0 / entityNames.Count : 0;
                double attrPascalPct = attributeNames.Count > 0 ? attrPascalCount * 100.0 / attributeNames.Count : 0;

                var topAttrTypes = attrTypeCounts.OrderByDescending(kv => kv.Value)
                    .Select(kv => new { type = kv.Key, count = kv.Value, pct = totalEntities > 0 ? (int)(kv.Value * 100.0 / attributeNames.Count) : 0 })
                    .Take(8).ToList();

                var topBaseEntities = baseEntityCounts.OrderByDescending(kv => kv.Value)
                    .Select(kv => new { parent = kv.Key, count = kv.Value })
                    .Take(5).ToList();

                // Standard audit attributes (present on ≥40% of entities)
                var standardAttrs = commonAttrNames
                    .Where(kv => kv.Value >= Math.Max(1, totalEntities * 0.4))
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => kv.Key)
                    .Take(8)
                    .ToList();

                var mostCommonDeleteBehavior = deleteBehaviorCounts.Count > 0
                    ? deleteBehaviorCounts.OrderByDescending(kv => kv.Value).First().Key
                    : "DeleteMeButKeepReferences";

                // ── Build result ──────────────────────────────────────────────
                var conventions = new
                {
                    entityNaming = new
                    {
                        pattern = entityPascalPct >= 90 ? "PascalCase" : entityPascalPct >= 70 ? "Mostly PascalCase" : "Mixed",
                        consistency = $"{(int)entityPascalPct}%",
                        examples = entityNames.Where(IsPascalCase).Take(5).ToList()
                    },
                    attributeNaming = new
                    {
                        pattern = attrPascalPct >= 90 ? "PascalCase" : attrPascalPct >= 70 ? "Mostly PascalCase" : "Mixed",
                        consistency = $"{(int)attrPascalPct}%"
                    },
                    microflowPrefixes = microflowPrefixCounts,
                    standardAuditAttributes = standardAttrs,
                    commonBaseEntities = baseEntityCounts.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value)
                };

                var statistics = new
                {
                    totalEntities,
                    totalAssociations,
                    totalMicroflows,
                    totalPages,
                    totalEventHandlers,
                    attributeTypeDistribution = topAttrTypes,
                    associationTypeRatio = new
                    {
                        oneToMany = assocOneToMany,
                        manyToMany = assocManyToMany,
                        oneToManyPct = totalAssociations > 0 ? $"{(int)(assocOneToMany * 100.0 / totalAssociations)}%" : "n/a"
                    },
                    commonDeleteBehavior = mostCommonDeleteBehavior,
                    deleteBehaviorDistribution = deleteBehaviorCounts,
                    eventHandlerDistribution = eventHandlerTypes,
                    entitiesWithCreatedDate = new { count = entitiesWithCreatedDate, pct = totalEntities > 0 ? $"{(int)(entitiesWithCreatedDate * 100.0 / totalEntities)}%" : "0%" },
                    entitiesWithChangedDate = new { count = entitiesWithChangedDate, pct = totalEntities > 0 ? $"{(int)(entitiesWithChangedDate * 100.0 / totalEntities)}%" : "0%" },
                    pageSuffixPatterns = pageSuffixCounts,
                    baseEntityDistribution = topBaseEntities
                };

                // ── Generate and optionally save skill file ───────────────────
                string? skillFilePath = null;
                bool skillFileWritten = false;
                if (saveSkill)
                {
                    var skillContent = GenerateProjectConventionSkill(projectName, conventions, statistics, moduleStats, totalEntities, totalMicroflows, totalPages);
                    skillFilePath = !string.IsNullOrEmpty(customSkillPath)
                        ? customSkillPath
                        : GetProjectSkillFilePath();

                    try
                    {
                        var dir = System.IO.Path.GetDirectoryName(skillFilePath);
                        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                            System.IO.Directory.CreateDirectory(dir);
                        await System.IO.File.WriteAllTextAsync(skillFilePath, skillContent);
                        skillFileWritten = true;
                        _logger.LogInformation($"Project conventions skill written to {skillFilePath}");
                    }
                    catch (Exception fileEx)
                    {
                        _logger.LogWarning(fileEx, $"Could not write skill file to {skillFilePath}");
                        skillFilePath = $"(write failed: {fileEx.Message})";
                    }
                }

                var result = new
                {
                    success = true,
                    projectName,
                    analyzedAt = DateTime.Now.ToString("o"),
                    modulesAnalyzed = allModules.Select(m => m.Name).ToList(),
                    conventions,
                    statistics,
                    modules = moduleStats,
                    skillFilePath,
                    skillFileWritten
                };

                return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing project patterns");
                return JsonSerializer.Serialize(new { error = $"Failed to analyze project patterns: {ex.Message}" });
            }
        }

        private static string SimplifyAttributeTypeName(string rawTypeName)
        {
            if (rawTypeName.Contains("String")) return "String";
            if (rawTypeName.Contains("Integer") || rawTypeName.Contains("Long")) return "Integer/Long";
            if (rawTypeName.Contains("Decimal")) return "Decimal";
            if (rawTypeName.Contains("Boolean")) return "Boolean";
            if (rawTypeName.Contains("DateTime")) return "DateTime";
            if (rawTypeName.Contains("AutoNumber")) return "AutoNumber";
            if (rawTypeName.Contains("Enumeration")) return "Enumeration";
            if (rawTypeName.Contains("Binary")) return "Binary";
            if (rawTypeName.Contains("Hashed")) return "HashedString";
            return rawTypeName;
        }

        private static bool IsPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return char.IsUpper(name[0]) && !name.Contains('_') && !name.Contains(' ');
        }

        private string GetProjectSkillFilePath()
        {
            // Derive skills folder from the extension DLL location
            try
            {
                var assemblyDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(assemblyDir))
                {
                    // Walk up from e.g. C:\Mendix Projects\MCPExtension-main\extensions\MCP\
                    // to find C:\Extensions\MCPExtension\.claude\skills\
                    // Try DLL-adjacent .claude\skills first (for deployed builds)
                    // Fallback: use a sibling path from project directory
                }
            }
            catch { /* ignore */ }

            // Hardcoded known path for this installation
            return @"C:\Extensions\MCPExtension\.claude\skills\mendix-project-context.md";
        }

        private string GenerateProjectConventionSkill(
            string projectName,
            dynamic conventions,
            dynamic statistics,
            List<object> moduleStats,
            int totalEntities,
            int totalMicroflows,
            int totalPages)
        {
            var sb = new System.Text.StringBuilder();
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            sb.AppendLine("---");
            sb.AppendLine("name: mendix-project-context");
            sb.AppendLine($"description: Auto-generated project conventions from '{projectName}' on {now}. Load before building anything in this project.");
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"# Project Conventions: {projectName}");
            sb.AppendLine($"*Generated: {now} — {totalEntities} entities, {totalMicroflows} microflows, {totalPages} pages*");
            sb.AppendLine();

            // Naming conventions
            sb.AppendLine("## Naming Conventions");
            sb.AppendLine();

            var entityPattern = (string)conventions.entityNaming.pattern;
            var entityConsistency = (string)conventions.entityNaming.consistency;
            var entityExamples = string.Join(", ", ((System.Collections.Generic.List<string>)conventions.entityNaming.examples).Take(4));
            sb.AppendLine($"**Entities:** {entityPattern} ({entityConsistency} consistent){(entityExamples.Length > 0 ? $" — e.g. {entityExamples}" : "")}");

            var attrPattern = (string)conventions.attributeNaming.pattern;
            var attrConsistency = (string)conventions.attributeNaming.consistency;
            sb.AppendLine($"**Attributes:** {attrPattern} ({attrConsistency} consistent)");

            sb.AppendLine();
            sb.AppendLine("**Microflow prefix patterns:**");
            var prefixes = (Dictionary<string, int>)conventions.microflowPrefixes;
            foreach (var kv in prefixes.OrderByDescending(x => x.Value).Where(x => x.Value > 0))
            {
                double pct = totalMicroflows > 0 ? kv.Value * 100.0 / totalMicroflows : 0;
                string label = kv.Key switch
                {
                    "ACT_" => "main action microflows",
                    "SUB_" => "sub-microflows (called by other microflows)",
                    "BCO_" => "before-commit event handlers",
                    "ACO_" => "after-commit event handlers",
                    "RUL_" => "business rules",
                    "IVK_" => "invoked/API microflows",
                    "DS_" => "data source microflows",
                    "none" => "no prefix (utility/other)",
                    _ => ""
                };
                sb.AppendLine($"  - `{kv.Key}` — {kv.Value} microflows ({(int)pct}%) → {label}");
            }

            // Structural patterns
            sb.AppendLine();
            sb.AppendLine("## Standard Patterns");
            sb.AppendLine();

            var auditAttrs = (List<string>)conventions.standardAuditAttributes;
            // Separate system-reserved names (must use configure_system_attributes) from safe manual attrs
            var systemReservedAuditNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "createddate", "changeddate", "owner", "changedby" };
            var systemAttrs = auditAttrs.Where(a => systemReservedAuditNames.Contains(a.ToLower())).ToList();
            var manualAttrs = auditAttrs.Where(a => !systemReservedAuditNames.Contains(a.ToLower())).ToList();

            if (systemAttrs.Count > 0)
            {
                sb.AppendLine($"**System audit tracking** ({string.Join(", ", systemAttrs)}): These are Mendix reserved names.");
                sb.AppendLine($"⚠️ Do NOT add via `add_attribute` — use `configure_system_attributes` instead:");
                sb.AppendLine($"```");
                sb.AppendLine($"configure_system_attributes  entity_name=<Entity>  module_name=<Module>");
                var hasCreated = systemAttrs.Any(a => a.Equals("CreatedDate", StringComparison.OrdinalIgnoreCase));
                var hasChanged = systemAttrs.Any(a => a.Equals("ChangedDate", StringComparison.OrdinalIgnoreCase));
                var hasOwner = systemAttrs.Any(a => a.Equals("Owner", StringComparison.OrdinalIgnoreCase));
                var hasChangedBy = systemAttrs.Any(a => a.Equals("ChangedBy", StringComparison.OrdinalIgnoreCase));
                if (hasCreated) sb.Append("  has_created_date=true  ");
                if (hasChanged) sb.Append("has_changed_date=true  ");
                if (hasOwner) sb.Append("has_owner=true  ");
                if (hasChangedBy) sb.Append("has_changed_by=true");
                sb.AppendLine();
                sb.AppendLine($"```");
            }
            if (manualAttrs.Count > 0)
                sb.AppendLine($"**Standard attributes** (add via `add_attribute`): {string.Join(", ", manualAttrs)} — present on most entities.");
            if (auditAttrs.Count == 0)
                sb.AppendLine("**Standard attributes:** No common audit attributes detected — add as needed.");

            var baseEntities = (Dictionary<string, int>)conventions.commonBaseEntities;
            var topBase = baseEntities.OrderByDescending(kv => kv.Value).FirstOrDefault();
            if (topBase.Key != null)
            {
                sb.AppendLine($"**Default base entity:** `{topBase.Key}` (used by {topBase.Value}/{totalEntities} entities)");
                var fileDocCount = baseEntities.Where(kv => kv.Key.Contains("FileDocument")).Sum(kv => kv.Value);
                var imageCount = baseEntities.Where(kv => kv.Key.Contains("Image")).Sum(kv => kv.Value);
                if (fileDocCount > 0) sb.AppendLine($"**File storage:** `System.FileDocument` — used by {fileDocCount} entities for attachments/uploads.");
                if (imageCount > 0) sb.AppendLine($"**Image storage:** `System.Image` — used by {imageCount} entities.");
            }

            var oneToManyPct = (string)statistics.associationTypeRatio.oneToManyPct;
            var manyToMany = (int)statistics.associationTypeRatio.manyToMany;
            sb.AppendLine($"**Associations:** {oneToManyPct} one-to-many{(manyToMany > 0 ? $", {manyToMany} many-to-many" : " (no many-to-many)")}");
            sb.AppendLine($"**Most common delete behavior:** `{(string)statistics.commonDeleteBehavior}`");

            // Module structure
            sb.AppendLine();
            sb.AppendLine("## Module Structure");
            sb.AppendLine();
            sb.AppendLine("| Module | Entities | Associations | Microflows | Pages |");
            sb.AppendLine("|--------|----------|--------------|------------|-------|");
            foreach (dynamic mod in moduleStats)
            {
                sb.AppendLine($"| {mod.name} | {mod.entities} | {mod.associations} | {mod.microflows} | {mod.pages} |");
            }

            // Attribute type distribution
            sb.AppendLine();
            sb.AppendLine("## Attribute Type Distribution");
            sb.AppendLine();
            var topTypes = ((System.Collections.IEnumerable)statistics.attributeTypeDistribution)
                .Cast<dynamic>()
                .Take(6)
                .ToList();
            foreach (var t in topTypes)
                sb.AppendLine($"  - {t.type}: {t.count} ({t.pct}%)");

            // Apply these conventions
            sb.AppendLine();
            sb.AppendLine("## Apply These Conventions");
            sb.AppendLine();
            sb.AppendLine("When creating anything new in this project:");
            sb.AppendLine($"1. **Names:** Use {entityPattern} for entities and attributes");
            if (prefixes.TryGetValue("ACT_", out int actCount) && actCount > 0)
                sb.AppendLine("2. **Microflows:** Prefix with `ACT_<Entity>_<Verb>` for actions, `SUB_` for sub-flows, `BCO_`/`ACO_` for event handlers");
            if (systemAttrs.Count > 0)
                sb.AppendLine($"3. **Audit fields:** Call `configure_system_attributes` (has_created_date/has_changed_date=true){(manualAttrs.Count > 0 ? $" + add `{string.Join("`, `", manualAttrs)}` via `add_attribute`" : "")}");
            else if (manualAttrs.Count > 0)
                sb.AppendLine($"3. **Standard fields:** Add {string.Join(", ", manualAttrs)} to every new entity via `add_attribute`");
            if (topBase.Key != null && topBase.Key != "System.Object")
                sb.AppendLine($"4. **Base entity:** Use `{topBase.Key}` as default generalization unless overriding");
            sb.AppendLine($"5. **Associations:** Default to one-to-many with `{(string)statistics.commonDeleteBehavior}` delete behavior");
            sb.AppendLine("6. **Before building:** Call `list_modules` + `read_domain_model` to see existing state");

            return sb.ToString();
        }

        #endregion

        #endregion
    }
}
