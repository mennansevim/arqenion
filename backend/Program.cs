using Microsoft.AspNetCore.HttpLogging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Arqenion.Api.Data;
using Arqenion.Api.Hubs;
using Arqenion.Api.Services;

// .env dosyasındaki anahtarları (ör. OPENAI_API_KEY) ortam değişkenine yükle.
LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();

// Tüm REST trafiğini (yol + gövde) konsola yazarak VR↔backend iletişimini görünür kılar.
builder.Services.AddHttpLogging(o =>
{
    o.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath
        | HttpLoggingFields.RequestBody | HttpLoggingFields.ResponseStatusCode | HttpLoggingFields.ResponseBody;
    o.RequestBodyLogLimit = 8192;
    o.ResponseBodyLogLimit = 8192;
    o.MediaTypeOptions.AddText("application/json");
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .SetIsOriginAllowed(_ => true)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var provider = builder.Configuration["Database:Provider"] ?? "Postgres";
builder.Services.AddDbContext<ArqenionDbContext>(opt =>
{
    if (provider.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        opt.UseInMemoryDatabase("arqenion");
    else
        opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres"));
});

builder.Services.AddSingleton<ProcedureStore>();
// 60 adımlık akış modeli (procedure-model/dist); "Procedure:Model" = "v1" ise yeni seanslar eski 25 adımlı modelle başlar.
builder.Services.AddSingleton(sp =>
{
    var cfg = builder.Configuration.GetSection("Procedure");
    var root = sp.GetRequiredService<IHostEnvironment>().ContentRootPath;
    return new FlowModelStore(
        Path.GetFullPath(Path.Combine(root, cfg["ModelDir"] ?? "../procedure-model/dist")),
        cfg["ModelVersion"] ?? "2.4",
        Path.Combine(root, "Procedures", "scoring.json"),
        Path.Combine(root, "Procedures", "instrument-descriptors.json"));
});
builder.Services.AddSingleton<FlowEngine>();
builder.Services.AddSingleton<FlowClinicalText>();
builder.Services.AddScoped<FlowProcedureService>();
builder.Services.AddSingleton<IssueStore>();
builder.Services.AddSingleton<ComplicationEngine>();
builder.Services.AddScoped<ScoringService>();
builder.Services.AddScoped<ProcedureService>();
builder.Services.AddScoped<IAiFeedbackService, AiFeedbackService>();

var app = builder.Build();

// Load procedure/scenario/clinical files eagerly so failures surface at startup.
app.Services.GetRequiredService<ProcedureStore>();
app.Services.GetRequiredService<FlowModelStore>();
app.Services.GetRequiredService<IssueStore>();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ArqenionDbContext>();
    db.Database.EnsureCreated();
    // EnsureCreated mevcut tabloya kolon eklemez; 60 adımlık model için gereken kolonlar veriye dokunmadan eklenir.
    if (db.Database.IsNpgsql())
        db.Database.ExecuteSqlRaw("""
            ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "ProcedureModel" text NULL;
            ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "CurrentNodeId" text NULL;
            ALTER TABLE "Sessions" ADD COLUMN IF NOT EXISTS "FlowStateJson" text NULL;
            ALTER TABLE "SessionEvents" ADD COLUMN IF NOT EXISTS "NodeId" text NULL;
            """);
}

app.UseHttpLogging();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();

var simulatorPath = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "simulator"));
if (Directory.Exists(simulatorPath))
{
    var fp = new PhysicalFileProvider(simulatorPath);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = fp,
        RequestPath = "/sim"
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = fp,
        RequestPath = "/sim"
    });
}

app.MapControllers();
app.MapHub<SimulationHub>("/hubs/simulation");
// Kök adres simülatöre yönlenir: tünel linkini paylaşırken "/sim/" eklemeyi unutmak
// sayfanın bozuk görünmesine yol açıyordu. Servis durumu /api/health altında kaldı.
app.MapGet("/", () => Results.Redirect("/sim/"));
app.MapGet("/api/health", () => Results.Ok(new { service = "Arqenion API", status = "ok" }));

app.Run();

static void LoadDotEnv()
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), ".env"),
        Path.Combine(AppContext.BaseDirectory, ".env"),
    };
    foreach (var path in candidates)
    {
        if (!File.Exists(path)) continue;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var val = line[(idx + 1)..].Trim().Trim('"');
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, val);
        }
        return;
    }
}
