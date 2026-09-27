using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Ws.Api.Telegram;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Scheduling;
using static Ws.Api.Telegram.TelegramApi;

namespace Ws.Tests;

/// <summary>Client reschedule buttons in Telegram, with the Bot API faked (every call is recorded).</summary>
public sealed class RescheduleFlowTests : IAsyncLifetime
{
    /// <summary>Records Bot API calls and answers ok.</summary>
    private sealed class FakeTelegram : HttpMessageHandler, IHttpClientFactory
    {
        public List<(string Method, JsonElement Body)> Calls { get; } = [];

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
            Calls.Add((request.RequestUri!.Segments[^1], body));
            const string ok = """{"ok":true,"result":{"message_id":10,"chat":{"id":555,"type":"private"}}}""";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ok, Encoding.UTF8, "application/json") };
        }

        public IEnumerable<string> Texts(string method) =>
            Calls.Where(c => c.Method == method).Select(c => c.Body.GetProperty("text").GetString()!);
    }

    private const long ClientTelegramId = 555;
    private static readonly DateTime MondayNoonUtc = new(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc); // 15:00 Minsk

    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 6, 0, 0, TimeSpan.Zero)); // Sunday, 30 h before
    private readonly FakeTelegram _telegram = new();
    private WsDbContext _db = null!;
    private RescheduleFlow _flow = null!;
    private int _bookingId;

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _db = new WsDbContext(new DbContextOptionsBuilder<WsDbContext>().UseSqlite(_conn).Options);
        await _db.Database.MigrateAsync();
        await SeedData.EnsureSeededAsync(_db);

        var bookings = new BookingService(_db, _clock);
        var api = new TelegramApi(_telegram, Options.Create(new TelegramOptions { BotToken = "test" }), NullLogger<TelegramApi>.Instance);
        _flow = new RescheduleFlow(api, _db, bookings, _clock);

        var result = await bookings.CreateAsync(new NewBooking
        {
            SalonId = 1,
            ServiceId = (await _db.Services.ToListAsync()).Single(s => s.Name.En == "Earlobe piercing").Id,
            ArtistId = (await _db.Artists.SingleAsync(a => a.Name == "Leo")).Id,
            StartUtc = MondayNoonUtc,
            ClientName = "Anna",
            TelegramUserId = ClientTelegramId,
            Language = "ru",
            AgeGroup = AgeGroup.Adult,
            Source = Channel.Telegram,
        });
        _bookingId = result.Booking!.Id;
        await bookings.ApproveAsync(_bookingId);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private Task Tap(string data, long from = ClientTelegramId) => _flow.HandleAsync(new CallbackQuery(
        "cb1", new User(from, "Anna", null, "ru"), new Message(10, new Chat(from, "private", null), null, null), data), default);

    private static long UnixMinutes(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeSeconds() / 60;

    private async Task<Booking> Reload() => await _db.Bookings.AsNoTracking().SingleAsync(b => b.Id == _bookingId);

    [Fact]
    public async Task Start_shows_days_with_free_time_as_buttons()
    {
        await Tap($"rs:{_bookingId}");

        var sent = Assert.Single(_telegram.Calls, c => c.Method == "sendMessage").Body;
        Assert.Contains("Выберите новый день", sent.GetProperty("text").GetString());
        var buttons = sent.GetProperty("reply_markup").GetProperty("inline_keyboard").EnumerateArray()
            .SelectMany(r => r.EnumerateArray()).Select(b => b.GetProperty("callback_data").GetString()).ToList();
        Assert.Contains($"rsd:{_bookingId}:20261005", buttons); // Monday: Leo works, own time counts as free
    }

    [Fact]
    public async Task Confirming_a_time_moves_the_booking_back_to_pending()
    {
        var newStart = MondayNoonUtc.AddHours(2);

        await Tap($"rsk:{_bookingId}:{UnixMinutes(newStart)}");

        var booking = await Reload();
        Assert.Equal(newStart, booking.StartUtc);
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Contains("Запрос на перенос отправлен", Assert.Single(_telegram.Texts("editMessageText")));
    }

    [Fact]
    public async Task Someone_elses_booking_is_ignored()
    {
        await Tap($"rsk:{_bookingId}:{UnixMinutes(MondayNoonUtc.AddHours(2))}", from: 777);

        Assert.Equal(MondayNoonUtc, (await Reload()).StartUtc);
        Assert.DoesNotContain(_telegram.Calls, c => c.Method is "sendMessage" or "editMessageText");
    }

    [Fact]
    public async Task Within_24h_the_client_is_asked_to_call()
    {
        _clock.SetUtcNow(new DateTimeOffset(MondayNoonUtc.AddHours(-10)));

        await Tap($"rs:{_bookingId}");

        Assert.Contains("не позже чем за 24", Assert.Single(_telegram.Texts("sendMessage")));
        Assert.Equal(BookingStatus.Confirmed, (await Reload()).Status);
    }
}
