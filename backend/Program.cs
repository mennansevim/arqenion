using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.StaticFiles;
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
// Swagger'da "Authorize" düğmesi: /api/auth/login'den alınan jeton yapıştırılır, sonraki denemeler onu taşır.
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Arqenion API", Version = "v1",
        Description = "VR olay sözleşmesi: docs/vr-events.md. Karar motorda verilir; AI yalnız hatayı açıklar." });
    var bearer = new OpenApiSecurityScheme
    {
        Name = "Authorization", In = ParameterLocation.Header, Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT",
        Description = "POST /api/auth/login cevabındaki token",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    o.AddSecurityDefinition("Bearer", bearer);
    o.AddSecurityRequirement(new OpenApiSecurityRequirement { [bearer] = Array.Empty<string>() });
});
builder.Services.AddHttpClient();

// REST trafiğini konsola yazar. Gövdeler (VR olayları, cevaplar) yalnız geliştirmede; üretimde yöntem, yol ve durum.
builder.Services.AddHttpLogging(o =>
{
    o.LoggingFields = HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath | HttpLoggingFields.ResponseStatusCode;
    if (builder.Environment.IsDevelopment())
        o.LoggingFields |= HttpLoggingFields.RequestBody | HttpLoggingFields.ResponseBody;
    o.RequestBodyLogLimit = 8192;
    o.ResponseBodyLogLimit = 8192;
    o.MediaTypeOptions.AddText("application/json");
});

// CORS: simülatör backend'in kendisinden (/sim) sunulduğu için çoğu kurulumda gerekmez. Ayrı adresten gelen istemciler
// (ör. python http.server ile 8000) Auth:AllowedOrigins listesinde olmalı. Jeton başlıkta taşındığı için çerez yok.
var allowedOrigins = builder.Configuration.GetSection("Auth:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

// Kimlik doğrulama: JWT (Authorization: Bearer …). SignalR tarayıcıdan başlık gönderemediği için hub'da ?access_token=.
var authOptions = AuthOptions.FromConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton<AuthService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = AuthOptions.Issuer,
        ValidAudience = AuthOptions.Audience,
        IssuerSigningKey = authOptions.SigningKey,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
    o.Events = new JwtBearerEvents
    {
        OnMessageReceived = ctx =>
        {
            var token = ctx.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                ctx.Token = token;
            return Task.CompletedTask;
        },
        // Pasif yapılan ya da silinen kullanıcının jetonu süresi dolmadan da geçersiz olur.
        OnTokenValidated = async ctx =>
        {
            var db = ctx.HttpContext.RequestServices.GetRequiredService<ArqenionDbContext>();
            var user = await db.Users.FindAsync(ctx.Principal?.UserId());
            if (user is null || !user.IsActive) ctx.Fail("Kullanıcı pasif ya da yok.");
        }
    };
});
// Varsayılan: her uç giriş ister. Açık uçlar [AllowAnonymous] ile işaretlidir (giriş, sağlık, katalog).
builder.Services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
// Girişte kaba kuvvet denemesine karşı: IP başına dakikada 10 deneme.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("Auth:LoginPerMinute", 10), Window = TimeSpan.FromMinutes(1) }));
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
        Path.Combine(root, "Procedures", "instrument-descriptors.json"),
        Path.Combine(root, "Procedures", "adaptive-rules.json"));
});
// VR modelleri (assets/catalog/<KOD>.glb); Assets:BaseUrl doluysa url'ler CDN'i gösterir.
builder.Services.AddSingleton(sp =>
{
    var root = sp.GetRequiredService<IHostEnvironment>().ContentRootPath;
    return new AssetStore(
        Path.GetFullPath(Path.Combine(root, builder.Configuration["Assets:Dir"] ?? "../assets")),
        builder.Configuration["Assets:BaseUrl"],
        sp.GetRequiredService<FlowModelStore>().Catalog.Keys);
});
builder.Services.AddSingleton<FlowEngine>();
builder.Services.AddSingleton<FlowClinicalText>();
builder.Services.AddScoped<FlowProcedureService>();
builder.Services.AddSingleton<IssueStore>();
builder.Services.AddSingleton<ComplicationEngine>();
builder.Services.AddScoped<ScoringService>();
builder.Services.AddScoped<ProcedureService>();
builder.Services.AddScoped<IAiFeedbackService, AiFeedbackService>();
builder.Services.AddSingleton<AiLimiter>();
// VR bağlantı testi (simulator/vr.html): sözleşme uçlarına gelen istekleri cihaz başına bellekte tutar ve canlı yayar.
builder.Services.AddSingleton<VrMonitor>();

