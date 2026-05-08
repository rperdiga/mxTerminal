using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class BundledSkillReaderTests : IDisposable
{
    private readonly string tmpDir;

    public BundledSkillReaderTests()
    {
        tmpDir = Path.Combine(Path.GetTempPath(), "skill-reader-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
    }

    public void Dispose() => Directory.Delete(tmpDir, recursive: true);

    [Fact]
    public void Enumerate_NonExistentRoot_ReturnsEmpty()
    {
        var missing = Path.Combine(tmpDir, "nope");
        BundledSkillReader.Enumerate(missing).Should().BeEmpty();
    }

    [Fact]
    public void Enumerate_ParsesNameAndDescriptionFromFrontmatter()
    {
        var skillDir = Path.Combine(tmpDir, "alpha");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name: alpha
            description: First skill for testing.
            ---

            # Alpha
            Body text.
            """);

        var skills = BundledSkillReader.Enumerate(tmpDir);
        skills.Should().HaveCount(1);
        skills[0].Name.Should().Be("alpha");
        skills[0].Description.Should().Be("First skill for testing.");
    }

    [Fact]
    public void Enumerate_FallsBackToFolderNameWhenFrontmatterMissingName()
    {
        var skillDir = Path.Combine(tmpDir, "beta");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            description: No name in frontmatter.
            ---
            Body.
            """);

        var skills = BundledSkillReader.Enumerate(tmpDir);
        skills[0].Name.Should().Be("beta");
        skills[0].Description.Should().Be("No name in frontmatter.");
    }

    [Fact]
    public void Enumerate_SkipsFoldersWithoutSkillMd()
    {
        Directory.CreateDirectory(Path.Combine(tmpDir, "gamma"));
        // gamma has no SKILL.md — it should be ignored.
        var deltaDir = Path.Combine(tmpDir, "delta");
        Directory.CreateDirectory(deltaDir);
        File.WriteAllText(Path.Combine(deltaDir, "SKILL.md"), """
            ---
            name: delta
            description: Has SKILL.md.
            ---
            """);

        var skills = BundledSkillReader.Enumerate(tmpDir);
        skills.Should().HaveCount(1);
        skills[0].Name.Should().Be("delta");
    }

    [Fact]
    public void Enumerate_HandlesMissingFrontmatterGracefully()
    {
        var skillDir = Path.Combine(tmpDir, "epsilon");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "No frontmatter here.\n");

        var skills = BundledSkillReader.Enumerate(tmpDir);
        skills[0].Name.Should().Be("epsilon");
        skills[0].Description.Should().Be("");
    }

    [Fact]
    public void Enumerate_ReturnsSkillsSortedByName()
    {
        foreach (var n in new[] { "zeta", "alpha", "delta" })
        {
            var d = Path.Combine(tmpDir, n);
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "SKILL.md"), $"---\nname: {n}\ndescription: x\n---\n");
        }
        var names = BundledSkillReader.Enumerate(tmpDir).Select(s => s.Name).ToArray();
        names.Should().Equal("alpha", "delta", "zeta");
    }

    [Fact]
    public void Enumerate_StripsLeadingAndTrailingWhitespaceFromValues()
    {
        var skillDir = Path.Combine(tmpDir, "eta");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), """
            ---
            name:    eta
            description:    Padded value.
            ---
            """);
        var s = BundledSkillReader.Enumerate(tmpDir).Single();
        s.Name.Should().Be("eta");
        s.Description.Should().Be("Padded value.");
    }
}
