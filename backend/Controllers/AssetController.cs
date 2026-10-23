using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Arqenion.Api.Services;

namespace Arqenion.Api.Controllers;

// VR'ın indireceği model listesi. Gözlük açılışta okur: manifestHash öncekiyle aynıysa bir şey indirmez;
// değilse her dosyanın sha256'sını kendi kopyasıyla karşılaştırıp yalnız değişenleri url'den indirir.
[ApiController]
[AllowAnonymous]
[Route("api/assets")]
public sealed class AssetController : ControllerBase
{
    private readonly AssetStore _assets;

    public AssetController(AssetStore assets) => _assets = assets;

    [HttpGet("manifest")]
    public ActionResult Manifest() => Ok(new
    {
        _assets.ManifestHash,
        Count = _assets.All.Count,
        TotalBytes = _assets.All.Sum(a => a.SizeBytes),
        Assets = _assets.All.OrderBy(a => a.Code, StringComparer.Ordinal),
        // Modeli olmayan katalog kodları: gözlük bunlar için yer tutucu gösterir.
        _assets.Missing
    });
}
