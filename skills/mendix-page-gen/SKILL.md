---
name: mendix-page-gen
description: Use when creating or modifying a Mendix page. Includes the tiered entry conditions (ped_* for simple pages → generate_overview_pages for list/detail scaffolding → Maia delegation for rich authoring → filesystem for .scss only), the widget catalog (LayoutGrid, DataView, ActionButton, TextBox, TextArea, DatePicker, CheckBox, RadioButtonGroup, DynamicText, DivContainer, TabContainer, Datagrid, etc.), the page-parameter rules, the data-source patterns, the validation checklist, the Maia delegation ladder (JSON construction, maia__ask, ped_check_errors verification, retry budget, refresh), and delete_document for page removal. Trigger when the user asks to create a new page, modify an existing page, add a widget, change a layout, or wire a button to a microflow.
---

## Tools in this environment

- `pg_read_page`, `pg_write_page` → **NOT exposed in this environment.** Delegate to Maia (see "Maia delegation" below).
- `ped_check_errors`, `ped_create_document`, `ped_read_document`, `ped_update_document` → `mcp__mendix-studio-pro__ped_*`. Used to verify pages after Maia writes them, to create page shells, and for simple page content (Tier 1).
- `mcp__concord-mcp__generate_overview_pages` — scaffolds list/detail pages from an entity (Tier 2). Prefer over manual JSON construction or Maia delegation for standard overview/form patterns.
- `mcp__concord-mcp__delete_document` — removes a page document with cascade cleanup. Use for page deletion; do not remove pages via raw `ped_update_document` operations.
- `mcp__concord-mcp__refresh_project` — refresh Studio Pro after a page write.

## Maia delegation

Because `pg_*` tools are not exposed in this environment, every page read or write must be delegated to Maia. The fixed recipe:

1. **Build the JSON locally.** Use the widget catalog below to construct the full minified `pg_write_page` JSON yourself. Do not ask Maia to design the page — just to write it.
2. **Ask Maia to write it.** Call `mcp__concord-mcp__maia__ask` with this prompt template:

   ```
   Use pg_write_page to create or update the page:
     module: <ModuleName>
     pageName: <PageName>
     content: <minified JSON>

   After writing, call ped_check_errors on the page document and on the
   module's DomainModels$DomainModel. Report any errors verbatim.
   ```

3. **Verify directly.** After Maia returns, call `mcp__mendix-studio-pro__ped_check_errors` yourself with `documentType: Pages$Page documentName: <ModuleName>.<PageName>` and again with `documentType: DomainModels$DomainModel documentName: <ModuleName>`. Do not trust Maia's self-report alone.
4. **Retry on errors.** If errors are reported, rebuild the JSON addressing the specific errors and repeat steps 2–3. Maximum two retry attempts. After the second failed attempt, surface the JSON, the Maia response, and the `ped_check_errors` output to the user and stop.
5. **Refresh Studio Pro.** After a successful write, call `mcp__concord-mcp__refresh_project` so the IDE picks up the change.

### CustomWidget exception

`ped_create_document` and `ped_update_document` cannot construct a `CustomWidgets$CustomWidget` element — Studio Pro fails with `NullReferenceException at Mendix.Modeler.WebUI.Forms.Widgets.CustomWidgets.CustomWidget.GetCustomDescription`. Maia's `pg_write_page` *can* sometimes handle CustomWidgets, but it is unreliable.

When a page needs a CustomWidget:

1. Build the page JSON **without** the CustomWidget — leave a placeholder slot (e.g., a `Pages$DivContainer` with a name like `customWidgetPlaceholder`).
2. Delegate to Maia per the recipe above to write the shell.
3. Tell the user: "I created the page shell. Please drag the `<widget name>` widget into the `<placeholder name>` container in Studio Pro — `ped_*` and `pg_*` cannot insert custom widgets reliably."
4. After the user confirms, call `mcp__concord-mcp__refresh_project` and verify with `ped_check_errors`.

# Page Authoring Entry Conditions

Before reaching for Maia, evaluate which tier fits the task. Work top-to-bottom and stop at the first tier that covers the need.

**Tier 1 — `mcp__mendix-studio-pro__ped_create_document` / `ped_update_document`**
Use for simple pages where content is static or structurally predictable: basic widgets (buttons, text, labels), no rich dynamic data binding, no custom layout. `ped_*` is deterministic and does not require a Maia session. Prefer it for scaffolding, stub pages, and page shells.

**Tier 2 — `mcp__concord-mcp__generate_overview_pages`**
Use when the user wants a list or detail page scaffolded directly from a domain entity. This tool generates the overview structure (grid + form) without requiring manual JSON construction or a Maia call. Invoke it before attempting a manual build or a Maia delegation for this class of page.

**Tier 3 — Maia delegate (see "Maia delegation" below)**
Use for richer authoring: custom widgets, complex layouts, dynamic interaction patterns, or any page that exceeds what `ped_*` and `generate_overview_pages` can produce. Once you're on the Maia path, the ladder documented in "Maia delegation" governs how you operate — JSON construction, `maia__ask`, direct `ped_check_errors` verification, retry budget, and refresh.

**Tier 4 — Direct filesystem**
For `.scss` / theme variant files only. Never write the page document itself via the filesystem.

**Page removal:** Use `mcp__concord-mcp__delete_document` to remove a page document. Do not use `ped_update_document` to remove a page from the module's document list — `delete_document` handles cascade cleanup (navigation references, etc.) that a raw remove does not.

---

# Page Generation Common Skills

You are an experienced AI assistant that generates Mendix pages.

## Tools

- `pg_read_page` — read a page by module and page name
- `pg_write_page` — create or update a page in a module

**CRITICAL:** Always use these two tools for reading or writing pages. Never use PED tools (i.e., tools starting with `ped_`) for page operations.

**Efficiency:** Use `pg_write_page` once per page with complete content. Do not create an empty page and then update it — generate the full page JSON in a single call.

**NEVER generate:** `"widgets": []` or `"widgets": [{}]` - these are invalid and will fail validation.

### Example: Simple Page with a Button

```json
{
    "moduleName": "MyFirstModule",
    "pageName": "Customer_Overview",
    "content": {
        "layout": "Atlas_Core.Atlas_Default",
        "parameters": [],
        "variables": [],
        "widgets": [
            {
                "$Type": "Pages$Content",
                "slot": "Main",
                "widgets": [
                    {
                        "$Type": "Pages$ActionButton",
                        "name": "createCustomerButton",
                        "appearance": {
                            "$Type": "Pages$Appearance",
                            "class": "",
                            "style": "",
                            "dynamicClasses": "",
                            "designProperties": {}
                        },
                        "conditionalVisibilitySettings": null,
                        "ct:caption": "Create Customer",
                        "t:tooltip": "",
                        "icon": null,
                        "action": {
                            "$Type": "Pages$CreateObjectClientAction",
                            "entityRef": {
                                "$Type": "DomainModels$DirectEntityRef",
                                "entity": "MyFirstModule.Customer"
                            },
                            "pageSettings": {
                                "$Type": "Pages$PageSettings",
                                "page": "MyFirstModule.Customer_NewEdit",
                                "parameterMappings": []
                            },
                            "disabledDuringExecution": true
                        },
                        "tabIndex": 0,
                        "renderType": "Button",
                        "buttonStyle": "Primary",
                        "ariaRole": "Button"
                    }
                ]
            }
        ]
    }
}
```

### Pre-Generation Checklist

Before calling `pg_write_page`, ALWAYS verify:

✅ **Structure:**

- Root `widgets` are always `Pages$Content`

✅ **Widget Requirements:**

- Every widget has `$Type` matching exactly from documentation
- All properties marked "Req: Y" are present

✅ **Page Parameter Requirements:**

1. **When to create page parameters:**
    - ALWAYS when using `Pages$DataViewSource` with `sourceVariable.pageParameter`
    - ALWAYS when a page needs to receive data from another page/action
    - When form pages need to edit/display a specific object

2. **Page Parameter Declaration:**
    - MUST be declared in root-level `parameters: []` array
    - MUST have `$QualifiedName: "Module.PageName.ParameterName"`
    - The parameter name in `sourceVariable.pageParameter` MUST match the declared parameter name

3. **Validation:**
    - Before writing a page, check: does any widget reference a page parameter?
    - If yes, ensure that parameter exists in the root `parameters` array

✅ **References:**

All references are by-name and can be discovered by reading the related documents using PED.

- Entity references use format: `"Module.Entity"`
- Attribute references use format: `"Module.Entity.Attribute"`
- Association references use format: `"Module.Entity_RelatedEntity"`
- Page references use format: `"Module.PageName"`
- Workflow references use format: `"Module.WorkflowName"`
- Microflow references use format: `"Module.MicroflowName"`
- Nanoflow references use format: `"Module.NanoflowName"`

✅ **Data Sources:**

- DataView has complete `dataSource` object with `entityRef` and `sourceVariable`
- Datagrid has complete `datasource` object with `entityRef` and `sortBar`

✅ **Refrencing Other Widgets**

- When generating a page with listening targets, ensure the listening target value EXACTLY matches the name property of the widget you're referencing. Double-check internal consistency before writing the page.
- If a data view listens to dataGrid1, then there must be a grid widget with name: 'dataGrid1' on that same page.

## Rules

**Output:** Minified JSON only — no whitespace, line breaks, or indentation. Single continuous string.

**Valid JSON:** Before finalizing output, mentally trace every opening bracket `{` and `[` and confirm it has a matching closing `}` and `]`.
The output must parse as valid JSON with zero extra or missing brackets.

**Strict compliance:** Use ONLY widgets and attributes documented below. Never invent undocumented solutions. If a requirement can't be met: state what's missing, suggest the closest alternative, ask for clarification.

**Modifications:** Apply ONLY what's explicitly requested. Preserve all existing structure, names, and order. Surgical edits only — changing button text means touching only `ct:caption`, nothing else. Restructure only when explicitly asked or physically impossible otherwise.

**You may ONLY use `$Type` values explicitly listed in this document.**
Before using any `$Type`, search this document to confirm it exists. If it's not listed, you cannot use it. Do not attempt to guess or create new types.

## Consistency Validation

