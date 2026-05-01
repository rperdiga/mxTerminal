using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Terminal;

/// <summary>
/// Reads Studio Pro's user-set theme preference from
/// <c>%LOCALAPPDATA%\Mendix\Settings.sqlite</c>.
/// <para>
/// Schema (verified 2026-05-01):
/// <c>CREATE TABLE ModelerSettings(Version TEXT PRIMARY KEY NOT NULL, Settings TEXT NOT NULL)</c>
/// where <c>Version</c> is the Studio Pro version (e.g. "11.10.0") and <c>Settings</c> is
/// a JSON blob containing a root-level <c>ThemeName</c> integer.
/// Observed mapping: 0 = Light, 1 = Dark.
/// </para>
/// <para>
/// We do this because <c>matchMedia('(prefers-color-scheme: dark)')</c> inside
/// Studio Pro's WebView2 follows the Windows OS app theme, NOT Studio Pro's own
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
    /// Probe Studio Pro's persisted theme. Returns a result that includes a
    /// short diagnostic string the caller can log (success path includes the
    /// resolved value; failure path includes the reason).
    /// </summary>
    /// <param name="studioProVersion">e.g. "11.10.0".</param>
    public static ProbeResult Read(string studioProVersion)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = Path.Combine(local, "Mendix", "Settings.sqlite");
        if (!File.Exists(dbPath))
            return new ProbeResult(null, $"db-not-found at {dbPath}");

        // Studio Pro holds an exclusive lock on Settings.sqlite while running,
        // and Microsoft.Data.Sqlite's ReadOnly mode is NOT enough to bypass it
        // on every Windows configuration. Copy the file to temp first — Mendix
        // settings are tiny (~100 KB), so the copy is free.
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
}
