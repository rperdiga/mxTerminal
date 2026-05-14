# Concord MCP tool sweep -- findings

Generated: 2026-05-13T22:49:42.3072526-04:00  
Endpoint: `http://127.0.0.1:7783/mcp`  
Matrix: `tests/concord-mcp-sweep/matrix.jsonc`

## Summary

| Status | Count |
|---|---|
| PASS | 78 / 93 |
| FAIL | 15 / 93 |
| SKIP | 0 / 93 |

## ConstantsEnums

### Failures

#### `create_enumeration` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 30 ms
- Args:
  ```json
  {"name":"SweepEnum_create_enumeration","module_name":"MyFirstModule","values":["Draft","Submitted","Approved"]}
  ```
- Error: Failed to create enumeration: Enumeration 'SweepEnum_create_enumeration' already exists in module 'MyFirstModule'.
- Resolution: _pending triage_

#### `update_enumeration` -- **CRASH**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 24 ms
- Args:
  ```json
  {"enumeration_name":"SweepEnum_create_enumeration","add_values":["Cancelled"],"module_name":"MyFirstModule"}
  ```
- Error: The given key 'Mendix.Modeler.ExtensionLoader.ModelProxies.Projects.ModuleProxy' was not present in the dictionary.
- Resolution: _pending triage_

#### `rename_enumeration_value` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 35 ms
- Args:
  ```json
  {"enumeration_name":"SweepEnum_create_enumeration","value_name":"Draft","new_name":"NewDraft","module_name":"MyFirstModule"}
  ```
- Error: Value 'Draft' not found in enumeration 'SweepEnum_create_enumeration'.
- Resolution: _pending triage_

### Passes

- `list_constants` (42 ms)
- `list_enumerations` (39 ms)
- `create_constant` (26 ms)
- `update_constant` (29 ms)
- `configure_constant_values` (39 ms)

## Diagnostics

### Failures

#### `analyze_project_patterns` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 31 ms
- Args:
  ```json
  {"module_name":"MyFirstModule"}
  ```
- Error: Failed to analyze project patterns: '<>f__AnonymousType134<int,int,int,int,int,System.Collections.Generic.List<<>f__AnonymousType129<string,int,int>>,<>f__AnonymousType135<int,int,string>,System.Collections.Generic.Dictionary<string,int>,<>f__AnonymousType136<int,string>,<>f__AnonymousType136<int,string>,System.Collections.Generic.Dictionary<string,int>,System.Collections.Generic.List<<>f__AnonymousType130<string,int>>>' does not contain a definition for 'commonDeleteBehavior'
- Resolution: _pending triage_

#### `check_project_errors` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 35 ms
- Args:
  ```json
  {}
  ```
- Error: success:false
- Resolution: _pending triage_

#### `list_java_actions` -- **CRASH**

- Phase: `read`
- Expected: `ok`
- Elapsed: 30 ms
- Args:
  ```json
  {}
  ```
- Error: The given key 'Mendix.Modeler.ExtensionLoader.ModelProxies.Projects.ModuleProxy' was not present in the dictionary.
- Resolution: _pending triage_

### Passes

- `list_available_tools` (30 ms)
- `list_available_tools_domain` (25 ms)
- `check_model` (55 ms)
- `check_variable_name` (35 ms)
- `diagnose_associations` (29 ms)
- `get_last_error` (28 ms)
- `get_last_error_domain` (43 ms)
- `get_studio_pro_logs` (28 ms)

## DomainModel

### Failures

#### `read_project_info` -- **CRASH**

- Phase: `read`
- Expected: `ok`
- Elapsed: 26 ms
- Args:
  ```json
  {}
  ```
- Error: Failed to read project info: The given key 'Mendix.Modeler.ExtensionLoader.ModelProxies.Projects.ModuleProxy' was not present in the dictionary.
- Resolution: _pending triage_

#### `create_module` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 26 ms
- Args:
  ```json
  {"module_name":"ConcordSweep_create_module"}
  ```
- Error: Module 'ConcordSweep_create_module' already exists
- Resolution: _pending triage_

#### `create_multiple_associations` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 51 ms
- Args:
  ```json
  {"associations":[{"name":"SweepEntityA_create_multiple_entities_SweepEntityB_create_multiple_entities","parent":"SweepEntityA_create_multiple_entities","child":"SweepEntityB_create_multiple_entities"}],"module_name":"MyFirstModule"}
  ```
- Error: Failed to create associations: Name is not valid or is not unique
- Resolution: _pending triage_