**CRITICAL:** After generating any page JSON, you MUST perform these validation checks before calling `pg_write_page`:

1. **Pre-Generation Manual Validation:**
    - **Bracket Matching:** Verify every `{` has a closing `}` and every `[` has a closing `]`
    - **Required Properties:** Confirm all properties marked "Req: Y" are present for each widget
    - **Type Consistency:** Ensure `$Type` values exactly match documented widget types
    - **Reference Validity:** Check all entity/attribute references use correct module.entity.attribute format
    - **Data Source Completeness:** Verify DataView and Datagrid have properly structured `dataSource` objects
    - **Widget Nesting:** Confirm widgets are in valid containers (e.g., form fields inside DataView)
    - **Page Parameters:** If the page uses a page parameter (e.g., in DataViewSource), ensure it's declared in the page's `parameters` array

2. **Post-Generation Tool Validation:**
    - **IMMEDIATELY** after calling `pg_write_page`, use the consistency checking tool on the generated page
    - The tool will identify structural errors, missing properties, invalid references, and missing page parameters
    - Review all errors reported by the tool

**Error Correction Process:**

- Perform manual validation before calling `pg_write_page` to catch obvious issues
- Call `pg_write_page` to generate the page
- **IMMEDIATELY** run the consistency checking tool `ped_check_errors` on the newly created page
- If the tool reports errors:
    - Identify each specific error (missing property, invalid reference, missing page parameter)
    - Fix ALL errors in the JSON
    - Call `pg_write_page` again with the corrected JSON
    - Re-run the consistency checking tool `ped_check_errors` to verify all issues are resolved
- Repeat until the consistency tool reports zero errors

## Layouts

- `Atlas_Core.Atlas_Default` — default page layout
- `Atlas_Core.PopupLayout` — modal popup layout

**Note:** This shows the structure of the `content` object only. When calling `pg_write_page`, you also pass `moduleName` and `pageName` as separate parameters (see examples above).

`parameters` — optional array of `Pages$PageParameter` objects defining the page's input parameters.

`appearance` — optional. Object with `class` (space-separated CSS classes), `style` (CSS string), `dynamicClasses` (expression), `designProperties` (key-value design property overrides), or `null`.

`widgets` — array of widget objects. Each widget must have a `$Type` property specifying its type (e.g., `Pages$ActionButton`) and other properties as defined in the documentation below.

### PageParameter

| Property       | Type    | Req | Notes                                                                      |
| -------------- | ------- | --- | -------------------------------------------------------------------------- |
| $Type          | string  | Y   | `Pages$PageParameter`                                                      |
| $QualifiedName | string  | Y   | fully-qualified name of the parameter (e.g. `Module.PageName.ParamName`)   |
| name           | string  | Y   | Parameter name                                                             |
| isRequired     | boolean | Y   | —                                                                          |
| parameterType  | object  | Y   | Defining the parameter's data type (see Parameter or Variable Types below) |
| defaultValue   | string  | N   | Default value for the parameter (null if none)                             |

### LocalVariable

Defines a local variable within a page or snippet scope for storing temporary data.

| Property     | Type   | Req | Notes                                                                                                                                   |
| ------------ | ------ | --- | --------------------------------------------------------------------------------------------------------------------------------------- |
| $Type        | string | Y   | `Pages$LocalVariable`                                                                                                                   |
| name         | string | Y   | Variable name                                                                                                                           |
| variableType | object | Y   | Defining the variable's data type (see Parameter or Variable Types below). **Important:** Local variables only support primitive types. |
| defaultValue | string | N   | Default value for the variable (null if none)                                                                                           |

**Parameter or Variable Types:**

The `parameterType` or `variableType` properties can be one of the following DataType objects:

**Primitive Types:**

```json
{ "$Type": "DataTypes$BooleanType" }
{ "$Type": "DataTypes$StringType" }
{ "$Type": "DataTypes$IntegerType" }
{ "$Type": "DataTypes$DecimalType" }
{ "$Type": "DataTypes$FloatType" }
{ "$Type": "DataTypes$DateTimeType" }
{ "$Type": "DataTypes$BinaryType" }
```

**Enumeration Type:**

```json
{
    "$Type": "DataTypes$EnumerationType",
    "enumeration": "ModuleName.EnumerationName"
}
```

**Entity Types:**

For a single object:

```json
{
    "$Type": "DataTypes$ObjectType",
    "entity": "ModuleName.EntityName"
}
```

For a list of objects:

```json
{
    "$Type": "DataTypes$ListType",
    "entity": "ModuleName.EntityName"
}
```

**When to use LocalVariables:**

- Holding intermediate calculation results
- Managing page-level data that doesn't need persistence

**IMPORTANT:** Only primitive types are allowed for local variables.

## Widgets

### Rules

**Widget names:** unique, camelCase (e.g. `layoutGrid1`, `actionButton1`).

### LayoutGrid

12-column responsive grid container.

| Property                      | Type        | Req | Default            | Notes                                |
| ----------------------------- | ----------- | --- | ------------------ | ------------------------------------ |
| $Type                         | string      | Y   | `Pages$LayoutGrid` | Must be exactly `Pages$LayoutGrid`   |
| name                          | string      | Y   | —                  | Identifier for the grid              |
| appearance                    | object      | Y   | —                  | `Pages$Appearance` object            |
| conditionalVisibilitySettings | object/null | N   | null               | Conditional visibility configuration |
| tabIndex                      | number      | Y   | 0                  | Tab order                            |
| width                         | enum        | Y   | `FullWidth`        | `FullWidth`, `FixedWidth`            |
| rows                          | array       | Y   | —                  | Array of `Pages$LayoutGridRow`       |

### LayoutGridRow

| Property                      | Type        | Req | Default               | Notes                                                                 |
| ----------------------------- | ----------- | --- | --------------------- | --------------------------------------------------------------------- |
| $Type                         | string      | Y   | `Pages$LayoutGridRow` | Must be exactly `Pages$LayoutGridRow`                                 |
| appearance                    | object      | Y   | —                     | `Pages$Appearance` object                                             |
| conditionalVisibilitySettings | object/null | N   | null                  | Conditional visibility configuration                                  |
| verticalAlignment             | enum        | Y   | `None`                | `None`, `Start`, `Center`, `End`                                      |
| horizontalAlignment           | enum        | Y   | `None`                | `None`, `Start`, `Center`, `End`                                      |
| spacingBetweenColumns         | boolean     | Y   | —                     | Whether to add spacing between columns                                |
| columns                       | array       | Y   | —                     | Array of `Pages$LayoutGridColumn`; weights per row must not exceed 12 |

### LayoutGridColumn

| Property                      | Type        | Req | Default                  | Notes                                                |
| ----------------------------- | ----------- | --- | ------------------------ | ---------------------------------------------------- |
| $Type                         | string      | Y   | `Pages$LayoutGridColumn` | Must be exactly `Pages$LayoutGridColumn`             |
| appearance                    | object      | Y   | —                        | `Appearance` object                                  |
| conditionalVisibilitySettings | object/null | N   | null                     | Conditional visibility configuration                 |
| weight                        | number      | Y   | —                        | 1–12, or `-1` for auto, or `-2` for special behavior |
| tabletWeight                  | number      | Y   | `-1`                     | 1–12, or `-1` for auto                               |
| phoneWeight                   | number      | Y   | `-1`                     | 1–12, or `-1` for auto                               |
| previewWidth                  | number      | Y   | `-1`                     | Preview width setting                                |
| verticalAlignment             | enum        | Y   | `None`                   | `None`, `Start`, `Center`, `End`                     |
| widgets                       | array       | Y   | —                        | Array of any widget objects                          |

### Title

Displays the page title. Automatically shows the page's title property.

| Property                      | Type        | Req | Default       | Notes                                |
| ----------------------------- | ----------- | --- | ------------- | ------------------------------------ |
| $Type                         | string      | Y   | `Pages$Title` | Must be exactly `Pages$Title`        |
| name                          | string      | Y   | —             | Identifier for the title widget      |
| appearance                    | object      | Y   | —             | `Pages$Appearance` object            |
| conditionalVisibilitySettings | object/null | N   | null          | Conditional visibility configuration |
| tabIndex                      | number      | Y   | 0             | Tab order                            |

### DataView

Displays and edits a single object. Always wrap form fields in a DataView.

| Property                       | Type        | Req | Default          | Notes                                                                                                    |
| ------------------------------ | ----------- | --- | ---------------- | -------------------------------------------------------------------------------------------------------- |
| $Type                          | string      | Y   | `Pages$DataView` | Must be exactly `Pages$DataView`                                                                         |
| name                           | string      | Y   | —                | Identifier for the data view                                                                             |
| appearance                     | object      | Y   | —                | `Pages$Appearance` object                                                                                |
| conditionalVisibilitySettings  | object/null | N   | null             | Conditional visibility configuration                                                                     |
| conditionalEditabilitySettings | object/null | N   | null             | Conditional editability configuration                                                                    |
| dataSource                     | object      | Y   | —                | `Pages$DataViewSource`, `Pages$MicroflowSource` or `Pages$ListenTargetSource` - see Data Sources section |
| widgets                        | array       | Y   | —                | Array of form field widgets                                                                              |
| footerWidgets                  | array       | Y   | —                | Array of footer widgets (typically Save/Cancel buttons)                                                  |
| t:noEntityMessage              | string      | Y   | —                | message when no entity is available                                                                      |
| tabIndex                       | number      | Y   | 0                | Tab order                                                                                                |
| editability                    | enum        | Y   | `Always`         | `Always`, `Never`                                                                                        |
| showFooter                     | boolean     | Y   | —                | Whether to display the footer section                                                                    |
| labelWidth                     | number      | Y   | —                | 0 = vertical labels, 1–11 = horizontal label width in columns                                            |
| readOnlyStyle                  | enum        | Y   | `Control`        | `Control`, `Inherit`                                                                                     |

### ActionButton

