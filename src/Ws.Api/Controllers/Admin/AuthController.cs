using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Ws.Api.Admin;
using Ws.Core.Data;
using Ws.Core.Domain;

namespace Ws.Api.Controllers.Admin;

/// <summary>Staff login/logout with an HttpOnly cookie.</summary>
[Route("api/admin/auth")]
public sealed class AuthController(WsDbContext db, TimeProvider clock) : ApiControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting(RateLimits.Login)]
    public async Task<ActionResult<MeDto>> Login(LoginRequest req, CancellationToken ct)
    {
        var login = (req.Login ?? "").Trim().ToLowerInvariant();
        var user = await db.StaffUsers.FirstOrDefaultAsync(u => u.Login == login && u.IsActive, ct);
        // Same answer for unknown login and wrong password.
        if (user == null || !StaffAuth.Verify(user, req.Password ?? ""))
            return Problem(statusCode: 401, title: "Wrong login or password", type: "BadCredentials");

        user.LastLoginAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(StaffAuth.SalonIdClaim, user.SalonId.ToString()),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
        return ToDto(user);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeDto>> Me(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Unauthorized();
        var user = await db.StaffUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct);
        return user == null ? Unauthorized() : ToDto(user);
    }

    private static MeDto ToDto(StaffUser u) => new(u.Id, u.Login, u.DisplayName, u.Role);
}

public sealed record LoginRequest(string Login, string Password);
public sealed record MeDto(int Id, string Login, string DisplayName, StaffRole Role);
