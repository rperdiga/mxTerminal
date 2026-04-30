using FluentAssertions;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class LoggingTests : IDisposable
{
    private readonly string tmpDir = Path.Combine(Path.GetTempPath(), "mxterm-log-" + Guid.NewGuid().ToString("N"));

    public LoggingTests() => Directory.CreateDirectory(tmpDir);
    public void Dispose() => Directory.Delete(tmpDir, recursive: true);

    [Fact]
    public void Info_WritesLineToFile()
    {
        var log = new Logger(tmpDir);
        log.Info("hello");
        var contents = File.ReadAllText(Path.Combine(tmpDir, "resources", "terminal.log"));
        contents.Should().Contain("hello");
        contents.Should().Contain("INFO");
    }

    [Fact]
    public void Error_IncludesException()
    {
        var log = new Logger(tmpDir);
        log.Error("oops", new InvalidOperationException("boom"));
        var contents = File.ReadAllText(Path.Combine(tmpDir, "resources", "terminal.log"));
        contents.Should().Contain("ERROR");
        contents.Should().Contain("oops");
        contents.Should().Contain("InvalidOperationException");
        contents.Should().Contain("boom");
    }

    [Fact]
    public void Clear_TruncatesFile()
    {
        var log = new Logger(tmpDir);
        log.Info("first run");
        log.Clear();
        log.Info("second run");
        var contents = File.ReadAllText(Path.Combine(tmpDir, "resources", "terminal.log"));
        contents.Should().NotContain("first run");
        contents.Should().Contain("second run");
    }

    [Fact]
    public void Logger_FailsSilently_WhenDirectoryCannotBeCreated()
    {
        // Pass an obviously invalid path; expect no exception.
        var log = new Logger("\0invalid\0");
        Action act = () => log.Info("x");
        act.Should().NotThrow();
    }
}
