# Concord MCP tool sweep -- findings

Generated: 2026-05-13T23:55:07.9260602-04:00  
Endpoint: `http://127.0.0.1:7783/mcp`  
Matrix: `tests/concord-mcp-sweep/matrix.jsonc`

## Summary

| Status | Count |
|---|---|
| PASS | 94 / 94 |
| FAIL | 0 / 94 |
| SKIP | 0 / 94 |

## ConstantsEnums

### Passes

- `list_constants` (25 ms)
- `list_enumerations` (20 ms)
- `create_enumeration` (18 ms)
- `update_enumeration` (23 ms)
- `rename_enumeration_value` (18 ms)
- `create_constant` (19 ms)
- `update_constant` (18 ms)
- `configure_constant_values` (17 ms)

## Diagnostics

### Passes

- `list_available_tools` (22 ms)
- `list_available_tools_domain` (20 ms)
- `analyze_project_patterns` (31 ms)
- `check_model` (22 ms)
- `check_project_errors` (19 ms)
- `check_variable_name` (19 ms)
- `diagnose_associations` (21 ms)
- `get_last_error` (20 ms)
- `get_last_error_domain` (21 ms)
- `get_studio_pro_logs` (18 ms)
- `list_java_actions` (20 ms)

## DomainModel

### Passes

- `create_entity` (112 ms)
- `add_attribute` (23 ms)
- `create_entity` (63 ms)
- `add_attribute` (19 ms)
- `list_modules` (21 ms)
- `read_domain_model` (38 ms)
- `read_project_info` (20 ms)
- `query_model_elements` (19 ms)
- `query_associations` (18 ms)
- `read_attribute_details` (19 ms)
- `validate_name` (20 ms)
- `create_module` (18 ms)
- `create_entity` (100 ms)
- `create_multiple_entities` (82 ms)
- `create_domain_model_from_schema` (101 ms)
- `add_attribute` (67 ms)
- `update_attribute` (54 ms)
- `rename_attribute` (179 ms)
- `set_calculated_attribute` (20 ms)
- `configure_system_attributes` (86 ms)
- `add_event_handler` (19 ms)
- `set_documentation` (88 ms)
- `rename_entity` (157 ms)
- `set_entity_generalization` (19 ms)
- `remove_entity_generalization` (19 ms)
- `copy_model_element` (80 ms)
- `create_association` (21 ms)
- `create_multiple_associations` (25 ms)
- `update_association` (34 ms)
- `rename_association` (19 ms)
- `arrange_domain_model` (85 ms)
- `manage_folders` (20 ms)
- `rename_document` (18 ms)
- `rename_module` (18 ms)
- `delete_model_element` (115 ms)

## Microflows

### Passes

- `create_microflow` (20 ms)
- `create_microflow` (19 ms)
- `list_microflows` (20 ms)
- `read_microflow_details` (20 ms)
- `list_nanoflows` (31 ms)
- `read_nanoflow_details` (18 ms)
- `list_scheduled_events` (17 ms)
- `create_microflow` (200 ms)
- `update_microflow` (73 ms)
- `create_microflow_activity` (61 ms)
- `create_microflow_activities_sequence` (29 ms)
- `modify_microflow_activity` (72 ms)
- `insert_before_activity` (67 ms)
- `set_microflow_url` (77 ms)

## Navigation

### Passes

- `manage_navigation` (18 ms)

## Pages

### Passes

- `list_pages` (46 ms)
- `read_page_details` (17 ms)
- `generate_overview_pages` (19 ms)
- `delete_document` (182 ms)
- `exclude_document` (26 ms)

## ProjectSettings

### Passes

- `read_runtime_settings` (18 ms)
- `read_configurations` (19 ms)
- `list_rest_services` (17 ms)
- `read_version_control` (20 ms)
- `set_runtime_settings` (24 ms)
- `set_configuration` (18 ms)
- `sync_filesystem` (20 ms)

## Security

### Passes

- `list_rules` (18 ms)
- `read_security_info` (19 ms)
- `read_entity_access_rules` (18 ms)
- `read_microflow_security` (19 ms)
- `audit_security` (19 ms)

## UiActions

### Passes

- `get_app_status` (18 ms)
- `get_active_run_configuration` (19 ms)
- `save_all` (22 ms)
- `run_app` (30081 ms)
- `stop_app` (1391 ms)
- `refresh_project` (19 ms)

## Workflows

### Passes

- `list_workflows` (19 ms)
- `read_workflow_details` (20 ms)


