using Microsoft.EntityFrameworkCore;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Notifications;

namespace Ws.Core.Scheduling;

public enum AgeGroup
{
    Adult,
    /// <summary>16-17, comes with a parent/guardian.</summary>
    MinorWithGuardian,
}

public sealed record NewBooking
{
    public required int SalonId { get; init; }
    public required int ServiceId { get; init; }
    public required int ArtistId { get; init; }
    public required DateTime StartUtc { get; init; }
    public required string ClientName { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public long? TelegramUserId { get; init; }
    public string Language { get; init; } = Languages.English;
    public required AgeGroup AgeGroup { get; init; }
    public required Channel Source { get; init; }
    public TattooDetails? Tattoo { get; init; }
    public string? Notes { get; init; }
}

public enum BookingError
{
    None,
    NotFound,
    ServiceNotBookableOnline,
    ArtistDoesNotDoService,
    AdultsOnly,
    MissingContact,
    InvalidPhone,
    SlotTaken,
    InvalidState,
}

public sealed record BookingResult(Booking? Booking, BookingError Error)
{
    public bool Ok => Error == BookingError.None;
    public static BookingResult Fail(BookingError error) => new(null, error);
}

public sealed record ArtistSlots(int ArtistId, string ArtistName, IReadOnlyList<DateTime> SlotsUtc);

/// <summary>Availability lookups and booking lifecycle. Channel-agnostic: website, AI and admin all go through here.</summary>
public sealed class BookingService(WsDbContext db, TimeProvider clock, IStaffNotifier? notifier = null)
{
    private readonly IStaffNotifier _notifier = notifier ?? NullStaffNotifier.Instance;

    // SQLite has a single writer anyway; this makes check-then-insert atomic within the process.
    // With Postgres + several instances this becomes a serializable transaction / advisory lock.
    private static readonly SemaphoreSlim WriteLock = new(1, 1);

    /// <summary>Free slots per artist for a service over local dates from..to. artistId null = every artist who does it.</summary>
    public async Task<IReadOnlyList<ArtistSlots>> GetAvailabilityAsync(
        int salonId, int serviceId, int? artistId, DateOnly from, DateOnly to, bool staff = false, CancellationToken ct = default)
    {
        var salon = await db.Salons.Include(s => s.Rooms).FirstOrDefaultAsync(s => s.Id == salonId, ct);
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == serviceId && s.SalonId == salonId && s.IsActive, ct);
        if (salon == null || service == null || (!service.BookableOnline && !staff)) return [];

