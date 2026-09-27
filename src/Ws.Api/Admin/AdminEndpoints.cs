using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Ws.Api.Telegram;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Notifications;
using Ws.Core.Scheduling;

namespace Ws.Api.Admin;

/// <summary>Staff admin panel API. Every endpoint needs a staff login and works on the staff member's salon.</summary>
public static class AdminEndpoints
{
    public static void MapAdminApi(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization();

        admin.MapGet("/bookings", ListBookings);
        admin.MapPost("/bookings", CreateBooking);
        admin.MapPost("/bookings/{id:int}/{action}", DecideBooking);
        admin.MapPut("/bookings/{id:int}/notes", UpdateNotes);
        admin.MapGet("/availability", Availability);

        admin.MapGet("/artists", ListArtists);
        admin.MapPost("/artists", (ArtistEdit edit, WsDbContext db, ClaimsPrincipal user, CancellationToken ct) => SaveArtist(null, edit, db, user, ct));
        admin.MapPut("/artists/{id:int}", (int id, ArtistEdit edit, WsDbContext db, ClaimsPrincipal user, CancellationToken ct) => SaveArtist(id, edit, db, user, ct));
        admin.MapPost("/artists/{id:int}/time-off", AddTimeOff);
        admin.MapDelete("/time-off/{id:int}", DeleteTimeOff);

        admin.MapGet("/services", ListServices);
        admin.MapPost("/services", (ServiceEdit edit, WsDbContext db, ClaimsPrincipal user, CancellationToken ct) => SaveService(null, edit, db, user, ct));
        admin.MapPut("/services/{id:int}", (int id, ServiceEdit edit, WsDbContext db, ClaimsPrincipal user, CancellationToken ct) => SaveService(id, edit, db, user, ct));

        admin.MapGet("/conversations", ListConversations);
        admin.MapGet("/conversations/{id:int}", GetConversation);
        admin.MapPost("/conversations/{id:int}/resolve", ResolveConversation);
    }

    private static int SalonId(ClaimsPrincipal user) => int.Parse(user.FindFirstValue(StaffAuth.SalonIdClaim)!, CultureInfo.InvariantCulture);
    private static string StaffName(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name) ?? "staff";

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    // ---------- Bookings ----------

    public sealed record AdminBookingDto(
        int Id, BookingStatus Status, DateTime StartUtc, DateTime EndUtc, int ArtistId, string ArtistName,
        int ServiceId, string ServiceName, int ClientId, string ClientName, string? Phone, string? Email, long? TelegramUserId,
        Channel Source, bool WithGuardian, bool NeedsPrivateRoom, TattooDetails? Tattoo, string? ClientNotes, string? StaffNotes,
        DateTime CreatedAtUtc, string? DeclineOrCancelReason);

    private static AdminBookingDto ToDto(Booking b) => new(
        b.Id, b.Status, b.StartUtc, b.EndUtc, b.ArtistId, b.Artist.Name, b.ServiceId, b.Service.Name.Get(Languages.Russian),
        b.ClientId, b.Client.Name, b.Client.Phone, b.Client.Email, b.Client.TelegramUserId, b.Source, b.WithGuardian,
        b.NeedsPrivateRoom, b.Tattoo, b.ClientNotes, b.StaffNotes, b.CreatedAtUtc, b.DeclineOrCancelReason);

    /// <summary>Bookings starting within local dates from..to (max 42 days), all statuses.</summary>
    private static async Task<IResult> ListBookings(DateOnly from, DateOnly to, WsDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        if (to < from || to.DayNumber - from.DayNumber > 42) return Invalid("to", "Range must be 0-42 days.");
        var salonId = SalonId(user);
        var zone = await ZoneAsync(db, salonId, ct);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(from.ToDateTime(TimeOnly.MinValue), zone);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(to.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);

        var list = await db.Bookings.AsNoTracking()
            .Where(b => b.SalonId == salonId && b.StartUtc >= fromUtc && b.StartUtc < toUtc)
            .Include(b => b.Artist).Include(b => b.Service).Include(b => b.Client)
            .OrderBy(b => b.StartUtc)
            .ToListAsync(ct);
        return Results.Ok(list.Select(ToDto));
    }