var app = builder.Build();

// Load procedure/scenario/clinical files eagerly so failures surface at startup.
app.Services.GetRequiredService<ProcedureStore>();
app.Services.GetRequiredService<FlowModelStore>();
app.Services.GetRequiredService<IssueStore>();
var assets = app.Services.GetRequiredService<AssetStore>();
app.Logger.LogInformation("Assetler: {Count} dosya, {Missing} katalog kodunun modeli yok", assets.All.Count, assets.Missing.Count);
if (assets.Unmatched.Count > 0)
    app.Logger.LogWarning("Katalogla eşleşmeyen asset dosyaları yayınlanmadı: {Files}", string.Join(", ", assets.Unmatched));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ArqenionDbContext>();
    await DatabaseMigrator.MigrateAsync(db, app.Logger);
    await scope.ServiceProvider.GetRequiredService<AuthService>()
        .EnsureBootstrapAdminAsync(db, app.Logger);
}

// Tünel/vekil arkasında (Pi'de ngrok) gerçek istemci IP'si X-Forwarded-For'dan alınır; giriş sınırı IP başına çalışsın.
// Yalnız Proxy:TrustForwardedHeaders açıkken; en sağdaki (vekilin eklediği) değer kullanılır.
if (app.Configuration.GetValue("Proxy:TrustForwardedHeaders", false))
{
    var fwd = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, ForwardLimit = 1 };
    fwd.KnownNetworks.Clear();
    fwd.KnownProxies.Clear();
    app.UseForwardedHeaders(fwd);
}
app.UseHttpLogging();
// API belgesi geliştirmede açık; başka ortamda Api:Swagger=true ile (Pi'de VR geliştiricisi için). Belge giriş istemez, uçlar yine ister.
if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Api:Swagger", false))
{
    app.UseSwagger();
    // Derin bağlantı: VR test ekranı her adımdan ilgili uca (#/Session/post_api_sessions) link verir.
    app.UseSwaggerUI(o => o.EnableDeepLinking());
}
app.UseCors();
// Statik dosyalardan önce: model indirmeleri (/assets/catalog) de VR testinde görünsün. Kimlik, istek bitince okunur.
var vrMonitor = app.Services.GetRequiredService<VrMonitor>();
app.Use((ctx, next) => vrMonitor.InvokeAsync(ctx, () => next(ctx)));

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
        RequestPath = "/sim",
        // Sayfa her açılışta yeniden doğrulanır; yoksa tarayıcı eski index.html ile eski app.js'i yükler.
        OnPrepareResponse = ctx =>
        {
            if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
    });
}

// VR modelleri giriş istemez (gizli değil; gözlük girişten önce indirebilir).
var assetDir = Path.Combine(assets.Root, AssetStore.Folder);
if (Directory.Exists(assetDir))
{
    var types = new FileExtensionContentTypeProvider();
    foreach (var (ext, type) in AssetStore.ContentTypes)
        types.Mappings[ext] = type;
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(assetDir),
        RequestPath = $"{AssetStore.RequestPath}/{AssetStore.Folder}",
        ContentTypeProvider = types,
        // Dosya değişince hash değişir; gözlük manifestle karşılaştırır. Tarayıcı/vekil için bir gün önbellek + ETag.
        OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public, max-age=86400"
    });
}
// Olmayan model dosyası giriş sormadan 404 (gözlük girişten önce de indirir).
app.MapWhen(ctx => ctx.Request.Path.StartsWithSegments(AssetStore.RequestPath),
    b => b.Run(ctx => { ctx.Response.StatusCode = StatusCodes.Status404NotFound; return Task.CompletedTask; }));

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<SimulationHub>("/hubs/simulation");
// Kök adres simülatöre yönlenir: tünel linkini paylaşırken "/sim/" eklemeyi unutmak
// sayfanın bozuk görünmesine yol açıyordu. Servis durumu /api/health altında kaldı.
app.MapGet("/", () => Results.Redirect("/sim/")).AllowAnonymous();
app.MapGet("/api/health", () => Results.Ok(new { service = "Arqenion API", status = "ok" })).AllowAnonymous();

app.Run();

static void LoadDotEnv()
{
    // Kökten (dotnet run --project backend/…) ya da backend klasöründen başlatılsa da backend/.env okunur.
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), ".env"),
        Path.Combine(Directory.GetCurrentDirectory(), "backend", ".env"),
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

// Uçtan uca HTTP testleri (WebApplicationFactory) için.
public partial class Program { }
