namespace Concord.Host11x.Spmcp;

using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Terminal.Mcp;
using Terminal.Spmcp.Tools;

public static class SpmcpToolBootstrap11x
{
    public static void Register(ToolCatalog catalog, string? projectDirectory = null)
    {
        var additional = new MendixAdditionalTools(NullLogger<MendixAdditionalTools>.Instance, projectDirectory);
        var domain = new MendixDomainModelTools(NullLogger<MendixDomainModelTools>.Instance);

        // --- MendixAdditionalTools (47) ---
        RegisterObject(catalog, "save_data", ToolFamily.DataSample, additional.SaveData);
        RegisterObject(catalog, "generate_sample_data", ToolFamily.DataSample, additional.GenerateSampleData);
        RegisterObject(catalog, "read_sample_data", ToolFamily.DataSample, additional.ReadSampleData);
        RegisterObject(catalog, "generate_overview_pages", ToolFamily.Pages, additional.GenerateOverviewPages);
        RegisterObject(catalog, "list_microflows", ToolFamily.Microflows, additional.ListMicroflows);
        RegisterObject(catalog, "read_microflow_details", ToolFamily.Microflows, additional.ReadMicroflowDetails);
        RegisterObject(catalog, "get_last_error", ToolFamily.Diagnostics, additional.GetLastError);
        RegisterObject(catalog, "get_studio_pro_logs", ToolFamily.Diagnostics, additional.GetStudioProLogs);
        RegisterObject(catalog, "check_project_errors", ToolFamily.Diagnostics, additional.CheckProjectErrors);
        RegisterObject(catalog, "list_available_tools", ToolFamily.Diagnostics, additional.ListAvailableTools);
        RegisterObject(catalog, "debug_info", ToolFamily.Diagnostics, additional.DebugInfo);
        RegisterObject(catalog, "create_microflow", ToolFamily.Microflows, additional.CreateMicroflow);
        RegisterObject(catalog, "create_microflow_activity", ToolFamily.Microflows, additional.CreateMicroflowActivity);
        RegisterString(catalog, "setup_data_import", ToolFamily.DataSample, additional.SetupDataImport);
        RegisterObject(catalog, "create_microflow_activities_sequence", ToolFamily.Microflows, additional.CreateMicroflowActivitiesSequence);
        RegisterString(catalog, "list_java_actions", ToolFamily.Diagnostics, additional.ListJavaActions);
        RegisterString(catalog, "read_runtime_settings", ToolFamily.ProjectSettings, additional.ReadRuntimeSettings);
        RegisterString(catalog, "set_runtime_settings", ToolFamily.ProjectSettings, additional.SetRuntimeSettings);
        RegisterString(catalog, "read_configurations", ToolFamily.ProjectSettings, additional.ReadConfigurations);
        RegisterString(catalog, "set_configuration", ToolFamily.ProjectSettings, additional.SetConfiguration);
        RegisterString(catalog, "read_version_control", ToolFamily.ProjectSettings, additional.ReadVersionControl);
        RegisterString(catalog, "set_microflow_url", ToolFamily.Microflows, additional.SetMicroflowUrl);
        RegisterString(catalog, "list_rules", ToolFamily.Security, additional.ListRules);
        RegisterString(catalog, "exclude_document", ToolFamily.Pages, additional.ExcludeDocument);
        RegisterString(catalog, "read_security_info", ToolFamily.Security, additional.ReadSecurityInfo);
        RegisterString(catalog, "read_entity_access_rules", ToolFamily.Security, additional.ReadEntityAccessRules);
        RegisterString(catalog, "read_microflow_security", ToolFamily.Security, additional.ReadMicroflowSecurity);
        RegisterString(catalog, "audit_security", ToolFamily.Security, additional.AuditSecurity);
        RegisterString(catalog, "read_nanoflow_details", ToolFamily.Microflows, additional.ReadNanoflowDetails);
        RegisterString(catalog, "list_nanoflows", ToolFamily.Microflows, additional.ListNanoflows);
        RegisterString(catalog, "list_scheduled_events", ToolFamily.Microflows, additional.ListScheduledEvents);
        RegisterString(catalog, "list_rest_services", ToolFamily.ProjectSettings, additional.ListRestServices);
        RegisterString(catalog, "query_model_elements", ToolFamily.DomainModel, additional.QueryModelElements);
        RegisterString(catalog, "manage_navigation", ToolFamily.Navigation, additional.ManageNavigation);
        RegisterString(catalog, "check_variable_name", ToolFamily.Microflows, additional.CheckVariableName);
        RegisterString(catalog, "modify_microflow_activity", ToolFamily.Microflows, additional.ModifyMicroflowActivity);
        RegisterString(catalog, "insert_before_activity", ToolFamily.Microflows, additional.InsertBeforeActivity);
        RegisterString(catalog, "list_pages", ToolFamily.Pages, additional.ListPages);
        RegisterString(catalog, "delete_document", ToolFamily.Pages, additional.DeleteDocument);
        RegisterString(catalog, "sync_filesystem", ToolFamily.ProjectSettings, additional.SyncFilesystem);
        RegisterString(catalog, "update_microflow", ToolFamily.Microflows, additional.UpdateMicroflow);
        RegisterString(catalog, "read_attribute_details", ToolFamily.DomainModel, additional.ReadAttributeDetails);
        RegisterString(catalog, "configure_constant_values", ToolFamily.ConstantsEnums, additional.ConfigureConstantValues);
        RegisterString(catalog, "read_page_details", ToolFamily.Pages, additional.ReadPageDetails);
        RegisterString(catalog, "list_workflows", ToolFamily.Workflows, additional.ListWorkflows);
        RegisterString(catalog, "read_workflow_details", ToolFamily.Workflows, additional.ReadWorkflowDetails);
        RegisterString(catalog, "analyze_project_patterns", ToolFamily.Diagnostics, additional.AnalyzeProjectPatterns);

        // --- MendixDomainModelTools (40) ---
        RegisterString(catalog, "list_modules", ToolFamily.DomainModel, domain.ListModules);
        RegisterString(catalog, "create_module", ToolFamily.DomainModel, domain.CreateModule);
        RegisterString(catalog, "set_entity_generalization", ToolFamily.DomainModel, domain.SetEntityGeneralization);
        RegisterString(catalog, "remove_entity_generalization", ToolFamily.DomainModel, domain.RemoveEntityGeneralization);
        RegisterString(catalog, "add_event_handler", ToolFamily.DomainModel, domain.AddEventHandler);
        RegisterString(catalog, "add_attribute", ToolFamily.DomainModel, domain.AddAttribute);
        RegisterString(catalog, "set_calculated_attribute", ToolFamily.DomainModel, domain.SetCalculatedAttribute);
        RegisterString(catalog, "check_model", ToolFamily.Diagnostics, domain.CheckModel);
        RegisterString(catalog, "create_constant", ToolFamily.ConstantsEnums, domain.CreateConstant);
        RegisterString(catalog, "list_constants", ToolFamily.ConstantsEnums, domain.ListConstants);
        RegisterString(catalog, "create_enumeration", ToolFamily.ConstantsEnums, domain.CreateEnumeration);
        RegisterString(catalog, "list_enumerations", ToolFamily.ConstantsEnums, domain.ListEnumerations);
        RegisterString(catalog, "read_project_info", ToolFamily.DomainModel, domain.ReadProjectInfo);
        RegisterString(catalog, "read_domain_model", ToolFamily.DomainModel, domain.ReadDomainModel);
        RegisterString(catalog, "create_entity", ToolFamily.DomainModel, domain.CreateEntity);
        RegisterString(catalog, "create_association", ToolFamily.DomainModel, domain.CreateAssociation);
        RegisterString(catalog, "create_multiple_entities", ToolFamily.DomainModel, domain.CreateMultipleEntities);
        RegisterString(catalog, "create_multiple_associations", ToolFamily.DomainModel, domain.CreateMultipleAssociations);
        RegisterString(catalog, "create_domain_model_from_schema", ToolFamily.DomainModel, domain.CreateDomainModelFromSchema);
        RegisterString(catalog, "delete_model_element", ToolFamily.DomainModel, domain.DeleteModelElement);
        RegisterString(catalog, "diagnose_associations", ToolFamily.Diagnostics, domain.DiagnoseAssociations);
        RegisterString(catalog, "get_last_error_domain", ToolFamily.Diagnostics, domain.GetLastError);
        RegisterString(catalog, "list_available_tools_domain", ToolFamily.Diagnostics, domain.ListAvailableTools);
        RegisterString(catalog, "arrange_domain_model", ToolFamily.DomainModel, domain.ArrangeDomainModel);
        RegisterString(catalog, "configure_system_attributes", ToolFamily.DomainModel, domain.ConfigureSystemAttributes);
        RegisterString(catalog, "manage_folders", ToolFamily.DomainModel, domain.ManageFolders);
        RegisterString(catalog, "validate_name", ToolFamily.DomainModel, domain.ValidateName);
        RegisterString(catalog, "copy_model_element", ToolFamily.DomainModel, domain.CopyModelElement);
        RegisterString(catalog, "rename_entity", ToolFamily.DomainModel, domain.RenameEntity);
        RegisterString(catalog, "rename_attribute", ToolFamily.DomainModel, domain.RenameAttribute);
        RegisterString(catalog, "rename_association", ToolFamily.DomainModel, domain.RenameAssociation);
        RegisterString(catalog, "rename_document", ToolFamily.DomainModel, domain.RenameDocument);
        RegisterString(catalog, "rename_module", ToolFamily.DomainModel, domain.RenameModule);
        RegisterString(catalog, "rename_enumeration_value", ToolFamily.ConstantsEnums, domain.RenameEnumerationValue);
        RegisterString(catalog, "update_attribute", ToolFamily.DomainModel, domain.UpdateAttribute);
        RegisterString(catalog, "update_association", ToolFamily.DomainModel, domain.UpdateAssociation);
        RegisterString(catalog, "update_constant", ToolFamily.ConstantsEnums, domain.UpdateConstant);
        RegisterString(catalog, "update_enumeration", ToolFamily.ConstantsEnums, domain.UpdateEnumeration);
        RegisterString(catalog, "set_documentation", ToolFamily.DomainModel, domain.SetDocumentation);
        RegisterString(catalog, "query_associations", ToolFamily.DomainModel, domain.QueryAssociations);
    }

    private static void RegisterObject(ToolCatalog catalog, string name, ToolFamily family, Func<JsonObject, Task<object>> invoke)
        => catalog.Register(new RegisteredTool(name, family, invoke));

    private static void RegisterString(ToolCatalog catalog, string name, ToolFamily family, Func<JsonObject, Task<string>> invoke)
        => catalog.Register(new RegisteredTool(name, family, async args => (object)await invoke(args)));

    private sealed record RegisteredTool(string Name, ToolFamily Family, Func<JsonObject, Task<object>> Invoke) : ITool;
}
