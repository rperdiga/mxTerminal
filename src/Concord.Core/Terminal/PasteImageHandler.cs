using System;
using System.IO;

namespace Terminal;

/// <summary>
/// Writes raw image bytes from a clipboard paste to a uniquely-named temp
/// file and returns the absolute path. The path is then injected into the
/// PTY by the caller in place of the raw bytes, so the receiving CLI sees
/// a file path (which Claude Code / Codex / Copilot CLI auto-recognize as
/// an image attachment).
///
/// Files live under &lt;TempPath&gt;/Concord/pastes/ and are swept by
/// <see cref="CleanupOlderThan"/> on extension startup (24 h retention).
/// </summary>
public sealed class PasteImageHandler
{
    public const long MaxBytes = 25L * 1024 * 1024;  // 25 MB

    private readonly string baseDir;

    public PasteImageHandler(string? baseDirOverride = null)
    {
        baseDir = baseDirOverride ?? Path.Combine(Path.GetTempPath(), "Concord", "pastes");
    }

    /// <summary>
    /// Writes <paramref name="bytes"/> to a new file under the configured
    /// temp dir and returns the absolute path. Throws if the byte count
    /// exceeds <see cref="MaxBytes"/> (defense in depth — the JS side
    /// rejects oversized pastes before the bridge call).
    /// </summary>
    public string WriteImage(string mime, byte[] bytes, string? nameHint)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));
        if (bytes.LongLength > MaxBytes)
            throw new ArgumentException($"Image too large: {bytes.LongLength} bytes (max {MaxBytes}).", nameof(bytes));

        Directory.CreateDirectory(baseDir);

        var stem = SanitizeNameHint(nameHint);
        var ext = MimeToExtension(mime);
        var ts = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var guid8 = Guid.NewGuid().ToString("N").Substring(0, 8);
        var fileName = $"{stem}-{ts}-{guid8}{ext}";
        var path = Path.Combine(baseDir, fileName);

        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// Delete files in the base dir older than <paramref name="age"/>.
    /// Best-effort: any IO error on an individual file is swallowed so a
    /// single permission/lock issue can't break cleanup of the rest.
    /// Returns the count of files deleted. No-op if base dir doesn't exist.
    /// </summary>
    public int CleanupOlderThan(TimeSpan age)
    {
        if (!Directory.Exists(baseDir)) return 0;
        var cutoff = DateTime.UtcNow - age;
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(baseDir))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch { /* best-effort */ }
        }
        return deleted;
    }

    private static string SanitizeNameHint(string? hint)
    {
        if (string.IsNullOrWhiteSpace(hint)) return "image";
        var s = hint.Trim();
        if (s.Length > 64) s = s.Substring(0, 64);
        var dot = s.LastIndexOf('.');
        if (dot >= 0) s = s.Substring(0, dot);   // NOTE: >= 0 (fixed from plan's `> 0` so ".png" wipes to empty)
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' ||
                c == '"' || c == '<' || c == '>' || c == '|' ||
                c < 0x20 || c == 0x7F || char.IsWhiteSpace(c))
            {
                chars[i] = '_';
            }
        }
        s = new string(chars);
        // Collapse runs of underscore and trim leading/trailing.
        while (s.Contains("__")) s = s.Replace("__", "_");
        s = s.Trim('_');
        return string.IsNullOrEmpty(s) ? "image" : s;
    }

    private static string MimeToExtension(string mime)
    {
        return (mime ?? "").ToLowerInvariant() switch
        {
            "image/png"  => ".png",
            "image/jpeg" => ".jpg",
            "image/gif"  => ".gif",
            "image/webp" => ".webp",
            "image/bmp"  => ".bmp",
            "image/tiff" => ".tiff",
            _            => ".png",
        };
    }
}
