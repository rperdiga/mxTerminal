namespace Terminal;

public sealed record BundledSkillInfo(string Name, string Description);

/// <summary>
/// Enumerates the bundled-skills folder shipped with the extension, parsing
/// each <c>&lt;name&gt;/SKILL.md</c>'s YAML frontmatter for <c>name:</c> and
/// <c>description:</c>. The full SKILL.md body is left to the installer to
/// copy verbatim — we only parse what the settings UI needs to render.
/// </summary>
public static class BundledSkillReader
{
    public static IReadOnlyList<BundledSkillInfo> Enumerate(string skillsRoot)
    {
        if (string.IsNullOrEmpty(skillsRoot) || !Directory.Exists(skillsRoot))
            return Array.Empty<BundledSkillInfo>();

        var skills = new List<BundledSkillInfo>();
        foreach (var dir in Directory.EnumerateDirectories(skillsRoot)
                                     .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
        {
            var skillMd = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillMd)) continue;
            var (frontName, frontDesc) = ReadFrontmatter(skillMd);
            var folderName = Path.GetFileName(dir);
            skills.Add(new BundledSkillInfo(
                Name: frontName ?? folderName,
                Description: frontDesc ?? ""));
        }
        return skills;
    }

    /// <summary>
    /// Tiny YAML-frontmatter parser — we only need <c>name:</c> and
    /// <c>description:</c> on their own line, both string scalars. No quoting,
    /// no folding, no nested maps. Returns (null, null) if the file has no
    /// frontmatter or the keys are absent.
    /// </summary>
    private static (string? Name, string? Description) ReadFrontmatter(string path)
    {
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { return (null, null); }

        if (lines.Length == 0 || lines[0].Trim() != "---") return (null, null);

        string? name = null;
        string? desc = null;
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim() == "---") break;
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var key = line.Substring(0, idx).Trim();
            var value = line.Substring(idx + 1).Trim();
            if (key.Equals("name", StringComparison.OrdinalIgnoreCase) && name is null)
                name = value;
            else if (key.Equals("description", StringComparison.OrdinalIgnoreCase) && desc is null)
                desc = value;
        }
        return (name, desc);
    }
}
