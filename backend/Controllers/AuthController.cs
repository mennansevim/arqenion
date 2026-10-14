using Arqenion.Api.Data;
using Arqenion.Api.Models;
using Arqenion.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Arqenion.Api.Controllers;

public sealed record LoginRequest(string Username, string Password);
public sealed record CreateUserRequest(string Username, string Password, string? Name, string? Role);
public sealed record ResetPasswordRequest(string Password);

// Giriş ve kullanıcı yönetimi. Kursiyer hesabını eğitmen açar; kendi kendine kayıt yoktur.
// Gövde loglanmaz: şifre ve jeton konsola düşmesin.
[ApiController]
[Route("api/auth")]
[HttpLogging(HttpLoggingFields.RequestMethod | HttpLoggingFields.RequestPath | HttpLoggingFields.ResponseStatusCode)]
public sealed class AuthController : ControllerBase
{
    private readonly ArqenionDbContext _db;
    private readonly AuthService _auth;

    public AuthController(ArqenionDbContext db, AuthService auth)
    {
        _db = db;
        _auth = auth;
    }

    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<ActionResult> Login([FromBody] LoginRequest req)
    {
        var username = (req.Username ?? "").Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        // Kullanıcı yok, pasif ya da şifre yanlış: aynı cevap (hangi kullanıcı adlarının var olduğu sızmaz).
        if (user is null || !user.IsActive || !AuthService.VerifyPassword(req.Password ?? "", user.PasswordHash))
            return Unauthorized(new { message = "Kullanıcı adı ya da şifre hatalı." });

        var (token, expiresAt) = _auth.IssueToken(user);
        return Ok(new { token, expiresAt, user = new { user.Id, user.Username, user.Name, user.Role } });
    }

    [HttpGet("me")]
    public async Task<ActionResult> Me()
    {
        var user = await _db.Users.FindAsync(User.UserId());
        return user is null ? Unauthorized() : Ok(new { user.Id, user.Username, user.Name, user.Role });
    }

    [Authorize(Roles = Roles.Instructor)]
    [HttpGet("users")]
    public async Task<ActionResult> Users()
        => Ok(await _db.Users.Where(u => u.Username != null).OrderBy(u => u.Username)
            .Select(u => new { u.Id, u.Username, u.Name, u.Role, u.IsActive, u.CreatedAt }).ToListAsync());

    [Authorize(Roles = Roles.Instructor)]
    [HttpPost("users")]
    public async Task<ActionResult> CreateUser([FromBody] CreateUserRequest req)
    {
        var username = (req.Username ?? "").Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(username, "^[a-z0-9._-]{3,40}$"))
            return BadRequest(new { message = "Kullanıcı adı 3–40 karakter olmalı: küçük harf, rakam, nokta, alt çizgi, tire." });
        var role = req.Role ?? Roles.Trainee;
        if (!Roles.All.Contains(role))
            return BadRequest(new { message = $"Bilinmeyen rol: {role}.", allowed = Roles.All });
        if (AuthService.PasswordProblem(req.Password) is { } problem)
            return BadRequest(new { message = problem });
        if (await _db.Users.AnyAsync(u => u.Username == username))
            return Conflict(new { message = "Bu kullanıcı adı alınmış." });

        var user = new User
        {
            Username = username, Name = string.IsNullOrWhiteSpace(req.Name) ? username : req.Name.Trim(),
            PasswordHash = AuthService.HashPassword(req.Password), Role = role
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return Ok(new { user.Id, user.Username, user.Name, user.Role });
    }

    [Authorize(Roles = Roles.Instructor)]
    [HttpPost("users/{id}/password")]
    public async Task<ActionResult> ResetPassword(string id, [FromBody] ResetPasswordRequest req)
    {
        var user = await _db.Users.FindAsync(id);
        if (user?.Username is null) return NotFound(new { message = "Kullanıcı bulunamadı." });
        if (AuthService.PasswordProblem(req.Password) is { } problem) return BadRequest(new { message = problem });
        user.PasswordHash = AuthService.HashPassword(req.Password);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = Roles.Instructor)]
    [HttpPost("users/{id}/active")]
    public async Task<ActionResult> SetActive(string id, [FromQuery] bool value)
    {
        var user = await _db.Users.FindAsync(id);
        if (user?.Username is null) return NotFound(new { message = "Kullanıcı bulunamadı." });
        if (user.Id == User.UserId() && !value) return BadRequest(new { message = "Kendi hesabınızı pasif yapamazsınız." });
        user.IsActive = value;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
