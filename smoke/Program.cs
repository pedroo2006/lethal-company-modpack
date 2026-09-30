using System.Net;
using System.Text;
using System.Text.Json;
using LethalModpackUpdater;

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
Console.WriteLine("Smoke test passou: instalação, canal de teste, atualização, remoção, backup e download corrompido.");

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

sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(handle(request));
}
