using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Ws.Api.Telegram;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Notifications;
using Ws.Core.Scheduling;

namespace Ws.Tests;

public sealed class NotificationTests : IAsyncLifetime
{
    private sealed class FakeNotifier : IStaffNotifier
    {
        public List<BookingNotice> Bookings { get; } = [];
        public List<HandoffNotice> Handoffs { get; } = [];
        public void BookingRequested(BookingNotice notice) => Bookings.Add(notice);
        public void HandoffRequested(HandoffNotice notice) => Handoffs.Add(notice);
    }

    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero)); // Thursday
    private readonly FakeNotifier _notifier = new();
    private WsDbContext _db = null!;
    private BookingService _bookings = null!;
    private int _earlobe, _leo;

    // Monday 2026-10-05 15:00 Minsk = 12:00 UTC.
    private static readonly DateTime MondayNoonUtc = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _db = new WsDbContext(new DbContextOptionsBuilder<WsDbContext>().UseSqlite(_conn).Options);
        await _db.Database.MigrateAsync();
        await SeedData.EnsureSeededAsync(_db);
        _bookings = new BookingService(_db, _clock, _notifier);
        _earlobe = (await _db.Services.ToListAsync()).Single(s => s.Name.En == "Earlobe piercing").Id;
        _leo = (await _db.Artists.SingleAsync(a => a.Name == "Leo")).Id;
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private async Task<Booking> Book(Channel source = Channel.Telegram, long? telegram = 555, string clientName = "Anna")
    {
        var result = await _bookings.CreateAsync(new NewBooking
        {
            SalonId = 1, ServiceId = _earlobe, ArtistId = _leo, StartUtc = MondayNoonUtc,
            ClientName = clientName, Phone = "+375291111111", TelegramUserId = telegram,
            AgeGroup = AgeGroup.Adult, Source = source, Language = "ru",
        });
        Assert.True(result.Ok, result.Error.ToString());
        return result.Booking!;
    }

    [Fact]
    public async Task Client_booking_requests_notify_staff()
    {
        var b = await Book();

        var notice = Assert.Single(_notifier.Bookings);
        Assert.Equal(b.Id, notice.BookingId);
        Assert.Equal("Прокол мочки уха", notice.ServiceName);
        Assert.Equal(new DateTime(2026, 10, 5, 15, 0, 0), notice.StartLocal); // salon-local
        Assert.Equal(555, notice.ClientTelegramUserId);
    }

    [Fact]
    public async Task Staff_made_bookings_do_not_ask_for_approval()
    {
        await Book(Channel.Admin);
        Assert.Empty(_notifier.Bookings);
    }

    [Fact]
    public async Task Reminders_follow_24h_and_2h_windows()
    {
        var b = await Book();
        await _bookings.ApproveAsync(b.Id);
        var reminders = new Reminders(_db, _clock);

        // Booked 4 days ahead. 3 days before: nothing yet.
        Assert.Empty(await reminders.GetDueAsync(default));

        _clock.SetUtcNow(MondayNoonUtc.AddHours(-20));
        var dayBefore = Assert.Single(await reminders.GetDueAsync(default));
        Assert.Equal(ReminderKind.DayBefore, dayBefore.Kind);
        await reminders.MarkSentAsync(dayBefore, default);
        Assert.Empty(await reminders.GetDueAsync(default));

        _clock.SetUtcNow(MondayNoonUtc.AddMinutes(-90));
        var twoHours = Assert.Single(await reminders.GetDueAsync(default));
        Assert.Equal(ReminderKind.TwoHoursBefore, twoHours.Kind);
        await reminders.MarkSentAsync(twoHours, default);
        Assert.Empty(await reminders.GetDueAsync(default));
    }

    [Fact]
    public async Task No_24h_reminder_for_last_minute_bookings()
    {
        _clock.SetUtcNow(MondayNoonUtc.AddHours(-10));
        var b = await Book();
        await _bookings.ApproveAsync(b.Id);

        Assert.Empty(await new Reminders(_db, _clock).GetDueAsync(default));
        _clock.SetUtcNow(MondayNoonUtc.AddHours(-1));
        Assert.Equal(ReminderKind.TwoHoursBefore, Assert.Single(await new Reminders(_db, _clock).GetDueAsync(default)).Kind);
    }

    [Fact]
    public async Task No_reminders_for_pending_bookings_or_clients_without_messenger()
    {
        await Book(); // still pending
        _clock.SetUtcNow(MondayNoonUtc.AddHours(-1));
        Assert.Empty(await new Reminders(_db, _clock).GetDueAsync(default));

        var web = await _bookings.CreateAsync(new NewBooking
        {
            SalonId = 1, ServiceId = _earlobe, ArtistId = _leo, StartUtc = MondayNoonUtc.AddHours(2),
            ClientName = "Web", Phone = "+375292222222", AgeGroup = AgeGroup.Adult, Source = Channel.Website,
        });
        await _bookings.ApproveAsync(web.Booking!.Id);
        _clock.SetUtcNow(MondayNoonUtc.AddHours(1));
        Assert.Empty(await new Reminders(_db, _clock).GetDueAsync(default));
    }

    [Fact]
    public async Task Staff_message_escapes_client_text()
    {
        await Book(clientName: "<b>Anna</b> & co");
        var html = StaffMessages.BookingRequest(_notifier.Bookings.Single());

        Assert.Contains("&lt;b&gt;Anna&lt;/b&gt; &amp; co", html);
        Assert.Contains("tg://user?id=555", html);
    }

    [Fact]
    public async Task Client_messages_use_the_client_language()
    {
        var b = await Book();
        var salon = await _db.Salons.SingleAsync();
        await _db.Entry(b).Reference(x => x.Client).LoadAsync();

        var text = ClientMessages.Confirmed(b, salon);
        Assert.Contains("Запись подтверждена", text);
        Assert.Contains("понедельник, 5 октября, 15:00", text);
    }
}
