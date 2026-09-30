using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 4) throw new ArgumentException("Uso: SourceAudit <sources.json> <manifest.json> <cache-dir> <report.json>");
var sources = JsonSerializer.Deserialize<List<Source>>(File.ReadAllText(args[0]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
var manifest = JsonDocument.Parse(File.ReadAllText(args[1])).RootElement;
var byHash = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
foreach (var file in manifest.GetProperty("Files").EnumerateObject())
{
    var hash = file.Value.GetString() ?? "";
    if (!byHash.TryGetValue(hash, out var paths)) byHash[hash] = paths = [];
    paths.Add(file.Name);
}
var cache = Path.GetFullPath(args[2]);
Directory.CreateDirectory(cache);
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("LethalModpackSourceAudit/1.0");
var errors = new ConcurrentBag<string>();
var unique = sources.DistinctBy(s => s.Package).ToList();
var done = 0;
await Parallel.ForEachAsync(unique, new ParallelOptions { MaxDegreeOfParallelism = 4 }, async (source, token) =>
{
    var target = Path.Combine(cache, source.Package + ".zip");
    try
    {
        if (!File.Exists(target))
        {
            var temp = target + ".download";
            using var response = await http.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = File.Create(temp))
                await input.CopyToAsync(output, token);
            using (var archive = ZipFile.OpenRead(temp)) _ = archive.Entries.Count;
            File.Move(temp, target, true);
        }
    }
    catch (Exception ex) { errors.Add(source.Package + ": " + ex.Message); }
    if (Interlocked.Increment(ref done) % 10 == 0) Console.WriteLine($"Baixados/verificados: {done}/{unique.Count}");
});

var matches = new Dictionary<string, List<Match>>(StringComparer.OrdinalIgnoreCase);
var sameName = new Dictionary<string, List<NameMatch>>(StringComparer.OrdinalIgnoreCase);
var localByName = byHash.Values.SelectMany(x => x).GroupBy(path => Path.GetFileName(path)!, StringComparer.OrdinalIgnoreCase)
    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
var archives = new List<Archive>();
foreach (var source in unique)
{
    var zipPath = Path.Combine(cache, source.Package + ".zip");
    if (!File.Exists(zipPath)) continue;
    using var zipStream = File.OpenRead(zipPath);
    var zipHash = Convert.ToHexString(SHA256.HashData(zipStream)).ToLowerInvariant();
    var matched = 0;
    using var zip = ZipFile.OpenRead(zipPath);
    foreach (var entry in zip.Entries)
    {
        if (entry.Name.Length == 0) continue;
        using var content = entry.Open();
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (localByName.TryGetValue(entry.Name, out var pathsWithName))
            foreach (var path in pathsWithName)
            {
                if (!sameName.TryGetValue(path, out var candidates)) sameName[path] = candidates = [];
                candidates.Add(new NameMatch(source.Package, entry.FullName, hash, entry.Length));
            }
        if (!byHash.TryGetValue(hash, out var localPaths)) continue;
        foreach (var path in localPaths)
        {
            if (!matches.TryGetValue(path, out var items)) matches[path] = items = [];
            items.Add(new Match(source.Package, entry.FullName));
            matched++;
        }
    }
    archives.Add(new Archive(source.Package, source.Url, zipHash, matched));
}
var missing = byHash.Values.SelectMany(x => x).Where(path => !matches.ContainsKey(path)).Order().ToList();
var report = new { LocalFiles = byHash.Values.Sum(x => x.Count), MatchedFiles = matches.Count, Archives = archives,
    Missing = missing, Matches = matches,
    SameName = sameName.Where(pair => missing.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
    Errors = errors.ToList() };
File.WriteAllText(args[3], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Arquivos reconhecidos: {matches.Count}/{report.LocalFiles}; pacotes: {archives.Count}/{unique.Count}; falhas: {errors.Count}");

sealed record Source(string Plugin, string Package, string Url);
sealed record Match(string Package, string Entry);
sealed record NameMatch(string Package, string Entry, string Sha256, long Size);
sealed record Archive(string Package, string Url, string Sha256, int MatchedFiles);
