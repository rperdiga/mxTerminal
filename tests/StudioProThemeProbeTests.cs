using System.Runtime.InteropServices;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Terminal;
using Xunit;

namespace Terminal.Tests;

public class StudioProThemeProbeTests
{
    [Fact]
    public void GetSettingsDbPath_ReturnsExpectedPathForCurrentOs()
    {
        var path = StudioProThemeProbe.GetSettingsDbPath();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path.Should().Be(Path.Combine(home, "Library", "Application Support", "Mendix", "Settings.sqlite"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            path.Should().Be(Path.Combine(local, "Mendix", "Settings.sqlite"));
        }
        else
        {
            path.Should().BeNull();
        }
    }

    [Fact]
    public void ReadMcpServerFromDb_MissingFile_ReturnsDbNotFound()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"mxterm-nope-{Guid.NewGuid():N}.sqlite");
        var info = StudioProThemeProbe.ReadMcpServerFromDb(bogus, "11.10.0");
        info.Enabled.Should().BeNull();
        info.Port.Should().BeNull();
        info.Diagnostic.Should().StartWith("db-not-found");
    }

    [Fact]
    public void ReadMcpServerFromDb_RealDb_ReturnsEnabledAndPort()
    {
        var dbPath = CreateTempDb("11.10.0", """{"EnableMcpServer":true,"McpServerPort":7782}""");
        try
        {
            var info = StudioProThemeProbe.ReadMcpServerFromDb(dbPath, "11.10.0");
            info.Enabled.Should().BeTrue();
            info.Port.Should().Be(7782);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void ReadMcpServerFromDb_RowMissing_ReturnsNoRow()
    {
        var dbPath = CreateTempDb("11.10.0", """{"EnableMcpServer":true,"McpServerPort":7782}""");
        try
        {
            var info = StudioProThemeProbe.ReadMcpServerFromDb(dbPath, "99.99.99");
            info.Enabled.Should().BeNull();
            info.Port.Should().BeNull();
            info.Diagnostic.Should().Contain("no-row");
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void ReadFromDb_DarkTheme_Resolves()
    {
        var dbPath = CreateTempDb("11.10.0", """{"ThemeName":1}""");
        try
        {
            var result = StudioProThemeProbe.ReadFromDb(dbPath, "11.10.0");
            result.Theme.Should().Be(StudioProThemeProbe.Theme.Dark);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    [Fact]
    public void ReadFromDb_ThemeMissing_ReturnsKeyMissing()
    {
        var dbPath = CreateTempDb("11.10.0", """{"EnableMcpServer":true}""");
        try
        {
            var result = StudioProThemeProbe.ReadFromDb(dbPath, "11.10.0");
            result.Theme.Should().BeNull();
            result.Diagnostic.Should().Contain("ThemeName-key-missing");
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    private static string CreateTempDb(string version, string settingsJson)
    {
        var path = Path.Combine(Path.GetTempPath(), $"mxterm-test-{Guid.NewGuid():N}.sqlite");
        var connStr = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var conn = new SqliteConnection(connStr);
        conn.Open();
        using (var ddl = conn.CreateCommand())
        {
            ddl.CommandText = "CREATE TABLE ModelerSettings (Version TEXT PRIMARY KEY NOT NULL, Settings TEXT NOT NULL)";
            ddl.ExecuteNonQuery();
        }
        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = "INSERT INTO ModelerSettings(Version, Settings) VALUES ($v, $s)";
            insert.Parameters.AddWithValue("$v", version);
            insert.Parameters.AddWithValue("$s", settingsJson);
            insert.ExecuteNonQuery();
        }
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
