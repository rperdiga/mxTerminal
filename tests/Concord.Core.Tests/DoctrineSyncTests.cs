namespace Concord.Core.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Terminal;
using Terminal.Interop;
using Terminal.Maia;
using Terminal.Mcp;
using Xunit;

[Collection("HostServices")]
public class DoctrineSyncTests : IDisposable
{
    private sealed class FakeProbe : IRunStateProbe
    {
        public string? GetActiveUrl() => null;
        public int? GetActivePort() => null;
        public Task<RunState> IsRunningAsync(CancellationToken ct = default)
            => Task.FromResult(RunState.Stopped);
    }
    private sealed class FakeUi : IStudioProUiAutomation
    {
        public bool TriggerRun() => false;
        public bool TriggerStop() => false;
        public bool TriggerRefreshFromDisk() => false;
        public bool TriggerSaveAll() => false;
        public string? LastFailureReason => null;
    }

    public DoctrineSyncTests()
    {
        HostServices.Reset();
        HostServices.SetRunStateProbe(new FakeProbe());
        HostServices.SetUiAutomation(new FakeUi());
    }

    public void Dispose()
    {
        HostServices.Reset();
        ToolCatalogRegistry.Active = null;
    }

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
        // SpmcpToolBootstrap10x lives in Concord.Host10x.
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

        var missing = expected.Where(t => !bundleText.Contains(t)).ToList();
        missing.Should().BeEmpty(
            "every concord-mcp tool registered on 10.x must be referenced at least once in rules-10x/ or skills-10x/. " +
            "If a tool is intentionally excluded from the doctrine (e.g. internal/debug), add it to the skip-list in DoctrineSyncTests with a reason. " +
            $"Missing: {string.Join(", ", missing)}");
    }

    internal static IEnumerable<string> EnumerateMdFiles(string root)
    {
        if (!Directory.Exists(root)) return Enumerable.Empty<string>();
        return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories);
    }

    internal static string RelativeTo(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
}
