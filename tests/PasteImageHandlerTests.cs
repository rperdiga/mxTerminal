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
}
