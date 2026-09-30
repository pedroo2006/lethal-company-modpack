using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LethalModpackUpdater;

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string Tag { get; set; } = "";
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
    [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = [];
    public GitHubAsset Asset(string name) => Assets.SingleOrDefault(a => a.Name == name)
        ?? throw new InvalidDataException($"A versão {Tag} não tem {name}.");
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
}

public sealed class Updater
{
    private const string PublicApi = "https://api.github.com/repos/pedroo2006/lethal-company-modpack/releases";
    private readonly HttpClient http;
    private readonly Action<string> report;
    private readonly string api;
    private readonly Action<LocalState> saveState;
    private readonly string backupRoot;
    private readonly bool includeTests;

    public Updater(Action<string> report, bool includeTests = false)
        : this(report, CreateClient(), PublicApi, Data.SaveState, Data.BackupRoot, includeTests) { }

    public Updater(Action<string> report, HttpClient http, string api, Action<LocalState> saveState, string backupRoot, bool includeTests = false)
    {
        this.report = report;
        this.http = http;
        this.api = api.TrimEnd('/');
        this.saveState = saveState;
        this.backupRoot = backupRoot;
        this.includeTests = includeTests;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LethalModpackUpdater", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public async Task<string> UpdateAsync(LocalState state)
    {
        var root = Path.GetFullPath(state.GamePath);
        if (!File.Exists(Path.Combine(root, "Lethal Company.exe"))) throw new DirectoryNotFoundException("Selecione a pasta que contém Lethal Company.exe.");
        if (Process.GetProcessesByName("Lethal Company").Length > 0)
            throw new IOException("Feche o jogo antes de atualizar.");

        report("Procurando a última versão aprovada...");
        var latest = await GetLatestAsync();
        if (latest.Tag == state.Tag) return $"A versão {latest.Tag} já está instalada.";

        var steps = new List<(GitHubRelease Release, ReleaseManifest Manifest)>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = latest;
        var forceFull = state.Tag is null;
        while (true)
        {
            if (!visited.Add(current.Tag) || steps.Count > 100) throw new InvalidDataException("Histórico de versões inválido.");
            var manifest = await GetManifestAsync(current);
            if (manifest.Tag != current.Tag) throw new InvalidDataException("A versão do manifesto não corresponde à publicação.");
            steps.Add((current, manifest));
            if (state.Tag is null || manifest.PreviousTag == state.Tag) break;
            if (manifest.PreviousTag is null)
            {
                forceFull = true;
                break;
            }
            current = await GetReleaseAsync("tags/" + Uri.EscapeDataString(manifest.PreviousTag));
        }
        if (forceFull) steps = [steps[0]];
        else steps.Reverse();

        foreach (var (release, manifest) in steps)
        {
            var isFull = forceFull;
            report($"Baixando {manifest.Tag}...");
            await ApplyAsync(root, state, release, manifest, isFull);
            state.Tag = manifest.Tag;
            state.Files = manifest.Files;
            saveState(state);
            forceFull = false;
        }
        return $"Atualizado para {state.Tag}.";
    }

    private async Task<GitHubRelease> GetLatestAsync()
    {
        if (!includeTests) return await GetReleaseAsync("latest");
        using var response = await http.GetAsync(api + "?per_page=30");
        response.EnsureSuccessStatusCode();
        var releases = await response.Content.ReadFromJsonAsync<List<GitHubRelease>>(Data.JsonOptions) ?? [];
        return releases.Where(r => !r.Draft && r.Assets.Any(a => a.Name == "manifest.json") &&
                r.Assets.Any(a => a.Name == "full.zip") && r.Assets.Any(a => a.Name == "delta.zip"))
            .OrderByDescending(r => r.PublishedAt)
            .FirstOrDefault() ?? throw new InvalidOperationException("Ainda não há uma versão de teste publicada.");
    }

    private async Task<GitHubRelease> GetReleaseAsync(string suffix)
    {
        using var response = await http.GetAsync(api + "/" + suffix);
        if ((int)response.StatusCode == 404) throw new InvalidOperationException("Ainda não há uma versão publicada no GitHub Releases.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GitHubRelease>(Data.JsonOptions)
            ?? throw new InvalidDataException("Resposta inválida do GitHub.");
    }

    private async Task<ReleaseManifest> GetManifestAsync(GitHubRelease release)
    {
        var text = await http.GetStringAsync(release.Asset("manifest.json").DownloadUrl);
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(text, Data.JsonOptions)
            ?? throw new InvalidDataException("Manifesto inválido.");
        Data.ValidateManifest(manifest);
        return manifest;
    }

    private async Task ApplyAsync(string root, LocalState state, GitHubRelease release, ReleaseManifest manifest, bool isFull)
    {
        var workspace = Path.Combine(Path.GetTempPath(), "LethalModpackUpdater", Guid.NewGuid().ToString("N"));
        var stage = Path.Combine(workspace, "stage");
        var backup = Path.Combine(backupRoot, manifest.Tag + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(workspace, "package.zip");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(backup);
        var name = isFull ? "full.zip" : "delta.zip";
        var expectedHash = isFull ? manifest.FullSha256 : manifest.DeltaSha256;
        try
        {
            using (var response = await http.GetAsync(release.Asset(name).DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = File.Create(zipPath);
                await input.CopyToAsync(output);
            }
            if (Data.HashFile(zipPath) != expectedHash) throw new InvalidDataException("Download corrompido: hash diferente.");

            var expected = new HashSet<string>(isFull ? manifest.Files.Keys : manifest.Changed, StringComparer.OrdinalIgnoreCase);
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    if (entry.Name.Length == 0) continue;
                    if (!expected.Contains(entry.FullName) || !found.Add(entry.FullName))
                        throw new InvalidDataException($"Arquivo inesperado no pacote: {entry.FullName}");
                    var target = Data.LocalPath(stage, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target);
                    if (Data.HashFile(target) != manifest.Files[entry.FullName])
                        throw new InvalidDataException($"Arquivo corrompido: {entry.FullName}");
                }
                if (!expected.SetEquals(found)) throw new InvalidDataException("Faltam arquivos no pacote.");
            }

            var toRemove = isFull ? EnumerateManagedFiles(root).Where(p => !manifest.Files.ContainsKey(p)).ToList()
                : manifest.Removed;
            var toReplace = isFull ? manifest.Files.Keys.ToList() : manifest.Changed;
            var touched = toRemove.Concat(toReplace).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var relative in touched)
            {
                var destination = Data.LocalPath(root, relative);
                EnsureNoLinks(root, destination);
                if (!File.Exists(destination)) continue;
                var saved = Data.LocalPath(backup, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                File.Copy(destination, saved);
            }

            report($"Instalando {manifest.Tag}...");
            try
            {
                foreach (var relative in toRemove)
                    File.Delete(Data.LocalPath(root, relative));
                foreach (var relative in toReplace)
                {
                    var destination = Data.LocalPath(root, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(Data.LocalPath(stage, relative), destination, true);
                    if (Data.HashFile(destination) != manifest.Files[relative])
                        throw new IOException($"Falha ao verificar arquivo instalado: {relative}");
                }
            }
            catch
            {
                foreach (var relative in touched)
                {
                    var destination = Data.LocalPath(root, relative);
                    var saved = Data.LocalPath(backup, relative);
                    if (File.Exists(saved))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(saved, destination, true);
                    }
                    else File.Delete(destination);
                }
                throw;
            }
        }
        finally
        {
            try { Directory.Delete(workspace, true); } catch { /* Temporary files can be cleared later. */ }
        }
    }

    private static List<string> EnumerateManagedFiles(string root)
    {
        var list = new List<string>();
        foreach (var folder in Data.ManagedFolders)
        {
            var path = Path.Combine(root, "BepInEx", folder);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            }))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (Data.IsManaged(relative)) list.Add(relative);
            }
        }
        return list;
    }

    private static void EnsureNoLinks(string root, string destination)
    {
        var current = Path.GetFullPath(root);
        foreach (var part in Path.GetRelativePath(root, destination).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"Link de pasta não permitido: {current}");
        }
    }
}
