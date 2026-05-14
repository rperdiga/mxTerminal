# Concord MCP tool sweep -- findings

Generated: 2026-05-13T21:17:25.6217562-04:00  
Endpoint: `http://127.0.0.1:7783/mcp`  
Matrix: `tests/concord-mcp-sweep/matrix.jsonc`

## Summary

| Status | Count |
|---|---|
| PASS | 55 / 88 |
| FAIL | 33 / 88 |
| SKIP | 0 / 88 |

## ConstantsEnums

### Failures

#### `update_enumeration` -- **CRASH**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 59 ms
- Args:
  ```json
  {"enumeration_name":"SweepEnum_create_enumeration","add_values":["Cancelled"],"module_name":"MyFirstModule"}
  ```
- Error: The given key 'Mendix.Modeler.ExtensionLoader.ModelProxies.Projects.ModuleProxy' was not present in the dictionary.
- Resolution: _pending triage_

#### `rename_enumeration_value` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 38 ms
- Args:
  ```json
  {"enumeration_name":"SweepEnum_create_enumeration","value_name":"Draft","new_name":"New","module_name":"MyFirstModule"}
  ```
- Error: Invalid name 'New': The name 'New' is a reserved word.
- Resolution: _pending triage_

#### `create_constant` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 34 ms
- Args:
  ```json
  {}
  ```
- Error: success:false
- Resolution: _pending triage_

#### `update_constant` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 34 ms
- Args:
  ```json
  {}
  ```
- Error: success:false
- Resolution: _pending triage_

#### `configure_constant_values` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 53 ms
- Args:
  ```json
  {}
  ```
- Error: success:false
- Resolution: _pending triage_

### Passes

- `list_constants` (43 ms)
- `list_enumerations` (30 ms)
- `create_enumeration` (674 ms)

## Diagnostics

### Failures

#### `analyze_project_patterns` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 30 ms
- Args:
  ```json
  {"module_name":"MyFirstModule"}
  ```
- Error: Failed to analyze project patterns: '<>f__AnonymousType134<int,int,int,int,int,System.Collections.Generic.List<<>f__AnonymousType129<string,int,int>>,<>f__AnonymousType135<int,int,string>,System.Collections.Generic.Dictionary<string,int>,<>f__AnonymousType136<int,string>,<>f__AnonymousType136<int,string>,System.Collections.Generic.Dictionary<string,int>,System.Collections.Generic.List<<>f__AnonymousType130<string,int>>>' does not contain a definition for 'commonDeleteBehavior'
- Resolution: _pending triage_

#### `check_project_errors` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 25 ms
- Args:
  ```json
  {}
  ```
- Error: success:false
- Resolution: _pending triage_

#### `check_variable_name` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 24 ms
- Args:
  ```json
  {"microflow_name":"MyFirstModule.ACT_Example","variable_name":"NewCustomer"}
  ```
- Error: Microflow 'MyFirstModule.ACT_Example' not found.
- Resolution: _pending triage_

#### `list_java_actions` -- **CRASH**

- Phase: `read`
- Expected: `ok`
- Elapsed: 24 ms
- Args:
  ```json
  {}
  ```
- Error: The given key 'Mendix.Modeler.ExtensionLoader.ModelProxies.Projects.ModuleProxy' was not present in the dictionary.
- Resolution: _pending triage_

### Passes

- `list_available_tools` (42 ms)
- `list_available_tools_domain` (27 ms)
- `check_model` (29 ms)
- `diagnose_associations` (30 ms)
- `get_last_error` (29 ms)
- `get_last_error_domain` (23 ms)
- `get_studio_pro_logs` (22 ms)

## DomainModel

### Failures

#### `read_project_info` -- **CRASH**

- Phase: `read`
- Expected: `ok`
- Elapsed: 25 ms
- Args:
  ```json
  {}
  ```
