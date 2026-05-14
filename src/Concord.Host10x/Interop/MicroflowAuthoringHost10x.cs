namespace Concord.Host10x.Interop;

using System.Security.Cryptography;
using System.Text;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.DataTypes;
using Mendix.StudioPro.ExtensionsAPI.Model.JavaActions;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows;
using Mendix.StudioPro.ExtensionsAPI.Model.Microflows.Actions;
using Mendix.StudioPro.ExtensionsAPI.Model.Projects;
using Mendix.StudioPro.ExtensionsAPI.Model.UntypedModel;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Terminal.Interop;

/// <summary>
/// Implements IMicroflowAuthoringHost against the 10.21.1 ExtensionsAPI surface.
/// Bodies ported from MendixAdditionalTools.ListMicroflows / ReadMicroflowDetails /
/// CreateMicroflowWithService / CreateMicroflowActivity / ModifyMicroflowActivity /
/// InsertBeforeActivity / SetMicroflowUrl / CheckVariableName / ListJavaActions,
/// and from ListNanoflows / ReadNanoflowDetails (via IUntypedModelAccessService).
/// JSON parsing stripped; Mendix modeling work retained.
/// </summary>
public sealed class MicroflowAuthoringHost10x : IMicroflowAuthoringHost
{
    private readonly IModel _model;
    private readonly IMicroflowService _microflowService;
    private readonly IMicroflowExpressionService? _exprService;
    private readonly IUntypedModelAccessService? _untyped;

    public MicroflowAuthoringHost10x(
        IModel model,
        IMicroflowService microflowService,
        IMicroflowExpressionService? exprService = null,
        IUntypedModelAccessService? untyped = null)
    {
        _model = model;
        _microflowService = microflowService;
        _exprService = exprService;
        _untyped = untyped;
    }

    // ── IMicroflowAuthoringHost ──────────────────────────────────────────────

    public bool IsAvailable => true; // IMicroflowService is required-inject; always available.

    // ── Microflows: read path ────────────────────────────────────────────────

    public IReadOnlyList<MicroflowSummary> ListMicroflows(ModuleId? moduleFilter)
    {
        var modules = GetModules(moduleFilter);
        var result = new List<MicroflowSummary>();
        foreach (var module in modules)
        {
            var microflows = CollectMicroflowsInFolder(module);
            foreach (var mf in microflows)
                result.Add(BuildSummary(mf, module.Name));
        }
        return result;
    }

    public MicroflowSummary? ReadMicroflow(string qualifiedName)
    {
        var (mf, module) = FindMicroflowByQualifiedName(qualifiedName);
        return mf is null ? null : BuildSummary(mf, module!.Name);
    }

    public IReadOnlyList<MicroflowActivitySummary> ReadActivities(string microflowQualifiedName)
    {
        var (mf, _) = FindMicroflowByQualifiedName(microflowQualifiedName);
        if (mf is null)
            throw new InvalidOperationException($"Microflow '{microflowQualifiedName}' not found.");

        var activities = _microflowService.GetAllMicroflowActivities(mf);
        var result = new List<MicroflowActivitySummary>();
        int pos = 0;
        foreach (var act in activities)
        {
            if (act is IActionActivity action)
            {
                pos++;
                result.Add(BuildActivitySummary(action, pos));
            }
        }
        return result;
    }

    // ── Microflows: write path ────────────────────────────────────────────────

