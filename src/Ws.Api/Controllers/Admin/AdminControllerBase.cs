using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Ws.Api.Admin;
using Ws.Core.Data;

namespace Ws.Api.Controllers.Admin;

/// <summary>Staff admin panel: every action needs a staff login and works on the staff member's salon.</summary>
[Authorize]
public abstract class AdminControllerBase : ApiControllerBase
{
    protected int SalonId => int.Parse(User.FindFirstValue(StaffAuth.SalonIdClaim)!, CultureInfo.InvariantCulture);
    protected string StaffName => User.FindFirstValue(ClaimTypes.Name) ?? "staff";

    protected async Task<TimeZoneInfo> SalonZoneAsync(WsDbContext db, CancellationToken ct) =>
        TimeZoneInfo.FindSystemTimeZoneById(await db.Salons.Where(s => s.Id == SalonId).Select(s => s.TimeZoneId).SingleAsync(ct));

    protected static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Text in both site languages, as edited in the admin panel.</summary>
public sealed record LocalizedDto(string En, string Ru);
