using System.IO.Compression;
using System.Text.Json;

namespace LethalModpackUpdater;

public static class Packager
{
    public static void Build(string[] args)
    {
        if (args.Length is < 3 or > 4)
            throw new ArgumentException("Uso: <pasta-do-jogo> <versão> <pasta-de-saída> [manifest-anterior.json]");
        var root = Path.GetFullPath(args[0]);
        var tag = args[1];
        var output = Path.GetFullPath(args[2]);
        if (tag.Length is < 1 or > 60 || tag.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.' and not '_'))
            throw new ArgumentException("Versão inválida. Exemplo: v1.0.0");
        if (!File.Exists(Path.Combine(root, "Lethal Company.exe"))) throw new DirectoryNotFoundException("Pasta do jogo inválida.");

        ReleaseManifest? previous = null;
        if (args.Length == 4)
        {
            previous = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(args[3]), Data.JsonOptions)
                ?? throw new InvalidDataException("Manifesto anterior inválido.");
            Data.ValidateManifest(previous);
            if (previous.Tag == tag) throw new ArgumentException("A versão deve ser diferente da anterior.");
        }

        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Data.ManagedFolders)
        {
            var dir = Path.Combine(root, "BepInEx", folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            }))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException($"Link não permitido: {file}");
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!Data.IsManaged(relative)) throw new InvalidDataException($"Caminho inválido: {relative}");
                files.Add(relative, Data.HashFile(file));
            }
        }
        if (files.Count == 0) throw new InvalidDataException("Nenhum arquivo de mod encontrado.");

        Directory.CreateDirectory(output);
        var changed = previous is null ? [] : files.Where(kv => !previous.Files.TryGetValue(kv.Key, out var oldHash) || oldHash != kv.Value)
            .Select(kv => kv.Key).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var removed = previous?.Files.Keys.Where(p => !files.ContainsKey(p)).Order(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        WriteZip(Path.Combine(output, "full.zip"), root, files.Keys);
        WriteZip(Path.Combine(output, "delta.zip"), root, changed);
        var manifest = new ReleaseManifest
        {
            Tag = tag,
            PreviousTag = previous?.Tag,
            Files = files,
            Changed = changed,
            Removed = removed,
            FullSha256 = Data.HashFile(Path.Combine(output, "full.zip")),
            DeltaSha256 = Data.HashFile(Path.Combine(output, "delta.zip"))
        };
        Data.ValidateManifest(manifest);
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Data.JsonOptions));
        Console.WriteLine($"{files.Count} arquivos no pacote; {changed.Count} alterados; {removed.Count} removidos.");
    }

    private static void WriteZip(string zipPath, string root, IEnumerable<string> files)
    {
        using var stream = File.Create(zipPath);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var relative in files.Order(StringComparer.OrdinalIgnoreCase))
            zip.CreateEntryFromFile(Data.LocalPath(root, relative), relative, CompressionLevel.Optimal);
    }
}
