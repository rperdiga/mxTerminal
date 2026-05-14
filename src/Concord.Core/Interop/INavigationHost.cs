namespace Terminal.Interop;

/// <summary>
/// A single entry in a navigation profile (responsive web navigation item).
/// Caption is the label shown in the nav menu; DocumentQualifiedName identifies
/// the page or microflow the item navigates to.
/// </summary>
public record NavigationItem(
    string Caption,
    string? DocumentQualifiedName,
    string? IconQualifiedName,
    string? RoleQualifiedName);

/// <summary>
/// A navigation profile (e.g. "Responsive", "Phone", "Tablet") and its ordered items.
/// </summary>
public record NavigationProfile(
    string Name,
    IReadOnlyList<NavigationItem> Items);

/// <summary>
/// Wraps Mendix INavigationManagerService (available as Services.INavigationManagerService
/// or UI.Services.INavigationManagerService depending on the ExtensionsAPI version;
/// the host implementation resolves the ambiguity — Core is unaffected).
///
/// The primary operation that SPMCP exercises is PopulateWebNavigationWith, which
/// corresponds to AddItems here. Read operations are included so Phase-4 refactored
/// tools can inspect current navigation state without a separate untyped-model pass.
/// </summary>
public interface INavigationHost
{
    /// <summary>
    /// List all navigation profiles available in the project (e.g. "Responsive", "Phone").
    /// </summary>
    IReadOnlyList<NavigationProfile> ListProfiles();

    /// <summary>
    /// Read the full item list for a single named profile.
    /// Returns null if the profile does not exist.
    /// </summary>
    NavigationProfile? ReadProfile(string profileName);

    /// <summary>
    /// Add a navigation item to the named profile.
    /// Corresponds to the caption+page pairs passed to PopulateWebNavigationWith.
    /// </summary>
    void AddItem(string profileName, NavigationItem item);

    /// <summary>
    /// Add multiple navigation items to the named profile in one call.
    /// Matches the batch signature that GenerateOverviewPages uses after page creation.
    /// </summary>
    void AddItems(string profileName, IReadOnlyList<NavigationItem> items);

    /// <summary>
    /// Remove the navigation item with the given caption from a profile.
    /// No-op if the caption is not found.
    /// </summary>
    void RemoveItem(string profileName, string caption);

    /// <summary>
    /// Change the target page or microflow of an existing navigation item.
    /// </summary>
    void SetItemTarget(string profileName, string caption, string microflowOrPageQualifiedName);

    /// <summary>
    /// Change the icon of an existing navigation item (null clears the icon).
    /// </summary>
    void SetItemIcon(string profileName, string caption, string? iconQualifiedName);
}
