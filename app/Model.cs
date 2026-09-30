using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    public Dictionary<string, SourceArchive> Archives { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, SourceFile> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SourceArchive
{
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public sealed class SourceFile
{
    public string Package { get; set; } = "";
    public string Entry { get; set; } = "";
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
        if (relative.StartsWith("BepInEx/plugins/MMHOOK/", StringComparison.OrdinalIgnoreCase) ||
            relative.Equals("BepInEx/plugins/CullFactory/version", StringComparison.OrdinalIgnoreCase)) return false;
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
        if (!Regex.IsMatch(manifest.Tag, @"^v?[0-9A-Za-z][0-9A-Za-z._-]{0,79}$"))
            throw new InvalidDataException("Versão inválida no manifesto.");
        if (manifest.Files.Keys.Any(p => !IsManaged(p)) ||
            manifest.Changed.Any(p => !IsManaged(p) || !manifest.Files.ContainsKey(p)) ||
            manifest.Removed.Any(p => !IsManaged(p) || manifest.Files.ContainsKey(p)))
            throw new InvalidDataException("Manifesto contém caminhos inválidos.");
        if (manifest.Files.Values.Any(h => !IsHash(h)))
            throw new InvalidDataException("Manifesto contém hashes inválidos.");
        if (manifest.Sources.Count == 0)
        {
            if (!IsHash(manifest.FullSha256) || !IsHash(manifest.DeltaSha256))
                throw new InvalidDataException("Manifesto contém hashes inválidos.");
            return;
        }
        if (manifest.Sources.Any(pair => !manifest.Files.ContainsKey(pair.Key) ||
                !IsManaged(pair.Key) || !manifest.Archives.ContainsKey(pair.Value.Package) ||
                !IsSafeZipEntry(pair.Value.Entry)) ||
            manifest.Archives.Any(pair => !IsHash(pair.Value.Sha256) ||
                !Uri.TryCreate(pair.Value.Url, UriKind.Absolute, out var url) ||
                url.Scheme != Uri.UriSchemeHttps ||
                (url.Host != "thunderstore.io" &&
                 !(url.Host == "github.com" && url.AbsolutePath.StartsWith(
                     "/pedroo2006/lethal-company-modpack/releases/download/", StringComparison.Ordinal)))))
            throw new InvalidDataException("Manifesto contém fontes inválidas.");
    }

    private static bool IsHash(string hash) => hash.Length == 64 && hash.All(Uri.IsHexDigit);

    private static bool IsSafeZipEntry(string entry) => entry.Length > 0 && !entry.StartsWith('/') &&
        !entry.Contains('\\') && entry.Split('/').All(part => part.Length > 0 && part != "." && part != ".." && !part.Contains(':'));
}
