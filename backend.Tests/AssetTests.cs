using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Arqenion.Api.Services;

namespace Arqenion.Api.Tests;

// VR modelleri: manifest, katalogdaki asset alanı ve dosyanın kendisi (docs/vr-events.md → Assetler).
public sealed class AssetTests : IClassFixture<VrReplayTests.Api>
{
    private static readonly string Dir = Path.Combine(TestModel.Repo, "assets", "catalog");
    private readonly VrReplayTests.Api _api;
    public AssetTests(VrReplayTests.Api api) => _api = api;

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    [Fact]
    public async Task Manifest_lists_every_file_with_its_hash_and_size_without_login()
    {
        var http = _api.CreateClient();   // girişsiz
        var res = await http.GetAsync("/api/assets/manifest");
        res.EnsureSuccessStatusCode();
        var m = JsonNode.Parse(await res.Content.ReadAsStringAsync())!;

        var files = Directory.GetFiles(Dir, "*.glb");
        Assert.NotEmpty(files);
        var assets = m["assets"]!.AsArray();
        Assert.Equal(files.Length, m["count"]!.GetValue<int>());
        Assert.Equal(files.Length, assets.Count);
        foreach (var file in files)
        {
            var code = Path.GetFileNameWithoutExtension(file);
            var a = assets.Single(x => x!["code"]!.GetValue<string>() == code)!;
            var bytes = await File.ReadAllBytesAsync(file);
            Assert.Equal($"catalog/{code}", a["assetId"]!.GetValue<string>());
            Assert.Equal($"/assets/catalog/{code}.glb", a["url"]!.GetValue<string>());
            Assert.Equal(Sha(bytes), a["sha256"]!.GetValue<string>());
            Assert.Equal(bytes.LongLength, a["sizeBytes"]!.GetValue<long>());
            Assert.Equal("model/gltf-binary", a["contentType"]!.GetValue<string>());
        }
        Assert.Equal(files.Sum(f => new FileInfo(f).Length), m["totalBytes"]!.GetValue<long>());
        Assert.Matches("^[0-9a-f]{64}$", m["manifestHash"]!.GetValue<string>());
        Assert.Contains("CLAMP_BABCOCK_16", m["missing"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public async Task Asset_file_downloads_without_login_and_matches_the_manifest_hash()
    {
        var http = _api.CreateClient();
        var m = JsonNode.Parse(await http.GetStringAsync("/api/assets/manifest"))!;
        foreach (var a in m["assets"]!.AsArray())
        {
            var res = await http.GetAsync(a!["url"]!.GetValue<string>());
            res.EnsureSuccessStatusCode();
            Assert.Equal("model/gltf-binary", res.Content.Headers.ContentType!.MediaType);
            Assert.Contains("max-age", res.Headers.CacheControl!.ToString());
            var bytes = await res.Content.ReadAsByteArrayAsync();
            Assert.Equal(a["sha256"]!.GetValue<string>(), Sha(bytes));
            // Geçerli GLB başlığı: "glTF", sürüm 2, toplam uzunluk dosyayla aynı.
            Assert.Equal("glTF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal(2, BitConverter.ToInt32(bytes, 4));
            Assert.Equal(bytes.Length, BitConverter.ToInt32(bytes, 8));
        }
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await http.GetAsync("/assets/catalog/YOK.glb")).StatusCode);
    }

    [Fact]
    public async Task Procedure_catalog_carries_the_asset_reference()
    {
        var p = JsonNode.Parse(await _api.CreateClient().GetStringAsync("/api/procedure"))!;
        JsonNode Item(string list, string code) => p[list]!.AsArray().Single(x => x!["code"]!.GetValue<string>() == code)!;

        var blade = Item("instrumentCatalog", "SCALPEL_BLADE_15")["asset"]!;
        Assert.Equal("catalog/SCALPEL_BLADE_15", blade["assetId"]!.GetValue<string>());
        Assert.Equal(Sha(await File.ReadAllBytesAsync(Path.Combine(Dir, "SCALPEL_BLADE_15.glb"))), blade["sha256"]!.GetValue<string>());
        Assert.NotNull(Item("sutureCatalog", "SUTURE_POLYGLACTIN_0")["asset"]);
        Assert.Null(Item("instrumentCatalog", "CLAMP_BABCOCK_16")["asset"]);
    }

    [Fact]
    public void Every_asset_file_matches_a_catalog_code()
    {
        // Yazım hatalı dosya adı yayınlanmaz; burada yakalanır.
        var store = new AssetStore(Path.Combine(TestModel.Repo, "assets"), null, TestModel.Model.Catalog.Keys);
        Assert.Empty(store.Unmatched);
    }

    [Fact]
    public void Cdn_base_url_unknown_files_and_hash_changes()
    {
        var root = Path.Combine(Path.GetTempPath(), "arq-assets-" + Guid.NewGuid().ToString("N"));
        var dir = Directory.CreateDirectory(Path.Combine(root, AssetStore.Folder)).FullName;
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "A.glb"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(dir, "YANLIS_KOD.glb"), new byte[] { 4 });
            File.WriteAllText(Path.Combine(dir, "notlar.txt"), "yok sayılır");
            var codes = new[] { "A", "B" };

            var store = new AssetStore(root, "https://cdn.example.org/arq/", codes);
            Assert.Equal("https://cdn.example.org/arq/catalog/A.glb", store.For("A")!.Url);
            Assert.Equal(3, store.For("A")!.SizeBytes);
            Assert.Equal(new[] { "YANLIS_KOD.glb" }, store.Unmatched);
            Assert.Equal(new[] { "B" }, store.Missing);

            File.WriteAllBytes(Path.Combine(dir, "A.glb"), new byte[] { 1, 2, 3, 4 });
            var changed = new AssetStore(root, null, codes);
            Assert.NotEqual(store.ManifestHash, changed.ManifestHash);
            Assert.NotEqual(store.For("A")!.Sha256, changed.For("A")!.Sha256);
        }
        finally { Directory.Delete(root, true); }
    }
}
