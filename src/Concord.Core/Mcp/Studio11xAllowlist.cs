namespace Terminal.Mcp;

/// <summary>
/// Tools that ship on Studio Pro 11.x. Studio Pro's built-in MCP covers
/// the rest; including those would create model-side ambiguity.
/// </summary>
public static class Studio11xAllowlist
{
    private static readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase)
    {
        // UI actions + Maia (always on 11.x — Concord-native)
        "run_app", "stop_app", "save_all", "refresh_project",
        "get_active_run_configuration", "get_app_status",
        "maia__send", "maia__status", "maia__wait", "maia__ask",
        "maia__reset", "maia__force_tier",
        "maia__busy", "maia__ping", "maia__health", "maia__new_chat",

        // Pages
        "generate_overview_pages", "delete_document",
        // Navigation
        "manage_navigation",
        // Security
        "read_security_info", "read_entity_access_rules", "read_microflow_security", "audit_security",
        // Domain Model — hard deletes + reference-safe renames + layout
        "delete_model_element",
        "rename_entity", "rename_attribute", "rename_association",
        "rename_document", "rename_module", "rename_enumeration_value",
        "set_documentation", "arrange_domain_model",
        // Microflows — gaps in studio-pro MCP edit surface
        "exclude_document", "set_microflow_url", "modify_microflow_activity", "insert_before_activity",
        // Project / Settings
        "read_runtime_settings", "set_runtime_settings", "read_configurations", "set_configuration",
        // Data & Sample
        "save_data", "generate_sample_data", "read_sample_data", "setup_data_import",
        // Diagnostics
        "check_model", "check_project_errors", "get_studio_pro_logs", "get_last_error", "analyze_project_patterns",
    };

    public static bool Contains(string toolName) => _names.Contains(toolName);
    public static IReadOnlyCollection<string> All => _names;
}
