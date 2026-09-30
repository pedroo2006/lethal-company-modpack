using System.Security.Cryptography;
using System.Text.Json;

namespace LethalModpackUpdater;

public sealed class ReleaseManifest
{
    public string Tag { get; set; } = "";
    public string? PreviousTag { get; set; }
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Changed { get; set; } = [];
    public List<string> Removed { get; set; } = [];
    public string FullSha256 { get; set; } = "";
    public string DeltaSha256 { get; set; } = "";
}

public sealed class LocalState
{
    public string GamePath { get; set; } = "";
    public string? Tag { get; set; }
    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class Data
{
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LethalModpackUpdater", "state.json");
    public static readonly string BackupRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LethalModpackUpdater", "backups");
    public static readonly string[] ManagedFolders = ["plugins", "patchers", "core", "config"];

    public static LocalState LoadState() => File.Exists(StatePath)
        ? JsonSerializer.Deserialize<LocalState>(File.ReadAllText(StatePath), JsonOptions) ?? new()
        : new();

    public static void SaveState(LocalState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        var temp = StatePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temp, StatePath, true);
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static bool IsManaged(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\')) return false;
        var parts = relative.Split('/');
        return parts.Length >= 3 && parts[0] == "BepInEx" &&
            ManagedFolders.Contains(parts[1], StringComparer.OrdinalIgnoreCase) &&
            parts.Skip(2).All(p => p.Length > 0 && p != "." && p != ".." && !p.Contains(':'));
    }

    public static string LocalPath(string root, string relative)
    {
        if (!IsManaged(relative)) throw new InvalidDataException($"Caminho inválido: {relative}");
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var basePath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Caminho fora do jogo.");
        return path;
    }

    public static void ValidateManifest(ReleaseManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Tag) || manifest.Files.Count == 0) throw new InvalidDataException("Manifesto vazio.");
        if (manifest.Files.Keys.Any(p => !IsManaged(p)) ||
            manifest.Changed.Any(p => !IsManaged(p) || !manifest.Files.ContainsKey(p)) ||
            manifest.Removed.Any(p => !IsManaged(p) || manifest.Files.ContainsKey(p)))
            throw new InvalidDataException("Manifesto contém caminhos inválidos.");
        if (manifest.Files.Values.Any(h => h.Length != 64 || !h.All(Uri.IsHexDigit)) ||
            manifest.FullSha256.Length != 64 || manifest.DeltaSha256.Length != 64)
            throw new InvalidDataException("Manifesto contém hashes inválidos.");
    }
}
