using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ws.Core.Domain;
using Ws.Core.Scheduling;

namespace Ws.Api.Controllers;

/// <summary>Booking requests from the public website form (pending until the artist approves).</summary>
[Route("api/bookings")]
public sealed class BookingsController(BookingService bookings, IOptions<WsOptions> options) : ApiControllerBase
{
    [HttpPost]
    public async Task<ActionResult<BookingCreatedDto>> Create(CreateBookingRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ClientName) || req.ClientName.Length > 100) return Invalid("clientName", "Name is required.");

        var result = await bookings.CreateAsync(new NewBooking
        {
            SalonId = options.Value.SalonId,
            ServiceId = req.ServiceId,
            ArtistId = req.ArtistId,
            StartUtc = req.StartUtc.ToUniversalTime(),
            ClientName = req.ClientName,
            Phone = req.Phone,
            Email = req.Email,
            AgeGroup = req.AgeGroup,
            Language = req.Language ?? Languages.English,
            Source = Channel.Website,
            Notes = req.Notes,
            Tattoo = req.Tattoo,
        }, ct);
        if (!result.Ok) return BookingProblem(result.Error);

        var b = result.Booking!;
        return Created($"/api/bookings/{b.Id}", new BookingCreatedDto(
            b.Id, b.Status, b.StartUtc, b.EndUtc, b.Artist.Name, b.Service.Name.Get(req.Language)));
    }
}

public sealed record CreateBookingRequest(
    int ServiceId, int ArtistId, DateTime StartUtc, string ClientName, string? Phone, string? Email,
    AgeGroup AgeGroup, string? Language, string? Notes, TattooDetails? Tattoo);

public sealed record BookingCreatedDto(int Id, BookingStatus Status, DateTime StartUtc, DateTime EndUtc, string ArtistName, string ServiceName);
