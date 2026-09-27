using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ws.Core.Data;
using Ws.Core.Domain;

namespace Ws.Api.Admin;

/// <summary>
/// First owner account, created at startup when there are no staff users yet.
/// Set locally with user-secrets (Admin:Login, Admin:Password), on servers with env vars. Never commit it.
/// </summary>
public sealed class AdminBootstrapOptions
{
    public const string Section = "Admin";
    public string? Login { get; set; }
    public string? Password { get; set; }
    public string DisplayName { get; set; } = "Owner";
}

public static class StaffAuth
{
    public const string LoginRateLimitPolicy = "login";
    public const string SalonIdClaim = "salon_id";
    private static readonly PasswordHasher<StaffUser> Hasher = new();

    public static void AddStaffAuth(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "ws_staff";
                o.Cookie.HttpOnly = true;
                // Strict + JSON-only endpoints: other sites can't make the browser send the cookie (CSRF).
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(14);
                o.SlidingExpiration = true;
                // An API answers 401/403 instead of redirecting to a login page.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            });
        services.AddAuthorization();
    }

    public static string Hash(StaffUser user, string password) => Hasher.HashPassword(user, password);

    public static bool Verify(StaffUser user, string password) =>
        Hasher.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;

    /// <summary>Creates the first owner from config if there are no staff users yet.</summary>
    public static async Task EnsureOwnerAsync(WsDbContext db, AdminBootstrapOptions opt, int salonId, TimeProvider clock, ILogger log)
    {
        if (await db.StaffUsers.AnyAsync()) return;
        if (string.IsNullOrWhiteSpace(opt.Login) || string.IsNullOrWhiteSpace(opt.Password))
        {
            log.LogWarning("No admin accounts yet. Set Admin:Login and Admin:Password (user-secrets) and restart to create the owner");
            return;
        }
        if (opt.Password.Length < 10)
        {
            log.LogWarning("Admin:Password is too short (min 10 characters); owner account not created");
            return;
        }

        var user = new StaffUser
        {
            SalonId = salonId,
            Login = opt.Login.Trim().ToLowerInvariant(),
            DisplayName = opt.DisplayName,
            Role = StaffRole.Owner,
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        user.PasswordHash = Hash(user, opt.Password);
        db.StaffUsers.Add(user);
        await db.SaveChangesAsync();
        log.LogInformation("Owner account {Login} created", user.Login);
    }

    public sealed record LoginRequest(string Login, string Password);
    public sealed record MeDto(int Id, string Login, string DisplayName, StaffRole Role);

    public static void MapStaffAuth(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/admin/auth");

        auth.MapPost("/login", async (LoginRequest req, WsDbContext db, HttpContext http, TimeProvider clock, CancellationToken ct) =>
        {
            var login = (req.Login ?? "").Trim().ToLowerInvariant();
            var user = await db.StaffUsers.FirstOrDefaultAsync(u => u.Login == login && u.IsActive, ct);
            // Same answer for unknown login and wrong password.
            if (user == null || !Verify(user, req.Password ?? "")) return Results.Problem(statusCode: 401, title: "Wrong login or password", type: "BadCredentials");

            user.LastLoginAtUtc = clock.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(SalonIdClaim, user.SalonId.ToString()),
            ], CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
            return Results.Ok(new MeDto(user.Id, user.Login, user.DisplayName, user.Role));
        }).RequireRateLimiting(LoginRateLimitPolicy);

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync();
            return Results.NoContent();
        });

        auth.MapGet("/me", async (ClaimsPrincipal principal, WsDbContext db, CancellationToken ct) =>
        {
            if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Results.Unauthorized();
            var user = await db.StaffUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct);
            return user == null ? Results.Unauthorized() : Results.Ok(new MeDto(user.Id, user.Login, user.DisplayName, user.Role));
        }).RequireAuthorization();
    }
}