        var zone = TimeZoneInfo.FindSystemTimeZoneById(salon.TimeZoneId);
        var maxTo = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Now, zone)).AddDays(salon.MaxBookingDaysAhead);
        if (!staff && to > maxTo) to = maxTo;
        if (to < from) return [];

        var links = await db.ArtistServices
            .Where(x => x.ServiceId == serviceId && x.Artist.IsActive && x.Artist.SalonId == salonId)
            .Where(x => artistId == null || x.ArtistId == artistId)
            .Include(x => x.Artist).ThenInclude(a => a.WorkingHours)
            .OrderBy(x => x.Artist.SortOrder)
            .ToListAsync(ct);

        // Pad by a day each side so bookings crossing the local-date edges are seen.
        var windowStart = TimeZoneInfo.ConvertTimeToUtc(from.AddDays(-1).ToDateTime(TimeOnly.MinValue), zone);
        var windowEnd = TimeZoneInfo.ConvertTimeToUtc(to.AddDays(2).ToDateTime(TimeOnly.MinValue), zone);
        var rooms = await LoadRoomsAsync(salon, windowStart, windowEnd, excludeBookingId: null, ct);

        var result = new List<ArtistSlots>();
        foreach (var link in links)
        {
            var query = await BuildQueryAsync(salon, zone, service, link, rooms, windowStart, windowEnd, null, staff, ct);
            result.Add(new ArtistSlots(link.ArtistId, link.Artist.Name, SlotFinder.FindSlots(query, from, to)));
        }
        return result;
    }

    public async Task<BookingResult> CreateAsync(NewBooking req, CancellationToken ct = default)
    {
        var staff = req.Source == Channel.Admin;
        if (string.IsNullOrWhiteSpace(req.Phone) && string.IsNullOrWhiteSpace(req.Email) && req.TelegramUserId == null)
            return BookingResult.Fail(BookingError.MissingContact);

        var salon = await db.Salons.Include(s => s.Rooms).FirstOrDefaultAsync(s => s.Id == req.SalonId, ct);
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == req.ServiceId && s.SalonId == req.SalonId && s.IsActive, ct);
        if (salon == null || service == null) return BookingResult.Fail(BookingError.NotFound);

        // Store phones in one format (E.164) so the same client is recognized however they typed it.
        string? phone = null;
        if (!string.IsNullOrWhiteSpace(req.Phone))
        {
            phone = PhoneFormat.Normalize(req.Phone, salon.Country);
            if (phone == null) return BookingResult.Fail(BookingError.InvalidPhone);
        }

        if (!service.BookableOnline && !staff) return BookingResult.Fail(BookingError.ServiceNotBookableOnline);
        if (service.AdultsOnly && req.AgeGroup != AgeGroup.Adult) return BookingResult.Fail(BookingError.AdultsOnly);

        var link = await db.ArtistServices
            .Include(x => x.Artist).ThenInclude(a => a.WorkingHours)
            .FirstOrDefaultAsync(x => x.ServiceId == req.ServiceId && x.ArtistId == req.ArtistId && x.Artist.IsActive, ct);
        if (link == null) return BookingResult.Fail(BookingError.ArtistDoesNotDoService);

        var zone = TimeZoneInfo.FindSystemTimeZoneById(salon.TimeZoneId);
        var startUtc = DateTime.SpecifyKind(req.StartUtc, DateTimeKind.Utc);
        var duration = link.DurationMinutes ?? service.DurationMinutes;

        await WriteLock.WaitAsync(ct);
        try
        {
            var windowStart = startUtc.AddDays(-1);
            var windowEnd = startUtc.AddDays(2);
            var rooms = await LoadRoomsAsync(salon, windowStart, windowEnd, excludeBookingId: null, ct);
            var query = await BuildQueryAsync(salon, zone, service, link, rooms, windowStart, windowEnd, null, staff, ct);
            var roomId = SlotFinder.FindRoom(query, startUtc);
            if (roomId == null) return BookingResult.Fail(BookingError.SlotTaken);

            var client = await FindOrCreateClientAsync(req with { Phone = phone }, ct);
            var booking = new Booking
            {
                SalonId = salon.Id,
                ArtistId = link.ArtistId,
                ServiceId = service.Id,
                Client = client,
                RoomId = roomId.Value,
                StartUtc = startUtc,
                EndUtc = startUtc.AddMinutes(duration),
                BlockedUntilUtc = startUtc.AddMinutes(duration + service.BufferMinutes),
                NeedsPrivateRoom = service.NeedsPrivateRoom,
                // Staff-created bookings need no approval; client requests wait for the artist.
                Status = staff ? BookingStatus.Confirmed : BookingStatus.Pending,
                Source = req.Source,
                WithGuardian = req.AgeGroup == AgeGroup.MinorWithGuardian,
                Tattoo = req.Tattoo,
                ClientNotes = req.Notes,
                CreatedAtUtc = Now,
                DecidedAtUtc = staff ? Now : null,
            };
            db.Bookings.Add(booking);
            await db.SaveChangesAsync(ct);
            booking.Artist = link.Artist;
            booking.Service = service;

            // Client requests wait for an artist's approval: tell the team (staff-made bookings are already confirmed).
            if (booking.Status == BookingStatus.Pending) _notifier.BookingRequested(BookingNotice.From(booking, zone));
            return new BookingResult(booking, BookingError.None);
        }
        finally
        {
            WriteLock.Release();
        }
    }

    public Task<BookingResult> ApproveAsync(int bookingId, CancellationToken ct = default) =>
        DecideAsync(bookingId, [BookingStatus.Pending], BookingStatus.Confirmed, null, ct);

    public Task<BookingResult> DeclineAsync(int bookingId, string? reason, CancellationToken ct = default) =>
        DecideAsync(bookingId, [BookingStatus.Pending], BookingStatus.Declined, reason, ct);

    public Task<BookingResult> CancelAsync(int bookingId, string? reason, CancellationToken ct = default) =>
        DecideAsync(bookingId, Booking.ActiveStatuses, BookingStatus.Cancelled, reason, ct);

    /// <summary>After the appointment: the client came.</summary>
    public Task<BookingResult> CompleteAsync(int bookingId, CancellationToken ct = default) =>
        DecideAsync(bookingId, [BookingStatus.Confirmed], BookingStatus.Completed, null, ct);

    /// <summary>After the appointment: the client didn't come.</summary>
    public Task<BookingResult> MarkNoShowAsync(int bookingId, CancellationToken ct = default) =>
        DecideAsync(bookingId, [BookingStatus.Confirmed], BookingStatus.NoShow, null, ct);

    private async Task<BookingResult> DecideAsync(
        int bookingId, BookingStatus[] allowedFrom, BookingStatus to, string? reason, CancellationToken ct)
    {
        var booking = await db.Bookings
            .Include(b => b.Artist).Include(b => b.Service).Include(b => b.Client)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        if (booking == null) return BookingResult.Fail(BookingError.NotFound);
        if (!allowedFrom.Contains(booking.Status)) return new BookingResult(booking, BookingError.InvalidState);

        booking.Status = to;
        booking.DecidedAtUtc = Now;
        if (reason != null) booking.DeclineOrCancelReason = reason;
        await db.SaveChangesAsync(ct);
        return new BookingResult(booking, BookingError.None);
    }

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private async Task<SlotQuery> BuildQueryAsync(
        Salon salon, TimeZoneInfo zone, Service service, ArtistService link, IReadOnlyList<RoomSchedule> rooms,
        DateTime windowStart, DateTime windowEnd, int? excludeBookingId, bool staff, CancellationToken ct)
    {
        var artistId = link.ArtistId;
        var bookings = await db.Bookings
            .Where(b => b.ArtistId == artistId && Booking.ActiveStatuses.Contains(b.Status))
            .Where(b => b.StartUtc < windowEnd && b.BlockedUntilUtc > windowStart && b.Id != excludeBookingId)
            .Select(b => new BusyInterval(b.StartUtc, b.BlockedUntilUtc, false))
            .ToListAsync(ct);
        var timeOff = await db.TimeOff
            .Where(t => t.ArtistId == artistId && t.StartUtc < windowEnd && t.EndUtc > windowStart)
            .Select(t => new BusyInterval(t.StartUtc, t.EndUtc, false))
            .ToListAsync(ct);

        return new SlotQuery
        {
            Zone = zone,
            DurationMinutes = link.DurationMinutes ?? service.DurationMinutes,
            BufferMinutes = service.BufferMinutes,
            NeedsPrivateRoom = service.NeedsPrivateRoom,
            ArtistHours = link.Artist.WorkingHours,
            ArtistBusy = [.. bookings, .. timeOff],
            Rooms = rooms,
            // Staff can book anything in the future; clients get lead time and a horizon.
            EarliestStartUtc = staff ? Now : Now.AddMinutes(salon.MinBookingLeadMinutes),
            LatestStartUtc = staff ? DateTime.MaxValue : Now.AddDays(salon.MaxBookingDaysAhead),
            StepMinutes = salon.SlotStepMinutes,
        };
    }

    private async Task<IReadOnlyList<RoomSchedule>> LoadRoomsAsync(
        Salon salon, DateTime windowStart, DateTime windowEnd, int? excludeBookingId, CancellationToken ct)
    {
        var busy = await db.Bookings
            .Where(b => b.SalonId == salon.Id && Booking.ActiveStatuses.Contains(b.Status))
            .Where(b => b.StartUtc < windowEnd && b.BlockedUntilUtc > windowStart && b.Id != excludeBookingId)
            .Select(b => new { b.RoomId, b.StartUtc, b.BlockedUntilUtc, b.NeedsPrivateRoom })
            .ToListAsync(ct);

        return salon.Rooms
            .OrderBy(r => r.Id)
            .Select(r => new RoomSchedule(r.Id, r.Workstations,
                busy.Where(b => b.RoomId == r.Id)
                    .Select(b => new BusyInterval(b.StartUtc, b.BlockedUntilUtc, b.NeedsPrivateRoom))
                    .ToList()))
            .ToList();
    }

    private async Task<Client> FindOrCreateClientAsync(NewBooking req, CancellationToken ct)
    {
        Client? client = null;
        if (req.TelegramUserId != null)
            client = await db.Clients.FirstOrDefaultAsync(c => c.SalonId == req.SalonId && c.TelegramUserId == req.TelegramUserId, ct);
        if (client == null && !string.IsNullOrWhiteSpace(req.Phone))
            client = await db.Clients.FirstOrDefaultAsync(c => c.SalonId == req.SalonId && c.Phone == req.Phone, ct);

        if (client == null)
        {
            client = new Client { SalonId = req.SalonId, CreatedAtUtc = Now };
            db.Clients.Add(client);
        }

        client.Name = req.ClientName.Trim();
        client.Phone = string.IsNullOrWhiteSpace(req.Phone) ? client.Phone : req.Phone.Trim();
        client.Email = string.IsNullOrWhiteSpace(req.Email) ? client.Email : req.Email.Trim();
        client.TelegramUserId ??= req.TelegramUserId;
        client.Language = Languages.Normalize(req.Language);
        return client;
    }
}
