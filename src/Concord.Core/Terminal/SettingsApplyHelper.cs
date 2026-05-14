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
    /// <param name="probeStudioProMcpAvailable">
    /// Returns whether the running Studio Pro version exposes the built-in
    /// <c>mendix-studio-pro</c> MCP server at all (requires 11.10+). When
    /// false, <see cref="ApplyMcpConfig"/> skips the upsert path AND removes
    /// any stale <c>mendix-studio-pro</c> entry from <c>.mcp.json</c> /
    /// <c>~/.codex/config.toml</c> — covers the cross-version migration case
    /// where a project last opened on 11.10+ is now opened on 10.x or 11.6–11.9.
    /// </param>
    public static string[] ApplyAll(
        string projectDir,
        string bundledSkillsRoot,
        string bundledRulesRoot,
        TerminalSettings prev,
        TerminalSettings next,
        Logger log,
        Func<int?> currentActionServerPort,
        Func<int?> probeStudioProMcpPort,
        Func<bool> probeStudioProMcpAvailable)
    {
        var touched = new List<string>();
        touched.AddRange(ApplyMcpConfig(projectDir, prev, next, log, probeStudioProMcpPort, probeStudioProMcpAvailable));
        touched.AddRange(ApplyActionsMcpConfig(projectDir, prev, next, log, currentActionServerPort));
        touched.AddRange(ApplySkillsConfig(projectDir, bundledSkillsRoot, prev, next, log));
        touched.AddRange(ApplyRulesConfig(projectDir, bundledRulesRoot, prev, next, log));
        return touched.ToArray();
    }

    /// <summary>
    /// Diff between previous and new MCP settings, written to <c>.mcp.json</c>
    /// (Claude Code + Copilot CLI) and <c>~/.codex/config.toml</c> (Codex).
    /// Mirrors the behavior previously inline on TerminalPaneViewModel.
    /// <para>
    /// When <paramref name="probeStudioProMcpAvailable"/> returns false (Studio
    /// Pro version &lt; 11.10), the upsert path is skipped AND the remove path
    /// is forced — even if neither prev nor next claims the entry was wired.
    /// This cleans up stale <c>mendix-studio-pro</c> entries left behind when
    /// a project last opened on 11.10+ is now opened on 10.x or 11.6–11.9.
    /// </para>
    /// </summary>
    private static string[] ApplyMcpConfig(
        string projectDir,
        TerminalSettings prev,
        TerminalSettings next,
        Logger log,
        Func<int?> probeStudioProMcpPort,
        Func<bool> probeStudioProMcpAvailable)
    {
        var available = probeStudioProMcpAvailable();

        var prevClients = new HashSet<string>(prev.McpClients, StringComparer.OrdinalIgnoreCase);
        // When the feature isn't available, treat `next` as MCP-off regardless
        // of saved settings — Concord must not advertise a non-existent server.
        var nextClients = (available && next.McpEnabled)
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

        log.Info($"[mcp-config] primary diff available={available} jsonNeeded={jsonNeeded} jsonHadIt={jsonHadIt} tomlNeeded={tomlNeeded} tomlHadIt={tomlHadIt} url={url}");

        try
        {
            if (jsonNeeded) { json.Upsert(url); log.Info($"[mcp-config-json] upserted {McpJsonConfigurator.ServerName} -> {url}"); touched.Add(LabelForJson(nextClients)); }
            else if (jsonHadIt) { json.Remove(); log.Info($"[mcp-config-json] removed {McpJsonConfigurator.ServerName}"); touched.Add(LabelForJson(prevClients) + " (removed)"); }
            else if (!available)
            {
                // Forced cleanup: project may have a stale mendix-studio-pro
                // entry from a prior 11.10+ open. Remove is idempotent — a
                // no-op when the entry isn't present.
                json.Remove();
                log.Info($"[mcp-config-json] forced-cleanup {McpJsonConfigurator.ServerName} (Studio Pro version doesn't expose this MCP)");
            }
        }
        catch (Exception ex) { log.Error("[mcp-config-json] primary write failed", ex); }

        try
        {
            if (tomlNeeded) { toml.Upsert(url); log.Info($"[mcp-config-toml] upserted {McpTomlConfigurator.ServerName} -> {url} at {toml.FilePath}"); touched.Add("Codex"); }
            else if (tomlHadIt) { toml.Remove(); log.Info($"[mcp-config-toml] removed {McpTomlConfigurator.ServerName}"); touched.Add("Codex (removed)"); }
            else if (!available)
            {
                toml.Remove();
                log.Info($"[mcp-config-toml] forced-cleanup {McpTomlConfigurator.ServerName} (Studio Pro version doesn't expose this MCP)");
            }
        }
        catch (Exception ex) { log.Error("[mcp-config-toml] primary write failed", ex); }

        // v4.2.2: when Codex is wired for this project, stamp it as
        // suppress-migration-prompt. Idempotent — no-ops when the stamp is
        // already future-dated. See McpTomlConfigurator.SuppressMigrationPromptForProject.
        //
        // Intentionally fires once per apply (here, in ApplyMcpConfig) — NOT
        // also from ApplyActionsMcpConfig. Suppression is per-project
        // (not per-server), so the same TOML entry would be written twice
        // for no observable effect. Single-fire keeps the log line count
        // sensible and avoids double-applying the idempotency check.
        if (tomlNeeded)
        {
            try
            {
                toml.SuppressMigrationPromptForProject(projectDir);
                log.Info($"[mcp-config-toml] stamped migration-prompt suppression for project {projectDir}");
            }
            catch (Exception ex) { log.Error("[mcp-config-toml] migration-prompt suppression failed", ex); }
        }

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
    /// Diff for rules: install bundled Concord rules and manage the
    /// per-CLI fenced block for newly-selected CLIs; remove on newly-
    /// deselected CLIs. Tracks the same enable+per-CLI toggle as skills
    /// (<see cref="TerminalSettings.SkillsEnabled"/> +
    /// <see cref="TerminalSettings.SkillClients"/>) since rules are
    /// conceptually part of the skill-pack contract.
    /// <para>
    /// v4.2.1 lights up Codex + Copilot CLI alongside Claude. All three
    /// CLIs use the same fenced-block pattern with identical content — only
    /// the destination markdown file and the rules subdirectory differ:
    /// </para>
    /// <list type="bullet">
    /// <item><c>claude</c>  → <c>.claude/rules/</c> + <c>CLAUDE.md</c></item>
    /// <item><c>codex</c>   → <c>.codex/rules/</c>  + <c>AGENTS.md</c></item>
    /// <item><c>copilot</c> → <c>.github/rules/</c> + <c>.github/copilot-instructions.md</c></item>
    /// </list>
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

        var perCli = new (string Key, string Label, string RulesSubdir, Func<string, string, Logger, ClaudeMdManager> ManagerFactory)[]
        {
            ("claude",  "Claude Code rules", Path.Combine(".claude", "rules"),
                (proj, sub, l) => new ClaudeMdManager(proj, sub, l)),
            ("codex",   "Codex rules",       Path.Combine(".codex",  "rules"),
                (proj, sub, l) => new AgentsMdManager(proj, sub, l)),
            ("copilot", "Copilot CLI rules", Path.Combine(".github", "rules"),
                (proj, sub, l) => new CopilotInstructionsManager(proj, sub, l)),
        };

        foreach (var (key, label, rulesSubdir, factory) in perCli)
        {
            var was = prevClients.Contains(key);
            var now = nextClients.Contains(key);
            try
            {
                var installer = new RulesInstaller(projectDir, bundledRulesRoot, log);
                var manager = factory(projectDir, rulesSubdir, log);
                if (now && !was)      { installer.InstallAll(rulesSubdir); manager.Apply(); touched.Add(label); }
                else if (now && was)  { installer.InstallAll(rulesSubdir); manager.Apply(); /* refresh on every save */ }
                else if (!now && was) { installer.RemoveAll(rulesSubdir);  manager.Remove();  touched.Add(label + " (removed)"); }
            }
            catch (Exception ex)
            {
                log.Error($"[rules] {label} apply failed", ex);
            }
        }

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
