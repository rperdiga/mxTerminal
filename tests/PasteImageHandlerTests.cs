using System;
using System.IO;
using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class PasteImageHandlerTests : IDisposable
{
    private readonly string baseDir;

    public PasteImageHandlerTests()
    {
        baseDir = Path.Combine(Path.GetTempPath(), "Concord-test-pastes-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(baseDir))
        {
            try { Directory.Delete(baseDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void WriteImage_WritesBytesToTempFile_AndReturnsPath()
    {
        var handler = new PasteImageHandler(baseDir);
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        var path = handler.WriteImage("image/png", bytes, nameHint: null);

        path.Should().StartWith(baseDir);
        File.Exists(path).Should().BeTrue();
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Theory]
    [InlineData("image/png",  ".png")]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/gif",  ".gif")]
    [InlineData("image/webp", ".webp")]
    [InlineData("image/bmp",  ".bmp")]
    [InlineData("image/tiff", ".tiff")]
    [InlineData("IMAGE/PNG",  ".png")]  // case-insensitive
    public void WriteImage_PicksExtensionFromMime(string mime, string expectedExt)
    {
        var handler = new PasteImageHandler(baseDir);
        var path = handler.WriteImage(mime, new byte[] { 1, 2, 3 }, nameHint: null);
        Path.GetExtension(path).Should().Be(expectedExt);
    }

    [Theory]
    [InlineData("image/avif")]
    [InlineData("image/heic")]
    [InlineData("")]
    [InlineData("text/plain")]
    public void WriteImage_FallsBackToPng_OnUnknownOrEmptyMime(string mime)
    {
        var handler = new PasteImageHandler(baseDir);
        var path = handler.WriteImage(mime, new byte[] { 1, 2, 3 }, nameHint: null);
        Path.GetExtension(path).Should().Be(".png");
    }

    [Theory]
    [InlineData(null,                "image")]
    [InlineData("",                  "image")]
    [InlineData("   ",               "image")]
    [InlineData("screenshot.png",    "screenshot")]
    [InlineData("my photo.jpeg",     "my_photo")]
    [InlineData("a/b\\c:d*e?f.png",  "a_b_c_d_e_f")]
    [InlineData("///",               "image")]      // wipes to empty → fallback
    [InlineData(".png",              "image")]      // extension-only → fallback
    public void WriteImage_SanitizesNameHint(string? hint, string expectedStem)
    {
        var handler = new PasteImageHandler(baseDir);
        var path = handler.WriteImage("image/png", new byte[] { 1 }, hint);
        var name = Path.GetFileName(path);
        // Filename is "<stem>-<UTC-yyyyMMddTHHmmssZ>-<guid8>.png".
        // The stem itself may contain dashes via sanitization, so split on "-<year>" to find the timestamp boundary.
        var idxOfTs = name.IndexOf("-20");
        var stem = idxOfTs > 0 ? name.Substring(0, idxOfTs) : name;
        stem.Should().Be(expectedStem);
    }

    [Fact]
    public void WriteImage_CapsLongNameHintAt64Chars()
    {
        var handler = new PasteImageHandler(baseDir);
        var longHint = new string('x', 200) + ".png";
        var path = handler.WriteImage("image/png", new byte[] { 1 }, longHint);
        var name = Path.GetFileName(path);
        var idxOfTs = name.IndexOf("-20");
        var stem = name.Substring(0, idxOfTs);
        stem.Length.Should().BeLessOrEqualTo(64);
    }

    [Fact]
    public void WriteImage_Throws_WhenBytesExceedMaxBytes()
    {
        var handler = new PasteImageHandler(baseDir);
        var oversized = new byte[PasteImageHandler.MaxBytes + 1];
        var act = () => handler.WriteImage("image/png", oversized, nameHint: null);
        act.Should().Throw<ArgumentException>().WithMessage("*too large*");
    }

    [Fact]
    public void WriteImage_FilenameMatchesExpectedFormat()
    {
        var handler = new PasteImageHandler(baseDir);
        var path = handler.WriteImage("image/png", new byte[] { 1 }, "screenshot");
        var name = Path.GetFileName(path);
        // <stem>-<yyyyMMddTHHmmssZ>-<guid8>.<ext>
        name.Should().MatchRegex(@"^screenshot-\d{8}T\d{6}Z-[0-9a-f]{8}\.png$");
    }

    [Fact]
    public void CleanupOlderThan_DeletesOldFiles_LeavesNewerAlone()
    {
        var handler = new PasteImageHandler(baseDir);
        Directory.CreateDirectory(baseDir);

        var oldFile = Path.Combine(baseDir, "old.png");
        var newFile = Path.Combine(baseDir, "new.png");
        File.WriteAllBytes(oldFile, new byte[] { 1 });
        File.WriteAllBytes(newFile, new byte[] { 2 });
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddHours(-25));
        File.SetLastWriteTimeUtc(newFile, DateTime.UtcNow.AddHours(-1));

        var deleted = handler.CleanupOlderThan(TimeSpan.FromHours(24));

        deleted.Should().Be(1);
        File.Exists(oldFile).Should().BeFalse();
        File.Exists(newFile).Should().BeTrue();
    }

    [Fact]
    public void CleanupOlderThan_NoOp_WhenBaseDirMissing()
    {
        var handler = new PasteImageHandler(Path.Combine(baseDir, "does-not-exist"));
        var deleted = handler.CleanupOlderThan(TimeSpan.FromHours(24));
        deleted.Should().Be(0);
    }
}
