namespace Terminal.Interop;

// ─── Access / visibility enums ───────────────────────────────────────────────

/// <summary>
/// Mirrors the Mendix microflow AllowedRoles semantics at the Interop boundary.
/// CheckPerOperation = only roles explicitly listed may call the microflow.
/// AllowAll           = all module roles may call it.
/// ModuleSpecific     = module-level allowed-roles list is used.
/// </summary>
public enum MicroflowAccessLevel { CheckPerOperation, AllowAll, ModuleSpecific }

// ─── Parameter / return-type descriptors ─────────────────────────────────────

/// <summary>
/// A single parameter of a microflow or nanoflow.
/// TypeQualifiedName is the Mendix data-type string (e.g. "MyModule.Customer",
/// "String", "Integer", "Boolean"). IsList reflects the IsList flag on the type.
/// </summary>
public record MicroflowParameter(
    string Name,
    string TypeQualifiedName,
    bool IsList,
    string? Documentation);

// ─── Microflow summary (read path) ───────────────────────────────────────────

/// <summary>
/// Lightweight summary of a microflow returned by ListMicroflows / ReadMicroflow.
/// Enough for tool output and decision-making; heavy activity detail goes through
/// ReadActivities.
/// </summary>
public record MicroflowSummary(
    string QualifiedName,
    string Module,
    string Name,
    string? Documentation,
    MicroflowAccessLevel AccessLevel,
    IReadOnlyList<MicroflowParameter> Parameters,
    string? ReturnTypeQualifiedName,
    bool ReturnIsList,
    int ActivityCount);

// ─── Activity descriptor ──────────────────────────────────────────────────────

/// <summary>
/// Describes one activity in a microflow's execution graph.
/// ActivityType is a normalised lower-kebab string matching the SPMCP activity-type
/// vocabulary (e.g. "create_object", "retrieve", "microflow_call", "java_action_call").
/// Parameters carries the activity-specific key/value configuration (entity name,
/// expression strings, output variable name, etc.) as plain strings — the host
/// compiles expressions via IMicroflowExpressionService before writing to the model.
/// Position is 1-based, matching SPMCP's insert-position convention.
/// </summary>
public record MicroflowActivitySummary(
    int Position,
    string ActivityType,
    string? Caption,
    string? OutputVariable,
    string? TargetEntity,
    string? TargetMicroflow,
    string? TargetJavaAction,
    IReadOnlyDictionary<string, string> Parameters);

// ─── Create request ───────────────────────────────────────────────────────────

/// <summary>
/// Everything needed to create a new microflow via IMicroflowService.CreateMicroflow.
/// The host resolves module name → IModule and constructs the MicroflowReturnValue
/// (including a default IMicroflowExpression for the return value); Core supplies
/// only plain strings.
/// </summary>
public record CreateMicroflowRequest(
    string ModuleName,
    string Name,
    IReadOnlyList<MicroflowParameter> Parameters,
    string? ReturnTypeQualifiedName,
    bool ReturnIsList,
    MicroflowAccessLevel AccessLevel,
    string? Documentation,
    string? FolderPath);

// ─── Activity insertion request ───────────────────────────────────────────────

/// <summary>
/// Request to insert a new activity into an existing microflow.
/// InsertPosition follows the SPMCP convention:
///   null / 1 = after start (prepend)
///   N         = insert before the (N-1)th existing action activity
/// The host calls TryInsertAfterStart or TryInsertBeforeActivity as appropriate.
/// </summary>
public record ActivityInsertion(
    string MicroflowQualifiedName,
    int? InsertPosition,
    MicroflowActivitySummary Activity);

// ─── Variable name check ─────────────────────────────────────────────────────

/// <summary>
/// Result of a variable-name uniqueness check within a microflow's scope.
/// SuggestedAlternative is non-null when InUse is true and an alternative was found.
/// </summary>
public record VariableNameCheckResult(
    bool InUse,
    string? SuggestedAlternative,
    IReadOnlyList<string> ExistingVariables);

// ─── Nanoflow summary (read-only) ────────────────────────────────────────────

/// <summary>
/// Lightweight summary of a nanoflow. Nanoflows are read-only at the Interop
/// boundary (no typed creation API is exposed by IMicroflowService for nanoflows).
/// Data is sourced from IUntypedModelAccessService.
/// </summary>
public record NanoflowSummary(
    string QualifiedName,
    string Module,
    string Name,
    string? Documentation,
    IReadOnlyList<MicroflowParameter> Parameters,
    string? ReturnTypeQualifiedName,
    int ActivityCount,
    int AllowedRoleCount);

// ─── Java action descriptor (read-only) ──────────────────────────────────────

/// <summary>
/// Lightweight descriptor for a Java action document.
/// Absorbs Model.JavaActions.IJavaAction at the Interop boundary; the host returns
/// DocumentId so callers can pass it back when creating a java_action_call activity.
/// ParameterNames lists the action parameters for tool output.
/// </summary>
public record JavaActionDescriptor(
    DocumentId Document,
    string Module,
    IReadOnlyList<string> ParameterNames);

