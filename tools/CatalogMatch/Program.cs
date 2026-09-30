using System.Text.Json;
using System.Text.RegularExpressions;

if (args.Length != 3) throw new ArgumentException("Uso: CatalogMatch <catalog.json> <BepInEx/LogOutput.log> <saida.tsv>");
var targets = File.ReadLines(args[1])
    .Select(line => Regex.Match(line, @"Loading \[(.+) ([0-9]+(?:\.[0-9]+){1,3})\]"))
    .Where(match => match.Success)
    .Select(match => (Name: match.Groups[1].Value, Version: match.Groups[2].Value))
    .Distinct().ToList();
var versions = targets.Select(t => t.Version).ToHashSet(StringComparer.OrdinalIgnoreCase);
var packages = new List<(string Owner, string Name, string Version, string Url)>();
await using var stream = File.OpenRead(args[0]);
await foreach (var item in JsonSerializer.DeserializeAsyncEnumerable<JsonElement>(stream))
{
    if (!item.TryGetProperty("owner", out var ownerElement) || !item.TryGetProperty("name", out var nameElement) ||
        !item.TryGetProperty("versions", out var versionElements)) continue;
    var owner = ownerElement.GetString() ?? "";
    var name = nameElement.GetString() ?? "";
    foreach (var versionElement in versionElements.EnumerateArray())
    {
        if (!versionElement.TryGetProperty("version_number", out var versionNumber)) continue;
        var version = versionNumber.GetString() ?? "";
        if (!versions.Contains(version)) continue;
        var url = versionElement.TryGetProperty("download_url", out var urlElement) ? urlElement.GetString() ?? "" : "";
        packages.Add((owner, name, version, url));
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
await using var output = new StreamWriter(args[2]);
await output.WriteLineAsync("plugin\tversion\tpackage\tscore\tdownload_url");
foreach (var target in targets)
{
    var candidates = packages.Where(p => p.Version == target.Version)
        .Select(p => (Package: p, Score: Score(target.Name, p.Name)))
        .OrderByDescending(p => p.Score).ThenBy(p => p.Package.Name).Take(5).ToList();
    if (candidates.Count == 0)
        await output.WriteLineAsync($"{target.Name}\t{target.Version}\tNOT_FOUND\t0\t");
    foreach (var candidate in candidates)
        await output.WriteLineAsync($"{target.Name}\t{target.Version}\t{candidate.Package.Owner}-{candidate.Package.Name}\t{candidate.Score:F2}\t{candidate.Package.Url}");
}
Console.WriteLine($"{targets.Count} plugins; {packages.Count} versões candidatas; saída: {args[2]}");

static double Score(string plugin, string package)
{
    var a = Normalize(plugin);
    var b = Normalize(package);
    if (a == b) return 1;
    if (a.Contains(b) || b.Contains(a)) return (double)Math.Min(a.Length, b.Length) / Math.Max(a.Length, b.Length);
    var previous = Enumerable.Range(0, b.Length + 1).ToArray();
    for (var i = 1; i <= a.Length; i++)
    {
        var current = new int[b.Length + 1];
        current[0] = i;
        for (var j = 1; j <= b.Length; j++)
            current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        previous = current;
    }
    return 1 - (double)previous[b.Length] / Math.Max(a.Length, b.Length);
}

static string Normalize(string value) => new(value.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
