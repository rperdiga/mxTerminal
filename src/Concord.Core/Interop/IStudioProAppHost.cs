namespace Terminal.Interop;

/// <summary>
/// Minimal abstraction over Studio Pro's IApp. Hosts provide a concrete
/// implementation that wraps the version-specific Mendix.StudioPro.ExtensionsAPI
/// types. Core uses only these methods.
/// </summary>
public interface IStudioProAppHost
{
    /// <summary>Absolute path to the open Mendix project directory.</summary>
    string ProjectPath { get; }

    /// <summary>The project's display name (matches the .mpr filename without extension).</summary>
    string ProjectName { get; }

    /// <summary>True if a project is currently open.</summary>
    bool HasOpenProject { get; }
}