    public DocumentId Create(CreateMicroflowRequest request)
    {
        var module = ResolveModuleByName(request.ModuleName);

        // Check for duplicate
        var existing = CollectMicroflowsInFolder(module)
            .FirstOrDefault(mf => string.Equals(mf.Name, request.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            throw new InvalidOperationException(
                $"Microflow '{request.Name}' already exists in module '{module.Name}'.");

        // Resolve folder
        IFolderBase folderBase = module;
        if (!string.IsNullOrEmpty(request.FolderPath))
        {
            var folder = FindFolderByPath(module, request.FolderPath);
            if (folder is not null)
                folderBase = folder;
        }

        // Build parameter list
        var paramList = BuildParamList(request.Parameters);

        // Build return value
        MicroflowReturnValue? returnValue = null;
        var returnDataType = ParseDataType(request.ReturnTypeQualifiedName, request.ReturnIsList);
        if (returnDataType != DataType.Void)
            returnValue = BuildReturnValue(returnDataType);

        using var tx = _model.StartTransaction($"Create microflow '{request.Name}'");
        var mf = _microflowService.CreateMicroflow(_model, folderBase, request.Name, returnValue, paramList.ToArray());
        if (mf is null)
            throw new InvalidOperationException($"IMicroflowService.CreateMicroflow returned null for '{request.Name}'.");

        // Note: IMicroflow.Documentation is not exposed in the 10.21.1 ExtensionsAPI surface.
        // Documentation is intentionally skipped.

        tx.Commit();

        return new DocumentId(ParseId(mf.Id), mf.QualifiedName?.FullName ?? $"{module.Name}.{mf.Name}");
    }

    public bool Delete(string microflowQualifiedName)
    {
        var (mf, module) = FindMicroflowByQualifiedName(microflowQualifiedName);
        if (mf is null) return false;

        IFolderBase? parent = FindParentFolder(module!, mf);
        if (parent is null) return false;

        using var tx = _model.StartTransaction($"Delete microflow '{mf.Name}'");
        parent.RemoveDocument(mf);
        tx.Commit();
        return true;
    }

    public int AddActivity(ActivityInsertion insertion)
    {
        var (mf, _) = FindMicroflowByQualifiedName(insertion.MicroflowQualifiedName);
        if (mf is null)
            throw new InvalidOperationException($"Microflow '{insertion.MicroflowQualifiedName}' not found.");

        using var tx = _model.StartTransaction($"Add activity to '{insertion.MicroflowQualifiedName}'");
        var activity = CreateActivity(insertion.Activity)
            ?? throw new InvalidOperationException($"Failed to create activity of type '{insertion.Activity.ActivityType}'.");

        bool inserted;
        int resultPosition;

        var pos = insertion.InsertPosition;
        if (pos.HasValue && pos.Value > 1)
        {
            var actionActivities = GetActionActivities(mf);
            int beforeIdx = pos.Value - 1; // position is 1-based; insert BEFORE that index
            if (beforeIdx > 0 && beforeIdx <= actionActivities.Count)
            {
                inserted = _microflowService.TryInsertBeforeActivity(actionActivities[beforeIdx - 1], activity);
                resultPosition = beforeIdx;
            }
            else
            {
                inserted = _microflowService.TryInsertAfterStart(mf, activity);
                resultPosition = 1;
            }
        }
        else
        {
            // null or 1 → prepend (after start)
            inserted = _microflowService.TryInsertAfterStart(mf, activity);
            resultPosition = 1;
        }

        if (!inserted)
            throw new InvalidOperationException("IMicroflowService rejected the activity insertion.");

        tx.Commit();
        return resultPosition;
    }

    public int InsertBeforeActivity(string microflowQualifiedName, int beforePosition, MicroflowActivitySummary activity)
    {
        var (mf, _) = FindMicroflowByQualifiedName(microflowQualifiedName);
        if (mf is null)
            throw new InvalidOperationException($"Microflow '{microflowQualifiedName}' not found.");

        var actionActivities = GetActionActivities(mf);
        if (beforePosition < 1 || beforePosition > actionActivities.Count)
            throw new InvalidOperationException(
                $"beforePosition {beforePosition} out of range (1-{actionActivities.Count}).");

        using var tx = _model.StartTransaction($"Insert activity before position {beforePosition}");
        var newActivity = CreateActivity(activity)
            ?? throw new InvalidOperationException($"Failed to create activity of type '{activity.ActivityType}'.");

        var inserted = _microflowService.TryInsertBeforeActivity(actionActivities[beforePosition - 1], newActivity);
        if (!inserted)
            throw new InvalidOperationException("IMicroflowService rejected the activity insertion.");

        tx.Commit();
        return beforePosition;
    }

    public void ModifyActivity(string microflowQualifiedName, int activityPosition, IReadOnlyDictionary<string, string> changes)
    {
        var (mf, _) = FindMicroflowByQualifiedName(microflowQualifiedName);
        if (mf is null)
            throw new InvalidOperationException($"Microflow '{microflowQualifiedName}' not found.");

        var actionActivities = GetActionActivities(mf);
        if (activityPosition < 1 || activityPosition > actionActivities.Count)
            throw new InvalidOperationException(
                $"activityPosition {activityPosition} out of range (1-{actionActivities.Count}).");

        var target = actionActivities[activityPosition - 1];

        using var tx = _model.StartTransaction($"Modify activity at position {activityPosition}");

        // Common properties
        if (changes.TryGetValue("caption", out var caption))
            target.Caption = caption;
        if (changes.TryGetValue("disabled", out var disabledStr) && bool.TryParse(disabledStr, out var disabled))
            target.Disabled = disabled;

        // Action-specific modifications
        switch (target.Action)
        {
            case ICreateObjectAction coa:
                if (changes.TryGetValue("output_variable", out var ov)) coa.OutputVariableName = ov;
                if (changes.TryGetValue("commit", out var commit) && Enum.TryParse<CommitEnum>(commit, true, out var ce)) coa.Commit = ce;
                if (changes.TryGetValue("refresh_in_client", out var ric) && bool.TryParse(ric, out var ricBool)) coa.RefreshInClient = ricBool;
                break;
            case IChangeObjectAction choa:
                if (changes.TryGetValue("change_variable", out var cv)) choa.ChangeVariableName = cv;
                if (changes.TryGetValue("commit", out var commit2) && Enum.TryParse<CommitEnum>(commit2, true, out var ce2)) choa.Commit = ce2;
                if (changes.TryGetValue("refresh_in_client", out var ric2) && bool.TryParse(ric2, out var ricBool2)) choa.RefreshInClient = ricBool2;
                break;
            case IRetrieveAction ra:
                if (changes.TryGetValue("output_variable", out var ov2)) ra.OutputVariableName = ov2;
                break;
            case ICommitAction ca:
                if (changes.TryGetValue("commit_variable", out var commitVar)) ca.CommitVariableName = commitVar;
                if (changes.TryGetValue("with_events", out var we) && bool.TryParse(we, out var weBool)) ca.WithEvents = weBool;
                if (changes.TryGetValue("refresh_in_client", out var ric3) && bool.TryParse(ric3, out var ricBool3)) ca.RefreshInClient = ricBool3;
                break;
            case IRollbackAction rba:
                if (changes.TryGetValue("rollback_variable", out var rbv)) rba.RollbackVariableName = rbv;
                if (changes.TryGetValue("refresh_in_client", out var ric4) && bool.TryParse(ric4, out var ricBool4)) rba.RefreshInClient = ricBool4;
                break;
            case IDeleteAction da:
                if (changes.TryGetValue("delete_variable", out var dv)) da.DeleteVariableName = dv;
                break;
            case ICreateListAction cla:
                if (changes.TryGetValue("output_variable", out var ov3)) cla.OutputVariableName = ov3;
                break;
        }

        tx.Commit();
    }

    public void DeleteActivity(string microflowQualifiedName, int activityPosition)
    {
        var (mf, _) = FindMicroflowByQualifiedName(microflowQualifiedName);
        if (mf is null)
            throw new InvalidOperationException($"Microflow '{microflowQualifiedName}' not found.");

        var actionActivities = GetActionActivities(mf);
        if (activityPosition < 1 || activityPosition > actionActivities.Count)
            throw new InvalidOperationException(
                $"activityPosition {activityPosition} out of range (1-{actionActivities.Count}).");

        // IActionActivity.Delete() is not exposed in the 10.21.1 ExtensionsAPI surface.
        // Activity deletion requires the untyped model API. Throw to signal unsupported.
        throw new NotSupportedException(
            "DeleteActivity is not supported via the 10.21.1 typed ExtensionsAPI surface. " +
            "Use the untyped model or modify the activity graph manually in Studio Pro.");
    }

    public void SetUrl(string microflowQualifiedName, string? url)
    {
        var (mf, _) = FindMicroflowByQualifiedName(microflowQualifiedName);
        if (mf is null)
            throw new InvalidOperationException($"Microflow '{microflowQualifiedName}' not found.");

        using var tx = _model.StartTransaction($"Set URL for microflow '{microflowQualifiedName}'");
        mf.Url = url ?? string.Empty;
        tx.Commit();
    }

    public void SetAccessLevel(string microflowQualifiedName, MicroflowAccessLevel level)
    {
        // IMicroflow in 10.21.1 does not expose an AllowedModuleRoles setter or AllMicroflowAccess
        // flag via the typed ExtensionsAPI surface. This operation is not possible without
        // the untyped model API (security settings live in a separate IModuleSecurity unit).
        // No-op with documentation.
        _ = microflowQualifiedName;
        _ = level;
    }

    public VariableNameCheckResult CheckVariableName(string microflowQualifiedName, string variableName)
    {
        var (mf, _) = FindMicroflowByQualifiedName(microflowQualifiedName);
        if (mf is null)
            throw new InvalidOperationException($"Microflow '{microflowQualifiedName}' not found.");

        var inUse = _microflowService.IsVariableNameInUse(mf, variableName);

        // Collect existing variable names
        var existingVars = new List<string>();
        try
        {
            var activities = _microflowService.GetAllMicroflowActivities(mf);
            foreach (var act in activities)
            {
                if (act is not IActionActivity aa) continue;
                switch (aa.Action)
                {
                    case ICreateObjectAction coa when !string.IsNullOrEmpty(coa.OutputVariableName):
                        existingVars.Add(coa.OutputVariableName!); break;
                    case IRetrieveAction ra when !string.IsNullOrEmpty(ra.OutputVariableName):
                        existingVars.Add(ra.OutputVariableName!); break;
                    case ICreateListAction cla when !string.IsNullOrEmpty(cla.OutputVariableName):
                        existingVars.Add(cla.OutputVariableName!); break;
                    case IListOperationAction loa when !string.IsNullOrEmpty(loa.OutputVariableName):
                        existingVars.Add(loa.OutputVariableName!); break;
                    case IMicroflowCallAction mca when mca.UseReturnVariable && !string.IsNullOrEmpty(mca.OutputVariableName):
                        existingVars.Add(mca.OutputVariableName!); break;
                }
            }
            // Parameter names
            foreach (var p in _microflowService.GetParameters(mf))
                existingVars.Add(p.Name);
        }
        catch { /* best effort */ }

        string? suggested = null;
        if (inUse)
        {
            for (int i = 2; i <= 20; i++)
            {
                var candidate = $"{variableName}{i}";
                if (!_microflowService.IsVariableNameInUse(mf, candidate))
                {
                    suggested = candidate;
                    break;
                }
            }
        }

        return new VariableNameCheckResult(inUse, suggested, existingVars.Distinct().ToList());
    }

    // ── Nanoflows (read-only via untyped model) ───────────────────────────────

    public IReadOnlyList<NanoflowSummary> ListNanoflows(ModuleId? moduleFilter)
    {
        if (_untyped is null)
            return Array.Empty<NanoflowSummary>();

        var root = _untyped.GetUntypedModel(_model);
        var nanoflows = GetUnitsWithFallback(root, "Microflows$Nanoflow");
        var result = new List<NanoflowSummary>();

        foreach (var nf in nanoflows)
        {
            if (moduleFilter is not null)
            {
                var f = moduleFilter.Value; // unwrap Nullable<ModuleId>
                var nfModule = nf.QualifiedName?.Split('.').FirstOrDefault();
                if (!string.Equals(nfModule, f.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            result.Add(BuildNanoflowSummary(nf));
        }
        return result;
    }

    public NanoflowSummary? ReadNanoflow(string qualifiedName)
    {
        if (_untyped is null) return null;

        var root = _untyped.GetUntypedModel(_model);
        var nanoflows = GetUnitsWithFallback(root, "Microflows$Nanoflow");
        var found = nanoflows.FirstOrDefault(nf =>
            string.Equals(nf.QualifiedName, qualifiedName, StringComparison.OrdinalIgnoreCase));
        return found is null ? null : BuildNanoflowSummary(found);
    }

    // ── Java actions (read-only) ──────────────────────────────────────────────

    public IReadOnlyList<JavaActionDescriptor> ListJavaActions(ModuleId? moduleFilter)
    {
        var modules = GetModules(moduleFilter);
        var result = new List<JavaActionDescriptor>();
        foreach (var module in modules)
        {
            var javaActions = _model.Root.GetModuleDocuments<IJavaAction>(module);
            foreach (var ja in javaActions)
            {
                var paramNames = ja.GetActionParameters()
                    .Select(p => p.Name)
                    .ToList();
                result.Add(new JavaActionDescriptor(
                    Document: new DocumentId(ParseId(ja.Id), ja.QualifiedName?.ToString() ?? $"{module.Name}.{ja.Name}"),
                    Module: module.Name,
                    ParameterNames: paramNames));
            }
        }
        return result;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static Guid ParseId(string id)
        => Guid.TryParse(id, out var guid) ? guid : GuidFromString(id);

    private static Guid GuidFromString(string s)
    {
        var bytes = new byte[16];
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(s));
        Array.Copy(hash, bytes, 16);
        return new Guid(bytes);
    }

    private IModule ResolveModuleByName(string name)
    {
        var module = _model.Root.GetModules()
            .FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
        return module ?? throw new InvalidOperationException($"Module '{name}' not found.");
    }

    private IEnumerable<IModule> GetModules(ModuleId? filter)
    {
        if (filter is null)
            return _model.Root.GetModules();
        var f = filter.Value; // unwrap Nullable<ModuleId>
        var module = _model.Root.GetModules()
            .FirstOrDefault(m => string.Equals(m.Name, f.Name, StringComparison.OrdinalIgnoreCase)
                              || (Guid.TryParse(m.Id, out var g) && g == f.Value));
        return module is not null ? new[] { module } : Array.Empty<IModule>();
    }

    private static List<IMicroflow> CollectMicroflowsInFolder(IFolderBase folder)
    {
        var result = folder.GetDocuments().OfType<IMicroflow>().ToList();
        foreach (var sub in folder.GetFolders())
            result.AddRange(CollectMicroflowsInFolder(sub));
        return result;
    }

    private (IMicroflow? mf, IModule? module) FindMicroflowByQualifiedName(string qualifiedName)
    {
        var dot = qualifiedName.IndexOf('.');
        string? moduleName = dot >= 0 ? qualifiedName[..dot] : null;
        var mfName = dot >= 0 ? qualifiedName[(dot + 1)..] : qualifiedName;

        foreach (var module in _model.Root.GetModules())
        {
            if (moduleName is not null &&
                !string.Equals(module.Name, moduleName, StringComparison.OrdinalIgnoreCase))
                continue;

            var mf = CollectMicroflowsInFolder(module)
                .FirstOrDefault(m => string.Equals(m.Name, mfName, StringComparison.OrdinalIgnoreCase));
            if (mf is not null)
                return (mf, module);
        }
        return (null, null);
    }

    private static IFolder? FindFolderByPath(IFolderBase parent, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        IFolderBase current = parent;
        foreach (var part in parts)
        {
            var next = current.GetFolders()
                .FirstOrDefault(f => string.Equals(f.Name, part, StringComparison.OrdinalIgnoreCase));
            if (next is null) return null;
            current = next;
        }
        return current as IFolder;
    }

    private static IFolderBase? FindParentFolder(IModule module, IMicroflow target)
    {
        if (module.GetDocuments().Contains(target)) return module;
        foreach (var folder in GetAllFolders(module))
            if (folder.GetDocuments().Contains(target)) return folder;
        return null;
    }

    private static IEnumerable<IFolder> GetAllFolders(IFolderBase parent)
    {
        foreach (var folder in parent.GetFolders())
        {
            yield return folder;
            foreach (var sub in GetAllFolders(folder))
                yield return sub;
        }
    }

    private List<IActionActivity> GetActionActivities(IMicroflow mf)
        => _microflowService.GetAllMicroflowActivities(mf)
            .OfType<IActionActivity>()
            .ToList();

    private MicroflowSummary BuildSummary(IMicroflow mf, string moduleName)
    {
        // Parameters
        var parameters = new List<MicroflowParameter>();
        try
        {
            foreach (var p in _microflowService.GetParameters(mf))
                parameters.Add(new MicroflowParameter(
                    Name: p.Name,
                    TypeQualifiedName: FormatDataType(p.Type),
                    IsList: p.Type is ListType,
                    Documentation: p.Documentation));
        }
        catch { /* resilient */ }

        // Activity count
        int actCount = 0;
        try { actCount = _microflowService.GetAllMicroflowActivities(mf).OfType<IActionActivity>().Count(); }
        catch { }

        // Return type
        var returnTypeStr = FormatDataType(mf.ReturnType);
        var returnIsList = mf.ReturnType is ListType;

        return new MicroflowSummary(
            QualifiedName: mf.QualifiedName?.FullName ?? $"{moduleName}.{mf.Name}",
            Module: moduleName,
            Name: mf.Name,
            Documentation: null, // IMicroflow.Documentation not exposed in 10.21.1 ExtensionsAPI
            AccessLevel: MicroflowAccessLevel.CheckPerOperation, // read-back not exposed in 10.21.1 typed API
            Parameters: parameters,
            ReturnTypeQualifiedName: returnTypeStr == "Void" ? null : returnTypeStr,
            ReturnIsList: returnIsList,
            ActivityCount: actCount);
    }

    private MicroflowActivitySummary BuildActivitySummary(IActionActivity action, int position)
    {
        var parameters = new Dictionary<string, string>();
        string activityType = "unknown";
        string? outputVar = null;
        string? targetEntity = null;
        string? targetMf = null;
        string? targetJa = null;
        string? caption = action.Caption;

        try
        {
            switch (action.Action)
            {
                case ICreateObjectAction coa:
                    activityType = "create_object";
                    outputVar = coa.OutputVariableName;
                    targetEntity = coa.Entity?.FullName;
                    if (targetEntity is not null) parameters["entity"] = targetEntity;
                    if (outputVar is not null) parameters["output_variable"] = outputVar;
                    break;
                case IChangeObjectAction choa:
                    activityType = "change_object";
                    parameters["change_variable"] = choa.ChangeVariableName ?? "";
                    break;
                case IRetrieveAction ra:
                    activityType = "retrieve";
                    outputVar = ra.OutputVariableName;
                    if (outputVar is not null) parameters["output_variable"] = outputVar;
                    if (ra.RetrieveSource is IDatabaseRetrieveSource dbSrc)
                    {
                        parameters["source"] = "database";
                        targetEntity = dbSrc.Entity?.FullName;
                        if (targetEntity is not null) parameters["entity"] = targetEntity;
                    }
                    else if (ra.RetrieveSource is IAssociationRetrieveSource assocSrc)
                    {
                        parameters["source"] = "association";
                        parameters["association"] = assocSrc.Association?.Name ?? "";
                    }
                    break;
                case ICommitAction ca:
                    activityType = "commit";
                    parameters["commit_variable"] = ca.CommitVariableName ?? "";
                    break;
                case IRollbackAction rba:
                    activityType = "rollback";
                    parameters["rollback_variable"] = rba.RollbackVariableName ?? "";
                    break;
                case IDeleteAction da:
                    activityType = "delete";
                    parameters["delete_variable"] = da.DeleteVariableName ?? "";
                    break;
                case ICreateListAction cla:
                    activityType = "create_list";
                    outputVar = cla.OutputVariableName;
                    targetEntity = cla.Entity?.FullName;
                    if (targetEntity is not null) parameters["entity"] = targetEntity;
                    if (outputVar is not null) parameters["output_variable"] = outputVar;
                    break;
                case IListOperationAction loa:
                    activityType = "list_operation";
                    outputVar = loa.OutputVariableName;
                    if (outputVar is not null) parameters["output_variable"] = outputVar;
                    break;
                case IMicroflowCallAction mca:
                    activityType = "microflow_call";
                    targetMf = mca.MicroflowCall?.Microflow?.FullName;
                    if (targetMf is not null) parameters["microflow"] = targetMf;
                    if (mca.UseReturnVariable) outputVar = mca.OutputVariableName;
                    if (outputVar is not null) parameters["output_variable"] = outputVar;
                    break;
                case IJavaActionCallAction jaca:
                    activityType = "java_action_call";
                    targetJa = jaca.JavaAction?.FullName;
                    if (targetJa is not null) parameters["java_action"] = targetJa;
                    break;
                default:
                    activityType = action.Action?.GetType().Name ?? "unknown";
                    break;
            }
        }
        catch { /* resilient */ }

        return new MicroflowActivitySummary(
            Position: position,
            ActivityType: activityType,
            Caption: caption,
            OutputVariable: outputVar,
            TargetEntity: targetEntity,
            TargetMicroflow: targetMf,
            TargetJavaAction: targetJa,
            Parameters: parameters);
    }

    private static string FormatDataType(DataType? dt)
    {
        if (dt is null) return "Void";
        return dt switch
        {
            ListType lt => $"List<{lt.EntityName?.FullName ?? "Unknown"}>",
            ObjectType ot => ot.EntityName?.FullName ?? "Object",
            EnumerationType et => et.EnumerationName?.FullName ?? "Enumeration",
            _ => dt.ToString() ?? "Unknown"
        };
    }

    private DataType ParseDataType(string? typeStr, bool isList)
    {
        if (string.IsNullOrWhiteSpace(typeStr) ||
            typeStr.Equals("void", StringComparison.OrdinalIgnoreCase))
            return DataType.Void;

        // Try known primitives
        var lower = typeStr.ToLowerInvariant();
        DataType? primitive = lower switch
        {
            "string" => DataType.String,
            "integer" => DataType.Integer,
            "boolean" => DataType.Boolean,
            "decimal" => DataType.Decimal,
            "datetime" => DataType.DateTime,
            _ => null
        };
        if (primitive is not null)
            return isList ? DataType.List(_model.ToQualifiedName<Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.IEntity>(typeStr)) : primitive;

        // Treat as entity qualified name
        if (isList)
            return DataType.List(_model.ToQualifiedName<Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.IEntity>(typeStr));
        return DataType.Object(_model.ToQualifiedName<Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.IEntity>(typeStr));
    }

    private List<(string, DataType)> BuildParamList(IReadOnlyList<MicroflowParameter> parameters)
    {
        var result = new List<(string, DataType)>();
        foreach (var p in parameters)
        {
            var dt = ParseDataType(p.TypeQualifiedName, p.IsList);
            result.Add((p.Name, dt));
        }
        return result;
    }

    private MicroflowReturnValue? BuildReturnValue(DataType returnDataType)
    {
        if (_exprService is null)
            return null; // cannot create expression without service

        var defaultExpr = returnDataType switch
        {
            var dt when dt == DataType.String => "''",
            var dt when dt == DataType.Integer => "0",
            var dt when dt == DataType.Decimal => "0.0",
            var dt when dt == DataType.Boolean => "false",
            var dt when dt == DataType.DateTime => "dateTime(1900)",
            _ => "empty"
        };

        try
        {
            var expr = _exprService.CreateFromString(defaultExpr);
            return new MicroflowReturnValue(returnDataType, expr);
        }
        catch
        {
            try
            {
                var expr = _exprService.CreateFromString("empty");
                return new MicroflowReturnValue(returnDataType, expr);
            }
            catch { return null; }
        }
    }

    private IActionActivity? CreateActivity(MicroflowActivitySummary activity)
    {
        // Create a minimal activity based on ActivityType.
        // The host delegates complex expression/entity wiring; parameters come in as strings.
        var p = activity.Parameters;

        switch (activity.ActivityType.ToLowerInvariant())
        {
            case "create_object":
            case "create_variable":
            case "create":
            {
                var entityName = activity.TargetEntity ?? (p.TryGetValue("entity", out var e) ? e : null);
                if (entityName is null) throw new InvalidOperationException("create_object requires 'entity'.");
                var a = _model.Create<ICreateObjectAction>();
                a.Entity = _model.ToQualifiedName<Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.IEntity>(entityName);
                a.OutputVariableName = activity.OutputVariable
                    ?? (p.TryGetValue("output_variable", out var ov) ? ov : entityName.Split('.').Last().ToLowerInvariant());
                var aa = _model.Create<IActionActivity>();
                aa.Action = a;
                if (!string.IsNullOrEmpty(activity.Caption)) aa.Caption = activity.Caption;
                return aa;
            }
            case "change_object":
            {
                var a = _model.Create<IChangeObjectAction>();
                a.ChangeVariableName = p.TryGetValue("change_variable", out var cv) ? cv : "";
                var aa = _model.Create<IActionActivity>();
                aa.Action = a;
                if (!string.IsNullOrEmpty(activity.Caption)) aa.Caption = activity.Caption;
                return aa;
            }
            case "retrieve":
            case "retrieve_from_database":
            case "database_retrieve":
            {
                var dbSrc = _model.Create<IDatabaseRetrieveSource>();
                var entityName = activity.TargetEntity ?? (p.TryGetValue("entity", out var e) ? e : null);
                if (entityName is not null)
                    dbSrc.Entity = _model.ToQualifiedName<Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.IEntity>(entityName);
                var a = _model.Create<IRetrieveAction>();
                a.RetrieveSource = dbSrc;
                a.OutputVariableName = activity.OutputVariable
                    ?? (p.TryGetValue("output_variable", out var ov) ? ov : "retrievedObject");
                var aa = _model.Create<IActionActivity>();
                aa.Action = a;
                if (!string.IsNullOrEmpty(activity.Caption)) aa.Caption = activity.Caption;
                return aa;
            }
            case "commit":
            case "commit_object":
            {
                var a = _model.Create<ICommitAction>();
                a.CommitVariableName = p.TryGetValue("commit_variable", out var cv) ? cv : "";
                var aa = _model.Create<IActionActivity>();
                aa.Action = a;
                if (!string.IsNullOrEmpty(activity.Caption)) aa.Caption = activity.Caption;
                return aa;
            }
            case "rollback":
            case "rollback_object":
            {
                var a = _model.Create<IRollbackAction>();
                a.RollbackVariableName = p.TryGetValue("rollback_variable", out var rv) ? rv : "";
                var aa = _model.Create<IActionActivity>();
                aa.Action = a;
                if (!string.IsNullOrEmpty(activity.Caption)) aa.Caption = activity.Caption;
                return aa;
            }
            case "delete":
            case "delete_object":
            {
                var a = _model.Create<IDeleteAction>();
                a.DeleteVariableName = p.TryGetValue("delete_variable", out var dv) ? dv : "";
                var aa = _model.Create<IActionActivity>();
                aa.Action = a;
                if (!string.IsNullOrEmpty(activity.Caption)) aa.Caption = activity.Caption;
                return aa;
            }
            case "create_list":
            case "new_list":
            {
                var entityName = activity.TargetEntity ?? (p.TryGetValue("entity", out var e) ? e : null);
                if (entityName is null) throw new InvalidOperationException("create_list requires 'entity'.");
                var a = _model.Create<ICreateListAction>();
                a.Entity = _model.ToQualifiedName<Mendix.StudioPro.ExtensionsAPI.Model.DomainModels.IEntity>(entityName);
                a.OutputVariableName = activity.OutputVariable
                    ?? (p.TryGetValue("output_variable", out var ov) ? ov : "newList");
                var aa = _model.Create<IActionActivity>();
                aa.Action = a;
                if (!string.IsNullOrEmpty(activity.Caption)) aa.Caption = activity.Caption;
                return aa;
            }
            case "microflow_call":
            case "call_microflow":
            {
                var mfName = activity.TargetMicroflow ?? (p.TryGetValue("microflow", out var mf) ? mf : null);
                if (mfName is null) throw new InvalidOperationException("microflow_call requires 'microflow'.");
                var mfCall = _model.Create<IMicroflowCall>();
                mfCall.Microflow = _model.ToQualifiedName<IMicroflow>(mfName);
                var a = _model.Create<IMicroflowCallAction>();
                a.MicroflowCall = mfCall;
                if (activity.OutputVariable is not null)
                {
                    a.UseReturnVariable = true;
                    a.OutputVariableName = activity.OutputVariable;
                }
                var aa = _model.Create<IActionActivity>();
                aa.Action = a;
                if (!string.IsNullOrEmpty(activity.Caption)) aa.Caption = activity.Caption;
                return aa;
            }
            case "java_action_call":
            case "call_java_action":
            {
                var jaName = activity.TargetJavaAction ?? (p.TryGetValue("java_action", out var ja) ? ja : null);
                if (jaName is null) throw new InvalidOperationException("java_action_call requires 'java_action'.");
                var a = _model.Create<IJavaActionCallAction>();
                a.JavaAction = _model.ToQualifiedName<IJavaAction>(jaName);
                var aa = _model.Create<IActionActivity>();
                aa.Action = a;
                if (!string.IsNullOrEmpty(activity.Caption)) aa.Caption = activity.Caption;
                return aa;
            }
            default:
                return null;
        }
    }

    // ── Nanoflow untyped helpers ───────────────────────────────────────────────

    private static List<IModelUnit> GetUnitsWithFallback(IModelRoot root, string typeString)
    {
        var units = root.GetUnitsOfType(typeString)?.ToList() ?? new List<IModelUnit>();
        if (units.Count == 0 && typeString.Contains('$'))
            units = root.GetUnitsOfType(typeString.Replace("$", "."))?.ToList() ?? new List<IModelUnit>();
        return units;
    }

    private NanoflowSummary BuildNanoflowSummary(IModelUnit nf)
    {
        var qualifiedName = nf.QualifiedName ?? "";
        var moduleName = qualifiedName.Split('.').FirstOrDefault() ?? "";
        var name = nf.Name ?? "";

        // Documentation
        string? doc = null;
        try { doc = ReadPropValue(nf, "documentation")?.ToString(); } catch { }

        // Parameters (from objectCollection)
        var parameters = new List<MicroflowParameter>();
        int activityCount = 0;
        int allowedRoleCount = 0;
        string? returnType = null;

        try
        {
            returnType = MapReturnType(nf);

            var objCollProp = nf.GetProperty("objectCollection");
            if (objCollProp?.Value is IModelStructure objColl)
            {
                var objects = objColl.GetProperty("objects");
                if (objects?.IsList == true)
                {
                    var vals = objects.GetValues();
                    if (vals is not null)
                    {
                        foreach (var v in vals)
                        {
                            if (v is not IModelStructure obj) continue;
                            var typeName = obj.Type ?? "";
                            if (typeName.Contains("ParameterObject"))
                            {
                                var paramName = obj.Name ?? ReadPropValue(obj, "name")?.ToString() ?? "";
                                parameters.Add(new MicroflowParameter(paramName, "Unknown", false, null));
                            }
                            else if (!typeName.Contains("StartEvent") && !typeName.Contains("EndEvent"))
                                activityCount++;
                        }
                    }
                }
            }
        }
        catch { }

        try
        {
            var rolesProp = nf.GetProperty("allowedModuleRoles");
            if (rolesProp?.IsList == true)
                allowedRoleCount = rolesProp.GetValues()?.Count() ?? 0;
        }
        catch { }

        return new NanoflowSummary(
            QualifiedName: qualifiedName,
            Module: moduleName,
            Name: name,
            Documentation: doc,
            Parameters: parameters,
            ReturnTypeQualifiedName: returnType,
            ActivityCount: activityCount,
            AllowedRoleCount: allowedRoleCount);
    }

    private static object? ReadPropValue(IModelStructure element, string propertyName)
    {
        try
        {
            var prop = element.GetProperty(propertyName);
            if (prop is null) return null;
            if (prop.IsList)
                return prop.GetValues()?.Select(v => v?.ToString()).ToList();
            return prop.Value?.ToString();
        }
        catch { return null; }
    }

    private static string MapReturnType(IModelUnit unit)
    {
        try
        {
            var rtProp = unit.GetProperty("microflowReturnType");
            if (rtProp?.Value is IModelStructure rtEl)
            {
                return rtEl.Type switch
                {
                    "DataTypes$ObjectType" => "Object",
                    "DataTypes$ListType" => "List",
                    "DataTypes$BooleanType" => "Boolean",
                    "DataTypes$StringType" => "String",
                    "DataTypes$IntegerType" => "Integer",
                    "DataTypes$DecimalType" => "Decimal",
                    "DataTypes$DateTimeType" => "DateTime",
                    "DataTypes$VoidType" => "Void",
                    _ => rtEl.Type?.Split('$').LastOrDefault() ?? "Unknown"
                };
            }
        }
        catch { }
        return "Void";
    }
}