| Property                      | Type                          | Req | Default              | Notes                                                                   |
| ----------------------------- | ----------------------------- | --- | -------------------- | ----------------------------------------------------------------------- |
| $Type                         | string                        | Y   | `Pages$ActionButton` | Must be exactly `Pages$ActionButton`                                    |
| name                          | string                        | Y   | —                    | Identifier for the button                                               |
| appearance                    | object                        | Y   | —                    | `Pages$Appearance` object                                               |
| conditionalVisibilitySettings | object/null                   | N   | null                 | Conditional visibility configuration                                    |
| ct:caption                    | string/`Pages$ClientTemplate` | Y   | —                    | button text                                                             |
| t:tooltip                     | string/`Texts$Text`           | Y   | —                    | hover tooltip                                                           |
| icon                          | object/null                   | N   | null                 | `Pages$IconCollectionIcon` object                                       |
| action                        | object                        | Y   | —                    | Action object - see Actions section                                     |
| tabIndex                      | number                        | Y   | 0                    | Tab order                                                               |
| renderType                    | enum                          | Y   | `Button`             | `Button`, `Link`                                                        |
| buttonStyle                   | enum                          | Y   | `Default`            | `Default`, `Inverse`, `Primary`, `Info`, `Success`, `Warning`, `Danger` |
| ariaRole                      | enum                          | Y   | `Button`             | `Button`                                                                |

**Style Guidelines:** Use `Success` for save actions, `Danger` for delete actions, `Default` for cancel actions.

### TextBox

Single-line text input.

| Property                       | Type                          | Req | Default         | Notes                                                         |
| ------------------------------ | ----------------------------- | --- | --------------- | ------------------------------------------------------------- |
| $Type                          | string                        | Y   | `Pages$TextBox` | Must be exactly `Pages$TextBox`                               |
| name                           | string                        | Y   | —               | Identifier for the text box                                   |
| appearance                     | object                        | Y   | —               | `Pages$Appearance` object                                     |
| conditionalVisibilitySettings  | object/null                   | N   | null            | Conditional visibility configuration                          |
| conditionalEditabilitySettings | object/null                   | N   | null            | Conditional editability configuration                         |
| ct:labelTemplate               | string/`Pages$ClientTemplate` | Y   | —               | label text                                                    |
| attributeRef                   | object                        | Y   | —               | `DomainModels:AttributeRef` object specifying bound attribute |
| validation                     | object                        | Y   | —               | `Pages$WidgetValidation` object                               |
| onChangeAction                 | object                        | Y   | —               | Action object (e.g., `Pages$NoClientAction`)                  |
| onEnterAction                  | object                        | Y   | —               | Action object (e.g., `Pages$NoClientAction`)                  |
| onLeaveAction                  | object                        | Y   | —               | Action object (e.g., `Pages$NoClientAction`)                  |
| sourceVariable                 | object/null                   | N   | null            | `Pages$PageVariable` object for alternate data source         |
| ct:placeholderTemplate         | string/`Pages$ClientTemplate` | Y   | —               | placeholder text                                              |
| formattingInfo                 | object                        | Y   | —               | `Pages$FormattingInfo` object for formatting configuration    |
| onEnterKeyPressAction          | object                        | Y   | —               | Action object (e.g., `Pages$NoClientAction`)                  |
| tabIndex                       | number                        | Y   | 0               | Tab order                                                     |
| editable                       | enum                          | Y   | `Always`        | `Always`, `Never`                                             |
| readOnlyStyle                  | enum                          | Y   | `Inherit`       | `Inherit`, `Control`, `Text`                                  |
| maxLengthCode                  | number                        | Y   | -1              | Maximum character length (-1 for unlimited)                   |
| autoFocus                      | boolean                       | Y   | false           | Whether to auto-focus on page load                            |
| inputMask                      | string                        | Y   | ""              | Input mask pattern (empty string for none)                    |
| isPasswordBox                  | boolean                       | Y   | false           | Whether to mask input as password                             |
| keyboardType                   | enum                          | Y   | `Default`       | `Default`                                                     |
| submitBehaviour                | enum                          | Y   | `OnEndEditing`  | `OnEndEditing`, `WhileEditing`                                |
| submitOnInputDelay             | number                        | Y   | 300             | Delay in milliseconds before submitting while editing         |

### TextArea

Multi-line text input. Use for long content (notes, descriptions, addresses).

| Property                       | Type                          | Req | Default          | Notes                                                         |
| ------------------------------ | ----------------------------- | --- | ---------------- | ------------------------------------------------------------- |
| $Type                          | string                        | Y   | `Pages$TextArea` | Must be exactly `Pages$TextArea`                              |
| name                           | string                        | Y   | —                | Identifier for the text area                                  |
| appearance                     | object                        | Y   | —                | `Pages$Appearance` object                                     |
| conditionalVisibilitySettings  | object/null                   | N   | null             | Conditional visibility configuration                          |
| conditionalEditabilitySettings | object/null                   | N   | null             | Conditional editability configuration                         |
| ct:labelTemplate               | string/`Pages$ClientTemplate` | Y   | —                | label text                                                    |
| attributeRef                   | object                        | Y   | —                | `DomainModels$AttributeRef` object specifying bound attribute |
| validation                     | object                        | Y   | —                | `Pages$WidgetValidation` object                               |
| onChangeAction                 | object                        | Y   | —               | Action object (e.g., `Pages$NoClientAction`)                  |
| onEnterAction                  | object                        | Y   | —                | Action object (e.g., `Pages$NoClientAction`)                  |
| onLeaveAction                  | object                        | Y   | —                | Action object (e.g., `Pages$NoClientAction`)                  |
| sourceVariable                 | object/null                   | N   | null             | `Pages$PageVariable` object for alternate data source         |
| ct:placeholderTemplate         | string/`Pages$ClientTemplate` | Y   | —                | placeholder text                                              |
| t:counterMessage               | string/`Texts$Text`           | N   | —                | character counter message                                     |
| t:textTooLongMessage           | string/`Texts$Text`           | N   | —                | text too long validation message                              |
| tabIndex                       | number                        | Y   | 0                | Tab order                                                     |
| editable                       | enum                          | Y   | `Always`         | `Always`, `Never`                                             |
| readOnlyStyle                  | enum                          | Y   | `Inherit`        | `Inherit`, `Control`, `Text`                                  |
| maxLengthCode                  | number                        | Y   | -1               | Maximum character length (-1 for unlimited)                   |
| autoFocus                      | boolean                       | Y   | false            | Whether to auto-focus on page load                            |
| numberOfLines                  | number                        | Y   | 0                | Number of visible lines (0 for auto-height)                   |
| autocomplete                   | boolean                       | Y   | true             | Whether to enable browser autocomplete                        |
| submitBehaviour                | enum                          | Y   | `OnEndEditing`   | `OnEndEditing`, `WhileEditing`                                |
| submitOnInputDelay             | number                        | Y   | 300              | Delay in milliseconds before submitting while editing         |

### DatePicker

| Property                       | Type                          | Req | Default            | Notes                                                         |
| ------------------------------ | ----------------------------- | --- | ------------------ | ------------------------------------------------------------- |
| $Type                          | string                        | Y   | `Pages$DatePicker` | Must be exactly `Pages$DatePicker`                            |
| name                           | string                        | Y   | —                  | Identifier for the date picker                                |
| appearance                     | object                        | Y   | —                  | `Pages$Appearance` object                                     |
| conditionalVisibilitySettings  | object/null                   | N   | null               | Conditional visibility configuration                          |
| conditionalEditabilitySettings | object/null                   | N   | null               | Conditional editability configuration                         |
| ct:labelTemplate               | string/`Pages$ClientTemplate` | Y   | null               | label text                                                    |
| attributeRef                   | object                        | Y   | —                  | `DomainModels$AttributeRef` object specifying bound attribute |
| validation                     | object                        | Y   | —                  | `Pages$WidgetValidation` object                               |
| onChangeAction                 | object                        | Y   | —                  | Action object (e.g., `Pages$NoClientAction`)                  |
| onEnterAction                  | object                        | Y   | —                  | Action object (e.g., `Pages$NoClientAction`)                  |
| onLeaveAction                  | object                        | Y   | —                  | Action object (e.g., `Pages$NoClientAction`)                  |
| sourceVariable                 | object/null                   | N   | null               | `Pages$PageVariable` object for alternate data source         |
| ct:placeholderTemplate         | string/`Pages$ClientTemplate` | N   | —                  | placeholder text                                              |
| formattingInfo                 | object                        | Y   | —                  | `Pages$FormattingInfo` object for date formatting             |
| tabIndex                       | number                        | Y   | 0                  | Tab order                                                     |
| editable                       | enum                          | Y   | `Always`           | `Always`, `Never`                                             |
| readOnlyStyle                  | enum                          | Y   | `Inherit`          | `Inherit`, `Control`, `Text`                                  |

### CheckBox

| Property                       | Type                          | Req | Default          | Notes                                                         |
| ------------------------------ | ----------------------------- | --- | ---------------- | ------------------------------------------------------------- |
| $Type                          | string                        | Y   | `Pages$CheckBox` | Must be exactly `Pages$CheckBox`                              |
| name                           | string                        | Y   | —                | Identifier for the checkbox                                   |
| appearance                     | object                        | Y   | —                | `Pages$Appearance` object                                     |
| conditionalVisibilitySettings  | object/null                   | N   | null             | Conditional visibility configuration                          |
| conditionalEditabilitySettings | object/null                   | N   | null             | Conditional editability configuration                         |
| ct:labelTemplate               | string/`Pages$ClientTemplate` | N   | —                | Label text                                                    |
| attributeRef                   | object                        | Y   | —                | `DomainModels$AttributeRef` object specifying bound attribute |
| validation                     | object                        | Y   | —                | `Pages$WidgetValidation` object                               |
| onChangeAction                 | object                        | Y   | —                | Action object (e.g., `Pages$NoClientAction`)                  |
| onEnterAction                  | object                        | Y   | —                | Action object (e.g., `Pages$NoClientAction`)                  |
| onLeaveAction                  | object                        | Y   | —                | Action object (e.g., `Pages$NoClientAction`)                  |
| sourceVariable                 | object/null                   | N   | null             | `Pages$PageVariable` object for alternate data source         |
| tabIndex                       | number                        | Y   | 0                | Tab order                                                     |
| editable                       | enum                          | Y   | `Always`         | `Always`, `Never`                                             |
| readOnlyStyle                  | enum                          | Y   | `Inherit`        | `Inherit`, `Control`, `Text`                                  |
| ariaRequired                   | boolean                       | Y   | false            | Whether checkbox is required for accessibility                |
| labelPosition                  | enum                          | Y   | `Default`        | `Default`, `BeforeControl`, `AfterControl`                    |

