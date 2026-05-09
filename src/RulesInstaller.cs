namespace Terminal;

/// <summary>
/// Installs the bundled Concord rules file into a CLI-specific project
/// subdirectory (e.g. <c>.claude/rules</c>). Mirrors the Save lifecycle of
/// <see cref="SkillInstaller"/>: each Save in the modal calls
/// <see cref="InstallAll"/> for newly-selected CLIs and <see cref="RemoveAll"/>
/// for newly-deselected ones.
/// <para>
/// The bundled rules root (typically <c>extensions/Concord/rules/</c>) contains
/// one or more <c>.md</c> files at its top level; <c>concord-build-rules.md</c>
/// is the canonical one. These are refreshed on every Save so a Concord upgrade
/// ships rules updates automatically.
/// </para>
/// <para>
/// A sibling <c>project/</c> subdirectory under the target rules folder is
/// reserved for user-authored project-specific rules. Concord pre-creates it
/// with a one-shot <c>README.md</c> stub on first install, then never touches
/// its contents again — neither on Save (refresh) nor on RemoveAll. The
/// fenced-block manager in <see cref="ClaudeMdManager"/> globs that folder to
/// auto-import its contents into <c>CLAUDE.md</c>.
/// </para>
/// </summary>
public sealed class RulesInstaller
{
    /// <summary>
    /// Name of the user-owned project rules folder. Created once on first
    /// install (with a README stub) and never overwritten thereafter.
    /// </summary>
    public const string ProjectFolderName = "project";

    /// <summary>
    /// Filename of the canonical Concord-managed rules file at the bundled
    /// root. Copied verbatim into the target on every Save.
    /// </summary>
    public const string CanonicalFileName = "concord-build-rules.md";

    /// <summary>
    /// Filename prefix that identifies a top-level <c>.md</c> file as
    /// Concord-managed (i.e. owned by the bundled set). Files matching this
    /// prefix at the rules root that are no longer in the currently-shipped
    /// bundle are removed on <see cref="InstallAll"/> (orphan cleanup so a
    /// release that drops a previously-shipped rules file actually removes
    /// the stale copy from upgraded installs). Files at the rules root that
    /// do <b>not</b> start with this prefix are treated as user-authored
    /// and never touched.
    /// </summary>
    public const string ConcordManagedPrefix = "concord-";

    private readonly string projectDir;
    private readonly string bundledRulesRoot;
    private readonly Logger log;

    public RulesInstaller(string projectDir, string bundledRulesRoot, Logger log)
    {
        this.projectDir = projectDir;
        this.bundledRulesRoot = bundledRulesRoot;
        this.log = log;
    }

