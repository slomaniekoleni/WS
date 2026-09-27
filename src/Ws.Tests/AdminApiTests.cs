using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Ws.Tests;

/// <summary>Admin API through the real HTTP pipeline (auth cookie, endpoints, SQLite file per test class).</summary>
public sealed class AdminApiTests : IClassFixture<AdminApiTests.App>
{
    // Test-only credentials, generated per run.
    private static readonly string Password = "t-" + Guid.NewGuid().ToString("N");

    public sealed class App : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ws-test-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Not "Development": user-secrets (real bot token, Claude key) must never load in tests.
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ws:DatabasePath"] = _dbPath,
                ["Admin:Login"] = "owner",
                ["Admin:Password"] = Password,
                ["Telegram:BotToken"] = "",
                ["Claude:ApiKey"] = "",
            }));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var f in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
                if (File.Exists(f)) File.Delete(f);
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly App _app;

    public AdminApiTests(App app) => _app = app;

    private async Task<HttpClient> LoggedIn()
    {
        var client = _app.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var login = await client.PostAsJsonAsync("/api/admin/auth/login", new { login = "Owner ", password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return client;
    }

    [Fact]
    public async Task Admin_api_requires_login()
    {
        var client = _app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/bookings?from=2026-10-01&to=2026-10-07")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected()
    {
        var client = _app.CreateClient();
        var res = await client.PostAsJsonAsync("/api/admin/auth/login", new { login = "owner", password = "nope-nope-nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Owner_can_log_in_and_out()
    {
        var client = await LoggedIn();
        var me = await client.GetFromJsonAsync<JsonElement>("/api/admin/auth/me", Json);
        Assert.Equal("owner", me.GetProperty("login").GetString());

        await client.PostAsync("/api/admin/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Staff_books_a_tattoo_session_and_manages_it()
    {
        var client = await LoggedIn();
        var services = await client.GetFromJsonAsync<JsonElement>("/api/admin/services", Json);
        var session = services.EnumerateArray().First(s => s.GetProperty("kind").GetString() == "TattooSession");
        var artistId = session.GetProperty("artistIds")[0].GetInt32();

        // Staff availability includes services clients can't book online.
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14));
        var avail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/admin/availability?serviceId={session.GetProperty("id").GetInt32()}&artistId={artistId}&from={day:yyyy-MM-dd}&to={day.AddDays(6):yyyy-MM-dd}", Json);
        var slot = avail[0].GetProperty("slots")[0].GetString();

        var created = await client.PostAsJsonAsync("/api/admin/bookings", new
        {
            serviceId = session.GetProperty("id").GetInt32(), artistId, startUtc = slot,
            clientName = "Walk-in", phone = "+375 29 333-33-33", ageGroup = "Adult",
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var booking = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Confirmed", booking.GetProperty("status").GetString());
        var id = booking.GetProperty("id").GetInt32();

        var listed = await client.GetFromJsonAsync<JsonElement>($"/api/admin/bookings?from={day:yyyy-MM-dd}&to={day.AddDays(7):yyyy-MM-dd}", Json);
        Assert.Contains(listed.EnumerateArray(), b => b.GetProperty("id").GetInt32() == id);

        // Approving a confirmed booking is not allowed; completing it is.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/admin/bookings/{id}/approve", null)).StatusCode);
        var done = await client.PostAsync($"/api/admin/bookings/{id}/complete", null);
        Assert.Equal("Completed", (await done.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Owner_edits_an_artists_hours_and_time_off()
    {
        var client = await LoggedIn();
        var artists = await client.GetFromJsonAsync<JsonElement>("/api/admin/artists", Json);
        var a = artists[0];
        var id = a.GetProperty("id").GetInt32();

        var edit = new
        {
            name = a.GetProperty("name").GetString(),
            specialty = a.GetProperty("specialty").GetString(),
            bio = new { en = "New bio", ru = "Новое био" },
            styles = new[] { "fine-line" },
            isActive = true,
            sortOrder = 1,
            telegramUsername = "@alex_ink",
            hours = new[] { new { day = "Monday", start = "12:00", end = "18:00" } },
            serviceIds = a.GetProperty("serviceIds").EnumerateArray().Select(x => x.GetInt32()).ToArray(),
        };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/admin/artists/{id}", edit)).StatusCode);

        var off = await client.PostAsJsonAsync($"/api/admin/artists/{id}/time-off",
            new { startLocal = "2030-01-01T00:00", endLocal = "2030-01-08T00:00", reason = "vacation" });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        var after = (await client.GetFromJsonAsync<JsonElement>("/api/admin/artists", Json)).EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == id);
        Assert.Equal("alex_ink", after.GetProperty("telegramUsername").GetString());
        Assert.Equal(1, after.GetProperty("hours").GetArrayLength());
        Assert.Equal(1, after.GetProperty("timeOff").GetArrayLength());
    }
}