### RadioButtonGroup

Single-select from mutually exclusive options.

| Property                       | Type                          | Req | Default                  | Notes                                                               |
| ------------------------------ | ----------------------------- | --- | ------------------------ | ------------------------------------------------------------------- |
| $Type                          | string                        | Y   | `Pages$RadioButtonGroup` | Must be exactly `Pages$RadioButtonGroup`                            |
| name                           | string                        | Y   | —                        | Identifier for the radio button group                               |
| appearance                     | object                        | Y   | —                        | `Pages$Appearance` object                                           |
| conditionalVisibilitySettings  | object/null                   | N   | null                     | Conditional visibility configuration                                |
| conditionalEditabilitySettings | object/null                   | N   | null                     | Conditional editability configuration                               |
| ct:labelTemplate               | string/`Pages$ClientTemplate` | Y   | null                     | label text                                                          |
| attributeRef                   | object                        | Y   | —                        | `DomainModels$AttributeRef` object specifying bound attribute       |
| validation                     | object                        | Y   | —                        | `Pages$WidgetValidation` object                                     |
| onChangeAction                 | object                        | Y   | —                        | Action object (e.g., `Pages$NoClientAction`)                        |
| onEnterAction                  | object                        | Y   | —                        | Action object (e.g., `Pages$NoClientAction`)                        |
| onLeaveAction                  | object                        | Y   | —                        | Action object (e.g., `Pages$NoClientAction`)                        |
| sourceVariable                 | object/null                   | N   | null                     | `Pages$PageVariable` object for alternate data source               |
| tabIndex                       | number                        | Y   | 0                        | Tab order                                                           |
| editable                       | enum                          | Y   | `Always`                 | `Always`, `Never`                                                   |
| readOnlyStyle                  | enum                          | Y   | `Inherit`                | `Inherit`, `Control`, `Text`                                        |
| renderHorizontal               | boolean                       | Y   | —                        | Whether to render options horizontally (true) or vertically (false) |

**IMPORTANT** Always use vertical radio buttons when you are using `Atlas_Core.PopupLayout`.

### DynamicText

Static or dynamic text display. Do not use for HTML content.

| Property        | Type                          | Req | Default             | Notes                                                   |
| --------------- | ----------------------------- | --- | ------------------- | ------------------------------------------------------- |
| $Type           | string                        | Y   | `Pages$DynamicText` | Must be exactly `Pages$DynamicText`                     |
| name            | string                        | Y   | —                   | Identifier for the text element                         |
| appearance      | object                        | Y   | —                   | `Pages$Appearance` object                               |
| ct:content      | string/`Pages$ClientTemplate` | Y   | —                   | Text content                                            |
| tabIndex        | number                        | Y   | 0                   | Tab order                                               |
| renderMode      | enum                          | Y   | —                   | `Text`, `Paragraph`, `H1`, `H2`, `H3`, `H4`, `H5`, `H6` |
| nativeTextStyle | enum                          | Y   | `Text`              | `Text` - text styling for native apps                   |

### DivContainer

Generic container for grouping widgets with semantic HTML5 elements.

| Property           | Type    | Req | Default              | Notes                                                                                        |
| ------------------ | ------- | --- | -------------------- | -------------------------------------------------------------------------------------------- |
| $Type              | string  | Y   | `Pages$DivContainer` | Must be exactly `Pages$DivContainer`                                                         |
| name               | string  | Y   | —                    | Identifier for the container                                                                 |
| appearance         | object  | Y   | —                    | `Pages$Appearance` object                                                                    |
| widgets            | array   | Y   | —                    | Array of child widgets contained within this container                                       |
| onClickAction      | object  | Y   | —                    | Action object (e.g., `Pages$NoClientAction`)                                                 |
| tabIndex           | number  | Y   | 0                    | Tab order                                                                                    |
| renderMode         | enum    | Y   | `Div`                | `Div`, `Section`, `Article`, `Header`, `Footer`, `Main`, `Nav`, `Aside`, `Hgroup`, `Address` |
| screenReaderHidden | boolean | Y   | false                | Whether to hide from screen readers                                                          |

### TabContainer

Container widget that organizes content into tabbed pages.

| Property   | Type   | Req | Default              | Notes                                |
| ---------- | ------ | --- | -------------------- | ------------------------------------ |
| $Type      | string | Y   | `Pages$TabContainer` | Must be exactly `Pages$TabContainer` |
| name       | string | Y   | —                    | Identifier for the tab container     |
| appearance | object | Y   | —                    | `Pages$Appearance` object            |
| tabPages   | array  | Y   | —                    | Array of `Pages$TabPage` objects     |
| tabIndex   | number | Y   | 0                    | Tab order                            |

### TabPage

Individual tab page within a TabContainer.

| Property      | Type                | Req | Default         | Notes                                     |
| ------------- | ------------------- | --- | --------------- | ----------------------------------------- |
| $Type         | string              | Y   | `Pages$TabPage` | Must be exactly `Pages$TabPage`           |
| name          | string              | Y   | —               | Identifier for the tab page               |
| t:caption     | string/`Texts$Text` | Y   | —               | Tab label text displayed to users         |
| widgets       | array               | Y   | —               | Array of widgets contained in this tab    |
| refreshOnShow | boolean             | Y   | false           | Whether to refresh data when tab is shown |

### Datagrid

Table widget for displaying and managing lists of data with advanced features like filtering, sorting, and pagination.

| Property                                | Type                          | Req | Default                                   | Notes                                                               |
| --------------------------------------- | ----------------------------- | --- | ----------------------------------------- | ------------------------------------------------------------------- |
| $Type                                   | string                        | Y   | `CustomWidgets$CustomWidget`              | Must be exactly `CustomWidgets$CustomWidget`                        |
| widgetId                                | string                        | Y   | `com.mendix.widget.web.datagrid.Datagrid` | Widget identifier for Datagrid                                      |
| appearance                              | object                        | Y   | —                                         | `Pages$Appearance` object                                           |
| name                                    | string                        | Y   | —                                         | Identifier for the widget                                           |
| tabIndex                                | number                        | Y   | 0                                         | Tab order                                                           |
| editable                                | enum                          | Y   | `Always`                                  | `Always` - editability setting                                      |
| object                                  | object                        | Y   | —                                         | Configuration object containing all datagrid-specific settings      |
| object.datasource                       | object                        | Y   | —                                         | `CustomWidgets$CustomWidgetXPathSource` - data source configuration |
| object.refreshInterval                  | number                        | Y   | 0                                         | Auto-refresh interval in seconds (0 = disabled)                     |
| object.columns                          | array                         | Y   | —                                         | Array of `CustomWidgets$WidgetObject` column definitions            |
| object.columnsFilterable                | boolean                       | Y   | true                                      | Whether columns can be filtered                                     |
| object.onClickTrigger                   | enum                          | Y   | `single`                                  | `single`, `double` - click behavior for row selection               |
| object.filtersPlaceholder               | array                         | Y   | —                                         | Widgets rendered in filter bar (e.g., ActionButton for "New")       |
| object.itemSelection                    | enum                          | Y   | `None`                                    | `None`, `Single` - row selection mode                               |
| object.itemSelectionMethod              | enum                          | Y   | `checkbox`                                | `checkbox`, `rowClick` - how users select rows                      |
| object.autoSelect                       | boolean                       | Y   | false                                     | Auto-select first row on load                                       |
| object.itemSelectionMode                | enum                          | Y   | `clear`                                   | `clear` - selection behavior                                        |
| object.showSelectAllToggle              | boolean                       | Y   | true                                      | Show select all checkbox                                            |
| object.enableSelectAll                  | boolean                       | Y   | false                                     | Enable selecting all rows across pages                              |
| object.keepSelection                    | boolean                       | Y   | false                                     | Maintain selection when data refreshes                              |
| object.selectionCounterPosition         | enum                          | Y   | `bottom`                                  | `bottom` - position of selection counter                            |
| object.loadingType                      | enum                          | Y   | `spinner`                                 | `spinner` - loading indicator style                                 |
| object.refreshIndicator                 | boolean                       | Y   | false                                     | Show refresh indicator                                              |
| object.pageSize                         | number                        | Y   | 20                                        | Number of rows per page                                             |
| object.pagination                       | enum                          | Y   | `buttons`                                 | `buttons` - pagination style                                        |
| object.useCustomPagination              | boolean                       | Y   | false                                     | Use custom pagination widgets                                       |
| object.customPagination                 | array                         | Y   | []                                        | Custom pagination widgets                                           |
| object.showPagingButtons                | enum                          | Y   | `always`                                  | `always` - when to show pagination buttons                          |
| object.showNumberOfRows                 | boolean                       | Y   | false                                     | Display total row count                                             |
| object.pagingPosition                   | enum                          | Y   | `bottom`                                  | `bottom` - position of pagination controls                          |
| object.showEmptyPlaceholder             | enum                          | Y   | `none`                                    | `none` - empty state display                                        |
| object.emptyPlaceholder                 | array                         | Y   | []                                        | Widgets shown when grid is empty                                    |
| object.columnsSortable                  | boolean                       | Y   | true                                      | Whether columns can be sorted                                       |
| object.columnsResizable                 | boolean                       | Y   | true                                      | Whether column widths can be resized                                |
| object.columnsDraggable                 | boolean                       | Y   | true                                      | Whether columns can be reordered                                    |
| object.columnsHidable                   | boolean                       | Y   | true                                      | Whether columns can be hidden                                       |
| object.configurationStorageType         | enum                          | Y   | `attribute`                               | `attribute` - how to persist grid configuration                     |
| object.storeFiltersInPersonalization    | boolean                       | Y   | true                                      | Save filter state in user preferences                               |
| object.ct:filterSectionTitle            | string/`Pages$ClientTemplate` | Y   | ""                                        | Filter section heading text                                         |
| object.ct:exportDialogLabel             | string/`Pages$ClientTemplate` | Y   | `Export progress`                         | Export dialog label text                                            |
| object.ct:cancelExportLabel             | string/`Pages$ClientTemplate` | Y   | `Cancel data export`                      | Cancel export button text                                           |
| object.ct:selectRowLabel                | string/`Pages$ClientTemplate` | Y   | `Select row`                              | Select row accessibility label                                      |
| object.ct:selectAllRowsLabel            | string/`Pages$ClientTemplate` | Y   | `Select all rows`                         | Select all rows accessibility label                                 |
| object.ct:selectingAllLabel             | string/`Pages$ClientTemplate` | Y   | `Selecting all items...`                  | Selecting all status text                                           |
| object.ct:cancelSelectionLabel          | string/`Pages$ClientTemplate` | Y   | `Cancel selection`                        | Cancel selection button text                                        |
| object.ct:selectedCountTemplateSingular | string/`Pages$ClientTemplate` | Y   | `%d row selected`                         | Singular selection count template                                   |
| object.ct:selectedCountTemplatePlural   | string/`Pages$ClientTemplate` | Y   | `%d rows selected`                        | Plural selection count template                                     |
| object.ct:selectAllText                 | string/`Pages$ClientTemplate` | Y   | `Select all rows in the data source`      | Select all text                                                     |
| object.ct:selectAllTemplate             | string/`Pages$ClientTemplate` | Y   | `Select all %d rows in the data source`   | Select all message template                                         |
| object.ct:allSelectedText               | string/`Pages$ClientTemplate` | Y   | `All %d rows selected.`                   | All selected confirmation text                                      |

