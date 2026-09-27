using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Scheduling;

namespace Ws.Core.Receptionist;

/// <summary>Tool definitions (name, description, JSON schema) independent of the SDK types.</summary>
public sealed record ToolSpec(string Name, string Description, Dictionary<string, object> Properties, string[] Required);

public sealed record ToolOutcome(string Content, bool IsError = false)
{
    public static ToolOutcome Json(object value) => new(JsonSerializer.Serialize(value, JsonOpts));
    public static ToolOutcome Error(string message) => new(message, IsError: true);

    internal static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
}

/// <summary>The receptionist's actions. Times go in and out as salon-local "yyyy-MM-dd HH:mm".</summary>
public sealed class ReceptionistTools(WsDbContext db, BookingService bookings, TimeProvider clock)
{
    private const string LocalFormat = "yyyy-MM-dd HH:mm";

    public static readonly ToolSpec[] Specs =
    [
        new("get_availability",
            "Free start times for a service, per artist, over a date range (max 7 days). Times are salon-local.",
            new()
            {
                ["service_id"] = new { type = "integer" },
                ["artist_id"] = new { type = "integer", description = "Optional: only this artist." },
                ["date_from"] = new { type = "string", description = "yyyy-MM-dd, salon-local." },
                ["date_to"] = new { type = "string", description = "yyyy-MM-dd, inclusive. Defaults to date_from." },
            },
            ["service_id", "date_from"]),
        new("create_booking",
            "Books an appointment (pending until the artist confirms). Only after the client explicitly confirmed the summary.",
            new()
            {
                ["service_id"] = new { type = "integer" },
                ["artist_id"] = new { type = "integer" },
                ["start"] = new { type = "string", description = "Salon-local start, yyyy-MM-dd HH:mm, from get_availability." },
                ["client_name"] = new { type = "string" },
                ["phone"] = new { type = "string", description = "Client's phone number as they gave it." },
                ["age_group"] = new { type = "string", @enum = new[] { "adult", "minor_with_guardian" } },
                ["client_confirmed"] = new { type = "boolean", description = "True only if the client said yes to the summary." },
                ["notes"] = new { type = "string", description = "Anything the artist should know." },
                ["tattoo_idea"] = new { type = "string", description = "Consultations: the idea in the client's words." },
                ["tattoo_placement"] = new { type = "string" },
                ["tattoo_size"] = new { type = "string" },
                ["tattoo_style"] = new { type = "string" },
            },
            ["service_id", "artist_id", "start", "client_name", "phone", "age_group", "client_confirmed"]),
        new("find_my_bookings",
            "Upcoming bookings made with this phone number.",
            new() { ["phone"] = new { type = "string" } },
            ["phone"]),
        new("cancel_booking",
            "Cancels a booking. The phone must match the one the booking was made with.",
            new()
            {
                ["booking_id"] = new { type = "integer" },
                ["phone"] = new { type = "string" },
                ["reason"] = new { type = "string" },
            },
            ["booking_id", "phone"]),
        new("get_reschedule_options",
            "Free times an existing booking could move to (its own time counts as free). The phone must match the booking. Max 7 days.",
            new()
            {
                ["booking_id"] = new { type = "integer" },
                ["phone"] = new { type = "string" },
                ["date_from"] = new { type = "string", description = "yyyy-MM-dd, salon-local." },
                ["date_to"] = new { type = "string", description = "yyyy-MM-dd, inclusive. Defaults to date_from." },
                ["artist_id"] = new { type = "integer", description = "Optional: only this artist (default: every artist who does the service)." },
            },
            ["booking_id", "phone", "date_from"]),
        new("reschedule_booking",
            "Moves a booking to a new time (optionally another artist). It goes back to pending until the artist confirms. "
            + "Only after the client explicitly confirmed the new time.",
            new()
            {
                ["booking_id"] = new { type = "integer" },
                ["phone"] = new { type = "string" },
                ["new_start"] = new { type = "string", description = "Salon-local start, yyyy-MM-dd HH:mm, from get_reschedule_options." },
                ["artist_id"] = new { type = "integer", description = "Optional: move to this artist. Default: same artist." },
                ["client_confirmed"] = new { type = "boolean", description = "True only if the client said yes to the new time." },
            },
            ["booking_id", "phone", "new_start", "client_confirmed"]),
        new("request_human",
            "Flags this conversation for the salon team to take over (complaints, health concerns, quotes, anything unclear).",
            new() { ["reason"] = new { type = "string", description = "Short summary for the team." } },
            ["reason"]),
    ];

