using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Arqenion.Api.Data;
using Arqenion.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Arqenion.Api.Services;

// Kullanıcı adı + şifre ile giriş; karşılığında süreli JWT. Şifreler PBKDF2-SHA256 ile özetlenir, düz metin tutulmaz.
public sealed class AuthService
{
    private const int Iterations = 210_000;   // OWASP 2023 önerisi (PBKDF2-HMAC-SHA256)
    private readonly AuthOptions _options;

    public AuthService(AuthOptions options) => _options = options;

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string? stored)
    {
        var parts = stored?.Split('$');
        if (parts is not { Length: 4 } || parts[0] != "pbkdf2-sha256" || !int.TryParse(parts[1], out var iterations)) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // Şifre kuralı: en az 10 karakter (NIST 800-63B: uzunluk, karmaşıklık kuralından önemlidir).
    public static string? PasswordProblem(string? password)
        => string.IsNullOrEmpty(password) || password.Length < 10 ? "Şifre en az 10 karakter olmalı." : null;

    public (string Token, DateTime ExpiresAt) IssueToken(User user)
    {
        var expires = DateTime.UtcNow.Add(_options.TokenLifetime);
        var token = new JwtSecurityToken(
            issuer: AuthOptions.Issuer,
            audience: AuthOptions.Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.Username ?? user.Id),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            },
            expires: expires,
            signingCredentials: new SigningCredentials(_options.SigningKey, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    // İlk eğitmen hesabı ortam değişkenlerinden (Auth__BootstrapAdmin__Username / __Password); eğitmen yoksa bir kez açılır.
    public async Task EnsureBootstrapAdminAsync(ArqenionDbContext db, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(_options.BootstrapUsername) || string.IsNullOrEmpty(_options.BootstrapPassword)) return;
        if (await db.Users.AnyAsync(u => u.Role == Roles.Instructor && u.PasswordHash != null)) return;
        if (PasswordProblem(_options.BootstrapPassword) is { } problem)
        {
            logger.LogError("İlk eğitmen hesabı açılmadı: {Problem}", problem);
            return;
        }
        db.Users.Add(new User
        {
            Username = _options.BootstrapUsername.Trim().ToLowerInvariant(),
            Name = _options.BootstrapUsername.Trim(),
            PasswordHash = HashPassword(_options.BootstrapPassword),
            Role = Roles.Instructor
        });
        await db.SaveChangesAsync();
        logger.LogInformation("İlk eğitmen hesabı açıldı: {Username}", _options.BootstrapUsername);
    }
}

public sealed class AuthOptions
{
    public const string Issuer = "arqenion";
    public const string Audience = "arqenion-clients";
    public required SymmetricSecurityKey SigningKey { get; init; }
    public TimeSpan TokenLifetime { get; init; } = TimeSpan.FromHours(8);   // bir eğitim günü
    public string? BootstrapUsername { get; init; }
    public string? BootstrapPassword { get; init; }

    public static AuthOptions FromConfiguration(IConfiguration cfg, IHostEnvironment env, ILogger? logger = null)
    {
        var key = cfg["Auth:SigningKey"];
        if (string.IsNullOrEmpty(key) || Encoding.UTF8.GetByteCount(key) < 32)
        {
            if (env.IsProduction())
                throw new InvalidOperationException("Auth:SigningKey ayarlanmamış ya da 32 bayttan kısa (ortam değişkeni Auth__SigningKey).");
            // Geliştirmede geçici anahtar: her yeniden başlatmada oturumlar düşer.
            key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            logger?.LogWarning("Auth:SigningKey yok; geçici anahtar üretildi. Yeniden başlatınca herkes yeniden giriş yapmalı.");
        }
        return new AuthOptions
        {
            SigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            TokenLifetime = TimeSpan.FromMinutes(cfg.GetValue("Auth:TokenMinutes", 480)),
            BootstrapUsername = cfg["Auth:BootstrapAdmin:Username"],
            BootstrapPassword = cfg["Auth:BootstrapAdmin:Password"]
        };
    }
}

public static class ClaimsExtensions
{
    public static string? UserId(this ClaimsPrincipal user)
        => user.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

    public static bool IsInstructor(this ClaimsPrincipal user) => user.IsInRole(Roles.Instructor);
}
