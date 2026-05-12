namespace Terminal.Interop;

/// <summary>
/// Unit descriptor returned when listing model units via the untyped model API.
/// Name and QualifiedName mirror the IModelUnit properties; TypeString is the
/// Mendix internal type identifier (e.g. "Microflows$Nanoflow").
/// </summary>
public record UntypedUnitDescriptor(
    string? Name,
    string? QualifiedName,
    string? TypeString);

/// <summary>
/// Wraps Mendix IUntypedModelAccessService, which exposes schema-agnostic traversal
/// of the Studio Pro model (used heavily by nanoflow listing, scheduled events,
/// security reading, and other introspection tools in SPMCP).
///
/// This service is not available on Studio Pro 10.21.1. IsAvailable MUST be checked
/// before calling any method; the Host10x implementation returns false when the
/// underlying service is null.
///
/// The interface is intentionally thin: the untyped model is used for read-only
/// introspection at the Interop boundary. Writes go through the typed host interfaces
/// (IDomainModelHost, IMicroflowAuthoringHost, etc.).
/// </summary>
public interface IUntypedModelHost
{
    /// <summary>
    /// True when IUntypedModelAccessService is present in this Studio Pro version.
    /// False on Studio Pro 10.21.1 and any other version that does not ship the service.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// List all model units of the given Mendix type string (e.g. "Microflows$Nanoflow",
    /// "JavaActions$JavaAction", "Security$ProjectSecurity").
    /// Tries both "$" and "." separators if the first returns empty (version compat).
    /// Throws InvalidOperationException if IsAvailable is false.
    /// </summary>
    IReadOnlyList<UntypedUnitDescriptor> GetUnitsOfType(string typeString);

    /// <summary>
    /// Read a model unit's properties as a JSON object string, keyed by property name.
    /// Useful for structured introspection of units that have no typed API equivalent.
    /// Throws InvalidOperationException if IsAvailable is false.
    /// </summary>
    string ReadUnitPropertiesAsJson(string qualifiedName);

    /// <summary>
    /// Read a specific property value from a model unit, returning its string
    /// representation. Returns null if the unit or property is not found.
    /// Throws InvalidOperationException if IsAvailable is false.
    /// </summary>
    string? ReadUnitProperty(string qualifiedName, string propertyName);
}
