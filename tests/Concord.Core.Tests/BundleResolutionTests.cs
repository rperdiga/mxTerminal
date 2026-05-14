namespace Concord.Core.Tests;

using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

public class BundleResolutionTests
{
    [Fact]
    public void Host10x_resolves_skills_10x_and_rules_10x_at_every_call_site()
    {
        var src = ReadHostSource("Concord.Host10x");

        var skillsCalls = Regex.Matches(src, @"ResolvePath\(\s*""skills(-10x)?""\s*\)");
        skillsCalls.Should().HaveCount(4, "Host10x has 4 call sites that resolve the skills bundle");
        skillsCalls.Should().AllSatisfy(m =>
            NormalizeCall(m.Value).Should().Be("ResolvePath(\"skills-10x\")",
                "Host10x must resolve the 10.x bundle, not the 11.x one"));

        var rulesCalls = Regex.Matches(src, @"ResolvePath\(\s*""rules(-10x)?""\s*\)");
        rulesCalls.Should().HaveCount(4, "Host10x has 4 call sites that resolve the rules bundle");
        rulesCalls.Should().AllSatisfy(m =>
            NormalizeCall(m.Value).Should().Be("ResolvePath(\"rules-10x\")",
                "Host10x must resolve the 10.x bundle, not the 11.x one"));
    }

    [Fact]
    public void Host11x_resolves_bare_skills_and_rules_at_every_call_site()
    {
        var src = ReadHostSource("Concord.Host11x");

        var skillsCalls = Regex.Matches(src, @"ResolvePath\(\s*""skills(-10x)?""\s*\)");
        skillsCalls.Should().HaveCount(4, "Host11x has 4 call sites that resolve the skills bundle");
        skillsCalls.Should().AllSatisfy(m =>
            NormalizeCall(m.Value).Should().Be("ResolvePath(\"skills\")",
                "Host11x must resolve the 11.x bundle, not the 10.x one"));

        var rulesCalls = Regex.Matches(src, @"ResolvePath\(\s*""rules(-10x)?""\s*\)");
        rulesCalls.Should().HaveCount(4, "Host11x has 4 call sites that resolve the rules bundle");
        rulesCalls.Should().AllSatisfy(m =>
            NormalizeCall(m.Value).Should().Be("ResolvePath(\"rules\")",
                "Host11x must resolve the 11.x bundle, not the 10.x one"));
    }

    // Strip whitespace inside ResolvePath(...) so the regex's \s* tolerance
    // doesn't break the equality check downstream.
    private static string NormalizeCall(string match) => Regex.Replace(match, @"\s+", "");

    private static string ReadHostSource(string hostAssemblyName)
    {
        var repoRoot = RepoRootFinder.Find();
        var path = Path.Combine(repoRoot, "src", hostAssemblyName, "Pane", "TerminalPaneExtension.cs");
        File.Exists(path).Should().BeTrue($"expected {path} to exist");
        return File.ReadAllText(path);
    }
}

internal static class RepoRootFinder
{
    /// <summary>Walks up from the test assembly location until CLAUDE.md is found at the directory level.</summary>
    public static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
            dir = dir.Parent;
        if (dir == null) throw new InvalidOperationException("repo root not found (CLAUDE.md sentinel missing)");
        return dir.FullName;
    }
}
