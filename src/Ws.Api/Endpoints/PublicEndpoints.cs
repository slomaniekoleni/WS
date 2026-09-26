using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Scheduling;

namespace Ws.Api.Endpoints;

/// <summary>Read-only salon data + booking for the public website. Text is returned in ?lang=en|ru.</summary>
public static class PublicEndpoints
{
    public static void MapPublicApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/salon", GetSalon);
        api.MapGet("/services", GetServices);
        api.MapGet("/artists", GetArtists);
        api.MapGet("/availability", GetAvailability);
        api.MapPost("/bookings", CreateBooking);
    }

    public sealed record SalonDto(
        string Name, string Address, string Phone, string? Instagram, string About, string Policies, string Currency,
        string TimeZone, int MinAgeWithGuardian, int MinAgeSolo, IReadOnlyList<HoursDto> OpeningHours);

    public sealed record HoursDto(DayOfWeek Day, TimeOnly Open, TimeOnly Close);

    public sealed record ServiceDto(
        int Id, ServiceKind Kind, string Name, string Description, int DurationMinutes, decimal? PriceFrom, decimal? PriceTo,
        bool BookableOnline, bool AdultsOnly, IReadOnlyList<int> ArtistIds);

    public sealed record ArtistDto(
        int Id, string Name, ArtistSpecialty Specialty, string Bio, IReadOnlyList<string> Styles, string? PhotoUrl,
        string? Instagram, IReadOnlyList<PortfolioDto> Portfolio, IReadOnlyList<int> ServiceIds);

    public sealed record PortfolioDto(string Url, string? Style, string Caption);

    public sealed record AvailabilityDto(int ArtistId, string ArtistName, IReadOnlyList<DateTime> Slots);

    public sealed record CreateBookingRequest(
        int ServiceId, int ArtistId, DateTime StartUtc, string ClientName, string? Phone, string? Email,
        AgeGroup AgeGroup, string? Language, string? Notes, TattooDetails? Tattoo);

    public sealed record BookingDto(int Id, BookingStatus Status, DateTime StartUtc, DateTime EndUtc, string ArtistName, string ServiceName);

    private static async Task<IResult> GetSalon(WsDbContext db, IOptions<WsOptions> o, string? lang, CancellationToken ct)
    {
        var s = await db.Salons.AsNoTracking().Include(x => x.OpeningHours).FirstOrDefaultAsync(x => x.Id == o.Value.SalonId, ct);
        if (s == null) return Results.NotFound();
        return Results.Ok(new SalonDto(
            s.Name, s.Address.Get(lang), s.Phone, s.Instagram, s.About.Get(lang), s.Policies.Get(lang), s.Currency,
            s.TimeZoneId, s.MinAgeWithGuardian, s.MinAgeSolo,
            s.OpeningHours.OrderBy(h => ((int)h.Day + 6) % 7).Select(h => new HoursDto(h.Day, h.Open, h.Close)).ToList()));
    }

    private static async Task<IResult> GetServices(WsDbContext db, IOptions<WsOptions> o, string? lang, CancellationToken ct)
    {
        var services = await db.Services.AsNoTracking()
            .Where(x => x.SalonId == o.Value.SalonId && x.IsActive)
            .Include(x => x.Artists)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);
        return Results.Ok(services.Select(x => new ServiceDto(
            x.Id, x.Kind, x.Name.Get(lang), x.Description.Get(lang), x.DurationMinutes, x.PriceFrom, x.PriceTo,
            x.BookableOnline, x.AdultsOnly, x.Artists.Select(a => a.ArtistId).ToList())));
    }

    private static async Task<IResult> GetArtists(WsDbContext db, IOptions<WsOptions> o, string? lang, CancellationToken ct)
    {
        var artists = await db.Artists.AsNoTracking()
            .Where(x => x.SalonId == o.Value.SalonId && x.IsActive)
            .Include(x => x.Portfolio).Include(x => x.Services)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);
        return Results.Ok(artists.Select(x => new ArtistDto(
            x.Id, x.Name, x.Specialty, x.Bio.Get(lang), x.Styles, x.PhotoUrl, x.InstagramHandle,
            x.Portfolio.OrderBy(p => p.SortOrder).Select(p => new PortfolioDto(p.Url, p.Style, p.Caption.Get(lang))).ToList(),
            x.Services.Select(s => s.ServiceId).ToList())));
    }

    /// <summary>Free start times (UTC) per artist for local dates from..to (max 31 days).</summary>
    private static async Task<IResult> GetAvailability(
        BookingService bookings, IOptions<WsOptions> o, int serviceId, int? artistId, DateOnly from, DateOnly? to, CancellationToken ct)
    {
        var until = to ?? from.AddDays(6);
        if (until < from || until.DayNumber - from.DayNumber > 31)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["Range must be 0-31 days."] });

        var result = await bookings.GetAvailabilityAsync(o.Value.SalonId, serviceId, artistId, from, until, ct: ct);
        return Results.Ok(result.Select(r => new AvailabilityDto(r.ArtistId, r.ArtistName, r.SlotsUtc)));
    }

    private static async Task<IResult> CreateBooking(
        BookingService bookings, IOptions<WsOptions> o, CreateBookingRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ClientName) || req.ClientName.Length > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["clientName"] = ["Name is required."] });

        var result = await bookings.CreateAsync(new NewBooking
        {
            SalonId = o.Value.SalonId,
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

        if (!result.Ok) return BookingErrorResult(result.Error);
        var b = result.Booking!;
        return Results.Created($"/api/bookings/{b.Id}", new BookingDto(
            b.Id, b.Status, b.StartUtc, b.EndUtc, b.Artist.Name, b.Service.Name.Get(req.Language)));
    }

    private static IResult BookingErrorResult(BookingError error) => error switch
    {
        BookingError.NotFound => Results.Problem(statusCode: 404, title: "Service not found", type: error.ToString()),
        BookingError.SlotTaken => Results.Problem(statusCode: 409, title: "This time is no longer available", type: error.ToString()),
        _ => Results.Problem(statusCode: 400, title: error.ToString(), type: error.ToString()),
    };
}