// ─── The interface ────────────────────────────────────────────────────────────

/// <summary>
/// Wraps Mendix IMicroflowService + nanoflow introspection (IUntypedModelAccessService)
/// + IJavaAction listing for SPMCP's microflow-authoring tool family.
///
/// Expression parameters (return-value expressions, retrieve constraints, etc.) are
/// passed as plain strings; the host compiles them via IMicroflowExpressionService
/// (absorbing MicroflowExpressions.IMicroflowExpression at the boundary per the
/// Task 4 inventory finding).
///
/// Java action activities are created by the host when ActivityType is
/// "java_action_call" and Parameters["java_action"] names the qualified action.
///
/// IsAvailable reflects whether IMicroflowService resolved from the DI container.
/// On any Studio Pro version that ships IMicroflowService this will be true; on
/// older host builds where the service is absent it will be false.
/// </summary>
public interface IMicroflowAuthoringHost
{
    bool IsAvailable { get; }

    // ── Microflows (server-side, full read+write) ─────────────────────────────

    /// <summary>
    /// List summaries of all microflows, optionally filtered to a single module.
    /// </summary>
    IReadOnlyList<MicroflowSummary> ListMicroflows(ModuleId? moduleFilter);

    /// <summary>
    /// Read the full summary for a single microflow by qualified name.
    /// Returns null if not found.
    /// </summary>
    MicroflowSummary? ReadMicroflow(string qualifiedName);

    /// <summary>
    /// Return the ordered activity list for the given microflow.
    /// Positions are 1-based; non-action activities (start/end events) are omitted.
    /// </summary>
    IReadOnlyList<MicroflowActivitySummary> ReadActivities(string microflowQualifiedName);

    /// <summary>
    /// Create a new microflow and return its DocumentId.
    /// The host wraps the call in a model transaction.
    /// </summary>
    DocumentId Create(CreateMicroflowRequest request);

    /// <summary>
    /// Delete the microflow with the given qualified name.
    /// No-op (returns false) if not found.
    /// </summary>
    bool Delete(string microflowQualifiedName);

    /// <summary>
    /// Insert a new activity into an existing microflow.
    /// Returns the 1-based position at which the activity was actually inserted
    /// (may differ from requested position due to API limitations).
    /// The host wraps the call in a model transaction.
    /// </summary>
    int AddActivity(ActivityInsertion insertion);

    /// <summary>
    /// Insert a new activity immediately before the activity at the given 1-based
    /// position. Corresponds to IMicroflowService.TryInsertBeforeActivity.
    /// Returns the position of the inserted activity.
    /// </summary>
    int InsertBeforeActivity(string microflowQualifiedName, int beforePosition, MicroflowActivitySummary activity);

    /// <summary>
    /// Apply a set of key/value changes to an existing activity.
    /// ActivityPosition is 1-based (matches ReadActivities output).
    /// The host re-creates or patches the activity as needed and commits in a transaction.
    /// </summary>
    void ModifyActivity(string microflowQualifiedName, int activityPosition, IReadOnlyDictionary<string, string> changes);

    /// <summary>
    /// Delete the activity at the given 1-based position from the microflow.
    /// </summary>
    void DeleteActivity(string microflowQualifiedName, int activityPosition);

    /// <summary>
    /// Set (or clear) the HTTP URL exposed by the microflow (applies only to
    /// microflows that are exposed as REST endpoints).
    /// </summary>
    void SetUrl(string microflowQualifiedName, string? url);

    /// <summary>
    /// Set the access level of the microflow (CheckPerOperation / AllowAll / ModuleSpecific).
    /// </summary>
    void SetAccessLevel(string microflowQualifiedName, MicroflowAccessLevel level);

    /// <summary>
    /// Check whether a variable name is already in scope within the given microflow.
    /// Corresponds to IMicroflowService.IsVariableNameInUse + parameter enumeration.
    /// </summary>
    VariableNameCheckResult CheckVariableName(string microflowQualifiedName, string variableName);

    // ── Nanoflows (client-side, read-only) ───────────────────────────────────

    /// <summary>
    /// List summaries of all nanoflows, optionally filtered to a single module.
    /// Data is sourced from IUntypedModelAccessService.
    /// </summary>
    IReadOnlyList<NanoflowSummary> ListNanoflows(ModuleId? moduleFilter);

    /// <summary>
    /// Read the summary for a single nanoflow by qualified name.
    /// Returns null if not found.
    /// </summary>
    NanoflowSummary? ReadNanoflow(string qualifiedName);

    // ── Java actions (read-only — referenced by microflow activities) ─────────

    /// <summary>
    /// List Java action documents, optionally filtered to a single module.
    /// Returns DocumentId so callers can reference them in ActivityInsertion.
    /// Corresponds to IModel.Root.GetModuleDocuments&lt;IJavaAction&gt;.
    /// </summary>
    IReadOnlyList<JavaActionDescriptor> ListJavaActions(ModuleId? moduleFilter);
}