    public sealed record StaffBookingRequest(
        int ServiceId, int ArtistId, DateTime StartUtc, string ClientName, string? Phone, string? Email,
        AgeGroup AgeGroup, string? Notes, TattooDetails? Tattoo);

    /// <summary>Staff books directly (e.g. a tattoo session after the consultation): confirmed right away.</summary>
    private static async Task<IResult> CreateBooking(StaffBookingRequest req, BookingService bookings, WsDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ClientName)) return Invalid("clientName", "Name is required.");
        var result = await bookings.CreateAsync(new NewBooking
        {
            SalonId = SalonId(user),
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
        if (!result.Ok) return BookingError(result.Error);
        await db.Entry(result.Booking!).Reference(b => b.Client).LoadAsync(ct);
        return Results.Ok(ToDto(result.Booking!));
    }

    public sealed record DecisionRequest(string? Reason);

    private static async Task<IResult> DecideBooking(
        int id, string action, DecisionRequest? body, BookingService bookings, WsDbContext db, ClientNotifier notifier,
        ClaimsPrincipal user, CancellationToken ct)
    {
        var salonId = SalonId(user);
        if (!await db.Bookings.AnyAsync(b => b.Id == id && b.SalonId == salonId, ct)) return Results.NotFound();

        var reason = body?.Reason is { Length: > 0 } r ? r : null;
        var result = action switch
        {
            "approve" => await bookings.ApproveAsync(id, ct),
            "decline" => await bookings.DeclineAsync(id, reason ?? $"Declined by {StaffName(user)}", ct),
            "cancel" => await bookings.CancelAsync(id, reason ?? $"Cancelled by {StaffName(user)}", ct),
            "complete" => await bookings.CompleteAsync(id, ct),
            "no-show" => await bookings.MarkNoShowAsync(id, ct),
            _ => null,
        };
        if (result == null) return Results.NotFound();
        if (!result.Ok) return BookingError(result.Error);

        // Clients on a messenger hear about approvals/declines/cancellations; others get a call from staff.
        await notifier.BookingDecidedAsync(result.Booking!, ct);
        return Results.Ok(ToDto(result.Booking!));
    }

    public sealed record NotesRequest(string? StaffNotes);

    private static async Task<IResult> UpdateNotes(int id, NotesRequest req, WsDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var salonId = SalonId(user);
        var booking = await db.Bookings.FirstOrDefaultAsync(b => b.Id == id && b.SalonId == salonId, ct);
        if (booking == null) return Results.NotFound();
        booking.StaffNotes = string.IsNullOrWhiteSpace(req.StaffNotes) ? null : req.StaffNotes.Trim();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Staff availability: any service (incl. tattoo sessions), no lead time or horizon limits.</summary>
    private static async Task<IResult> Availability(
        int serviceId, int? artistId, DateOnly from, DateOnly? to, BookingService bookings, ClaimsPrincipal user, CancellationToken ct)
    {
        var until = to ?? from;
        if (until < from || until.DayNumber - from.DayNumber > 31) return Invalid("to", "Range must be 0-31 days.");
        var result = await bookings.GetAvailabilityAsync(SalonId(user), serviceId, artistId, from, until, staff: true, ct);
        return Results.Ok(result.Select(r => new { r.ArtistId, r.ArtistName, Slots = r.SlotsUtc }));
    }

    private static IResult BookingError(BookingError error) => error switch
    {
        Core.Scheduling.BookingError.NotFound => Results.NotFound(),
        Core.Scheduling.BookingError.SlotTaken => Results.Problem(statusCode: 409, title: "This time is not available", type: error.ToString()),
        Core.Scheduling.BookingError.InvalidState => Results.Problem(statusCode: 409, title: "Booking is not in a state that allows this", type: error.ToString()),
        _ => Results.Problem(statusCode: 400, title: error.ToString(), type: error.ToString()),
    };

    // ---------- Artists ----------

    public sealed record Localized(string En, string Ru);
    public sealed record HoursDto(DayOfWeek Day, TimeOnly Start, TimeOnly End);
    public sealed record TimeOffDto(int Id, DateTime StartUtc, DateTime EndUtc, string? Reason);

    public sealed record AdminArtistDto(
        int Id, string Name, ArtistSpecialty Specialty, Localized Bio, List<string> Styles, string? PhotoUrl,
        string? InstagramHandle, string? TelegramUsername, bool IsActive, int SortOrder,
        List<HoursDto> Hours, List<TimeOffDto> TimeOff, List<int> ServiceIds);

    public sealed record ArtistEdit(
        string Name, ArtistSpecialty Specialty, Localized Bio, List<string>? Styles, string? PhotoUrl,
        string? InstagramHandle, string? TelegramUsername, bool IsActive, int SortOrder,
        List<HoursDto>? Hours, List<int>? ServiceIds);

    private static async Task<IResult> ListArtists(WsDbContext db, TimeProvider clock, ClaimsPrincipal user, CancellationToken ct)
    {
        var salonId = SalonId(user);
        var now = clock.GetUtcNow().UtcDateTime;
        var artists = await db.Artists.AsNoTracking()
            .Where(a => a.SalonId == salonId)
            .Include(a => a.WorkingHours).Include(a => a.TimeOff).Include(a => a.Services)
            .OrderBy(a => a.SortOrder)
            .ToListAsync(ct);
        return Results.Ok(artists.Select(a => new AdminArtistDto(
            a.Id, a.Name, a.Specialty, new Localized(a.Bio.En, a.Bio.Ru), a.Styles, a.PhotoUrl, a.InstagramHandle,
            a.TelegramUsername, a.IsActive, a.SortOrder,
            a.WorkingHours.OrderBy(h => ((int)h.Day + 6) % 7).ThenBy(h => h.Start).Select(h => new HoursDto(h.Day, h.Start, h.End)).ToList(),
            a.TimeOff.Where(t => t.EndUtc > now).OrderBy(t => t.StartUtc).Select(t => new TimeOffDto(t.Id, t.StartUtc, t.EndUtc, t.Reason)).ToList(),
            a.Services.Select(s => s.ServiceId).ToList())));
    }

    private static async Task<IResult> SaveArtist(int? id, ArtistEdit edit, WsDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(edit.Name)) return Invalid("name", "Name is required.");
        if (edit.Hours?.Any(h => h.End <= h.Start) == true) return Invalid("hours", "Each shift must end after it starts.");
        var salonId = SalonId(user);

        Artist? artist;
        if (id == null)
        {
            artist = new Artist { SalonId = salonId };
            db.Artists.Add(artist);
        }
        else
        {
            artist = await db.Artists.Include(a => a.WorkingHours).Include(a => a.Services)
                .FirstOrDefaultAsync(a => a.Id == id && a.SalonId == salonId, ct);
            if (artist == null) return Results.NotFound();
        }

        artist.Name = edit.Name.Trim();
        artist.Specialty = edit.Specialty;
        artist.Bio = new LocalizedText(edit.Bio.En.Trim(), edit.Bio.Ru.Trim());
        artist.Styles = edit.Styles?.Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().ToList() ?? [];
        artist.PhotoUrl = Blank(edit.PhotoUrl);
        artist.InstagramHandle = Blank(edit.InstagramHandle)?.TrimStart('@');
        artist.TelegramUsername = Blank(edit.TelegramUsername)?.TrimStart('@');
        artist.IsActive = edit.IsActive;
        artist.SortOrder = edit.SortOrder;

        if (edit.Hours != null)
        {
            db.WorkingHours.RemoveRange(artist.WorkingHours);
            artist.WorkingHours = edit.Hours.Select(h => new WorkingHours { Day = h.Day, Start = h.Start, End = h.End }).ToList();
        }

        if (edit.ServiceIds != null)
        {
            var valid = await db.Services.Where(s => s.SalonId == salonId && edit.ServiceIds.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct);
            artist.Services.RemoveAll(s => !valid.Contains(s.ServiceId));
            foreach (var sid in valid.Where(v => artist.Services.All(s => s.ServiceId != v)))
                artist.Services.Add(new ArtistService { ServiceId = sid, Artist = artist });
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(new { artist.Id });
    }

    public sealed record TimeOffRequest(DateTime StartLocal, DateTime EndLocal, string? Reason);

    /// <summary>Days off / vacations, entered in salon-local time.</summary>
    private static async Task<IResult> AddTimeOff(int id, TimeOffRequest req, WsDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var salonId = SalonId(user);
        if (!await db.Artists.AnyAsync(a => a.Id == id && a.SalonId == salonId, ct)) return Results.NotFound();
        if (req.EndLocal <= req.StartLocal) return Invalid("endLocal", "End must be after start.");

        var zone = await ZoneAsync(db, salonId, ct);
        var off = new TimeOff
        {
            ArtistId = id,
            StartUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(req.StartLocal, DateTimeKind.Unspecified), zone),
            EndUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(req.EndLocal, DateTimeKind.Unspecified), zone),
            Reason = Blank(req.Reason),
        };
        db.TimeOff.Add(off);
        await db.SaveChangesAsync(ct);
        return Results.Ok(new TimeOffDto(off.Id, off.StartUtc, off.EndUtc, off.Reason));
    }

    private static async Task<IResult> DeleteTimeOff(int id, WsDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var salonId = SalonId(user);
        var off = await db.TimeOff.FirstOrDefaultAsync(t => t.Id == id && db.Artists.Any(a => a.Id == t.ArtistId && a.SalonId == salonId), ct);
        if (off == null) return Results.NotFound();
        db.TimeOff.Remove(off);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ---------- Services ----------

    public sealed record AdminServiceDto(
        int Id, ServiceKind Kind, Localized Name, Localized Description, int DurationMinutes, int BufferMinutes,
        decimal? PriceFrom, decimal? PriceTo, bool NeedsPrivateRoom, bool AdultsOnly, bool BookableOnline, bool IsActive,
        int SortOrder, List<int> ArtistIds);

    public sealed record ServiceEdit(
        ServiceKind Kind, Localized Name, Localized Description, int DurationMinutes, int BufferMinutes,
        decimal? PriceFrom, decimal? PriceTo, bool NeedsPrivateRoom, bool AdultsOnly, bool BookableOnline, bool IsActive, int SortOrder);

    private static async Task<IResult> ListServices(WsDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var salonId = SalonId(user);
        var services = await db.Services.AsNoTracking().Where(s => s.SalonId == salonId).Include(s => s.Artists).OrderBy(s => s.SortOrder).ToListAsync(ct);
        return Results.Ok(services.Select(s => new AdminServiceDto(
            s.Id, s.Kind, new Localized(s.Name.En, s.Name.Ru), new Localized(s.Description.En, s.Description.Ru),
            s.DurationMinutes, s.BufferMinutes, s.PriceFrom, s.PriceTo, s.NeedsPrivateRoom, s.AdultsOnly, s.BookableOnline,
            s.IsActive, s.SortOrder, s.Artists.Select(a => a.ArtistId).ToList())));
    }

    private static async Task<IResult> SaveService(int? id, ServiceEdit edit, WsDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(edit.Name.En) && string.IsNullOrWhiteSpace(edit.Name.Ru)) return Invalid("name", "Name is required.");
        if (edit.DurationMinutes is < 5 or > 720) return Invalid("durationMinutes", "Duration must be 5-720 minutes.");
        if (edit.BufferMinutes is < 0 or > 240) return Invalid("bufferMinutes", "Buffer must be 0-240 minutes.");
        if (edit.PriceFrom < 0 || edit.PriceTo < edit.PriceFrom) return Invalid("priceTo", "Price range is invalid.");
        var salonId = SalonId(user);

        Service? service;
        if (id == null)
        {
            service = new Service { SalonId = salonId };
            db.Services.Add(service);
        }
        else
        {
            service = await db.Services.FirstOrDefaultAsync(s => s.Id == id && s.SalonId == salonId, ct);
            if (service == null) return Results.NotFound();
        }

        service.Kind = edit.Kind;
        // Either language may be filled first; the other falls back to it.
        var nameEn = edit.Name.En.Trim();
        var nameRu = edit.Name.Ru.Trim();
        service.Name = new LocalizedText(nameEn.Length > 0 ? nameEn : nameRu, nameRu);
        service.Description = new LocalizedText(edit.Description.En.Trim(), edit.Description.Ru.Trim());
        service.DurationMinutes = edit.DurationMinutes;
        service.BufferMinutes = edit.BufferMinutes;
        service.PriceFrom = edit.PriceFrom;
        service.PriceTo = edit.PriceTo;
        service.NeedsPrivateRoom = edit.NeedsPrivateRoom;
        service.AdultsOnly = edit.AdultsOnly;
        service.BookableOnline = edit.BookableOnline;
        service.IsActive = edit.IsActive;
        service.SortOrder = edit.SortOrder;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { service.Id });
    }

    // ---------- Conversations ----------

    public sealed record ConversationSummary(
        int Id, Channel Channel, string? ClientName, string? ClientPhone, bool NeedsHuman, string? HandoffReason,
        DateTime LastMessageAtUtc, string? LastMessage, int MessageCount);

    public sealed record TranscriptMessage(MessageRole Role, string Text, DateTime AtUtc);

    private static async Task<IResult> ListConversations(bool? needsHuman, WsDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var salonId = SalonId(user);
        var query = db.Conversations.AsNoTracking().Where(c => c.SalonId == salonId);
        if (needsHuman == true) query = query.Where(c => c.NeedsHuman);

        var list = await query
            .OrderByDescending(c => c.NeedsHuman).ThenByDescending(c => c.LastMessageAtUtc)
            .Take(200)
            .Select(c => new ConversationSummary(
                c.Id, c.Channel,
                db.Clients.Where(cl => cl.Id == c.ClientId).Select(cl => cl.Name).FirstOrDefault(),
                db.Clients.Where(cl => cl.Id == c.ClientId).Select(cl => cl.Phone).FirstOrDefault(),
                c.NeedsHuman, c.HandoffReason, c.LastMessageAtUtc,
                c.Messages.Where(m => m.Role == MessageRole.User && m.Text != "").OrderByDescending(m => m.Id).Select(m => m.Text).FirstOrDefault(),
                c.Messages.Count(m => m.Role != MessageRole.ToolResult)))
            .ToListAsync(ct);
        return Results.Ok(list);
    }

    private static async Task<IResult> GetConversation(int id, WsDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var salonId = SalonId(user);
        var conv = await db.Conversations.AsNoTracking()
            .Include(c => c.Messages.OrderBy(m => m.Id))
            .FirstOrDefaultAsync(c => c.Id == id && c.SalonId == salonId, ct);
        if (conv == null) return Results.NotFound();
        var client = conv.ClientId == null ? null : await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conv.ClientId, ct);

        return Results.Ok(new
        {
            conv.Id,
            conv.Channel,
            conv.ExternalId,
            conv.Language,
            conv.NeedsHuman,
            conv.HandoffReason,
            ClientName = client?.Name,
            ClientPhone = client?.Phone,
            ClientTelegramUserId = client?.TelegramUserId,
            Messages = conv.Messages
                .Where(m => m.Role != MessageRole.ToolResult && m.Text.Length > 0)
                .Select(m => new TranscriptMessage(m.Role, m.Text, m.CreatedAtUtc)),
        });
    }

    private static async Task<IResult> ResolveConversation(int id, WsDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var salonId = SalonId(user);
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id && c.SalonId == salonId, ct);
        if (conv == null) return Results.NotFound();
        conv.NeedsHuman = false;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<TimeZoneInfo> ZoneAsync(WsDbContext db, int salonId, CancellationToken ct) =>
        TimeZoneInfo.FindSystemTimeZoneById(await db.Salons.Where(s => s.Id == salonId).Select(s => s.TimeZoneId).SingleAsync(ct));

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
