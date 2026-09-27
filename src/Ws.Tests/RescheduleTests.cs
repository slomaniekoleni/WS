using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Notifications;
using Ws.Core.Scheduling;

namespace Ws.Tests;

/// <summary>Moving bookings: client moves go back to Pending (24h rule), staff moves keep the status.</summary>
public sealed class RescheduleTests : IAsyncLifetime
{
    private sealed class FakeNotifier : IStaffNotifier
    {
        public List<BookingNotice> Bookings { get; } = [];
        public void BookingRequested(BookingNotice notice) => Bookings.Add(notice);
        public void HandoffRequested(HandoffNotice notice) { }
    }

    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 6, 0, 0, TimeSpan.Zero)); // Sunday
    private readonly FakeNotifier _notifier = new();
    private WsDbContext _db = null!;
    private BookingService _svc = null!;

    // Monday 2026-10-05 15:00 in Minsk (UTC+3) = 12:00 UTC, 30 h after the clock. Both piercers (Kate, Leo) work Mondays.
    private static readonly DateTime MondayNoonUtc = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _db = new WsDbContext(new DbContextOptionsBuilder<WsDbContext>().UseSqlite(_conn).Options);
        await _db.Database.MigrateAsync();
        await SeedData.EnsureSeededAsync(_db);
        _svc = new BookingService(_db, _clock, _notifier);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private async Task<int> ArtistId(string name) => (await _db.Artists.SingleAsync(a => a.Name == name)).Id;

    private async Task<Booking> Confirmed(string artist, DateTime startUtc, string phone = "+375291111111")
    {
        var serviceId = (await _db.Services.ToListAsync()).Single(s => s.Name.En == "Earlobe piercing").Id;
        var result = await _svc.CreateAsync(new NewBooking
        {
            SalonId = 1, ServiceId = serviceId, ArtistId = await ArtistId(artist), StartUtc = startUtc,
            ClientName = "Test Client", Phone = phone, AgeGroup = AgeGroup.Adult, Source = Channel.Telegram,
        });
        Assert.True(result.Ok);
        await _svc.ApproveAsync(result.Booking!.Id);
        _notifier.Bookings.Clear();
        return result.Booking;
    }

    [Fact]
    public async Task Client_move_goes_back_to_pending_frees_the_old_time_and_tells_staff()
    {
        var booking = await Confirmed("Leo", MondayNoonUtc);

        var moved = await _svc.RescheduleAsync(booking.Id, MondayNoonUtc.AddHours(2), null, staff: false);

        Assert.True(moved.Ok);
        Assert.Equal(BookingStatus.Pending, moved.Booking!.Status);
        Assert.Equal(MondayNoonUtc.AddHours(2), moved.Booking.StartUtc);
        Assert.Equal(MondayNoonUtc.AddHours(2).AddMinutes(45), moved.Booking.BlockedUntilUtc); // 30 min + 15 buffer kept
        Assert.Equal(MondayNoonUtc, moved.Booking.RescheduledFromUtc);

        var notice = Assert.Single(_notifier.Bookings);
        Assert.Equal(new DateTime(2026, 10, 5, 15, 0, 0), notice.RescheduledFromLocal); // Minsk time

        // Someone else can take the old time now.
        var serviceId = booking.ServiceId;
        var slots = await _svc.GetAvailabilityAsync(1, serviceId, booking.ArtistId, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5));
        Assert.Contains(MondayNoonUtc, slots.Single().SlotsUtc);
    }

    [Fact]
    public async Task A_booking_can_move_into_its_own_time()
    {
        var booking = await Confirmed("Leo", MondayNoonUtc);

        // 30 minutes later overlaps the booking itself: that must not count as taken.
        var slots = await _svc.GetRescheduleSlotsAsync(booking.Id, null, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5));
        Assert.Contains(MondayNoonUtc.AddMinutes(30), slots.Single(a => a.ArtistId == booking.ArtistId).SlotsUtc);

        Assert.True((await _svc.RescheduleAsync(booking.Id, MondayNoonUtc.AddMinutes(30), null, staff: false)).Ok);
    }

    [Fact]
    public async Task Cannot_move_onto_someone_elses_booking()
    {
        var mine = await Confirmed("Leo", MondayNoonUtc);
        await Confirmed("Leo", MondayNoonUtc.AddHours(3), phone: "+375292222222");

        var result = await _svc.RescheduleAsync(mine.Id, MondayNoonUtc.AddHours(3), null, staff: false);

        Assert.Equal(BookingError.SlotTaken, result.Error);
        Assert.Equal(MondayNoonUtc, (await _db.Bookings.SingleAsync(b => b.Id == mine.Id)).StartUtc);
    }

    [Fact]
    public async Task Clients_cannot_move_within_24h_but_staff_can()
    {
        var booking = await Confirmed("Leo", MondayNoonUtc);
        _clock.SetUtcNow(new DateTimeOffset(MondayNoonUtc.AddHours(-12)));

        var client = await _svc.RescheduleAsync(booking.Id, MondayNoonUtc.AddHours(2), null, staff: false);
        Assert.Equal(BookingError.TooLateToChange, client.Error);

        var staff = await _svc.RescheduleAsync(booking.Id, MondayNoonUtc.AddHours(2), null, staff: true);
        Assert.True(staff.Ok);
        Assert.Equal(BookingStatus.Confirmed, staff.Booking!.Status); // staff moves need no approval
        Assert.Null(staff.Booking.RescheduledFromUtc);
        Assert.Empty(_notifier.Bookings);
    }

    [Fact]
    public async Task Moving_to_another_artist_who_does_the_service()
    {
        var booking = await Confirmed("Leo", MondayNoonUtc);
        var kate = await ArtistId("Kate");

        var result = await _svc.RescheduleAsync(booking.Id, MondayNoonUtc, kate, staff: true);

        Assert.True(result.Ok);
        Assert.Equal(kate, result.Booking!.ArtistId);
        Assert.Equal("Kate", result.Booking.Artist.Name);
    }

    [Fact]
    public async Task Reminders_are_sent_again_for_the_new_time()
    {
        var booking = await Confirmed("Leo", MondayNoonUtc);
        booking.Reminder24hSentAtUtc = _clock.GetUtcNow().UtcDateTime;
        await _db.SaveChangesAsync();

        var result = await _svc.RescheduleAsync(booking.Id, MondayNoonUtc.AddDays(7), null, staff: true);

        Assert.True(result.Ok);
        Assert.Null(result.Booking!.Reminder24hSentAtUtc);
    }

    [Fact]
    public async Task Finished_bookings_cannot_be_moved()
    {
        var booking = await Confirmed("Leo", MondayNoonUtc);
        await _svc.CancelAsync(booking.Id, "changed mind");

        var result = await _svc.RescheduleAsync(booking.Id, MondayNoonUtc.AddHours(2), null, staff: true);
        Assert.Equal(BookingError.InvalidState, result.Error);
    }
}
