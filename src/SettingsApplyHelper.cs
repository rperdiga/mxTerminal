namespace Terminal;

/// <summary>
/// Encapsulates the apply-on-save chain: writing MCP server entries into
/// <c>.mcp.json</c> / <c>~/.codex/config.toml</c> and installing/uninstalling
/// bundled skill folders into the per-CLI subdirectories. Used by both
/// <see cref="TerminalPaneViewModel"/>'s save handler and
/// <see cref="TerminalPaneExtension"/>'s first-run apply path.
/// </summary>
public static class SettingsApplyHelper
{
    /// <summary>
    /// Apply the diff between <paramref name="prev"/> and <paramref name="next"/>
    /// to the project tree. Returns the list of human-readable "touched"
    /// labels for the result banner.
    /// </summary>
    /// <param name="currentActionServerPort">
    /// Returns the live bound port of the Concord MCP server, or null when
    /// the bridge isn't running. The Concord MCP entry written into
    /// <c>.mcp.json</c> uses this live port (with fallback to
    /// <c>next.McpServerPort</c>).
    /// </param>
    /// <param name="probeStudioProMcpPort">
    /// Returns Studio Pro's actual MCP-server port, probed live from
    /// <c>Settings.sqlite</c>, or null when the probe fails. The
    /// <c>mendix-studio-pro</c> entry written into <c>.mcp.json</c> uses
    /// this port (with fallback to <c>next.McpPort</c>).
    /// </param>
    public static string[] ApplyAll(
        string projectDir,
        string bundledSkillsRoot,
        string bundledRulesRoot,
        TerminalSettings prev,
        TerminalSettings next,
        Logger log,
        Func<int?> currentActionServerPort,
        Func<int?> probeStudioProMcpPort)
    {
        var touched = new List<string>();
        touched.AddRange(ApplyMcpConfig(projectDir, prev, next, log, probeStudioProMcpPort));
        touched.AddRange(ApplyActionsMcpConfig(projectDir, prev, next, log, currentActionServerPort));
        touched.AddRange(ApplySkillsConfig(projectDir, bundledSkillsRoot, prev, next, log));
        touched.AddRange(ApplyRulesConfig(projectDir, bundledRulesRoot, prev, next, log));
        return touched.ToArray();
    }

