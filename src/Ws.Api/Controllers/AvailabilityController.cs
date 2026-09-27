using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ws.Core.Scheduling;

namespace Ws.Api.Controllers;

/// <summary>Free start times for the public booking form (online-bookable services, lead time and horizon applied).</summary>
[Route("api/availability")]
public sealed class AvailabilityController(BookingService bookings, IOptions<WsOptions> options) : ApiControllerBase
{
    /// <summary>Free start times (UTC) per artist for local dates from..to (max 31 days; to defaults to a week).</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AvailabilityDto>>> Get(
        int serviceId, int? artistId, DateOnly from, DateOnly? to, CancellationToken ct)
    {
        var until = to ?? from.AddDays(6);
        if (until < from || until.DayNumber - from.DayNumber > 31) return Invalid("to", "Range must be 0-31 days.");

        var result = await bookings.GetAvailabilityAsync(options.Value.SalonId, serviceId, artistId, from, until, ct: ct);
        return Ok(result.Select(r => new AvailabilityDto(r.ArtistId, r.ArtistName, r.SlotsUtc)));
    }
}

public sealed record AvailabilityDto(int ArtistId, string ArtistName, IReadOnlyList<DateTime> Slots);