#### `rename_module` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 30 ms
- Args:
  ```json
  {"module_name":"ConcordSweep_create_module","new_name":"ConcordSweep_rename_module"}
  ```
- Error: Name is not valid or is not unique
- Resolution: _pending triage_

### Passes

- `create_entity` (207 ms)
- `add_attribute` (42 ms)
- `create_entity` (144 ms)
- `list_modules` (34 ms)
- `read_domain_model` (35 ms)
- `query_model_elements` (36 ms)
- `query_associations` (33 ms)
- `read_attribute_details` (36 ms)
- `validate_name` (29 ms)
- `create_entity` (151 ms)
- `create_multiple_entities` (150 ms)
- `create_domain_model_from_schema` (69 ms)
- `add_attribute` (143 ms)
- `update_attribute` (133 ms)
- `rename_attribute` (575 ms)
- `set_calculated_attribute` (46 ms)
- `configure_system_attributes` (127 ms)
- `add_event_handler` (47 ms)
- `set_documentation` (170 ms)
- `rename_entity` (456 ms)
- `set_entity_generalization` (28 ms)
- `remove_entity_generalization` (22 ms)
- `copy_model_element` (169 ms)
- `create_association` (547 ms)
- `update_association` (134 ms)
- `rename_association` (60 ms)
- `arrange_domain_model` (130 ms)
- `manage_folders` (26 ms)
- `rename_document` (36 ms)
- `delete_model_element` (187 ms)

## Microflows

### Failures

#### `create_microflow_activity` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 43 ms
- Args:
  ```json
  {"microflow_name":"SweepMf_create_microflow","module_name":"MyFirstModule","activity_type":"log","activity_config":{"message":"\u0027SweepTest log entry\u0027"}}
  ```
- Error: Failed to create activity of type 'log'.
- Resolution: _pending triage_

#### `modify_microflow_activity` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 24 ms
- Args:
  ```json
  {"microflow_name":"SweepMf_create_microflow","module_name":"MyFirstModule","position":1,"caption":"Updated Caption"}
  ```
- Error: Invalid position 1. Microflow has 0 action activities (1-0)
- Resolution: _pending triage_

#### `insert_before_activity` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 30 ms
- Args:
  ```json
  {"microflow_name":"SweepMf_create_microflow","module_name":"MyFirstModule","before_position":1,"activity":{"type":"log","message":"\u0027inserted before\u0027"}}
  ```
- Error: Invalid before_position 1. Microflow has 0 action activities (1-0)
- Resolution: _pending triage_

### Passes

- `create_microflow` (752 ms)
- `create_microflow` (25 ms)
- `list_microflows` (29 ms)
- `read_microflow_details` (24 ms)
- `list_nanoflows` (84 ms)
- `read_nanoflow_details` (26 ms)
- `list_scheduled_events` (36 ms)
- `create_microflow` (738 ms)
- `update_microflow` (129 ms)
- `create_microflow_activities_sequence` (50 ms)
- `set_microflow_url` (140 ms)

## Navigation

### Passes

- `manage_navigation` (30 ms)

## Pages

### Failures

#### `generate_overview_pages` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 31 ms
- Args:
  ```json
  {"module_name":"MyFirstModule","entity_names":["Customer","Order"]}
  ```
- Error: One or more of the passed entities does not have any attributes (Parameter 'entities')
- Resolution: _pending triage_

### Passes

- `list_pages` (157 ms)
- `read_page_details` (28 ms)
- `delete_document` (667 ms)
- `exclude_document` (36 ms)

## ProjectSettings

### Failures

#### `set_runtime_settings` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 35 ms
- Args:
  ```json
  {"key":"com.mendix.core.SessionTimeout","value":"3600"}
  ```
- Error: success:false
- Resolution: _pending triage_

### Passes

- `read_runtime_settings` (33 ms)
- `read_configurations` (29 ms)
- `list_rest_services` (24 ms)
- `read_version_control` (30 ms)
- `set_configuration` (41 ms)
- `sync_filesystem` (36 ms)

## Security

### Passes

- `list_rules` (38 ms)
- `read_security_info` (30 ms)
- `read_entity_access_rules` (22 ms)
- `read_microflow_security` (23 ms)
- `audit_security` (31 ms)

## UiActions

### Passes

- `get_app_status` (32 ms)
- `get_active_run_configuration` (32 ms)
- `save_all` (35 ms)
- `run_app` (30078 ms)
- `stop_app` (708 ms)
- `refresh_project` (33 ms)

## Workflows

### Passes

- `list_workflows` (37 ms)
- `read_workflow_details` (20 ms)


