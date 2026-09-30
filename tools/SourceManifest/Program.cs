using System.Text.Json;
using System.IO.Compression;
using LethalModpackUpdater;

if (args.Length < 4)
    throw new ArgumentException("Uso: SourceManifest <manifesto-local.json> <saida.json> <pendencias.txt> <auditoria1.json> [auditoria2.json ...] [--config-root <jogo>]");
var configOption = Array.IndexOf(args, "--config-root");
if (configOption >= 0 && configOption != args.Length - 2)
    throw new ArgumentException("--config-root deve ser seguido pela pasta do jogo.");
var reportPaths = args.Skip(3).Take((configOption < 0 ? args.Length : configOption) - 3);
var local = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(args[0]), Data.JsonOptions)
    ?? throw new InvalidDataException("Manifesto local inválido.");
var result = new ReleaseManifest { Tag = local.Tag, PreviousTag = local.PreviousTag, Files = local.Files };
foreach (var reportPath in reportPaths)
{
    using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
    var report = document.RootElement;
    foreach (var archive in report.GetProperty("Archives").EnumerateArray())
    {
        var package = archive.GetProperty("Package").GetString()!;
        result.Archives[package] = new SourceArchive
        {
            Url = archive.GetProperty("Url").GetString()!,
            Sha256 = archive.GetProperty("Sha256").GetString()!
        };
    }
    foreach (var file in report.GetProperty("Matches").EnumerateObject())
    {
        if (result.Sources.ContainsKey(file.Name)) continue;
        var match = file.Value[0];
        result.Sources[file.Name] = new SourceFile
        {
            Package = match.GetProperty("Package").GetString()!,
            Entry = match.GetProperty("Entry").GetString()!
        };
    }
}
if (configOption >= 0)
{
    var gameRoot = Path.GetFullPath(args[configOption + 1]);
    var configFiles = result.Files.Keys.Where(path => path.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) && !result.Sources.ContainsKey(path)).Order().ToArray();
    var settingsZip = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "settings.zip");
    var settingsTemp = settingsZip + "." + Guid.NewGuid().ToString("N") + ".tmp";
    using (var zip = ZipFile.Open(settingsTemp, ZipArchiveMode.Create))
        foreach (var relative in configFiles)
        {
            var localPath = Data.LocalPath(gameRoot, relative);
            if (Data.HashFile(localPath) != result.Files[relative])
                throw new InvalidDataException($"Configuração mudou após o inventário: {relative}");
            zip.CreateEntryFromFile(localPath, relative, CompressionLevel.Optimal);
            result.Sources[relative] = new SourceFile { Package = "modpack-settings", Entry = relative };
        }
    File.Move(settingsTemp, settingsZip, true);
    result.Archives["modpack-settings"] = new SourceArchive
    {
        Url = $"https://github.com/pedroo2006/lethal-company-modpack/releases/download/{Uri.EscapeDataString(result.Tag)}/settings.zip",
        Sha256 = Data.HashFile(settingsZip)
    };
}
Data.ValidateManifest(result);
var pending = result.Files.Keys.Where(path => !result.Sources.ContainsKey(path)).Order().ToArray();
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(args[1], JsonSerializer.Serialize(result, Data.JsonOptions));
File.WriteAllLines(args[2], pending);
Console.WriteLine($"Fontes verificadas: {result.Sources.Count}/{result.Files.Count}; pendentes: {pending.Length}");
