namespace Terminal;

/// <summary>
/// Installs bundled skill folders into (and removes them from) a CLI-specific
/// project subdirectory (e.g. <c>.claude/skills</c>, <c>.codex/skills</c>,
/// <c>.github/skills</c>). Mirrors the prev/next-diff lifecycle of the MCP
/// configurators: each Save in the modal calls <see cref="InstallAll"/> for
/// newly-selected CLIs and <see cref="RemoveAll"/> for newly-deselected ones.
/// <para>
/// An optional <c>overlaySkillsRoot</c> is copied on top of the primary
/// bundled root after the primary copy. Same-named files inside same-named
/// skill folders win. Used to swap a single skill (e.g. <c>mendix-page-gen</c>)
/// for a platform-specific variant on Mac without forking all 7 packs.
/// </para>
/// </summary>
public sealed class SkillInstaller
{
    private readonly string projectDir;
    private readonly string bundledSkillsRoot;
    private readonly string? overlaySkillsRoot;
    private readonly Logger log;

    public SkillInstaller(string projectDir, string bundledSkillsRoot, Logger log)
        : this(projectDir, bundledSkillsRoot, overlaySkillsRoot: null, log) { }

    public SkillInstaller(string projectDir, string bundledSkillsRoot, string? overlaySkillsRoot, Logger log)
    {
        this.projectDir = projectDir;
        this.bundledSkillsRoot = bundledSkillsRoot;
        this.overlaySkillsRoot = overlaySkillsRoot;
        this.log = log;
    }

    /// <summary>
    /// Copy every bundled skill folder into <paramref name="targetSubdir"/>.
    /// Overwrites existing files inside matching skill folders so a Concord
    /// upgrade refreshes content. If an overlay root is configured, it is
    /// copied on top of the primary set. Idempotent. No-op when the bundled
    /// root is missing.
    /// </summary>
    public void InstallAll(string targetSubdir)
    {
        if (!Directory.Exists(bundledSkillsRoot))
        {
            log.Warn($"[skills] bundled root missing: {bundledSkillsRoot}");
            return;
        }
        var targetRoot = Path.Combine(projectDir, NormalizeSubdir(targetSubdir));
        Directory.CreateDirectory(targetRoot);

        foreach (var srcDir in Directory.EnumerateDirectories(bundledSkillsRoot))
        {
            var name = Path.GetFileName(srcDir);
            var dstDir = Path.Combine(targetRoot, name);
            CopyDirectory(srcDir, dstDir);
            log.Info($"[skills] installed {name} -> {dstDir}");
        }

        if (!string.IsNullOrEmpty(overlaySkillsRoot) && Directory.Exists(overlaySkillsRoot))
        {
            foreach (var srcDir in Directory.EnumerateDirectories(overlaySkillsRoot))
            {
                var name = Path.GetFileName(srcDir);
                var dstDir = Path.Combine(targetRoot, name);
                CopyDirectory(srcDir, dstDir);
                log.Info($"[skills] overlay {name} -> {dstDir}");
            }
        }
    }

    /// <summary>
    /// Remove only the skill folders whose names appear in the bundle. User-
    /// authored sibling folders are left intact. After clean-up, if the skills
    /// directory itself is empty, remove it; same for its <c>.claude</c> /
    /// <c>.codex</c> / <c>.github</c> ancestor — but never delete an ancestor
    /// that has unrelated content (e.g. <c>.github/CODEOWNERS</c>).
    /// </summary>
    public void RemoveAll(string targetSubdir)
    {
        var normalized = NormalizeSubdir(targetSubdir);
        var targetRoot = Path.Combine(projectDir, normalized);
        if (!Directory.Exists(targetRoot)) return;

        var bundledNames = Directory.Exists(bundledSkillsRoot)
            ? new HashSet<string>(
                Directory.EnumerateDirectories(bundledSkillsRoot).Select(Path.GetFileName)!,
                StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.EnumerateDirectories(targetRoot))
        {
            var name = Path.GetFileName(dir);
            if (!bundledNames.Contains(name)) continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                log.Info($"[skills] removed {dir}");
            }
            catch (Exception ex)
            {
                log.Warn($"[skills] failed to remove {dir}: {ex.Message}");
            }
        }

        // Climb the directory tree, pruning empties — but stop before deleting
        // the project dir itself, and never delete a directory that has any
        // remaining content (so e.g. .github/CODEOWNERS keeps .github alive).
        TryPruneEmpty(targetRoot);
        var parent = Path.GetDirectoryName(targetRoot);
        if (!string.IsNullOrEmpty(parent) &&
            !Path.GetFullPath(parent).Equals(Path.GetFullPath(projectDir), StringComparison.OrdinalIgnoreCase))
        {
            TryPruneEmpty(parent);
        }
    }

    private static string NormalizeSubdir(string sub) =>
        sub.Replace('/', Path.DirectorySeparatorChar)
           .Replace('\\', Path.DirectorySeparatorChar);

    private void TryPruneEmpty(string dir)
    {
        try
        {
            if (Directory.Exists(dir) &&
                !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
                log.Info($"[skills] pruned empty {dir}");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"[skills] failed to prune {dir}: {ex.Message}");
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var dst = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, dst, overwrite: true);
        }
        foreach (var sub in Directory.EnumerateDirectories(sourceDir))
        {
            var dst = Path.Combine(destDir, Path.GetFileName(sub));
            CopyDirectory(sub, dst);
        }
    }
}
