using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Receptionist;
using Ws.Core.Scheduling;

namespace Ws.Tests;

public sealed class ReceptionistToolsTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 4, 6, 0, 0, TimeSpan.Zero)); // Sunday
    private WsDbContext _db = null!;
    private ReceptionistTools _tools = null!;
    private Conversation _conv = null!;
    private int _earlobe, _leo;

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _db = new WsDbContext(new DbContextOptionsBuilder<WsDbContext>().UseSqlite(_conn).Options);
        await _db.Database.MigrateAsync();
        await SeedData.EnsureSeededAsync(_db);
        _tools = new ReceptionistTools(_db, new BookingService(_db, _clock), _clock);
        _conv = new Conversation { SalonId = 1, Channel = Channel.WebChat, ExternalId = Guid.NewGuid().ToString(), Language = "ru" };
        _db.Conversations.Add(_conv);
        await _db.SaveChangesAsync();
        _earlobe = (await _db.Services.ToListAsync()).Single(s => s.Name.En == "Earlobe piercing").Id;
        _leo = (await _db.Artists.SingleAsync(a => a.Name == "Leo")).Id;
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private Task<ToolOutcome> Run(string tool, object input) =>
        _tools.ExecuteAsync(tool, JsonSerializer.SerializeToElement(input).Deserialize<Dictionary<string, JsonElement>>()!, _conv, default);

    private object Booking(bool confirmed = true, string phone = "+375 29 111-11-11", string start = "2026-10-05 15:00") => new
    {
        service_id = _earlobe, artist_id = _leo, start, client_name = "Anna", phone, age_group = "adult", client_confirmed = confirmed,
    };

    [Fact]
    public async Task Availability_is_in_salon_local_time()
    {
        var result = await Run("get_availability", new { service_id = _earlobe, artist_id = _leo, date_from = "2026-10-05" });

        Assert.False(result.IsError);
        Assert.Contains("\"2026-10-05 (Monday)\":\"11:00 11:30", result.Content); // Minsk opens 11:00 local (08:00 UTC)
    }

    [Fact]
    public async Task Booking_requires_client_confirmation()
    {
        var result = await Run("create_booking", Booking(confirmed: false));

        Assert.True(result.IsError);
        Assert.Empty(await _db.Bookings.ToListAsync());
    }

    [Fact]
    public async Task Booking_converts_local_time_and_links_client()
    {
        var result = await Run("create_booking", Booking());

        Assert.False(result.IsError, result.Content);
        var booking = await _db.Bookings.Include(b => b.Client).SingleAsync();
        Assert.Equal(new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc), booking.StartUtc); // 15:00 Minsk = 12:00 UTC
        Assert.Equal(BookingStatus.Pending, booking.Status);
        Assert.Equal(Channel.WebChat, booking.Source);
        Assert.Equal("+375291111111", booking.Client.Phone);
        Assert.Equal(booking.ClientId, _conv.ClientId);
    }

    [Fact]
    public async Task Tattoo_sessions_cannot_be_booked_by_the_ai()
    {
        var session = (await _db.Services.ToListAsync()).First(s => s.Kind == ServiceKind.TattooSession).Id;
        var result = await Run("get_availability", new { service_id = session, date_from = "2026-10-05" });
        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Cancel_needs_the_matching_phone()
    {
        await Run("create_booking", Booking());
        var id = (await _db.Bookings.SingleAsync()).Id;

        var wrong = await Run("cancel_booking", new { booking_id = id, phone = "+375 29 999-99-99" });
        Assert.True(wrong.IsError);

        var found = await Run("find_my_bookings", new { phone = "80291111111" });
        Assert.Contains($"\"booking_id\":{id}", found.Content);

        var ok = await Run("cancel_booking", new { booking_id = id, phone = "80291111111" });
        Assert.False(ok.IsError, ok.Content);
        Assert.Equal(BookingStatus.Cancelled, (await _db.Bookings.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Request_human_flags_the_conversation()
    {
        await Run("request_human", new { reason = "complaint about healing" });
        Assert.True(_conv.NeedsHuman);
        Assert.Equal("complaint about healing", _conv.HandoffReason);
    }

    [Fact]
    public async Task Bad_input_is_a_tool_error_not_an_exception()
    {
        var result = await Run("create_booking", new { service_id = _earlobe, client_confirmed = true });
        Assert.True(result.IsError);
    }

    [Fact]
    public void Stored_blocks_round_trip_to_sdk_params()
    {
        List<StoredBlock> blocks =
        [
            StoredBlock.OfText("hi"),
            new() { Type = "thinking", Text = "", Signature = "sig" },
            new() { Type = "tool_use", Id = "tu_1", Name = "find_my_bookings", Input = new() { ["phone"] = JsonSerializer.SerializeToElement("+375291111111") } },
            StoredBlock.OfToolResult("tu_1", "[]", false),
        ];
        var restored = StoredBlock.Deserialize(StoredBlock.Serialize(blocks));

        Assert.Equal(blocks.Count, restored.Count);
        Assert.All(restored, b => Assert.NotNull(b.ToParam()));
        Assert.Equal("+375291111111", restored[2].Input!["phone"].GetString());
    }
}