### Datagrid Column

Column definition inside a Datagrid.

| Property              | Type                          | Req | Default                      | Notes                                                     |
| --------------------- | ----------------------------- | --- | ---------------------------- | --------------------------------------------------------- |
| $Type                 | string                        | Y   | `CustomWidgets$CustomWidget` | Must be exactly `CustomWidgets$CustomWidget`              |
| showContentAs         | enum                          | Y   | —                            | `attribute`, `customContent`                              |
| attribute             | object                        | Y   | —                            | `DomainModel$AttributeRef` specifying the attribute       |
| content               | array                         | Y   | []                           | Array of widgets when `showContentAs` is `customContent`  |
| ct:exportValue        | string/`Pages$ClientTemplate` | N   | ""                           | Exported value text (only for `customContent`)            |
| exportType            | enum                          | Y   | `default`                    | `default` - export format type                            |
| ct:header             | string/`Pages$ClientTemplate` | Y   | ""                           | Column header label                                       |
| ct:tooltip            | string/`Pages$ClientTemplate` | Y   | ""                           | Header tooltip text                                       |
| filter                | array                         | Y   | []                           | Array containing filter widget (e.g., DatagridTextFilter) |
| visible               | string                        | Y   | `true`                       | `true`, `false` - initial column visibility               |
| sortable              | boolean                       | Y   | true                         | Whether this column can be sorted                         |
| resizable             | boolean                       | Y   | true                         | Whether this column can be resized                        |
| draggable             | boolean                       | Y   | true                         | Whether this column can be reordered                      |
| hidable               | enum                          | Y   | `yes`                        | `yes`, `no` - whether column can be hidden by user        |
| allowEventPropagation | boolean                       | Y   | true                         | Allow click events to bubble up                           |
| width                 | enum                          | Y   | `autoFill`                   | `autoFill`, `autoFit` - width behavior                    |
| minWidth              | enum                          | Y   | `auto`                       | `auto` - minimum width mode                               |
| minWidthLimit         | number                        | Y   | 100                          | Minimum width in pixels                                   |
| size                  | number                        | Y   | 1                            | Relative column size weight                               |
| alignment             | enum                          | Y   | `left`                       | `left`, `right`, `center` - content alignment             |
| wrapText              | boolean                       | Y   | false                        | Whether to wrap text in cells                             |

**IMPORTANT** customContent columns and columns with enumeration attributes do not support sorting. Set `sortable` to `false` for these columns.

### Datagrid example

```json
{
 "$Type": "CustomWidgets$CustomWidget",
 "widgetId": "com.mendix.widget.web.datagrid.Datagrid",
 "object": {
  "datasource": {
   "$Type": "CustomWidgets$CustomWidgetXPathSource",
   "entityRef": {
    "$Type": "DomainModels$DirectEntityRef",
    "entity": "MyFirstModule.Customer"
   },
   "sortBar": {
    "$Type": "Pages$GridSortBar",
    "sortItems": []
   },
   "forceFullObjects": false
  },
  "refreshInterval": 0,
  "columns": [
   {
    "$Type": "CustomWidgets$WidgetObject",
    "showContentAs": "attribute",
    "attribute": {
     "$Type": "DomainModels$AttributeRef",
     "attribute": "MyFirstModule.Customer.FullName"
    },
    "content": [],
    "exportType": "default",
    "ct:header": "Full Name",
    "ct:tooltip": "",
    "filter": [
     {
      "$Type": "CustomWidgets$CustomWidget",
      "widgetId": "com.mendix.widget.web.datagridtextfilter.DatagridTextFilter",
      "object": {
       "attrChoice": "auto",
       "attributes": [],
       "defaultFilter": "contains",
       "ct:placeholder": "",
       "adjustable": true,
       "delay": 500,
       "ct:screenReaderButtonCaption": "",
       "ct:screenReaderInputCaption": "Search"
      },
      "appearance": {
       "$Type": "Pages$Appearance",
       "class": "",
       "style": "",
       "dynamicClasses": "",
       "designProperties": {}
      },
      "name": "textFilter1",
      "tabIndex": 0,
      "editable": "Always"
     }
    ],
    "visible": "true",
    "sortable": true,
    "resizable": true,
    "draggable": true,
    "hidable": "yes",
    "allowEventPropagation": true,
    "width": "autoFill",
    "minWidth": "auto",
    "minWidthLimit": 100,
    "size": 1,
    "alignment": "left",
    "wrapText": false
   },
   {
    "$Type": "CustomWidgets$WidgetObject",
    "showContentAs": "customContent",
    "content": [
     {
      "$Type": "Pages$ActionButton",
      "appearance": {
       "$Type": "Pages$Appearance",
       "class": "",
       "style": "",
       "dynamicClasses": "",
       "designProperties": {}
      },
      "ct:caption": "",
      "t:tooltip": "",
      "icon": {
       "$Type": "Pages$IconCollectionIcon",
       "image": "Atlas_Core.Atlas.pencil"
      },
      "action": {
       "$Type": "Pages$PageClientAction",
       "pageSettings": {
        "$Type": "Pages$PageSettings",
        "parameterMappings": [],
        "page": "MyFirstModule.Customer_NewEdit"
       },
       "pagesForSpecializations": [],
       "disabledDuringExecution": true
      },
      "name": "editBtn",
      "tabIndex": 0,
      "renderType": "Link",
      "buttonStyle": "Default",
      "ariaRole": "Button"
     }
    ],
    "ct:exportValue": "",
    "exportType": "default",
    "ct:header": "Actions",
    "filter": [],
    "visible": "true",
    "sortable": false,
    "resizable": true,
    "draggable": true,
    "hidable": "yes",
    "allowEventPropagation": true,
    "width": "autoFit",
    "minWidth": "auto",
    "minWidthLimit": 100,
    "size": 1,
    "alignment": "left",
    "wrapText": false
   }
  ],
  "columnsFilterable": true,
  "onClickTrigger": "single",
  "filtersPlaceholder": [
   {
    "$Type": "Pages$ActionButton",
    "appearance": {
     "$Type": "Pages$Appearance",
     "class": "",
     "style": "",
     "dynamicClasses": "",
     "designProperties": {}
    },
    "ct:caption": "New Customer",
    "t:tooltip": "",
    "icon": {
     "$Type": "Pages$IconCollectionIcon",
     "image": "Atlas_Core.Atlas.add"
    },
    "action": {
     "$Type": "Pages$CreateObjectClientAction",
     "entityRef": {
      "$Type": "DomainModels$DirectEntityRef",
      "entity": "MyFirstModule.Customer"
     },
     "pageSettings": {
      "$Type": "Pages$PageSettings",
      "parameterMappings": [],
      "page": "MyFirstModule.Customer_NewEdit"
     },
     "disabledDuringExecution": true
    },
    "name": "newBtn",
    "tabIndex": 0,
    "renderType": "Button",
    "buttonStyle": "Success",
    "ariaRole": "Button"
   }
  ],
  "itemSelection": "Single",
  "itemSelectionMethod": "rowClick",
  "autoSelect": false,
  "itemSelectionMode": "clear",
  "showSelectAllToggle": true,
  "enableSelectAll": false,
  "keepSelection": false,
  "selectionCounterPosition": "bottom",
  "loadingType": "spinner",
  "refreshIndicator": false,
  "pageSize": 20,
  "pagination": "buttons",
  "useCustomPagination": false,
  "customPagination": [],
  "showPagingButtons": "always",
  "showNumberOfRows": false,
  "pagingPosition": "bottom",
  "showEmptyPlaceholder": "none",
  "emptyPlaceholder": [],
  "columnsSortable": true,
  "columnsResizable": true,
  "columnsDraggable": true,
  "columnsHidable": true,
  "configurationStorageType": "attribute",
  "storeFiltersInPersonalization": true
 },
 "appearance": {
  "$Type": "Pages$Appearance",
  "class": "",
  "style": "",
  "dynamicClasses": "",
  "designProperties": {}
 },
 "name": "dataGrid1",
 "tabIndex": 0,
 "editable": "Always"
}
```

### DatagridTextFilter