- Error: Failed to read project info: The given key 'Mendix.Modeler.ExtensionLoader.ModelProxies.Projects.ModuleProxy' was not present in the dictionary.
- Resolution: _pending triage_

#### `query_associations` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 25 ms
- Args:
  ```json
  {"entity_name":"Customer","module_name":"MyFirstModule"}
  ```
- Error: Entity 'Customer' not found
- Resolution: _pending triage_

#### `read_attribute_details` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 24 ms
- Args:
  ```json
  {"entity_name":"Customer","attribute_name":"Name"}
  ```
- Error: Entity 'Customer' not found
- Resolution: _pending triage_

#### `copy_model_element` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 44 ms
- Args:
  ```json
  {"element_type":"entity","source_name":"Customer","new_name":"SweepEntity_copy","source_module":"MyFirstModule"}
  ```
- Error: Entity 'Customer' not found in module 'MyFirstModule'.
- Resolution: _pending triage_

### Passes

- `list_modules` (24 ms)
- `read_domain_model` (24 ms)
- `query_model_elements` (23 ms)
- `validate_name` (25 ms)
- `create_module` (991 ms)
- `create_entity` (198 ms)
- `create_multiple_entities` (325 ms)
- `create_domain_model_from_schema` (276 ms)
- `add_attribute` (158 ms)
- `update_attribute` (146 ms)
- `rename_attribute` (537 ms)
- `set_calculated_attribute` (72 ms)
- `configure_system_attributes` (172 ms)
- `add_event_handler` (50 ms)
- `set_documentation` (142 ms)
- `rename_entity` (513 ms)
- `set_entity_generalization` (57 ms)
- `remove_entity_generalization` (28 ms)
- `create_association` (64 ms)
- `create_multiple_associations` (564 ms)
- `update_association` (54 ms)
- `rename_association` (29 ms)
- `arrange_domain_model` (177 ms)
- `manage_folders` (61 ms)
- `rename_document` (42 ms)
- `rename_module` (633 ms)
- `delete_model_element` (206 ms)

## Microflows

### Failures

#### `read_microflow_details` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 25 ms
- Args:
  ```json
  {"microflow_name":"ACT_Example","module_name":"MyFirstModule"}
  ```
- Error: Microflow 'MyFirstModule.ACT_Example' not found.
- Resolution: _pending triage_

#### `read_nanoflow_details` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 32 ms
- Args:
  ```json
  {"nanoflow_name":"MyNanoflow","module_name":"MyFirstModule"}
  ```
- Error: Nanoflow 'MyFirstModule.MyNanoflow' not found
- Resolution: _pending triage_

#### `create_microflow_activity` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 64 ms
- Args:
  ```json
  {"microflow_name":"SweepMf_create_microflow","module_name":"MyFirstModule","activity_type":"log","activity_config":{"message":"\u0027SweepTest log entry\u0027"}}
  ```
- Error: Failed to create activity of type 'log'.
- Resolution: _pending triage_

#### `modify_microflow_activity` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 53 ms
- Args:
  ```json
  {"microflow_name":"SweepMf_create_microflow","module_name":"MyFirstModule","position":1,"caption":"Updated Caption"}
  ```
- Error: Invalid position 1. Microflow has 0 action activities (1-0)
- Resolution: _pending triage_

#### `insert_before_activity` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 59 ms
- Args:
  ```json
  {"microflow_name":"SweepMf_create_microflow","module_name":"MyFirstModule","before_position":1,"activity":{"type":"log","message":"\u0027inserted before\u0027"}}
  ```
- Error: Invalid before_position 1. Microflow has 0 action activities (1-0)
- Resolution: _pending triage_

### Passes

- `list_microflows` (25 ms)
- `list_nanoflows` (72 ms)
- `list_scheduled_events` (31 ms)
- `create_microflow` (723 ms)
- `update_microflow` (144 ms)
- `create_microflow_activities_sequence` (126 ms)
- `set_microflow_url` (131 ms)

## Navigation

### Passes

