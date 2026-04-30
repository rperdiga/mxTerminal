using System.Text.Json;

namespace MxStudioProTerminal;

public sealed record TerminalSettings(
    string ShellPath,
    string[] Args,
    int RingBufferKB,
    int XtermScrollbackLines,
    string Theme)
{
    public static TerminalSettings Defaults() => new(
        ShellPath: "powershell.exe",
        Args: Array.Empty<string>(),
        RingBufferKB: 4096,
        XtermScrollbackLines: 10000,
        Theme: "dark");

    private const string FileName = "terminal-settings.json";
    private const string SubDir = "resources";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static string PathFor(string projectDir) =>
        System.IO.Path.Combine(projectDir, SubDir, FileName);

    public static TerminalSettings Load(string projectDir)
    {
        var path = PathFor(projectDir);
        if (!File.Exists(path)) return Defaults();
        try
        {
            using var stream = File.OpenRead(path);
            var dto = JsonSerializer.Deserialize<Dto>(stream, Json);
            if (dto is null) return Defaults();
            var def = Defaults();
            return new TerminalSettings(
                ShellPath: dto.ShellPath ?? def.ShellPath,
                Args: dto.Args ?? def.Args,
                RingBufferKB: dto.RingBufferKB ?? def.RingBufferKB,
                XtermScrollbackLines: dto.XtermScrollbackLines ?? def.XtermScrollbackLines,
                Theme: dto.Theme ?? def.Theme);
        }
        catch (JsonException)
        {
            return Defaults();
        }
    }

    public void Save(string projectDir)
    {
        var dir = System.IO.Path.Combine(projectDir, SubDir);
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, FileName);
        var dto = new Dto(ShellPath, Args, RingBufferKB, XtermScrollbackLines, Theme);
        File.WriteAllText(path, JsonSerializer.Serialize(dto, Json));
    }

    private sealed record Dto(
        string? ShellPath,
        string[]? Args,
        int? RingBufferKB,
        int? XtermScrollbackLines,
        string? Theme);
}