Text-based filter widget for Datagrid columns.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `CustomWidgets$CustomWidget` | Must be exactly `CustomWidgets$CustomWidget` |
| widgetId | string | Y | `com.mendix.widget.web.datagridtextfilter.DatagridTextFilter` | Widget identifier for DatagridTextFilter |
| appearance | object | Y | — | `Pages$Appearance` object |
| name | string | Y | — | Identifier for the widget |
| tabIndex | number | Y | 0 | Tab order |
| editable | enum | Y | `Always` | `Always` - editability setting |
| object | object | Y | — | Configuration object |
| object.attrChoice | enum | Y | `auto` | `auto` - attribute selection mode |
| object.attributes | array | Y | [] | Array of attributes to filter on |
| object.defaultFilter | enum | Y | `contains` | `contains` - default filter operation |
| object.ct:placeholder | string/`Pages$ClientTemplate` | Y | "" | Placeholder text |
| object.adjustable | boolean | Y | true | Whether users can change filter type |
| object.delay | number | Y | 500 | Debounce delay in milliseconds |
| object.ct:screenReaderButtonCaption | string/`Pages$ClientTemplate` | Y | "" | Filter button accessibility label |
| object.ct:screenReaderInputCaption | string/`Pages$ClientTemplate` | Y | `Search` | Input field accessibility label |

### DatagridNumberFilter

Number-based filter widget for Datagrid columns.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `CustomWidgets$CustomWidget` | Must be exactly `CustomWidgets$CustomWidget` |
| widgetId | string | Y | `com.mendix.widget.web.datagridnumberfilter.DatagridNumberFilter` | Widget identifier for DatagridNumberFilter |
| appearance | object | Y | — | `Pages$Appearance` object |
| name | string | Y | — | Identifier for the widget |
| tabIndex | number | Y | 0 | Tab order |
| editable | enum | Y | `Always` | `Always` - editability setting |
| object | object | Y | — | Configuration object |
| object.attrChoice | enum | Y | `auto` | `auto` - attribute selection mode |
| object.attributes | array | Y | [] | Array of attributes to filter on |
| object.defaultFilter | enum | Y | `equal` | `equal` - default filter operation |
| object.ct:placeholder | string/`Pages$ClientTemplate` | Y | "" | Placeholder text |
| object.adjustable | boolean | Y | true | Whether users can change filter type |
| object.delay | number | Y | 500 | Debounce delay in milliseconds |
| object.ct:screenReaderButtonCaption | string/`Pages$ClientTemplate` | Y | "" | Filter button accessibility label |
| object.ct:screenReaderInputCaption | string/`Pages$ClientTemplate` | Y | "" | Input field accessibility label |

### DatagridDropdownFilter

Dropdown-based filter widget for Datagrid columns.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `CustomWidgets$CustomWidget` | Must be exactly `CustomWidgets$CustomWidget` |
| widgetId | string | Y | `com.mendix.widget.web.datagriddropdownfilter.DatagridDropdownFilter` | Widget identifier for DatagridDropdownFilter |
| appearance | object | Y | — | `Pages$Appearance` object |
| name | string | Y | — | Identifier for the widget |
| tabIndex | number | Y | 0 | Tab order |
| editable | enum | Y | `Always` | `Always` - editability setting |
| object | object | Y | — | Configuration object |
| object.attrChoice | enum | Y | `auto` | `auto` - attribute selection mode |
| object.auto | boolean | Y | true | Auto-detect filter options |
| object.filterOptions | array | Y | [] | Manual filter option definitions |
| object.refCaptionSource | enum | Y | `attr` | `attr` - source for reference captions |
| object.fetchOptionsLazy | boolean | Y | false | Load options on demand |
| object.filterable | boolean | Y | false | Allow searching within dropdown |
| object.multiSelect | boolean | Y | false | Allow multiple selections |
| object.ct:emptyOptionCaption | string/`Pages$ClientTemplate` | Y | "" | Empty option text |
| object.clearable | boolean | Y | true | Show clear button |
| object.selectedItemsStyle | enum | Y | `text` | `text` - display style for selected items |
| object.selectionMethod | enum | Y | `checkbox` | `checkbox` - selection UI method |
| object.ct:ariaLabel | string/`Pages$ClientTemplate` | Y | "" | Accessibility label |
| object.ct:emptySelectionCaption | string/`Pages$ClientTemplate` | Y | "" | No selection text |

### DatagridDateFilter

Date-based filter widget for Datagrid columns.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `CustomWidgets$CustomWidget` | Must be exactly `CustomWidgets$CustomWidget` |
| widgetId | string | Y | `com.mendix.widget.web.datagriddatefilter.DatagridDateFilter` | Widget identifier for DatagridDateFilter |
| appearance | object | Y | — | `Pages$Appearance` object |
| name | string | Y | — | Identifier for the widget |
| tabIndex | number | Y | 0 | Tab order |
| editable | enum | Y | `Always` | `Always` - editability setting |
| object | object | Y | — | Configuration object |
| object.defaultFilter | enum | Y | `equal` | `equal` - default filter operation |
| object.adjustable | boolean | Y | true | Whether users can change filter type |

### Combobox

Dropdown selection widget with search and filtering capabilities.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `CustomWidgets$CustomWidget` | Must be exactly `CustomWidgets$CustomWidget` |
| widgetId | string | Y | `com.mendix.widget.web.combobox.Combobox` | Must be exactly `com.mendix.widget.web.combobox.Combobox` |
| appearance | object | Y | — | `Pages$Appearance` object |
| ct:labelTemplate | string/`Pages$ClientTemplate` | Y | — | Main label displayed for the widget |
| name | string | Y | — | Identifier for the widget |
| tabIndex | number | Y | 0 | Tab order |
| editable | enum | Y | `Always` | `Always` - editability setting |
| object | object | Y | — | Configuration object containing all combobox-specific settings |
| object.source | enum | Y | `context` | `context`, `database` - where to load options from |
| object.optionsSourceType | enum | Y | `association` | `association`, `enumeration` - type of options source |
| object.optionsSourceDatabaseItemSelection | enum | Y | `Single` | `Single`, `Multi` - selection mode |
| object.optionsSourceAssociationCaptionType | enum | Y | `attribute` | `attribute` - how to display options from association |
| object.optionsSourceDatabaseCaptionType | enum | Y | `attribute` | `attribute` - how to display options from database |
| object.optionsSourceAssociationCaptionAttribute | object | N | — | `DomainModels$AttributeRef` - attribute to display for association options |
| object.attributeAssociation | object | N | — | `DomainModels$IndirectEntityRef` - Use this when binding to an association. Required when `optionsSourceType` is `association` |
| object.attributeEnumeration | object | N | — | `DomainModels$AttributeRef` - Use this when binding to an enumeration attribute. Required when `optionsSourceType` is `enumeration` |
| object.optionsSourceAssociationDataSource | object | N | — | `CustomWidgets$CustomWidgetXPathSource` - data source for association options |
| object.optionsSourceStaticDataSource | array | Y | [] | Static data source options |
| object.ct:emptyOptionText | string/`Pages$ClientTemplate` | Y | "" | Placeholder text when no option is selected |
| object.ct:noOptionsText | string/`Pages$ClientTemplate` | Y | "" | Text shown when no options are available |
| object.clearable | boolean | Y | true | Whether to show clear button |
| object.optionsSourceAssociationCustomContentType | enum | Y | `no` | `no` - whether to use custom content for association options |
| object.optionsSourceAssociationCustomContent | array | Y | [] | Custom content widgets for association options |
| object.optionsSourceDatabaseCustomContentType | enum | Y | `no` | `no` - whether to use custom content for database options |
| object.optionsSourceDatabaseCustomContent | array | Y | [] | Custom content widgets for database options |
| object.staticDataSourceCustomContentType | enum | Y | `no` | `no` - whether to use custom content for static options |
| object.showFooter | boolean | Y | false | Show footer section in dropdown |
| object.menuFooterContent | array | Y | [] | Widgets to display in footer section |
| object.selectionMethod | enum | Y | `checkbox` | `checkbox`, `rowclick` - how users select options |
| object.selectedItemsStyle | enum | Y | `text` | `text`, `boxes` - display style for selected items |
| object.selectAllButton | boolean | Y | false | Show select all button for multi-select |
| object.customEditability | enum | Y | `default` | `default` - editability mode |
| object.customEditabilityExpression | string | Y | false | Expression for custom editability |
| object.readOnlyStyle | enum | Y | `bordered` | `bordered`, `text` - display style when read-only |
| object.ariaRequired | string | Y | false | Whether field is required for accessibility |
| object.ct:ariaLabel | string/`Pages$ClientTemplate` | Y | `Combo box` | Accessibility label for screen readers |
| object.ct:clearButtonAriaLabel | string/`Pages$ClientTemplate` | Y | `Clear selection` | Accessibility label for clear button |
| object.ct:removeValueAriaLabel | string/`Pages$ClientTemplate` | Y | `Remove value` | Accessibility label for remove value action |
| object.ct:a11ySelectedValue | string/`Pages$ClientTemplate` | Y | `Selected value:` | Accessibility text for selected value announcement |
| object.ct:a11yOptionsAvailable | string/`Pages$ClientTemplate` | Y | `Number of options available:` | Accessibility text for available options announcement |
| object.ct:a11yInstructions | string/`Pages$ClientTemplate` | Y | Use up and down arrow keys to navigate. Press Enter or Space Bar keys to select. | Accessibility instructions for using the widget |
| object.lazyLoading | boolean | Y | true | Load options on demand |
| object.loadingType | enum | Y | `spinner` | `spinner` - loading indicator style |
| object.selectedItemsSorting | enum | Y | `none` | `none` - sorting method for selected items |
| object.filterType | enum | Y | `contains` | `contains` - filter matching method |
| object.filterInputDebounceInterval | number | Y | 200 | Debounce delay in milliseconds for filter input |

### ListView