    /// <summary>
    /// Diff between previous and new MCP settings, written to <c>.mcp.json</c>
    /// (Claude Code + Copilot CLI) and <c>~/.codex/config.toml</c> (Codex).
    /// Mirrors the behavior previously inline on TerminalPaneViewModel.
    /// </summary>
    private static string[] ApplyMcpConfig(
        string projectDir,
        TerminalSettings prev,
        TerminalSettings next,
        Logger log,
        Func<int?> probeStudioProMcpPort)
    {
        var prevClients = new HashSet<string>(prev.McpClients, StringComparer.OrdinalIgnoreCase);
        var nextClients = next.McpEnabled
            ? new HashSet<string>(next.McpClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var jsonNeeded = nextClients.Contains("claude") || nextClients.Contains("copilot");
        var jsonHadIt  = prev.McpEnabled && (prevClients.Contains("claude") || prevClients.Contains("copilot"));
        var tomlNeeded = nextClients.Contains("codex");
        var tomlHadIt  = prev.McpEnabled && prevClients.Contains("codex");

        var probedPort = probeStudioProMcpPort() ?? TerminalSettings.StudioProMcpDefaultPort;
        var url = $"http://localhost:{probedPort}/mcp";
        var json = new McpJsonConfigurator(projectDir);
        var toml = new McpTomlConfigurator();
        var touched = new List<string>();

        log.Info($"[mcp-config] primary diff jsonNeeded={jsonNeeded} jsonHadIt={jsonHadIt} tomlNeeded={tomlNeeded} tomlHadIt={tomlHadIt} url={url}");

        try
        {
            if (jsonNeeded) { json.Upsert(url); log.Info($"[mcp-config-json] upserted {McpJsonConfigurator.ServerName} -> {url}"); touched.Add(LabelForJson(nextClients)); }
            else if (jsonHadIt) { json.Remove(); log.Info($"[mcp-config-json] removed {McpJsonConfigurator.ServerName}"); touched.Add(LabelForJson(prevClients) + " (removed)"); }
        }
        catch (Exception ex) { log.Error("[mcp-config-json] primary write failed", ex); }

        try
        {
            if (tomlNeeded) { toml.Upsert(url); log.Info($"[mcp-config-toml] upserted {McpTomlConfigurator.ServerName} -> {url} at {toml.FilePath}"); touched.Add("Codex"); }
            else if (tomlHadIt) { toml.Remove(); log.Info($"[mcp-config-toml] removed {McpTomlConfigurator.ServerName}"); touched.Add("Codex (removed)"); }
        }
        catch (Exception ex) { log.Error("[mcp-config-toml] primary write failed", ex); }

        return touched.ToArray();
    }

    /// <summary>
    /// Diff for the Concord MCP entry (the in-process action server).
    /// Mirrors the behavior previously inline on TerminalPaneViewModel.
    /// </summary>
    private static string[] ApplyActionsMcpConfig(
        string projectDir,
        TerminalSettings prev,
        TerminalSettings next,
        Logger log,
        Func<int?> currentActionServerPort)
    {
        var prevClients = new HashSet<string>(prev.McpClients, StringComparer.OrdinalIgnoreCase);
        var nextClients = next.McpServerEnabled
            ? new HashSet<string>(next.McpClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var jsonNeeded = nextClients.Contains("claude") || nextClients.Contains("copilot");
        var jsonHadIt  = prev.McpServerEnabled && (prevClients.Contains("claude") || prevClients.Contains("copilot"));
        var tomlNeeded = nextClients.Contains("codex");
        var tomlHadIt  = prev.McpServerEnabled && prevClients.Contains("codex");

        var port = currentActionServerPort() ?? StudioProActionServer.DefaultPort;
        var url = $"http://localhost:{port}/mcp";
        var json = new McpJsonConfigurator(projectDir);
        var toml = new McpTomlConfigurator();
        var touched = new List<string>();

        log.Info($"[mcp-config] actions diff jsonNeeded={jsonNeeded} jsonHadIt={jsonHadIt} tomlNeeded={tomlNeeded} tomlHadIt={tomlHadIt} url={url} live-port={currentActionServerPort()?.ToString() ?? "null"}");

        try
        {
            if (jsonNeeded) { json.UpsertActions(url); log.Info($"[mcp-config-json] upserted {McpJsonConfigurator.ActionsServerName} -> {url}"); touched.Add(LabelForJson(nextClients) + " actions"); }
            else if (jsonHadIt) { json.RemoveActions(); log.Info($"[mcp-config-json] removed {McpJsonConfigurator.ActionsServerName}"); touched.Add(LabelForJson(prevClients) + " actions (removed)"); }
        }
        catch (Exception ex) { log.Error("[mcp-config-json] actions write failed", ex); }

        try
        {
            if (tomlNeeded) { toml.UpsertActions(url); log.Info($"[mcp-config-toml] upserted {McpTomlConfigurator.ActionsServerName} -> {url} at {toml.FilePath}"); touched.Add("Codex actions"); }
            else if (tomlHadIt) { toml.RemoveActions(); log.Info($"[mcp-config-toml] removed {McpTomlConfigurator.ActionsServerName}"); touched.Add("Codex actions (removed)"); }
        }
        catch (Exception ex) { log.Error("[mcp-config-toml] actions write failed", ex); }

        return touched.ToArray();
    }

    /// <summary>
    /// Diff for skill packs: install bundled folders for newly-selected
    /// CLIs, remove for newly-deselected CLIs. Mirrors the behavior
    /// previously inline on TerminalPaneViewModel.
    /// </summary>
    private static string[] ApplySkillsConfig(
        string projectDir,
        string bundledSkillsRoot,
        TerminalSettings prev,
        TerminalSettings next,
        Logger log)
    {
        var prevClients = prev.SkillsEnabled
            ? new HashSet<string>(prev.SkillClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextClients = next.SkillsEnabled
            ? new HashSet<string>(next.SkillClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // On Mac, overlay skills-mac/ on top of skills/ at install time.
        // Lets us swap mendix-page-gen for the no-Maia variant without forking
        // the other 6 packs. The overlay dir sits next to the primary bundled
        // root inside the deployed extension layout (e.g.
        // <ext>/skills/ + <ext>/skills-mac/).
        string? overlayRoot = null;
        if (OperatingSystem.IsMacOS())
        {
            var parent = Path.GetDirectoryName(bundledSkillsRoot);
            if (!string.IsNullOrEmpty(parent))
            {
                var candidate = Path.Combine(parent, "skills-mac");
                if (Directory.Exists(candidate)) overlayRoot = candidate;
            }
        }

        var installer = new SkillInstaller(projectDir, bundledSkillsRoot, overlayRoot, log);
        var touched = new List<string>();

        var perCli = new (string Key, string Label, string Subdir)[]
        {
            ("claude",  "Claude Code skills",       Path.Combine(".claude", "skills")),
            ("copilot", "Copilot CLI skills",       Path.Combine(".github", "skills")),
            ("codex",   "Codex skills",             Path.Combine(".codex",  "skills")),
        };

        log.Info($"[skills] diff prev={{{string.Join(",", prevClients)}}} next={{{string.Join(",", nextClients)}}} bundled-root={bundledSkillsRoot} overlay-root={overlayRoot ?? "<none>"}");

        foreach (var (key, label, subdir) in perCli)
        {
            var was = prevClients.Contains(key);
            var now = nextClients.Contains(key);
            try
            {
                if (now && !was)       { installer.InstallAll(subdir); touched.Add(label); }
                else if (now && was)   { installer.InstallAll(subdir); /* refresh on every save */ }
                else if (!now && was)  { installer.RemoveAll(subdir);  touched.Add(label + " (removed)"); }
            }
            catch (Exception ex)
            {
                log.Error($"[skills] {label} apply failed", ex);
            }
        }

        return touched.ToArray();
    }

    /// <summary>
    /// Diff for rules: install bundled <c>concord-build-rules.md</c> and
    /// manage the <c>CLAUDE.md</c> fenced block for newly-selected CLIs;
    /// remove on newly-deselected CLIs. Tracks the same enable+per-CLI
    /// toggle as skills (<see cref="TerminalSettings.SkillsEnabled"/> +
    /// <see cref="TerminalSettings.SkillClients"/>) since rules are
    /// conceptually part of the skill-pack contract.
    /// <para>
    /// Phase 1: Claude only. Codex (<c>AGENTS.md</c>) and Copilot CLI
    /// (<c>.github/copilot-instructions.md</c>) follow the same fenced-block
    /// pattern in their respective files; they are wired here as no-ops with
    /// TODO markers and will light up in a follow-up phase once Concord has
    /// validated the Claude path on real builds.
    /// </para>
    /// </summary>
    private static string[] ApplyRulesConfig(
        string projectDir,
        string bundledRulesRoot,
        TerminalSettings prev,
        TerminalSettings next,
        Logger log)
    {
        var prevClients = prev.SkillsEnabled
            ? new HashSet<string>(prev.SkillClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextClients = next.SkillsEnabled
            ? new HashSet<string>(next.SkillClients, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var touched = new List<string>();
        log.Info($"[rules] diff prev={{{string.Join(",", prevClients)}}} next={{{string.Join(",", nextClients)}}} bundled-root={bundledRulesRoot}");

        // Claude — install rules + manage CLAUDE.md fenced block.
        {
            var key = "claude";
            var label = "Claude Code rules";
            var rulesSubdir = Path.Combine(".claude", "rules");
            var was = prevClients.Contains(key);
            var now = nextClients.Contains(key);
            try
            {
                var installer = new RulesInstaller(projectDir, bundledRulesRoot, log);
                var manager = new ClaudeMdManager(projectDir, rulesSubdir, log);
                if (now && !was)      { installer.InstallAll(rulesSubdir); manager.Apply(); touched.Add(label); }
                else if (now && was)  { installer.InstallAll(rulesSubdir); manager.Apply(); /* refresh on every save */ }
                else if (!now && was) { installer.RemoveAll(rulesSubdir);  manager.Remove();  touched.Add(label + " (removed)"); }
            }
            catch (Exception ex)
            {
                log.Error($"[rules] {label} apply failed", ex);
            }
        }

        // TODO Phase 2: Codex (AGENTS.md + .codex/rules/), Copilot CLI
        // (.github/copilot-instructions.md + .github/skills/<x>/rules.md or
        // a parallel folder). Same fenced-block pattern; same lifecycle. The
        // RulesInstaller class works against any rules-subdir target — only
        // the per-CLI manager (file path + import-directive syntax) varies.

        return touched.ToArray();
    }

    private static string LabelForJson(HashSet<string> clients)
    {
        var parts = new List<string>();
        if (clients.Contains("claude"))  parts.Add("Claude Code");
        if (clients.Contains("copilot")) parts.Add("Copilot CLI");
        return string.Join(" + ", parts);
    }
}
