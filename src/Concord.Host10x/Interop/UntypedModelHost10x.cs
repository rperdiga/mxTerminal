namespace Concord.Host10x.Interop;

using System.Text.Json;
using Mendix.StudioPro.ExtensionsAPI.Model;
using Mendix.StudioPro.ExtensionsAPI.Model.UntypedModel;
using Mendix.StudioPro.ExtensionsAPI.Services;
using Terminal.Interop;

/// <summary>
/// Implements IUntypedModelHost against the 10.21.1 ExtensionsAPI surface.
/// Bodies ported from MendixAdditionalTools untyped-model helpers (SPMCP source),
/// with JSON parsing stripped and Mendix modeling work retained.
///
/// IUntypedModelAccessService lives in Mendix.StudioPro.ExtensionsAPI.Services
/// on both 10.21.1 and 10.21.1. The service is resolved via optional constructor
/// injection; IsAvailable reflects whether the injected instance is non-null.
/// </summary>
public sealed class UntypedModelHost10x : IUntypedModelHost
{
    private readonly IModel _model;
    private readonly IUntypedModelAccessService? _untyped;

    public UntypedModelHost10x(
        IModel model,
        IUntypedModelAccessService? untyped = null)
    {
        _model = model;
        _untyped = untyped;
    }

    // ── IUntypedModelHost ─────────────────────────────────────────────────────

    public bool IsAvailable => _untyped is not null;

    public IReadOnlyList<UntypedUnitDescriptor> GetUnitsOfType(string typeString)
    {
        EnsureAvailable();
        var root = _untyped!.GetUntypedModel(_model);

        // Try with $ separator first, then . separator (version compat)
        var units = root.GetUnitsOfType(typeString)?.ToList()
                    ?? new List<IModelUnit>();
        if (units.Count == 0 && typeString.Contains('$'))
            units = root.GetUnitsOfType(typeString.Replace("$", "."))?.ToList()
                    ?? new List<IModelUnit>();

        return units
            .Select(u => new UntypedUnitDescriptor(u.Name, u.QualifiedName, u.Type))
            .ToList();
    }

    public string ReadUnitPropertiesAsJson(string qualifiedName)
    {
        EnsureAvailable();
        var root = _untyped!.GetUntypedModel(_model);
        var unit = FindUnit(root, qualifiedName)
            ?? throw new InvalidOperationException($"Model unit '{qualifiedName}' not found.");

        var dict = new Dictionary<string, object?>();
        try
        {
            foreach (var prop in unit.GetProperties())
            {
                try
                {
                    if (prop.IsList)
                        dict[prop.Name] = prop.GetValues()?.Select(v => v?.ToString()).ToList();
                    else
                        dict[prop.Name] = prop.Value?.ToString();
                }
                catch { dict[prop.Name] = null; }
            }
        }
        catch { /* best-effort */ }

        return JsonSerializer.Serialize(dict);
    }

    public string? ReadUnitProperty(string qualifiedName, string propertyName)
    {
        EnsureAvailable();
        var root = _untyped!.GetUntypedModel(_model);
        var unit = FindUnit(root, qualifiedName);
        if (unit is null) return null;

        try
        {
            var prop = unit.GetProperty(propertyName);
            if (prop is null) return null;
            if (prop.IsList)
                return string.Join(", ", prop.GetValues()?.Select(v => v?.ToString()) ?? Enumerable.Empty<string?>());
            return prop.Value?.ToString();
        }
        catch { return null; }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void EnsureAvailable()
    {
        if (_untyped is null)
            throw new InvalidOperationException(
                "IUntypedModelAccessService is not available. " +
                "IsAvailable is false — check Studio Pro version and DI registration.");
    }

    private static IModelUnit? FindUnit(IModelRoot root, string qualifiedName)
    {
        // Search across all unit types — try a broad GetUnitsOfType then filter
        // We don't know the type, so we scan all known separators.
        // Use a heuristic: scan common type prefixes then do a qualified-name match.
        // The untyped root doesn't expose a GetUnitByQualifiedName, so we scan.
        foreach (var typeHint in new[] { "Microflows$Microflow", "Microflows$Nanoflow",
            "DomainModels$DomainModel", "Security$ProjectSecurity", "Security$ModuleSecurity",
            "JavaActions$JavaAction", "Pages$Page", "ScheduledEvents$ScheduledEvent",
            "WebServices$RestService", "WebServices$RestPublishedService" })
        {
            var candidates = root.GetUnitsOfType(typeHint)?.ToList() ?? new List<IModelUnit>();
            var found = candidates.FirstOrDefault(u =>
                string.Equals(u.QualifiedName, qualifiedName, StringComparison.OrdinalIgnoreCase));
            if (found is not null) return found;
        }
        return null;
    }
}