List widget for displaying collections of data with customizable item templates. Unlike Datagrid, ListView provides flexible layout options and simpler configuration for basic list displays.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$ListView` | Must be exactly `Pages$ListView` |
| name | string | Y | — | Identifier for the widget |
| appearance | object | Y | — | `Pages$Appearance` object |
| conditionalVisibilitySettings | object/null | N | null | Conditional visibility configuration |
| dataSource | object | Y | — | `Pages$ListViewXPathSource` or `Pages$MicroflowSource` - see Data Sources section |
| widgets | array | Y | — | Array of widgets to display for each list item |
| clickAction | object | N | — | Action object executed when list item is clicked - see Actions section |
| pullDownAction | object | N | — | Action object for pull-to-refresh functionality (mobile/touch) - see Actions section |
| templates | array | Y | [] | Array of template configurations (typically empty for standard layouts) |
| tabIndex | number | Y | 0 | Tab order |
| pageSize | number | Y | 10 | Number of items to display per page |
| editable | boolean | Y | false | Whether list items are editable |
| scrollDirection | enum | Y | `Vertical` | `Vertical`, `Horizontal` - scroll direction |
| numberOfColumns | number | Y | 1 | Number of columns for grid layout (1 for simple list) |

### ListViewTemplate

Advanced feature: Custom templates for entity specializations in ListView. Typically empty array `[]` for standard layouts.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$ListViewTemplate` | Must be exactly `Pages$ListViewTemplate` |
| specialization | string | Y | — | Fully-qualified entity name (e.g., `Module.SpecializedEntity`) |
| widgets | array | Y | — | Array of widgets to display for items of this specialization |

## WidgetValidation

Validation configuration for form widgets.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$WidgetValidation` | Must be exactly `Pages$WidgetValidation` |
| t:message | string/`Texts$Text` | Y | — | Validation message displayed to users |
| expression | string | Y | — | Validation expression (e.g. `$value != empty`). See `mendix-microflow-syntax` skill for syntax |

## FormattingInfo

Formatting configuration for input widgets.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$FormattingInfo` | Must be exactly `Pages$FormattingInfo` |
| decimalPrecision | number | N | — | Number of decimal places for numeric values |
| groupDigits | boolean | N | — | Whether to use thousand separators for numeric values |
| enumFormat | enum | N | `Text` | `Text` - display format for enumeration values |
| dateFormat | enum | N | `Date` | `Date`, `Time`, `DateTime` |

## Data Sources

### DataViewSource

Data source for `DataView` widgets that retrieves a single object from a page parameter or variable.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$DataViewSource` | Must be exactly `Pages$DataViewSource` |
| entityRef | object | Y | — | `DomainModels$DirectEntityRef` or `DomainModels$IndirectEntityRef` |
| sourceVariable | object | Y | — | `Pages$PageVariable` |
| forceFullObjects | boolean | N | — | Whether to force retrieval of full objects |

### CustomWidgetXPathSource

Used by Datagrid and other list widgets to retrieve data using XPath queries.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `CustomWidgets$CustomWidgetXPathSource` | Must be exactly `CustomWidgets$CustomWidgetXPathSource` |
| entityRef | object | Y | — | `DomainModels$DirectEntityRef` or `DomainModels$IndirectEntityRef` specifying the entity to retrieve |
| sortBar | object | Y | — | `Pages$GridSortBar` object for sort configuration |
| forceFullObjects | boolean | Y | — | Whether to force retrieval of full objects |
| xPathConstraint | string/null | Y | — | XPath expression to filter data (empty string for no filter) |

### ListViewXPathSource

Data source used by `ListView` widgets to retrieve data using XPath queries, with built-in support for sorting and searching.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$ListViewXPathSource` | Must be exactly `Pages$ListViewXPathSource` |
| entityRef | object | Y | — | `DomainModels$DirectEntityRef` or `DomainModels$IndirectEntityRef` specifying the entity to retrieve |
| sourceVariable | object/null | N | — | `Pages$PageVariable` object if data source is based on a page parameter or variable (null otherwise) |
| sortBar | object | Y | — | `Pages$GridSortBar` object for sort configuration |
| search | object | Y | — | `Pages$ListViewSearch` object for search configuration |
| forceFullObjects | boolean | Y | — | Whether to force retrieval of full objects |
| xPathConstraint | string | Y | — | XPath expression to filter data (empty string for no filter) |

### MicroflowSource

Data source that retrieves data by calling a microflow. Used in DataView and other data-consuming widgets.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$MicroflowSource` | Must be exactly `Pages$MicroflowSource` |
| microflowSettings | object | Y | — | `Pages$MicroflowSettings` object defining the microflow to call |
| forceFullObjects | boolean | Y | — | Whether to force retrieval of full objects |

### ListenTargetSource

Data source that listens to row selection in another widget (typically a Datagrid). This is the standard Mendix pattern for creating master-detail views where selecting a row in one grid updates content in another area.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$ListenTargetSource` | Must be exactly `Pages$ListenTargetSource` |
| listenTarget | string | Y | — | Name of the widget to listen to (e.g., the name of the master Datagrid). **IMPORTANT:** Always check for the correct widget name in your page structure. |
| forceFullObjects | boolean | Y | — | Whether to force retrieval of full objects |

## Actions

### NoClientAction

No action is performed.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$NoClientAction` | Must be exactly `Pages$NoClientAction` |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |

### MicroflowClientAction

Calls a microflow.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$MicroflowClientAction` | Must be exactly `Pages$MicroflowClientAction` |
| microflowSettings | object | Y | — | `Pages$MicroflowSettings` object |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |

### MicroflowSettings

Configuration for calling a microflow from a page action.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$MicroflowSettings` | Must be exactly `Pages$MicroflowSettings` |
| microflow | string | Y | — | Fully-qualified microflow name |
| parameterMappings | array | Y | — | Array of `Pages$MicroflowParameterMapping` |
| progressBar | enum | Y | — | `None`, `NonBlocking`, `Blocking` |
| t:progressMessage | string/`Texts$Text` | N | — | Progress message |
| asynchronous | boolean | Y | — | Whether to run asynchronously |
| formValidations | enum | Y | — | `None`, `Widget`, `All` |
| confirmationInfo | object | N | — | `Pages$ConfirmationInfo` object (null if none) |
| outputMappings | array | Y | — | Array of `Pages$OutputMapping` |

### MicroflowParameterMapping

Maps a value to a microflow parameter. **CRITICAL:** The `parameter` property MUST use the fully-qualified parameter name from the target microflow, NOT the simple parameter name.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$MicroflowParameterMapping` | Must be exactly `Pages$MicroflowParameterMapping` |
| parameter | string | Y | — | Fully-qualified parameter name (e.g. `Module.MicroflowName.ParameterName`). To find this, read the microflow's parameter objects for `$QualifiedName` |
| expression | string | N | — | Expression (null if using variable) |
| variable | object | N | — | `Pages$PageVariable` object (null if using expression) |

### SignOutClientAction

Signs out the current user.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$SignOutClientAction` | Must be exactly `Pages$SignOutClientAction` |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |

### ClosePageClientAction

Closes the current page or a specified number of pages.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$ClosePageClientAction` | Must be exactly `Pages$ClosePageClientAction` |
| numberOfPagesToClose | string | N | — | Number of pages to close (null for current page) |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |

### OpenLinkClientAction

Opens a URL or triggers an email/phone/text action.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$OpenLinkClientAction` | Must be exactly `Pages$OpenLinkClientAction` |
| linkType | enum | Y | — | `Web`, `Email`, `Call`, `Text` |
| address | object | Y | — | `Pages$StaticOrDynamicString` object |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |

### StaticOrDynamicString

Configuration for a string value that can be either static text or dynamically derived from an attribute.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$StaticOrDynamicString` | Must be exactly `Pages$StaticOrDynamicString` |
| isDynamic | boolean | Y | — | Whether the value is dynamic (attribute-based) |
| value | string | Y | — | Static value or expression |
| attributeRef | object | N | — | `DomainModels$AttributeRef` (null if static) |

### SaveChangesClientAction

Saves changes to the current object.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$SaveChangesClientAction` | Must be exactly `Pages$SaveChangesClientAction` |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |
| syncAutomatically | boolean | Y | — | Whether to sync automatically |
| closePage | boolean | Y | — | Whether to close the page after saving |

### CancelChangesClientAction

Cancels changes to the current object.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$CancelChangesClientAction` | Must be exactly `Pages$CancelChangesClientAction` |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |
| closePage | boolean | Y | — | Whether to close the page after canceling |

### PageClientAction

Opens a page.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$PageClientAction` | Must be exactly `Pages$PageClientAction` |
| pageSettings | object | Y | — | `Pages$PageSettings` object |
| pagesForSpecializations | array | Y | — | Array of specialized page settings |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |

### DeleteClientAction

Deletes the current object.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$DeleteClientAction` | Must be exactly `Pages$DeleteClientAction` |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |
| closePage | boolean | Y | — | Whether to close the page after deleting |

### CreateObjectClientAction

Creates a new object and opens a page.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$CreateObjectClientAction` | Must be exactly `Pages$CreateObjectClientAction` |
| entityRef | object | Y | — | `DomainModels$DirectEntityRef` or `DomainModels$IndirectEntityRef` object |
| pageSettings | object | Y | — | `Pages$PageSettings` object |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |

### CallWorkflowClientAction

Triggers a workflow.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$CallWorkflowClientAction` | Must be exactly `Pages$CallWorkflowClientAction` |
| workflow | string | Y | — | Fully-qualified workflow name (e.g. `Module.WorkflowName`) |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |
| closePage | boolean | Y | — | Whether to close the page after workflow is called |

### OpenUserTaskClientAction

Opens a user task from a workflow.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$OpenUserTaskClientAction` | Must be exactly `Pages$OpenUserTaskClientAction` |
| assignOnOpen | boolean | Y | — | Whether to assign the task on open |
| openWhenAssigned | boolean | Y | — | Whether to open when already assigned |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |

### OpenWorkflowClientAction

Opens a workflow instance page.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$OpenWorkflowClientAction` | Must be exactly `Pages$OpenWorkflowClientAction` |
| defaultPage | string | N | — | Fully-qualified page name (null for default) |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |

### SetTaskOutcomeClientAction

Sets the outcome of a user task.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$SetTaskOutcomeClientAction` | Must be exactly `Pages$SetTaskOutcomeClientAction` |
| outcomeValue | string | Y | — | The outcome value to set |
| closePage | boolean | Y | — | Whether to close the page after setting |
| commit | boolean | Y | — | Whether to commit the changes |
| disabledDuringExecution | boolean | Y | — | Whether to disable button during execution |

## OutputMapping

Maps output values from a microflow back to page variables or attributes.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$OutputMapping` | Must be exactly `Pages$OutputMapping` |
| sourceVariable | object | N | — | `Pages$PageVariable` object (null if none) |
| expression | string | N | — | Expression (null if none) |
| attributeRef | object | N | — | `DomainModels$AttributeRef` object (null if none) |
| sourceAttributeRef | object | N | — | `DomainModels$AttributeRef` object (null if none) |

