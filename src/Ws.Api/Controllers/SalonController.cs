using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ws.Core.Data;

namespace Ws.Api.Controllers;

/// <summary>Public salon info for the website. Text in ?lang=en|ru.</summary>
[Route("api/salon")]
public sealed class SalonController(WsDbContext db, IOptions<WsOptions> options) : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<SalonDto>> Get(string? lang, CancellationToken ct)
    {
        var s = await db.Salons.AsNoTracking().Include(x => x.OpeningHours)
            .FirstOrDefaultAsync(x => x.Id == options.Value.SalonId, ct);
        if (s == null) return NotFound();

        return new SalonDto(
            s.Name, s.Address.Get(lang), s.Phone, s.Instagram, s.About.Get(lang), s.Policies.Get(lang), s.Currency,
            s.TimeZoneId, s.Country, s.CoverImageUrl, s.MinAgeWithGuardian, s.MinAgeSolo,
            s.OpeningHours.OrderBy(h => ((int)h.Day + 6) % 7).Select(h => new OpeningHoursDto(h.Day, h.Open, h.Close)).ToList());
    }
}

public sealed record SalonDto(
    string Name, string Address, string Phone, string? Instagram, string About, string Policies, string Currency,
    string TimeZone, string Country, string? CoverImageUrl, int MinAgeWithGuardian, int MinAgeSolo,
    IReadOnlyList<OpeningHoursDto> OpeningHours);

public sealed record OpeningHoursDto(DayOfWeek Day, TimeOnly Open, TimeOnly Close);
