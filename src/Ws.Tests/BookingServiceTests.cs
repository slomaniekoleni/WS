using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Scheduling;

namespace Ws.Tests;

/// <summary>BookingService against a real (in-memory) SQLite DB with the seed data.</summary>
public sealed class BookingServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 6, 0, 0, TimeSpan.Zero)); // Sunday
    private WsDbContext _db = null!;
    private BookingService _svc = null!;

    // Monday 2026-10-05 14:00 in Warsaw (CEST) = 12:00 UTC. Both piercers (Kate, Leo) work Mondays.
    private static readonly DateTime MondayNoonUtc = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _db = new WsDbContext(new DbContextOptionsBuilder<WsDbContext>().UseSqlite(_conn).Options);
        await _db.Database.MigrateAsync();
        await SeedData.EnsureSeededAsync(_db);
        _svc = new BookingService(_db, _clock);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private async Task<int> ServiceId(string englishName) =>
        (await _db.Services.ToListAsync()).Single(s => s.Name.En == englishName).Id;

    private async Task<int> ArtistId(string name) => (await _db.Artists.SingleAsync(a => a.Name == name)).Id;

    private async Task<BookingResult> Book(string service, string artist, DateTime startUtc,
        AgeGroup age = AgeGroup.Adult, Channel source = Channel.Website, string phone = "+1000") =>
        await _svc.CreateAsync(new NewBooking
        {
            SalonId = 1,
            ServiceId = await ServiceId(service),
            ArtistId = await ArtistId(artist),
            StartUtc = startUtc,
            ClientName = "Test Client",
            Phone = phone,
            AgeGroup = age,
            Source = source,
        });

    [Fact]
    public async Task Client_booking_is_pending_and_holds_the_slot()
    {
        var first = await Book("Earlobe piercing", "Leo", MondayNoonUtc);
        Assert.True(first.Ok);
        Assert.Equal(BookingStatus.Pending, first.Booking!.Status);
        Assert.Equal(MondayNoonUtc.AddMinutes(45), first.Booking.BlockedUntilUtc); // 30 min + 15 buffer

        var second = await Book("Earlobe piercing", "Leo", MondayNoonUtc.AddMinutes(30), phone: "+2000");
        Assert.Equal(BookingError.SlotTaken, second.Error);
    }

    [Fact]
    public async Task Declined_booking_frees_the_slot()
    {
        var first = await Book("Earlobe piercing", "Leo", MondayNoonUtc);
        await _svc.DeclineAsync(first.Booking!.Id, "sick");

        Assert.True((await Book("Earlobe piercing", "Leo", MondayNoonUtc, phone: "+2000")).Ok);
    }

    [Fact]
    public async Task Tattoo_sessions_are_staff_only_and_confirmed_immediately()
    {
        var online = await Book("Tattoo session: small (up to ~10 cm)", "Alex", MondayNoonUtc.AddDays(1));
        Assert.Equal(BookingError.ServiceNotBookableOnline, online.Error);

        var staff = await Book("Tattoo session: small (up to ~10 cm)", "Alex", MondayNoonUtc.AddDays(1), source: Channel.Admin);
        Assert.True(staff.Ok);
        Assert.Equal(BookingStatus.Confirmed, staff.Booking!.Status);
    }

    [Fact]
    public async Task Minors_cannot_book_adults_only_services()
    {
        var result = await Book("Nipple piercing", "Leo", MondayNoonUtc, AgeGroup.MinorWithGuardian);
        Assert.Equal(BookingError.AdultsOnly, result.Error);

        var ok = await Book("Earlobe piercing", "Leo", MondayNoonUtc, AgeGroup.MinorWithGuardian);
        Assert.True(ok.Ok);
        Assert.True(ok.Booking!.WithGuardian);
    }

    [Fact]
    public async Task Private_procedure_blocks_the_room_for_other_artists()
    {
        Assert.True((await Book("Nipple piercing", "Leo", MondayNoonUtc)).Ok);

        var kate = await Book("Earlobe piercing", "Kate", MondayNoonUtc, phone: "+2000");
        Assert.Equal(BookingError.SlotTaken, kate.Error);

        var slots = await _svc.GetAvailabilityAsync(1, await ServiceId("Earlobe piercing"), await ArtistId("Kate"),
            new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5));
        Assert.DoesNotContain(MondayNoonUtc, slots.Single().SlotsUtc);
        // 11:30 + 30 min + 15 min cleanup would run into the private procedure; 11:00 ends (with cleanup) at 11:45.
        Assert.DoesNotContain(MondayNoonUtc.AddMinutes(-30), slots.Single().SlotsUtc);
        Assert.Contains(MondayNoonUtc.AddMinutes(-60), slots.Single().SlotsUtc);
    }

    [Fact]
    public async Task Same_client_is_reused_by_phone()
    {
        await Book("Earlobe piercing", "Leo", MondayNoonUtc);
        await Book("Earlobe piercing", "Kate", MondayNoonUtc.AddHours(2));

        Assert.Equal(1, await _db.Clients.CountAsync());
    }

    [Fact]
    public async Task Booking_needs_some_contact()
    {
        var result = await Book("Earlobe piercing", "Leo", MondayNoonUtc, phone: "");
        Assert.Equal(BookingError.MissingContact, result.Error);
    }
}
