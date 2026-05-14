using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Terminal;

/// <summary>
/// Reads Studio Pro's user-set theme preference from Settings.sqlite.
/// Path differs by platform — see <see cref="GetSettingsDbPath"/>.
/// <para>
/// Schema (verified 2026-05-01 on Win, 2026-05-07 on Mac):
/// <c>CREATE TABLE ModelerSettings(Version TEXT PRIMARY KEY NOT NULL, Settings TEXT NOT NULL)</c>
/// where <c>Version</c> is the Studio Pro version (e.g. "11.10.0") and <c>Settings</c> is
/// a JSON blob containing a root-level <c>ThemeName</c> integer.
/// Observed mapping: 0 = Light, 1 = Dark.
/// </para>
/// <para>
/// We do this because <c>matchMedia('(prefers-color-scheme: dark)')</c> inside
/// Studio Pro's WebView follows the OS app theme, NOT Studio Pro's own
/// theme preference. Reading the SQLite directly is the most reliable signal we
/// have until Mendix exposes a theme service via the Extensibility API.
/// </para>
/// </summary>
public static class StudioProThemeProbe
{
    public enum Theme { Light, Dark }

    /// <summary>Result with diagnostic info for logging.</summary>
    public readonly record struct ProbeResult(Theme? Theme, string Diagnostic);

