using System.Text.Json;
using Arqenion.Api.Models;

namespace Arqenion.Api.Services;

// Uzaktaki (ngrok) kullanıcıların bildirdiği hataları sunucu diskinde tutar.
// Bilerek veritabanı kullanılmaz: Development'ta EF InMemory sağlayıcısı çalışıyor ve
// backend her yeniden derlendiğinde kayıtlar silinirdi. Düz JSON dosyası restart'a dayanır.
public sealed class IssueStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _path;
    private readonly object _lock = new();
    private readonly ILogger<IssueStore> _logger;

    public IssueStore(IHostEnvironment env, ILogger<IssueStore> logger)
    {
        _logger = logger;
        var dir = Path.Combine(env.ContentRootPath, "AppData");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "issues.json");
        if (!File.Exists(_path)) File.WriteAllText(_path, "[]");
        _logger.LogInformation("Issue store: {Path} ({Count} kayıt)", _path, ReadAll().Count);
    }

    public string Path_ => _path;

    public List<IssueRecord> ReadAll()
    {
        lock (_lock)
        {
            try
            {
                return JsonSerializer.Deserialize<List<IssueRecord>>(File.ReadAllText(_path), JsonOpts) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Issue dosyası okunamadı; boş liste dönülüyor.");
                return new();
            }
        }
    }

    public IssueRecord Add(IssueRecord issue)
    {
        lock (_lock)
        {
            var all = ReadAllUnlocked();
            issue.Number = all.Count == 0 ? 1 : all.Max(i => i.Number) + 1;
            all.Add(issue);
            WriteUnlocked(all);
            return issue;
        }
    }

    // Aynı hatayı tekrar tekrar çözmemek için: kapatılan kayıt RESOLVED olarak işaretlenir.
    public IssueRecord? Resolve(string id, string? resolution, string? resolvedBy)
    {
        lock (_lock)
        {
            var all = ReadAllUnlocked();
            var issue = all.FirstOrDefault(i =>
                i.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                i.Number.ToString() == id);
            if (issue is null) return null;
            issue.Status = "RESOLVED";
            issue.ResolvedAt = DateTime.UtcNow;
            issue.Resolution = resolution;
            issue.ResolvedBy = resolvedBy;
            WriteUnlocked(all);
            return issue;
        }
    }

    private List<IssueRecord> ReadAllUnlocked()
    {
        try
        {
            return JsonSerializer.Deserialize<List<IssueRecord>>(File.ReadAllText(_path), JsonOpts) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void WriteUnlocked(List<IssueRecord> all)
    {
        // Atomik yazım: geçici dosyaya yaz, sonra taşı — yarıda kesilirse dosya bozulmaz.
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, JsonOpts));
        File.Move(tmp, _path, overwrite: true);
    }
}
