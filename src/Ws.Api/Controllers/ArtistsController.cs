using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ws.Core.Data;
using Ws.Core.Domain;

namespace Ws.Api.Controllers;

/// <summary>Public artist profiles with portfolio. Text in ?lang=en|ru.</summary>
[Route("api/artists")]
public sealed class ArtistsController(WsDbContext db, IOptions<WsOptions> options) : ApiControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<ArtistDto>> List(string? lang, CancellationToken ct)
    {
        var artists = await db.Artists.AsNoTracking()
            .Where(x => x.SalonId == options.Value.SalonId && x.IsActive)
            .Include(x => x.Portfolio).Include(x => x.Services)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

        return artists.Select(x => new ArtistDto(
            x.Id, x.Name, x.Specialty, x.Bio.Get(lang), x.Styles, x.PhotoUrl, x.InstagramHandle,
            x.Portfolio.OrderBy(p => p.SortOrder).Select(p => new PortfolioDto(p.Url, p.Style, p.Caption.Get(lang))).ToList(),
            x.Services.Select(s => s.ServiceId).ToList()));
    }
}

public sealed record ArtistDto(
    int Id, string Name, ArtistSpecialty Specialty, string Bio, IReadOnlyList<string> Styles, string? PhotoUrl,
    string? Instagram, IReadOnlyList<PortfolioDto> Portfolio, IReadOnlyList<int> ServiceIds);

public sealed record PortfolioDto(string Url, string? Style, string Caption);
