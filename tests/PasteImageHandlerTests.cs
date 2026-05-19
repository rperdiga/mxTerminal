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
}