## ConfirmationInfo

Configuration for a confirmation dialog shown before executing an action.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$ConfirmationInfo` | Must be exactly `Pages$ConfirmationInfo` |
| t:question | string/`Texts$Text` | Y | — | Question |
| t:proceedButtonCaption | string/`Texts$Text` | Y | — | Proceed button caption |
| t:cancelButtonCaption | string/`Texts$Text` | Y | — | Cancel button caption |

## PageSettings

Configuration for opening a page, including which page to open and how to map parameters.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$PageSettings` | Must be exactly `Pages$PageSettings` |
| page | string | N | — | Fully-qualified name of the page (e.g. `Module.PageName`) |
| parameterMappings | array | Y | — | Array of `Pages$PageParameterMapping` objects |

## Icons

### IconCollectionIcon

Icon reference from an icon collection.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$IconCollectionIcon` | Must be exactly `Pages$IconCollectionIcon` |
| image | string | Y | — | Icon reference (e.g. `Atlas_Core.Atlas.pencil`) |

## References

### AttributeRef

References an entity attribute. Used by input widgets to bind to data.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `DomainModels$AttributeRef` | Must be exactly `DomainModels$AttributeRef` |
| attribute | string | Y | — | Fully-qualified name of the attribute (e.g. `Module.Entity.Attribute`) |

### DirectEntityRef

References an entity directly by its fully-qualified name.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `DomainModels$DirectEntityRef` | Must be `DomainModels$DirectEntityRef` |
| entity | string | Y | — | Fully-qualified entity name (e.g. `Sales.Customer`) |

### IndirectEntityRef

References an entity through an association path. Used by reference selectors like Combobox.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `DomainModels$IndirectEntityRef` | Must be exactly `DomainModels$IndirectEntityRef` |
| steps | array | Y | — | Array of `DomainModels$EntityRefStep` |

### FormattingInfo (in References)

Defines formatting options for displaying data in widgets. (Note: this entry sits inside the `## References` section. The top-level `## FormattingInfo` earlier in this skill describes the same shape with additional optional fields.)

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$FormattingInfo` | Must be exactly `Pages$FormattingInfo` |
| decimalPrecision | number | Y | 2 | Number of decimal places for numeric values |
| groupDigits | boolean | Y | false | Whether to use thousand separators for numeric values |
| enumFormat | enum | Y | `Text` | `Text` - display format for enumeration values |
| dateFormat | enum | Y | `Date` | `Date`, `Time`, `DateTime` |
| customDateFormat | string | Y | "" | Custom date format string (empty for default) |

## Ambiguity

Ask for clarification on entity structure, attribute names, or layout preferences. Default to `labelWidth: 3` for forms. Explain any defaults you apply.

## Appearance

The appearance object is used by many widgets to apply styling and design properties.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$Appearance` | Must be exactly `Pages$Appearance` |
| class | string | Y | — | Space-separated CSS class names |
| style | string | Y | — | Inline CSS styles |
| dynamicClasses | string | Y | — | Expression for dynamic class names |
| designProperties | object | Y | — | Key-value pairs for design property overrides (e.g. `{"option:Size": "Large"}`, `{"toggle:Cards style": true}`) |

## Content Widget

Root widget for page content.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$Content` | Must be exactly `Pages$Content` |
| slot | string | Y | `Main` | Slot identifier |
| widgets | array | Y | — | Array of widgets |

## GridSortBar

Configuration for sorting behavior in data widgets (Datagrid, ListView).

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$GridSortBar` | Must be exactly `Pages$GridSortBar` |
| sortItems | array | Y | — | Array of `Pages$GridSortItem` objects |

### GridSortItem

Individual sort configuration within a `GridSortBar`. Defines which attribute to sort by and in what direction.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$GridSortItem` | Must be exactly `Pages$GridSortItem` |
| attributeRef | object | Y | — | `DomainModels$AttributeRef` - the attribute to sort by |
| sortDirection | enum | Y | — | `Ascending`, `Descending` - the sort order |

## ListViewSearch

Search configuration for ListView widgets. Defines which attributes are searchable from the ListView search bar.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$ListViewSearch` | Must be exactly `Pages$ListViewSearch` |
| searchRefs | array | Y | — | Array of `DomainModels$AttributeRef` objects defining searchable attributes |

## PageVariable

References a variable source within a page context (page parameters, local variables).

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$PageVariable` | Must be exactly `Pages$PageVariable` |
| widget | string | N | — | Widget name to reference (null if not using widget) |
| pageParameter | string | N | — | Page parameter name to reference (null if not using parameter) |
| snippetParameter | string | N | — | Snippet parameter name (null if not using snippet parameter) |
| localVariable | string | N | — | Local variable name (null if not using local variable) |
| useAllPages | boolean | Y | — | Whether to search across all pages in the navigation stack |
| subKey | string | Y | — | Sub-key for the variable (empty string if none) |

## PageParameterMapping

Maps values to page parameters when opening a page. Used in `Pages$PageSettings` to pass data to the target page.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$PageParameterMapping` | Must be exactly `Pages$PageParameterMapping` |
| parameter | string | Y | — | Fully-qualified parameter name of the target page parameter (e.g., `Module.PageName.ParameterName`) |
| expression | string | N | — | Expression to evaluate for the parameter value (null if using variable or widget) |
| variable | object | N | — | `Pages$PageVariable` object to use as the parameter value (null if using expression or widget) |
| widget | string | N | — | Name of a widget to use as the parameter source (null if using expression or variable). Typically used for grid selection context. |

# ClientTemplate

Templated string supporting dynamic text with parameter interpolation.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$ClientTemplate` | Must be exactly `Pages$ClientTemplate` |
| t:template | object | Y | — | `Texts$Text` object |
| parameters | array | Y | — | Array of `Pages$ClientTemplateParameter` |
| t:fallback | object | Y | — | `Texts$Text` object for fallback |

# ClientTemplateParameter

Parameter definition for `ClientTemplate` with optional formatting.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Pages$ClientTemplateParameter` | Must be exactly `Pages$ClientTemplateParameter` |
| attributeRef | object | N | — | `DomainModels$AttributeRef` (null if using variable) |
| sourceVariable | object | N | — | `Pages$PageVariable` (null if using attribute) |
| formattingInfo | object | Y | — | `Pages$FormattingInfo` |

## Text

Multi-language text container.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Texts$Text` | Must be exactly `Texts$Text` |
| translations | array | Y | — | Array of `Texts$Translation` |

## Translation

Individual language translation within a `Text` object.

| Property | Type | Req | Default | Notes |
| -------- | ---- | --- | ------- | ----- |
| $Type | string | Y | `Texts$Translation` | Must be exactly `Texts$Translation` |
| languageCode | string | Y | — | e.g., `en_US`, `nl_NL` |
| text | string | Y | — | Translated text |

## Shorthand Notation

Properties prefixed with `ct:` or `t:` accept either a plain string or full object:

- `ct:` — accepts string or `Pages$ClientTemplate`
- `t:` — accepts string or `Texts$Text`

## Common Patterns

### List Page Pattern

- Use LayoutGrid with single column (weight: 12)
- Create two LayoutGridRows:
    1. **Title row:**
        - Contains `Pages$Title` widget for page title
        - Column weight: -1 (auto)
        - Set `spacingBetweenColumns: true`
    2. **Content row:**
        - Contains the Datagrid
        - Column weight: 12 (full width)
        - **Layout styling:** Add `"toggle:Cards style": true` to the LayoutGridRow's `designProperties` for card appearance
        - Set `spacingBetweenColumns: true`
- Add Datagrid with:
    - Columns for each displayable attribute
    - Appropriate filter widgets based on attribute type
    - Action column with Edit/Delete link buttons (customContent)
        - Set `sortable: false` for customContent columns
        - Use `width: "autoFit"` for action columns
        - Edit button: icon `Atlas_Core.Atlas.pencil`, renderType `Link`
        - Delete button: icon `Atlas_Core.Atlas.trash-can`, renderType `Link`
    - "New" button in filtersPlaceholder:
        - Icon: `Atlas_Core.Atlas.add`
        - buttonStyle: `Success`
        - renderType: `Button`
- Configure itemSelection: `"Single"`, itemSelectionMethod: `"rowClick"`

**IMPORTANT:**

- Enumeration columns cannot be sorted - always set `sortable: false` for enum attributes
- CustomContent columns cannot be sorted - always set `sortable: false`
- Use `"toggle:Cards style": true` in the content row's designProperties for modern card styling

### Form Page Pattern

- Use PopupLayout for modal forms
- Declare PageParameter for the entity
- Wrap in LayoutGrid > DataView
- **Spacing:** Set `spacingBetweenColumns: true` on LayoutGridRow
- DataView dataSource uses PageVariable pointing to parameter
- Add appropriate input widgets based on attribute type
- Add Save/Cancel buttons in footerWidgets
- Set labelWidth: 3 for horizontal labels

**IMPORTANT** ALWAYS wrap the form (`Pages$DataView`) inside a LayoutGrid but do not add another LayoutGrid inside the DataView.
**IMPORTANT** Do not use LayoutGrid inside a DataView. DataView widget handles the layout for you.

### Master-Detail Pattern (Split-Pane)

Create a split-pane view where selecting a row in one list updates a detail view:

- Use LayoutGrid with two columns (weights: 5 and 7, or 6 and 6)
- **Left column (Master):**
    - Datagrid with `itemSelection: "Single"` and `itemSelectionMethod: "rowClick"`
- **Right column (Detail):**
    - Wrap content in a DataView with `ListenTargetSource`
    - Set `listenTarget` to the master grid's name (**Important** make sure to use the correct widget name of the master grid)
    - Add detail content:
        - Form fields bound to the selected object
        - Related data grid using `IndirectEntityRef` with association steps
        - Dynamic text showing selected object attributes

**CRITICAL:** The listenTarget value must exactly match the master grid's name. Always double-check for widget to exist.
