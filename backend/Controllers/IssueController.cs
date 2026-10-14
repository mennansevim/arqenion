using Microsoft.AspNetCore.Mvc;
using Arqenion.Api.Models;
using Arqenion.Api.Services;

namespace Arqenion.Api.Controllers;

public sealed class CreateIssueRequest
{
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Severity { get; set; } = "NORMAL";
    public string? Area { get; set; }
    public string? ReportedBy { get; set; }
    public Dictionary<string, string>? Context { get; set; }
}

public sealed class ResolveIssueRequest
{
    public string? Resolution { get; set; }
    public string? ResolvedBy { get; set; }
}

[ApiController]
[Route("api/issues")]
public sealed class IssueController : ControllerBase
{
    private readonly IssueStore _store;
    private readonly ILogger<IssueController> _logger;

    public IssueController(IssueStore store, ILogger<IssueController> logger)
    {
        _store = store;
        _logger = logger;
    }

    // Uzaktaki kullanıcı "Hata Bildir" ile buraya yazar.
    [HttpPost]
    public ActionResult<IssueRecord> Create([FromBody] CreateIssueRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Body) && string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(new { message = "Hata açıklaması boş olamaz." });

        var title = string.IsNullOrWhiteSpace(req.Title)
            ? req.Body.Trim().Split('\n')[0][..Math.Min(80, req.Body.Trim().Split('\n')[0].Length)]
            : req.Title.Trim();

        var issue = _store.Add(new IssueRecord
        {
            Title = title,
            Body = req.Body?.Trim() ?? "",
            Severity = Normalize(req.Severity),
            Area = req.Area,
            ReportedBy = string.IsNullOrWhiteSpace(req.ReportedBy) ? "anonim" : req.ReportedBy.Trim(),
            Context = req.Context ?? new()
        });

        _logger.LogWarning("🐞 HATA BİLDİRİMİ #{Number} [{Severity}] {Title} — {Body}",
            issue.Number, issue.Severity, issue.Title, issue.Body);

        return Ok(issue);
    }

    // status=open | resolved | all (varsayılan: all)
    [HttpGet]
    public ActionResult List([FromQuery] string status = "all")
    {
        var all = _store.ReadAll().OrderByDescending(i => i.Number).ToList();
        var filtered = status.ToLowerInvariant() switch
        {
            "open" => all.Where(i => i.Status == "OPEN").ToList(),
            "resolved" => all.Where(i => i.Status == "RESOLVED").ToList(),
            _ => all
        };
        return Ok(new
        {
            file = _store.Path_,
            total = all.Count,
            open = all.Count(i => i.Status == "OPEN"),
            resolved = all.Count(i => i.Status == "RESOLVED"),
            issues = filtered
        });
    }

    // Çözülen kayıt işaretlenir; böylece aynı hata tekrar ele alınmaz.
    [HttpPost("{id}/resolve")]
    public ActionResult<IssueRecord> Resolve(string id, [FromBody] ResolveIssueRequest? req)
    {
        var issue = _store.Resolve(id, req?.Resolution, req?.ResolvedBy ?? "claude");
        if (issue is null) return NotFound(new { message = $"Kayıt bulunamadı: {id}" });
        _logger.LogInformation("✅ Hata #{Number} çözüldü: {Resolution}", issue.Number, issue.Resolution);
        return Ok(issue);
    }

    private static string Normalize(string? s) => (s ?? "NORMAL").ToUpperInvariant() switch
    {
        "LOW" => "LOW",
        "HIGH" => "HIGH",
        "BLOCKER" => "BLOCKER",
        _ => "NORMAL"
    };
}