    public async Task<ToolOutcome> ExecuteAsync(
        string name, IReadOnlyDictionary<string, JsonElement> input, Conversation conversation, CancellationToken ct)
    {
        try
        {
            return name switch
            {
                "get_availability" => await GetAvailabilityAsync(input, conversation, ct),
                "create_booking" => await CreateBookingAsync(input, conversation, ct),
                "find_my_bookings" => await FindBookingsAsync(input, conversation, ct),
                "cancel_booking" => await CancelAsync(input, conversation, ct),
                "get_reschedule_options" => await RescheduleOptionsAsync(input, conversation, ct),
                "reschedule_booking" => await RescheduleAsync(input, conversation, ct),
                "request_human" => await RequestHumanAsync(input, conversation, ct),
                _ => ToolOutcome.Error($"Unknown tool {name}."),
            };
        }
        catch (ToolInputException ex)
        {
            return ToolOutcome.Error(ex.Message);
        }
    }

    private async Task<ToolOutcome> GetAvailabilityAsync(IReadOnlyDictionary<string, JsonElement> input, Conversation c, CancellationToken ct)
    {
        var from = Date(input, "date_from");
        var to = input.ContainsKey("date_to") ? Date(input, "date_to") : from;
        if (to < from) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > 6) to = from.AddDays(6);