    /// <summary>
    /// Resolve the path to Studio Pro's Settings.sqlite for the current OS.
    /// Returns null on unsupported platforms.
    /// <list type="bullet">
    ///   <item>Windows: <c>%LOCALAPPDATA%\Mendix\Settings.sqlite</c></item>
    ///   <item>macOS: <c>~/Library/Application Support/Mendix/Settings.sqlite</c></item>
    /// </list>
    /// On Mac, <see cref="Environment.SpecialFolder.LocalApplicationData"/> resolves to
    /// <c>~/.local/share</c> (XDG) which is NOT where Studio Pro writes — hence
    /// the explicit Mac branch.
    /// </summary>
    public static string? GetSettingsDbPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Mendix", "Settings.sqlite");
        }
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Application Support", "Mendix", "Settings.sqlite");
        }
        return null;
    }

    /// <summary>
    /// Probe Studio Pro's persisted theme. Returns a result that includes a
    /// short diagnostic string the caller can log (success path includes the
    /// resolved value; failure path includes the reason).
    /// </summary>
    /// <param name="studioProVersion">e.g. "11.10.0".</param>
    public static ProbeResult Read(string studioProVersion)
    {
        var dbPath = GetSettingsDbPath();
        if (dbPath is null)
            return new ProbeResult(null, $"theme-probe-skipped: unsupported platform ({Environment.OSVersion.Platform})");
        return ReadFromDb(dbPath, studioProVersion);
    }

    /// <summary>Test seam: read the theme from an explicit DB path.</summary>
    internal static ProbeResult ReadFromDb(string dbPath, string studioProVersion)
    {
        if (!File.Exists(dbPath))
            return new ProbeResult(null, $"db-not-found at {dbPath}");

        // Studio Pro can hold a write lock on Settings.sqlite (Windows is
        // strict about this; Mac is more permissive but WAL state can still
        // surprise readers). Copy the file to temp first — Mendix settings
        // are tiny (~100 KB), so the copy is free.
        string tempCopy;
        try
        {
            tempCopy = Path.Combine(Path.GetTempPath(), $"mxterm-settings-{Guid.NewGuid():N}.sqlite");
            File.Copy(dbPath, tempCopy, overwrite: true);
        }
        catch (Exception ex)
        {
            return new ProbeResult(null, $"copy-failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = tempCopy,
                Mode       = SqliteOpenMode.ReadOnly,
            }.ToString();
            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Settings FROM ModelerSettings WHERE Version = $v LIMIT 1";
            cmd.Parameters.AddWithValue("$v", studioProVersion);
            var json = cmd.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(json))
                return new ProbeResult(null, $"no-row for Version={studioProVersion}");

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("ThemeName", out var t))
                return new ProbeResult(null, "ThemeName-key-missing in JSON blob");
            if (t.ValueKind != JsonValueKind.Number)
                return new ProbeResult(null, $"ThemeName not a number (got {t.ValueKind})");

            return t.GetInt32() switch
            {
                0 => new ProbeResult(Theme.Light, "ThemeName=0 → light"),
                1 => new ProbeResult(Theme.Dark,  "ThemeName=1 → dark"),
                int other => new ProbeResult(null, $"ThemeName={other} (unknown — fallback)"),
            };
        }
        catch (Exception ex)
        {
            return new ProbeResult(null, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { File.Delete(tempCopy); } catch { /* best-effort */ }
        }
    }

    /// <summary>Convert a probed theme to a URL-friendly lowercase string.</summary>
    public static string ToUrlValue(Theme t) => t == Theme.Dark ? "dark" : "light";

    /// <summary>
    /// Whether the running Studio Pro version exposes the built-in
    /// <c>mendix-studio-pro</c> MCP server. Introduced in Studio Pro 11.10
    /// — versions below that (any 10.x, plus 11.0 through 11.9.x) don't
    /// have the feature, so Concord must NOT wire <c>mendix-studio-pro</c>
    /// into <c>.mcp.json</c> / <c>~/.codex/config.toml</c> and must hide
    /// the Studio Pro MCP section in Settings.
    /// <para>
    /// Conservative on parse failure: returns <c>false</c> if the version
    /// string is null, empty, or unparseable. This matches the user-chosen
    /// "hide by default when probe fails" semantics — a Studio Pro 11.10+
    /// install with a non-standard exe path will lose the section until the
    /// version can be detected, but a 10.x install will never accidentally
    /// surface the feature.
    /// </para>
    /// </summary>
    /// <param name="studioProVersion">e.g. "11.10.0", "10.24.13".</param>
    public static bool IsMcpServerSupported(string? studioProVersion)
    {
        if (string.IsNullOrWhiteSpace(studioProVersion)) return false;
        if (!Version.TryParse(studioProVersion, out var v)) return false;
        return v >= new Version(11, 10);
    }

    /// <summary>
    /// Whether the running Studio Pro version exposes the Maia AI panel that
    /// Concord's Maia bridge depends on. The Maia panel ships with Studio
    /// Pro 11.10+; older versions (10.x, 11.0–11.9.x) don't have a Maia
    /// surface to inject into, so Concord must NOT register the
    /// <c>maia__*</c> tools, must NOT build the CDP plumbing, and must NOT
    /// surface Maia controls in the Settings UI.
    /// <para>
    /// Same hide-by-default semantics as <see cref="IsMcpServerSupported"/>
    /// on probe failure. Currently the two thresholds happen to match
    /// (11.10) but they're kept as separate methods so a future Mendix
    /// change to either feature can shift one without affecting the other.
    /// </para>
    /// </summary>
    public static bool IsMaiaSupported(string? studioProVersion)
    {
        if (string.IsNullOrWhiteSpace(studioProVersion)) return false;
        if (!Version.TryParse(studioProVersion, out var v)) return false;
        return v >= new Version(11, 10);
    }

    /// <summary>
    /// Extracts a "<c>major.minor.patch</c>" version from Studio Pro's
    /// process exe path. Works for both Windows
    /// (<c>...\Mendix\11.10.0\modeler\studiopro.exe</c>) and the Mac
    /// bundle layout (e.g. <c>/Applications/Mendix Studio Pro 11.10.0.app/...</c>).
    /// Returns null if the path doesn't contain a version triple.
    /// <para>
    /// Lives in Core so <see cref="Concord.Host10x.Host10xEntry"/> /
    /// <see cref="Concord.Host11x.Host11xEntry"/> can use it at MEF
    /// activation time to gate version-specific tool registration. Both
    /// hosts' <c>TerminalPaneExtension</c> classes also call this for
    /// settings-payload + Probe* helpers.
    /// </para>
    /// </summary>
    public static string? StudioProVersionFromExePath()
    {
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return null;
            var match = System.Text.RegularExpressions.Regex.Match(exePath, @"\d+\.\d+\.\d+");
            return match.Success ? match.Value : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Studio Pro's MCP-server preference snapshot.</summary>
    public readonly record struct McpServerInfo(bool? Enabled, int? Port, string Diagnostic);

    /// <summary>
    /// Read Studio Pro's MCP server preference from the same Settings.sqlite.
    /// Keys discovered 2026-05-01 in ModelerSettings JSON root:
    ///   EnableMcpServer (bool), McpServerPort (int).
    /// Used to warn the user when the port WE wire into Claude's .mcp.json
    /// doesn't match the port Studio Pro is actually serving on.
    /// </summary>
    public static McpServerInfo ReadMcpServer(string studioProVersion)
    {
        var dbPath = GetSettingsDbPath();
        if (dbPath is null)
            return new McpServerInfo(null, null, $"mcp-probe-skipped: unsupported platform ({Environment.OSVersion.Platform})");
        return ReadMcpServerFromDb(dbPath, studioProVersion);
    }

    /// <summary>Test seam: read MCP info from an explicit DB path.</summary>
    internal static McpServerInfo ReadMcpServerFromDb(string dbPath, string studioProVersion)
    {
        if (!File.Exists(dbPath))
            return new McpServerInfo(null, null, $"db-not-found at {dbPath}");

        string tempCopy;
        try
        {
            tempCopy = Path.Combine(Path.GetTempPath(), $"mxterm-settings-mcp-{Guid.NewGuid():N}.sqlite");
            File.Copy(dbPath, tempCopy, overwrite: true);
        }
        catch (Exception ex)
        {
            return new McpServerInfo(null, null, $"copy-failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = tempCopy,
                Mode       = SqliteOpenMode.ReadOnly,
            }.ToString();
            using var conn = new SqliteConnection(connStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Settings FROM ModelerSettings WHERE Version = $v LIMIT 1";
            cmd.Parameters.AddWithValue("$v", studioProVersion);
            var json = cmd.ExecuteScalar() as string;
            if (string.IsNullOrEmpty(json))
                return new McpServerInfo(null, null, $"no-row for Version={studioProVersion}");

            using var doc = JsonDocument.Parse(json);
            bool? enabled = null;
            int? port = null;
            if (doc.RootElement.TryGetProperty("EnableMcpServer", out var en) && en.ValueKind == JsonValueKind.True) enabled = true;
            else if (doc.RootElement.TryGetProperty("EnableMcpServer", out var en2) && en2.ValueKind == JsonValueKind.False) enabled = false;
            if (doc.RootElement.TryGetProperty("McpServerPort", out var p) && p.ValueKind == JsonValueKind.Number) port = p.GetInt32();
            return new McpServerInfo(enabled, port, $"enabled={enabled} port={port}");
        }
        catch (Exception ex)
        {
            return new McpServerInfo(null, null, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally { try { File.Delete(tempCopy); } catch { } }
    }
}
