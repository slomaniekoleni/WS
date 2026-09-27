using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ws.Core.Data;
using Ws.Core.Scheduling;

namespace Ws.Api.Controllers.Admin;

/// <summary>Staff availability: any service (incl. tattoo sessions), no lead time or horizon limits.</summary>
[Route("api/admin/availability")]
public sealed class AdminAvailabilityController(WsDbContext db, BookingService bookings) : AdminControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AvailabilityDto>>> Get(
        int serviceId, int? artistId, DateOnly from, DateOnly? to, CancellationToken ct)
    {
        var until = to ?? from;
        if (until < from || until.DayNumber - from.DayNumber > 31) return Invalid("to", "Range must be 0-31 days.");

        var result = await bookings.GetAvailabilityAsync(SalonId, serviceId, artistId, from, until, staff: true, ct);
        return Ok(result.Select(r => new AvailabilityDto(r.ArtistId, r.ArtistName, r.SlotsUtc)));
    }

    /// <summary>Where a booking could move to (its own time counts as free), per artist who does its service.</summary>
    [HttpGet("move/{bookingId:int}")]
    public async Task<ActionResult<IEnumerable<AvailabilityDto>>> ForMove(
        int bookingId, int? artistId, DateOnly from, DateOnly? to, CancellationToken ct)
    {
        var until = to ?? from;
        if (until < from || until.DayNumber - from.DayNumber > 31) return Invalid("to", "Range must be 0-31 days.");
        var salonId = SalonId;
        if (!await db.Bookings.AnyAsync(b => b.Id == bookingId && b.SalonId == salonId, ct)) return NotFound();

        var result = await bookings.GetRescheduleSlotsAsync(bookingId, artistId, from, until, staff: true, ct);
        return Ok(result.Select(r => new AvailabilityDto(r.ArtistId, r.ArtistName, r.SlotsUtc)));
    }
}
