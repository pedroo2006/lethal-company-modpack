using System.Net;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LethalModpackUpdater;

if (args.Length == 2)
{
    var draft = JsonSerializer.Deserialize<ReleaseManifest>(File.ReadAllText(args[0]), Data.JsonOptions)
        ?? throw new InvalidDataException("Manifesto preliminar inválido.");
    using var draftClient = new HttpClient(new FakeHandler(request => request.RequestUri!.AbsolutePath switch
    {
        "/releases/latest" => Json(new { tag_name = draft.Tag, assets = Assets(draft.Tag) }),
        var path when path == $"/assets/{draft.Tag}/manifest.json" => Json(draft),
        var path => throw new InvalidOperationException("Download inesperado: " + path)
    }));
    var draftState = new LocalState { GamePath = args[1] };
    var draftUpdater = new Updater(Console.WriteLine, draftClient, "https://test.local/releases", _ => { },
        Path.Combine(Path.GetTempPath(), "LethalModpackDraftBackups"));
    var result = await draftUpdater.UpdateAsync(draftState);
    Check(draftState.Tag == draft.Tag, "Instalação local não corresponde ao manifesto preliminar.");
    Console.WriteLine(result);
    return;
}

var testRoot = Path.Combine(Path.GetTempPath(), "LethalModpackSmoke", Guid.NewGuid().ToString("N"));
var fixtures = Path.Combine(testRoot, "fixtures");
var source = Path.Combine(testRoot, "source");
Directory.CreateDirectory(Path.Combine(source, "BepInEx", "plugins"));
Directory.CreateDirectory(Path.Combine(source, "BepInEx", "config"));
File.WriteAllText(Path.Combine(source, "Lethal Company.exe"), "");
File.WriteAllText(Path.Combine(source, "BepInEx", "plugins", "A.dll"), "a1");
File.WriteAllText(Path.Combine(source, "BepInEx", "plugins", "B.dll"), "b1");
Packager.Build([source, "v1", Path.Combine(fixtures, "v1")]);
File.WriteAllText(Path.Combine(source, "BepInEx", "plugins", "A.dll"), "a2");
File.Delete(Path.Combine(source, "BepInEx", "plugins", "B.dll"));
File.WriteAllText(Path.Combine(source, "BepInEx", "config", "C.cfg"), "c1");
Packager.Build([source, "v2", Path.Combine(fixtures, "v2"), Path.Combine(fixtures, "v1", "manifest.json")]);
var game = Path.Combine(testRoot, "game");
Directory.CreateDirectory(Path.Combine(game, "BepInEx", "plugins"));
File.WriteAllText(Path.Combine(game, "Lethal Company.exe"), "");
File.WriteAllText(Path.Combine(game, "BepInEx", "plugins", "Old.dll"), "old");
var latest = "v1";
var corruptDelta = false;
var fullZipRequests = 0;
using var client = new HttpClient(new FakeHandler(request =>
{
    var path = request.RequestUri!.AbsolutePath;
    if (path == "/releases/latest") return Json(new
    {
        tag_name = latest,
        assets = Assets(latest)
    });
    if (path == "/releases") return Json(new[]
    {
        new { tag_name = "v2", draft = false, prerelease = true,
            published_at = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero), assets = Assets("v2") },
        new { tag_name = "v1", draft = false, prerelease = false,
            published_at = new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero), assets = Assets("v1") }
    });
    if (path.StartsWith("/releases/tags/"))
    {
        var tag = path.Split('/').Last();
        return Json(new { tag_name = tag, assets = Assets(tag) });
    }
    if (path.StartsWith("/assets/"))
    {
        var parts = path.Split('/');
        if (parts[3] == "full.zip") fullZipRequests++;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(corruptDelta && parts[2] == "v2" && parts[3] == "delta.zip"
                ? [1, 2, 3]
                : File.ReadAllBytes(Path.Combine(fixtures, parts[2], parts[3])))
        };
    }
    throw new InvalidOperationException(path);
}));
var state = new LocalState { GamePath = game };
var updater = new Updater(Console.WriteLine, client, "https://test.local/releases", _ => { }, Path.Combine(testRoot, "backups"));
await updater.UpdateAsync(state);
Check(state.Tag == "v1", "Versão inicial não instalada.");
Check(!File.Exists(Path.Combine(game, "BepInEx", "plugins", "Old.dll")), "Arquivo extra não removido.");
Check(File.Exists(Path.Combine(game, "BepInEx", "plugins", "B.dll")), "B.dll ausente na versão inicial.");

var adoptGame = Path.Combine(testRoot, "existing-game");
Directory.CreateDirectory(Path.Combine(adoptGame, "BepInEx", "plugins"));
File.WriteAllText(Path.Combine(adoptGame, "Lethal Company.exe"), "");
File.WriteAllText(Path.Combine(adoptGame, "BepInEx", "plugins", "A.dll"), "a1");
File.WriteAllText(Path.Combine(adoptGame, "BepInEx", "plugins", "B.dll"), "b1");
var adoptState = new LocalState { GamePath = adoptGame };
await updater.UpdateAsync(adoptState);
Check(adoptState.Tag == "v1" && fullZipRequests == 1, "Instalação idêntica baixou o pacote completo.");