- `manage_navigation` (37 ms)

## Pages

### Failures

#### `read_page_details` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 36 ms
- Args:
  ```json
  {"page_name":"Customer_Overview","module_name":"MyFirstModule"}
  ```
- Error: Page 'Customer_Overview' not found
- Resolution: _pending triage_

#### `generate_overview_pages` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 54 ms
- Args:
  ```json
  {"module_name":"MyFirstModule","entity_names":["Customer","Order"]}
  ```
- Error: None of the requested entities were found in module 'MyFirstModule'.
- Resolution: _pending triage_

### Passes

- `list_pages` (135 ms)
- `delete_document` (725 ms)
- `exclude_document` (73 ms)

## ProjectSettings

### Failures

#### `set_runtime_settings` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 49 ms
- Args:
  ```json
  {"key":"com.mendix.core.SessionTimeout","value":"3600"}
  ```
- Error: success:false
- Resolution: _pending triage_

#### `set_configuration` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 34 ms
- Args:
  ```json
  {"configuration_name":"Default"}
  ```
- Error: success:false
- Resolution: _pending triage_

#### `sync_filesystem` -- **BUG**

- Phase: `mutate`
- Expected: `ok`
- Elapsed: 34 ms
- Args:
  ```json
  {}
  ```
- Error: success:false
- Resolution: _pending triage_

### Passes

- `read_runtime_settings` (38 ms)
- `read_configurations` (30 ms)
- `list_rest_services` (27 ms)
- `read_version_control` (50 ms)

## Security

### Failures

#### `list_rules` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 42 ms
- Args:
  ```json
  {}
  ```
- Error: success:false
- Resolution: _pending triage_

#### `read_security_info` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 24 ms
- Args:
  ```json
  {}
  ```
- Error: success:false
- Resolution: _pending triage_

#### `read_entity_access_rules` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 36 ms
- Args:
  ```json
  {}
  ```
- Error: success:false
- Resolution: _pending triage_

#### `read_microflow_security` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 36 ms
- Args:
  ```json
  {}
  ```
- Error: success:false
- Resolution: _pending triage_

#### `audit_security` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 33 ms
- Args:
  ```json
  {}
  ```
- Error: success:false
- Resolution: _pending triage_

## UiActions

### Failures

#### `get_app_status` -- **CRASH**

- Phase: `read`
- Expected: `ok`
- Elapsed: 33 ms
- Args:
  ```json
  {}
  ```
- Error: {"error":"Pending Task 15 \u002B Task 1 spike \u2014 10.x IApp surface verification"}
- Resolution: _pending triage_

#### `get_active_run_configuration` -- **CRASH**

- Phase: `read`
- Expected: `ok`
- Elapsed: 35 ms
- Args:
  ```json
  {}
  ```
- Error: {"error":"Pending Task 15 \u2014 10.x ILocalRunConfigurationsService surface verification"}
- Resolution: _pending triage_

#### `run_app` -- **TRANSPORT**

- Phase: `lifecycle`
- Expected: `ok`
- Elapsed: 30075 ms
- Args:
  ```json
  {}
  ```
- Error: The request was aborted: The operation has timed out.
- Resolution: _pending triage_

#### `stop_app` -- **TRANSPORT**

- Phase: `lifecycle`
- Expected: `ok`
- Elapsed: 30014 ms
- Args:
  ```json
  {}
  ```
- Error: The request was aborted: The operation has timed out.
- Resolution: _pending triage_

### Passes

- `save_all` (66 ms)
- `refresh_project` (782 ms)

## Workflows

### Failures

#### `read_workflow_details` -- **BUG**

- Phase: `read`
- Expected: `ok`
- Elapsed: 25 ms
- Args:
  ```json
  {"workflow_name":"MyWorkflow","module_name":"MyFirstModule"}
  ```
- Error: Workflow 'MyWorkflow' not found
- Resolution: _pending triage_

### Passes

- `list_workflows` (35 ms)


