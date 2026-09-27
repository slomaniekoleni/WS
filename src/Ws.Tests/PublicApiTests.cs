using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ws.Tests;

/// <summary>Public website API through the real HTTP pipeline.</summary>
public sealed class PublicApiTests(AdminApiTests.App app) : IClassFixture<AdminApiTests.App>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Salon_services_and_artists_are_served_in_the_requested_language()
    {
        var client = app.CreateClient();
        var salon = await client.GetFromJsonAsync<JsonElement>("/api/salon?lang=ru", Json);
        Assert.Equal("Wise City", salon.GetProperty("name").GetString());
        Assert.Equal("Europe/Minsk", salon.GetProperty("timeZone").GetString());

        var services = await client.GetFromJsonAsync<JsonElement>("/api/services?lang=ru", Json);
        Assert.Contains(services.EnumerateArray(), s => s.GetProperty("name").GetString() == "Прокол мочки уха");
        Assert.Equal("Piercing", services.EnumerateArray().First(s => s.GetProperty("name").GetString() == "Прокол мочки уха").GetProperty("kind").GetString());

        var artists = await client.GetFromJsonAsync<JsonElement>("/api/artists", Json);
        Assert.Equal(5, artists.GetArrayLength());
    }

    [Fact]
    public async Task Booking_errors_come_back_as_problem_codes()
    {
        var client = app.CreateClient();
        var services = await client.GetFromJsonAsync<JsonElement>("/api/services", Json);
        var earlobe = services.EnumerateArray().First(s => s.GetProperty("kind").GetString() == "Piercing");
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var avail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/availability?serviceId={earlobe.GetProperty("id").GetInt32()}&from={day:yyyy-MM-dd}", Json);
        var withSlots = avail.EnumerateArray().First(a => a.GetProperty("slots").GetArrayLength() > 0);

        var badPhone = await client.PostAsJsonAsync("/api/bookings", new
        {
            serviceId = earlobe.GetProperty("id").GetInt32(), artistId = withSlots.GetProperty("artistId").GetInt32(),
            startUtc = withSlots.GetProperty("slots")[0].GetString(), clientName = "Test", phone = "123", ageGroup = "Adult",
        });
        Assert.Equal(HttpStatusCode.BadRequest, badPhone.StatusCode);
        Assert.Equal("InvalidPhone", (await badPhone.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("type").GetString());

        var badRange = await client.GetAsync($"/api/availability?serviceId=1&from={day:yyyy-MM-dd}&to={day.AddDays(40):yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.BadRequest, badRange.StatusCode);
    }

    [Fact]
    public async Task Chat_without_claude_key_is_unavailable_and_unknown_api_paths_are_404()
    {
        var client = app.CreateClient();
        var chat = await client.PostAsJsonAsync("/api/chat", new { sessionId = Guid.NewGuid(), message = "hi" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, chat.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/nope")).StatusCode);
    }
}
