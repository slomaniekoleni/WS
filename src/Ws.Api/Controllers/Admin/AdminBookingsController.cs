using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ws.Api.Telegram;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Scheduling;

namespace Ws.Api.Controllers.Admin;

/// <summary>The booking calendar: list, staff-made bookings, approve/decline/cancel/complete/no-show, notes.</summary>
[Route("api/admin/bookings")]
public sealed class AdminBookingsController(WsDbContext db, BookingService bookings, ClientNotifier notifier) : AdminControllerBase
{
    /// <summary>Bookings starting within local dates from..to (max 42 days), all statuses.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdminBookingDto>>> List(DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 42) return Invalid("to", "Range must be 0-42 days.");
        var zone = await SalonZoneAsync(db, ct);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), zone);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
        var salonId = SalonId;

        var list = await db.Bookings.AsNoTracking()
            .Where(b => b.SalonId == salonId && b.StartUtc >= fromUtc && b.StartUtc < toUtc)
            .Include(b => b.Artist).Include(b => b.Service).Include(b => b.Client)
            .OrderBy(b => b.StartUtc)
            .ToListAsync(ct);
        return Ok(list.Select(AdminBookingDto.From));
    }

    /// <summary>Staff books directly (e.g. a tattoo session after the consultation): confirmed right away.</summary>
    [HttpPost]
    public async Task<ActionResult<AdminBookingDto>> Create(StaffBookingRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ClientName)) return Invalid("clientName", "Name is required.");
        var result = await bookings.CreateAsync(new NewBooking
        {
            SalonId = SalonId,
            ServiceId = req.ServiceId,
            ArtistId = req.ArtistId,
            StartUtc = req.StartUtc.ToUniversalTime(),
            ClientName = req.ClientName,
            Phone = req.Phone,
            Email = req.Email,
            AgeGroup = req.AgeGroup,
            Language = Languages.Russian,
            Source = Channel.Admin,
            Notes = req.Notes,
            Tattoo = req.Tattoo,
        }, ct);
        if (!result.Ok) return BookingProblem(result.Error);

        await db.Entry(result.Booking!).Reference(b => b.Client).LoadAsync(ct);
        return AdminBookingDto.From(result.Booking!);
    }

    /// <summary>Staff moves a booking (new time, optionally another artist). Status stays; clients on a messenger are told.</summary>
    [HttpPost("{id:int}/move")]
    public async Task<ActionResult<AdminBookingDto>> Move(int id, MoveRequest req, CancellationToken ct)
    {
        var salonId = SalonId;
        if (!await db.Bookings.AnyAsync(b => b.Id == id && b.SalonId == salonId, ct)) return NotFound();

        var result = await bookings.RescheduleAsync(id, req.StartUtc.ToUniversalTime(), req.ArtistId, staff: true, ct);
        if (!result.Ok) return BookingProblem(result.Error);

        await notifier.BookingMovedAsync(result.Booking!, ct);
        return AdminBookingDto.From(result.Booking!);
    }

    /// <summary>decision: approve | decline | cancel | complete | no-show. Clients on a messenger are told.</summary>
    [HttpPost("{id:int}/{decision}")] // not {action}: MVC reserves that route value
    public async Task<ActionResult<AdminBookingDto>> Decide(int id, string decision, DecisionRequest? body, CancellationToken ct)
    {
        var salonId = SalonId;
        if (!await db.Bookings.AnyAsync(b => b.Id == id && b.SalonId == salonId, ct)) return NotFound();

        var reason = Blank(body?.Reason);
        var result = decision switch
        {
            "approve" => await bookings.ApproveAsync(id, ct),
            "decline" => await bookings.DeclineAsync(id, reason ?? $"Declined by {StaffName}", ct),
            "cancel" => await bookings.CancelAsync(id, reason ?? $"Cancelled by {StaffName}", ct),
            "complete" => await bookings.CompleteAsync(id, ct),
            "no-show" => await bookings.MarkNoShowAsync(id, ct),
            _ => null,
        };
        if (result == null) return NotFound();
        if (!result.Ok) return BookingProblem(result.Error);

        // Website clients have no messenger; staff call them.
        await notifier.BookingDecidedAsync(result.Booking!, ct);
        return AdminBookingDto.From(result.Booking!);
    }

    [HttpPut("{id:int}/notes")]
    public async Task<IActionResult> UpdateNotes(int id, NotesRequest req, CancellationToken ct)
    {
        var salonId = SalonId;
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id && b.SalonId == salonId, ct);
        if (booking == null) return NotFound();
        booking.StaffNotes = Blank(req.StaffNotes);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public sealed record AdminBookingDto(
    int Id, BookingStatus Status, DateTime StartUtc, DateTime EndUtc, int ArtistId, string ArtistName,
    int ServiceId, string ServiceName, int ClientId, string ClientName, string? Phone, string? Email, long? TelegramUserId,
    Channel Source, bool WithGuardian, bool NeedsPrivateRoom, TattooDetails? Tattoo, string? ClientNotes, string? StaffNotes,
    DateTime CreatedAtUtc, string? DeclineOrCancelReason, DateTime? RescheduledFromUtc)
{
    /// <summary>Booking with Artist, Service and Client loaded. Service name in Russian (staff language).</summary>
    public static AdminBookingDto From(Booking b) => new(
        b.Id, b.Status, b.StartUtc, b.EndUtc, b.ArtistId, b.Artist.Name, b.ServiceId, b.Service.Name.Get(Languages.Russian),
        b.ClientId, b.Client.Name, b.Client.Phone, b.Client.Email, b.Client.TelegramUserId, b.Source, b.WithGuardian,
        b.NeedsPrivateRoom, b.Tattoo, b.ClientNotes, b.StaffNotes, b.CreatedAtUtc, b.DeclineOrCancelReason, b.RescheduledFromUtc);
}

public sealed record StaffBookingRequest(
    int ServiceId, int ArtistId, DateTime StartUtc, string ClientName, string? Phone, string? Email,
    AgeGroup AgeGroup, string? Notes, TattooDetails? Tattoo);

public sealed record DecisionRequest(string? Reason);

public sealed record MoveRequest(DateTime StartUtc, int? ArtistId);

public sealed record NotesRequest(string? StaffNotes);
