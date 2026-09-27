using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ws.Core.Data;
using Ws.Core.Domain;

namespace Ws.Api.Controllers;

/// <summary>Public price list. Text in ?lang=en|ru.</summary>
[Route("api/services")]
public sealed class ServicesController(WsDbContext db, IOptions<WsOptions> options) : ApiControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<ServiceDto>> List(string? lang, CancellationToken ct)
    {
        var services = await db.Services.AsNoTracking()
            .Where(x => x.SalonId == options.Value.SalonId && x.IsActive)
            .Include(x => x.Artists)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

        return services.Select(x => new ServiceDto(
            x.Id, x.Kind, x.Name.Get(lang), x.Description.Get(lang), x.DurationMinutes, x.PriceFrom, x.PriceTo,
            x.BookableOnline, x.AdultsOnly, x.Artists.Select(a => a.ArtistId).ToList()));
    }
}

public sealed record ServiceDto(
    int Id, ServiceKind Kind, string Name, string Description, int DurationMinutes, decimal? PriceFrom, decimal? PriceTo,
    bool BookableOnline, bool AdultsOnly, IReadOnlyList<int> ArtistIds);
