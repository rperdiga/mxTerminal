using System.Text.Json;

namespace Terminal;

/// <summary>
/// Persistent per-project tab state. Distinct from <see cref="TerminalSettings"/>
/// because tabs are user-state (they survive Studio Pro restart) rather than
/// configuration. Stored next to the settings file so they share a backup
/// boundary with the project.
/// </summary>
public sealed record TerminalState(
    IReadOnlyList<TerminalState.Tab> Tabs,
    int? ActiveTabOrdinal)
{
    public sealed record Tab(
        string Title,
        string ShellPath,
        string[] Args,
        int Ordinal);

    public static TerminalState Empty() =>
        new(Array.Empty<Tab>(), null);

    private const string FileName = "terminal-state.json";
    private const string SubDir   = "resources";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private static string PathFor(string projectDir) =>
        System.IO.Path.Combine(projectDir, SubDir, FileName);

    public static TerminalState Load(string projectDir)
    {
        var path = PathFor(projectDir);
        if (!File.Exists(path)) return Empty();
        try
        {
            using var stream = File.OpenRead(path);
            var dto = JsonSerializer.Deserialize<TerminalState>(stream, Json);
            if (dto is null) return Empty();
            // Migrate per-tab shell paths so a project moved across OSes (Windows
            // ↔ Mac) doesn't fail every tab restore with posix_spawnp(cmd.exe).
            var migrated = dto.Tabs
                .Select(t => t with { ShellPath = TerminalSettings.MigrateShellPathForPlatform(t.ShellPath) })
                .ToList();
            return dto with { Tabs = migrated };
        }
        catch (JsonException) { return Empty(); }
        catch (IOException)   { return Empty(); }
    }

    public void Save(string projectDir)
    {
        var dir = System.IO.Path.Combine(projectDir, SubDir);
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, FileName);
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, path, overwrite: true);
        }
        catch (IOException)
        {
            // Best-effort. State persistence failure shouldn't crash the pane.
        }
    }
}