    /// <summary>
    /// Copy every <c>.md</c> file at the bundled root into
    /// <paramref name="targetSubdir"/>. Overwrites existing files so a Concord
    /// upgrade refreshes content. Pre-creates the user-owned <c>project/</c>
    /// subfolder with a README stub on first install only — its contents are
    /// never overwritten on subsequent calls. Idempotent. No-op when the
    /// bundled root is missing.
    /// </summary>
    public void InstallAll(string targetSubdir)
    {
        if (!Directory.Exists(bundledRulesRoot))
        {
            log.Warn($"[rules] bundled root missing: {bundledRulesRoot}");
            return;
        }
        var targetRoot = Path.Combine(projectDir, NormalizeSubdir(targetSubdir));
        Directory.CreateDirectory(targetRoot);

        // Copy every .md at the bundled root (typically just concord-build-rules.md).
        var bundledNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var src in Directory.EnumerateFiles(bundledRulesRoot, "*.md", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(src);
            bundledNames.Add(name);
            var dst = Path.Combine(targetRoot, name);
            File.Copy(src, dst, overwrite: true);
            log.Info($"[rules] installed {name} -> {dst}");
        }

        // Orphan cleanup: remove any concord-prefixed top-level .md file in
        // the target that is no longer in the currently-shipped bundle. This
        // lets a future Concord release retire a previously-shipped rules
        // file cleanly on upgraded installs. User-authored files (no
        // concord- prefix) are untouched.
        foreach (var existing in Directory.EnumerateFiles(targetRoot, $"{ConcordManagedPrefix}*.md", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(existing);
            if (bundledNames.Contains(name)) continue;
            try
            {
                File.Delete(existing);
                log.Info($"[rules] removed orphan concord-managed file {existing}");
            }
            catch (Exception ex)
            {
                log.Warn($"[rules] failed to remove orphan {existing}: {ex.Message}");
            }
        }

        // Pre-create project/ folder with README stub on first install only.
        // Once it exists, never touch its contents — it's the user's space.
        var projectFolder = Path.Combine(targetRoot, ProjectFolderName);
        if (!Directory.Exists(projectFolder))
        {
            Directory.CreateDirectory(projectFolder);
            var readme = Path.Combine(projectFolder, "README.md");
            File.WriteAllText(readme, ProjectReadmeStub);
            log.Info($"[rules] seeded user project folder {projectFolder}");
        }
    }

    /// <summary>
    /// Remove only the canonical Concord-managed rules file (and any other
    /// <c>.md</c> at the bundled root). The user-owned <c>project/</c> folder
    /// and any user-authored sibling files are left intact. After clean-up, if
    /// the rules directory itself is empty, remove it; same for its
    /// <c>.claude</c> / <c>.codex</c> / <c>.github</c> ancestor — but never
    /// delete an ancestor that has unrelated content (e.g. <c>.github/CODEOWNERS</c>),
    /// and never delete the <c>project/</c> folder.
    /// </summary>
    public void RemoveAll(string targetSubdir)
    {
        var normalized = NormalizeSubdir(targetSubdir);
        var targetRoot = Path.Combine(projectDir, normalized);
        if (!Directory.Exists(targetRoot)) return;

        var bundledNames = Directory.Exists(bundledRulesRoot)
            ? new HashSet<string>(
                Directory.EnumerateFiles(bundledRulesRoot, "*.md", SearchOption.TopDirectoryOnly)
                         .Select(Path.GetFileName)!,
                StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(targetRoot, "*.md", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (!bundledNames.Contains(name)) continue;
            try
            {
                File.Delete(file);
                log.Info($"[rules] removed {file}");
            }
            catch (Exception ex)
            {
                log.Warn($"[rules] failed to remove {file}: {ex.Message}");
            }
        }

        // Climb the directory tree, pruning empties — but stop before deleting
        // the project dir itself, never delete an ancestor that has remaining
        // content (so e.g. .github/CODEOWNERS keeps .github alive), and never
        // delete the user-owned project/ subfolder under the rules dir.
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
                log.Info($"[rules] pruned empty {dir}");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"[rules] failed to prune {dir}: {ex.Message}");
        }
    }

    private const string ProjectReadmeStub =
        "# Project-specific rules\n" +
        "\n" +
        "Drop additional `.md` files into this folder to extend Concord's always-loaded\n" +
        "rules with content specific to *this* Mendix project — naming conventions,\n" +
        "domain glossary, design-system tokens, integration patterns, anything you'd\n" +
        "want every Claude Code session in this project to load on startup.\n" +
        "\n" +
        "**Concord upgrades never overwrite this folder.** The `concord-build-rules.md`\n" +
        "file at the parent level is refreshed on every Save (and on Concord upgrade);\n" +
        "anything here is yours and stays untouched.\n" +
        "\n" +
        "On every Save, Concord scans this folder for `*.md` files and adds an `@`-import\n" +
        "directive for each into the managed block of your project's `CLAUDE.md`. Just\n" +
        "drop a file here and hit Save — Claude Code will pick it up on the next session.\n" +
        "\n" +
        "Delete this README if you don't want it. Concord won't recreate it.\n";
}