        var serviceId = Int(input, "service_id");
        var service = await db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serviceId && s.SalonId == c.SalonId, ct);
        if (service == null) return ToolOutcome.Error("No such service.");
        if (!service.BookableOnline) return ToolOutcome.Error("This service is booked by the artist after a consultation, not online.");

        var zone = await ZoneAsync(c.SalonId, ct);
        var result = await bookings.GetAvailabilityAsync(c.SalonId, serviceId, OptionalInt(input, "artist_id"), from, to, ct: ct);
        return ToolOutcome.Json(result.Select(a => new
        {
            artist_id = a.ArtistId,
            artist = a.ArtistName,
            days = a.SlotsUtc
                .Select(s => TimeZoneInfo.ConvertTimeFromUtc(s, zone))
                .GroupBy(l => l.ToString("yyyy-MM-dd (dddd)", CultureInfo.InvariantCulture))
                .ToDictionary(g => g.Key, g => string.Join(" ", g.Select(l => l.ToString("HH:mm", CultureInfo.InvariantCulture)))),
        }));
    }

    private async Task<ToolOutcome> CreateBookingAsync(IReadOnlyDictionary<string, JsonElement> input, Conversation c, CancellationToken ct)
    {
        if (!input.TryGetValue("client_confirmed", out var confirmed) || confirmed.ValueKind != JsonValueKind.True)
            return ToolOutcome.Error("Show the client the booking summary and get an explicit yes first.");

        var zone = await ZoneAsync(c.SalonId, ct);
        if (!DateTime.TryParseExact(Str(input, "start"), LocalFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw new ToolInputException("start must be yyyy-MM-dd HH:mm.");
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(local, zone);

        var ageGroup = Str(input, "age_group") switch
        {
            "adult" => AgeGroup.Adult,
            "minor_with_guardian" => AgeGroup.MinorWithGuardian,
            _ => throw new ToolInputException("age_group must be adult or minor_with_guardian."),
        };

        TattooDetails? tattoo = null;
        if (input.Keys.Any(k => k.StartsWith("tattoo_")))
        {
            tattoo = new TattooDetails
            {
                Idea = OptionalStr(input, "tattoo_idea"),
                Placement = OptionalStr(input, "tattoo_placement"),
                Size = OptionalStr(input, "tattoo_size"),
                Style = OptionalStr(input, "tattoo_style"),
            };
        }

        var result = await bookings.CreateAsync(new NewBooking
        {
            SalonId = c.SalonId,
            ServiceId = Int(input, "service_id"),
            ArtistId = Int(input, "artist_id"),
            StartUtc = startUtc,
            ClientName = Str(input, "client_name"),
            Phone = Str(input, "phone"),
            TelegramUserId = c.Channel == Channel.Telegram && long.TryParse(c.ExternalId, out var tg) ? tg : null,
            Language = c.Language,
            AgeGroup = ageGroup,
            Source = c.Channel,
            Notes = OptionalStr(input, "notes"),
            Tattoo = tattoo,
        }, ct);

        if (!result.Ok)
        {
            return ToolOutcome.Error(result.Error switch
            {
                BookingError.SlotTaken => "That time is not available (taken, outside hours or too soon). Check get_availability again.",
                BookingError.InvalidPhone => "The phone number is not valid. Ask the client to check it.",
                BookingError.AdultsOnly => "This service is 18+ only.",
                BookingError.ServiceNotBookableOnline => "This service is booked by the artist after a consultation.",
                BookingError.ArtistDoesNotDoService => "This artist doesn't do this service.",
                _ => $"Booking failed: {result.Error}.",
            });
        }

        var b = result.Booking!;
        c.ClientId = b.ClientId;
        await db.SaveChangesAsync(ct);
        return ToolOutcome.Json(new
        {
            booking_id = b.Id,
            status = "pending_artist_confirmation",
            service = b.Service.Name.En,
            artist = b.Artist.Name,
            start = TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, zone).ToString(LocalFormat, CultureInfo.InvariantCulture),
            end = TimeZoneInfo.ConvertTimeFromUtc(b.EndUtc, zone).ToString(LocalFormat, CultureInfo.InvariantCulture),
        });
    }

    private async Task<ToolOutcome> FindBookingsAsync(IReadOnlyDictionary<string, JsonElement> input, Conversation c, CancellationToken ct)
    {
        var salon = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == c.SalonId, ct);
        var phone = PhoneFormat.Normalize(Str(input, "phone"), salon.Country);
        if (phone == null) return ToolOutcome.Error("The phone number is not valid.");

        var zone = TimeZoneInfo.FindSystemTimeZoneById(salon.TimeZoneId);
        var now = clock.GetUtcNow().UtcDateTime;
        var list = await db.Bookings.AsNoTracking()
            .Where(b => b.SalonId == c.SalonId && b.Client.Phone == phone && b.StartUtc > now && Booking.ActiveStatuses.Contains(b.Status))
            .Include(b => b.Service).Include(b => b.Artist)
            .OrderBy(b => b.StartUtc)
            .ToListAsync(ct);
        return ToolOutcome.Json(list.Select(b => new
        {
            booking_id = b.Id,
            status = b.Status == BookingStatus.Pending ? "pending_artist_confirmation" : "confirmed",
            service = b.Service.Name.En,
            artist = b.Artist.Name,
            start = TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, zone).ToString(LocalFormat, CultureInfo.InvariantCulture),
        }));
    }

    private async Task<ToolOutcome> CancelAsync(IReadOnlyDictionary<string, JsonElement> input, Conversation c, CancellationToken ct)
    {
        var bookingId = Int(input, "booking_id");
        if (!await OwnsBookingAsync(input, c, bookingId, ct)) return ToolOutcome.Error(NoSuchBooking);

        var result = await bookings.CancelAsync(bookingId, OptionalStr(input, "reason") ?? "Cancelled by client via chat", ct);
        return result.Ok
            ? ToolOutcome.Json(new { booking_id = bookingId, status = "cancelled" })
            : ToolOutcome.Error("This booking can't be cancelled (already cancelled or finished).");
    }

    private async Task<ToolOutcome> RescheduleOptionsAsync(IReadOnlyDictionary<string, JsonElement> input, Conversation c, CancellationToken ct)
    {
        var bookingId = Int(input, "booking_id");
        if (!await OwnsBookingAsync(input, c, bookingId, ct)) return ToolOutcome.Error(NoSuchBooking);

        var salon = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == c.SalonId, ct);
        var booking = await db.Bookings.AsNoTracking().SingleAsync(b => b.Id == bookingId, ct);
        if (!bookings.ClientCanChange(booking, salon)) return ToolOutcome.Error(TooLate(salon));

        var from = Date(input, "date_from");
        var to = input.ContainsKey("date_to") ? Date(input, "date_to") : from;
        if (to < from) (from, to) = (to, from);
        if (to.DayNumber - from.DayNumber > 6) to = from.AddDays(6);

        var zone = TimeZoneInfo.FindSystemTimeZoneById(salon.TimeZoneId);
        var result = await bookings.GetRescheduleSlotsAsync(bookingId, OptionalInt(input, "artist_id"), from, to, ct: ct);
        return ToolOutcome.Json(result.Select(a => new
        {
            artist_id = a.ArtistId,
            artist = a.ArtistName,
            days = a.SlotsUtc
                .Select(s => TimeZoneInfo.ConvertTimeFromUtc(s, zone))
                .GroupBy(l => l.ToString("yyyy-MM-dd (dddd)", CultureInfo.InvariantCulture))
                .ToDictionary(g => g.Key, g => string.Join(" ", g.Select(l => l.ToString("HH:mm", CultureInfo.InvariantCulture)))),
        }));
    }

    private async Task<ToolOutcome> RescheduleAsync(IReadOnlyDictionary<string, JsonElement> input, Conversation c, CancellationToken ct)
    {
        if (!input.TryGetValue("client_confirmed", out var confirmed) || confirmed.ValueKind != JsonValueKind.True)
            return ToolOutcome.Error("Tell the client the new time and get an explicit yes first.");

        var bookingId = Int(input, "booking_id");
        if (!await OwnsBookingAsync(input, c, bookingId, ct)) return ToolOutcome.Error(NoSuchBooking);

        var salon = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == c.SalonId, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(salon.TimeZoneId);
        if (!DateTime.TryParseExact(Str(input, "new_start"), LocalFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            throw new ToolInputException("new_start must be yyyy-MM-dd HH:mm.");

        var result = await bookings.RescheduleAsync(bookingId, TimeZoneInfo.ConvertTimeToUtc(local, zone),
            OptionalInt(input, "artist_id"), staff: false, ct);
        if (!result.Ok)
        {
            return ToolOutcome.Error(result.Error switch
            {
                BookingError.TooLateToChange => TooLate(salon),
                BookingError.SlotTaken => "That time is not available. Check get_reschedule_options again.",
                BookingError.ArtistDoesNotDoService => "This artist doesn't do this service.",
                BookingError.InvalidState => "This booking is no longer active (cancelled, declined or finished).",
                _ => $"Reschedule failed: {result.Error}.",
            });
        }

        var b = result.Booking!;
        return ToolOutcome.Json(new
        {
            booking_id = b.Id,
            status = "pending_artist_confirmation",
            service = b.Service.Name.En,
            artist = b.Artist.Name,
            start = TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, zone).ToString(LocalFormat, CultureInfo.InvariantCulture),
        });
    }

    private const string NoSuchBooking = "No booking with this number and phone.";

    private static string TooLate(Salon salon) =>
        $"Too close to the appointment to change it online (less than {salon.ClientChangeNoticeHours} h). "
        + $"The client should call the salon: {salon.Phone}.";

    /// <summary>
    /// The phone must be the one the booking was made with. Same answer for "no such booking" and "wrong phone",
    /// so bookings can't be probed.
    /// </summary>
    private async Task<bool> OwnsBookingAsync(IReadOnlyDictionary<string, JsonElement> input, Conversation c, int bookingId, CancellationToken ct)
    {
        var country = await db.Salons.Where(s => s.Id == c.SalonId).Select(s => s.Country).SingleAsync(ct);
        var phone = PhoneFormat.Normalize(Str(input, "phone"), country);
        var owner = await db.Bookings.AsNoTracking()
            .Where(b => b.Id == bookingId && b.SalonId == c.SalonId).Select(b => b.Client.Phone).FirstOrDefaultAsync(ct);
        return phone != null && owner == phone;
    }

    private async Task<ToolOutcome> RequestHumanAsync(IReadOnlyDictionary<string, JsonElement> input, Conversation c, CancellationToken ct)
    {
        c.NeedsHuman = true;
        c.HandoffReason = OptionalStr(input, "reason");
        await db.SaveChangesAsync(ct);
        return ToolOutcome.Json(new { status = "team_notified" });
    }

    private async Task<TimeZoneInfo> ZoneAsync(int salonId, CancellationToken ct) =>
        TimeZoneInfo.FindSystemTimeZoneById(await db.Salons.Where(s => s.Id == salonId).Select(s => s.TimeZoneId).SingleAsync(ct));

    private static string Str(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        OptionalStr(input, key) ?? throw new ToolInputException($"{key} is required.");

    private static string? OptionalStr(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        input.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static int Int(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        OptionalInt(input, key) ?? throw new ToolInputException($"{key} is required.");

    private static int? OptionalInt(IReadOnlyDictionary<string, JsonElement> input, string key)
    {
        if (!input.TryGetValue(key, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out n)) return n;
        return null;
    }

    private static DateOnly Date(IReadOnlyDictionary<string, JsonElement> input, string key) =>
        DateOnly.TryParseExact(Str(input, key), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new ToolInputException($"{key} must be yyyy-MM-dd.");

    private sealed class ToolInputException(string message) : Exception(message);
}