await updater.UpdateAsync(state);
Check(state.Tag == "v1", "Canal normal instalou um pré-lançamento.");
var testUpdater = new Updater(Console.WriteLine, client, "https://test.local/releases", _ => { },
    Path.Combine(testRoot, "backups"), includeTests: true);
corruptDelta = true;
try
{
    await testUpdater.UpdateAsync(state);
    throw new Exception("Pacote corrompido foi aceito.");
}
catch (InvalidDataException) { }
Check(state.Tag == "v1" && File.Exists(Path.Combine(game, "BepInEx", "plugins", "B.dll")),
    "Download corrompido alterou a instalação.");
corruptDelta = false;
await testUpdater.UpdateAsync(state);
Check(state.Tag == "v2", "Atualização não instalada.");
Check(!File.Exists(Path.Combine(game, "BepInEx", "plugins", "B.dll")), "B.dll não removido.");
Check(File.ReadAllText(Path.Combine(game, "BepInEx", "plugins", "A.dll")).Trim() == "a2", "A.dll não atualizado.");
Check(File.Exists(Path.Combine(game, "BepInEx", "config", "C.cfg")), "C.cfg não instalado.");
Check(Directory.EnumerateFiles(Path.Combine(testRoot, "backups"), "Old.dll", SearchOption.AllDirectories).Any(), "Backup inicial ausente.");
Console.WriteLine("Smoke test passou: instalação, adoção sem download, canal de teste, atualização, remoção, backup e download corrompido.");

var sourceGame = Path.Combine(testRoot, "source-game");
Directory.CreateDirectory(Path.Combine(sourceGame, "BepInEx", "plugins"));
File.WriteAllText(Path.Combine(sourceGame, "Lethal Company.exe"), "");
File.WriteAllText(Path.Combine(sourceGame, "BepInEx", "plugins", "A.dll"), "old");
File.WriteAllText(Path.Combine(sourceGame, "BepInEx", "plugins", "Removed.dll"), "remove me");
var sourceZip = Path.Combine(testRoot, "upstream.zip");
using (var zip = ZipFile.Open(sourceZip, ZipArchiveMode.Create))
{
    using var writer = new StreamWriter(zip.CreateEntry("plugins/A.dll").Open());
    writer.Write("new");
}
var sourceManifest = new ReleaseManifest
{
    Tag = "v3",
    Files = new() { ["BepInEx/plugins/A.dll"] = HashText("new") },
    Archives = new() { ["upstream-1"] = new SourceArchive
    {
        Url = "https://thunderstore.io/package/download/test/upstream/1/",
        Sha256 = Data.HashFile(sourceZip)
    } },
    Sources = new() { ["BepInEx/plugins/A.dll"] = new SourceFile { Package = "upstream-1", Entry = "plugins/A.dll" } }
};
var sourceDownloadCount = 0;
var corruptSource = true;
using var sourceClient = new HttpClient(new FakeHandler(request =>
{
    var path = request.RequestUri!.AbsolutePath;
    if (path == "/releases/latest") return Json(new { tag_name = "v3", assets = Assets("v3") });
    if (path == "/assets/v3/manifest.json") return Json(sourceManifest);
    if (request.RequestUri.Host == "thunderstore.io")
    {
        sourceDownloadCount++;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(corruptSource ? [1, 2, 3] : File.ReadAllBytes(sourceZip))
        };
    }
    throw new InvalidOperationException(path);
}));
var sourceState = new LocalState
{
    GamePath = sourceGame,
    Tag = "v2",
    Files = new() { ["BepInEx/plugins/A.dll"] = HashText("old"), ["BepInEx/plugins/Removed.dll"] = HashText("remove me") }
};
var sourceUpdater = new Updater(Console.WriteLine, sourceClient, "https://test.local/releases", _ => { }, Path.Combine(testRoot, "source-backups"));
try { await sourceUpdater.UpdateAsync(sourceState); throw new Exception("Fonte corrompida foi aceita."); }
catch (InvalidDataException) { }
Check(sourceState.Tag == "v2" && File.Exists(Path.Combine(sourceGame, "BepInEx", "plugins", "Removed.dll")),
    "Download corrompido alterou a instalação.");
corruptSource = false;
await sourceUpdater.UpdateAsync(sourceState);
Check(sourceState.Tag == "v3" && File.ReadAllText(Path.Combine(sourceGame, "BepInEx", "plugins", "A.dll")) == "new" &&
    !File.Exists(Path.Combine(sourceGame, "BepInEx", "plugins", "Removed.dll")), "Fonte original não atualizou e removeu corretamente.");
await sourceUpdater.UpdateAsync(sourceState);
Check(sourceDownloadCount == 2, "Repetiu download após atualização.");
Console.WriteLine("Fonte original: verificação, alteração, remoção e economia de download passaram.");

static object[] Assets(string tag) => new[] { "manifest.json", "full.zip", "delta.zip" }
    .Select(name => (object)new { name, browser_download_url = $"https://test.local/assets/{tag}/{name}" }).ToArray();

static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
{
    Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
};

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static string HashText(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(handle(request));
}
