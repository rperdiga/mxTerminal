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

    private static string LabelForJson(HashSet<string> clients)
    {
        var parts = new List<string>();
        if (clients.Contains("claude"))  parts.Add("Claude Code");
        if (clients.Contains("copilot")) parts.Add("Copilot CLI");
        return string.Join(" + ", parts);
    }
}
