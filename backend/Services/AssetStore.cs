using System.Security.Cryptography;
using System.Text;

namespace Arqenion.Api.Services;

// Katalogdaki bir alet/ipliğin VR modeli. Dosya adı katalog kodudur: assets/catalog/SCALPEL_BLADE_15.glb.
public sealed record AssetInfo(string AssetId, string Code, string Url, string Sha256, long SizeBytes, string ContentType);

// VR'ın indireceği dosyalar (docs/vr-events.md → Assetler). Açılışta klasör taranır, her dosyanın SHA-256'sı ve boyutu
// hesaplanır; gözlük kendi kopyasının hash'i farklıysa indirir. Dosyalar API cevabına gömülmez (base64 yok).
// Assets:BaseUrl boşsa dosyaları backend /assets altında sunar (göreli url); doluysa (CDN) url o adrese göre kurulur.
public sealed class AssetStore
{
    public const string RequestPath = "/assets";
    public const string Folder = "catalog";

    public static readonly IReadOnlyDictionary<string, string> ContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".glb"] = "model/gltf-binary",
        [".gltf"] = "model/gltf+json",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".ktx2"] = "image/ktx2",
    };

    private readonly Dictionary<string, AssetInfo> _byCode;

    public AssetStore(string root, string? baseUrl, IEnumerable<string> catalogCodes)
    {
        Root = root;
        var codes = catalogCodes.ToHashSet();
        var prefix = string.IsNullOrWhiteSpace(baseUrl) ? RequestPath : baseUrl.TrimEnd('/');
        var dir = Path.Combine(root, Folder);
        var files = Directory.Exists(dir)
            ? Directory.GetFiles(dir).Where(f => ContentTypes.ContainsKey(Path.GetExtension(f))).OrderBy(f => f, StringComparer.Ordinal)
            : Enumerable.Empty<string>();

        _byCode = new();
        var unmatched = new List<string>();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var code = Path.GetFileNameWithoutExtension(file);
            if (!codes.Contains(code) || _byCode.ContainsKey(code))
            {
                unmatched.Add(name);   // katalogda olmayan kod ya da aynı koda ikinci dosya: yayınlanmaz
                continue;
            }
            var bytes = File.ReadAllBytes(file);
            _byCode[code] = new AssetInfo($"{Folder}/{code}", code, $"{prefix}/{Folder}/{Uri.EscapeDataString(name)}",
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.LongLength,
                ContentTypes[Path.GetExtension(file)]);
        }

        Unmatched = unmatched;
        Missing = codes.Where(c => !_byCode.ContainsKey(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();
        // Listenin tek özeti: gözlük bu değer değişmediyse hiçbir dosyaya bakmaz.
        var summary = string.Join("\n", _byCode.Values.OrderBy(a => a.Code, StringComparer.Ordinal).Select(a => $"{a.Code} {a.Sha256}"));
        ManifestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(summary))).ToLowerInvariant();
    }

    public string Root { get; }
    public string ManifestHash { get; }
    public IReadOnlyCollection<AssetInfo> All => _byCode.Values;
    // Klasörde olup katalogla eşleşmeyen dosyalar (yazım hatası); açılışta uyarı olarak loglanır.
    public IReadOnlyList<string> Unmatched { get; }
    // Modeli henüz olmayan katalog kodları.
    public IReadOnlyList<string> Missing { get; }

    public AssetInfo? For(string code) => _byCode.GetValueOrDefault(code);
}
