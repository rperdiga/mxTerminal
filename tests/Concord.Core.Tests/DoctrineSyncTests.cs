namespace Concord.Core.Tests;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Terminal;
using Terminal.Mcp;
using Xunit;

// No HostServices fakes needed here: these tests only enumerate tool names
// from a catalog and substring-match against on-disk markdown. Bootstrap
// Register() methods populate the catalog without invoking any tool, so
// nothing reaches into HostServices.RunStateProbe / .UiAutomation.
public class DoctrineSyncTests
{
    private static readonly string[] ForbiddenIn10x = new[]
    {
        "mcp__mendix-studio-pro__",
        "maia__",
        "pg_write_page",
    };

    [Fact]
    public void Bundle_10x_does_not_reference_forbidden_tools()
    {
        var repoRoot = RepoRootFinder.Find();
        var bundleFiles = EnumerateMdFiles(Path.Combine(repoRoot, "rules-10x"))
            .Concat(EnumerateMdFiles(Path.Combine(repoRoot, "skills-10x")))
            .ToList();
        bundleFiles.Should().NotBeEmpty("the 10.x bundle should have content after Phase 2");

        foreach (var file in bundleFiles)
        {
            var content = File.ReadAllText(file);
            foreach (var forbidden in ForbiddenIn10x)
            {
                content.Should().NotContain(forbidden,
                    $"10.x bundle file {RelativeTo(repoRoot, file)} must not reference '{forbidden}' — that tool is not available on Studio Pro 10.x–11.9.");
            }
        }
    }

    [Fact]
    public void Bundle_10x_references_every_registered_concord_mcp_tool()
    {
        // Enumerate the 10.x tool surface via the existing bootstrap pattern.
        var catalog = new ToolCatalog(TargetMode.Studio10x);
        UiActionsBootstrap.Register(catalog);
        Concord.Host10x.Spmcp.SpmcpToolBootstrap10x.Register(catalog);
        // Maia is intentionally NOT registered for the 10.x doctrine — even
        // though Host10x may register it at runtime, the bundle excludes it
        // per Joe's "Maia doesn't work well on 10.x" directive.

        var registered = catalog.ListVisibleNames().ToHashSet();
        var skip = new HashSet<string>(); // No 10.x-specific skips at v1.

        var expected = registered.Except(skip).ToList();
        expected.Should().NotBeEmpty();

        var repoRoot = RepoRootFinder.Find();
        var bundleText = string.Concat(
            EnumerateMdFiles(Path.Combine(repoRoot, "rules-10x"))
                .Concat(EnumerateMdFiles(Path.Combine(repoRoot, "skills-10x")))
                .Select(File.ReadAllText));

        // Bare-name substring match (not "mcp__concord-mcp__<name>") is
        // intentional: the §1 catalog listing in rules-10x/concord-build-rules.md
        // enumerates tools by bare name, which satisfies the assertion.
        // Bare names are also unambiguous — no registered concord-mcp tool name
        // is a substring of another.
        var missing = expected.Where(t => !bundleText.Contains(t)).ToList();
        missing.Should().BeEmpty(
            "every concord-mcp tool registered on 10.x must be referenced at least once in rules-10x/ or skills-10x/. " +
            "If a tool is intentionally excluded from the doctrine (e.g. internal/debug), add it to the skip-list in DoctrineSyncTests with a reason. " +
            $"Missing: {string.Join(", ", missing)}");
    }

    private static readonly HashSet<string> Skip11x = new()
    {
        "maia__force_tier", // debug-only; explicitly excluded by existing rules
    };

    [Fact]
    public void Bundle_11x_references_every_studio11x_allowlist_tool()
    {
        var expected = Studio11xAllowlist.All
            .Where(t => !Skip11x.Contains(t))
            .ToList();
        expected.Should().NotBeEmpty();

        var repoRoot = RepoRootFinder.Find();
        var bundleText = string.Concat(
            EnumerateMdFiles(Path.Combine(repoRoot, "rules"))
                .Concat(EnumerateMdFiles(Path.Combine(repoRoot, "skills")))
                .Select(File.ReadAllText));

        var missing = expected.Where(t => !bundleText.Contains(t)).ToList();
        missing.Should().BeEmpty(
            "every tool in Studio11xAllowlist must be referenced at least once in rules/ or skills/. " +
            "If a tool is intentionally excluded from the doctrine, add it to Skip11x in DoctrineSyncTests with a reason. " +
            $"Missing: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Bundle_11x_consistently_uses_the_concord_mcp_prefix_or_omits_it_intentionally()
    {
        // Sanity check: the 11.x bundle should mostly use mcp__concord-mcp__<tool>
        // for clarity, but bare tool names are acceptable in skill snippets and
        // recipe blocks. This test does not enforce prefix consistency — it
        // asserts only that when the concord-mcp prefix IS used, it's spelled
        // correctly (no underscore-separator typos).
        var repoRoot = RepoRootFinder.Find();
        var bundleFiles = EnumerateMdFiles(Path.Combine(repoRoot, "rules"))
            .Concat(EnumerateMdFiles(Path.Combine(repoRoot, "skills")));

        foreach (var file in bundleFiles)
        {
            var content = File.ReadAllText(file);
            content.Should().NotContain("mcp__concord_mcp__",
                $"{RelativeTo(repoRoot, file)} uses underscore-separator instead of dash in the concord-mcp prefix");
        }
    }

    internal static IEnumerable<string> EnumerateMdFiles(string root)
    {
        if (!Directory.Exists(root)) return Enumerable.Empty<string>();
        return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories);
    }

    internal static string RelativeTo(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
}
