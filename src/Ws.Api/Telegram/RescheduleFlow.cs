using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Notifications;
using Ws.Core.Scheduling;
using static Ws.Api.Telegram.TelegramApi;

namespace Ws.Api.Telegram;

/// <summary>
/// Client moves a booking with inline buttons: day -> time -> confirm. Stateless: everything is in the callback data
/// ("rs:{id}", "rsp:{id}:{yyyyMMdd}" day page, "rsd:{id}:{yyyyMMdd}" times of a day, "rsy:{id}:{min}" confirm,
/// "rsk:{id}:{min}" do it; min = unix minutes). Every step re-checks that the booking is this Telegram user's.
/// </summary>
public sealed class RescheduleFlow(TelegramApi api, WsDbContext db, BookingService bookings, TimeProvider clock)
{
    public const string Prefix = "rs";
    private const int DaysPerPage = 14;
    private const int MaxDayButtons = 8;

    /// <summary>The "Reschedule" button under a confirmation message.</summary>
    public static object StartButton(Booking b) => new
    {
        inline_keyboard = new[] { new[] { Button(ClientMessages.RescheduleButton(b.Client.Language), $"rs:{b.Id}") } },
    };

    public static bool Handles(string data) => data.StartsWith(Prefix) && data.IndexOf(':') is 2 or 3;

    public async Task HandleAsync(CallbackQuery cb, CancellationToken ct)
    {
        var parts = cb.Data!.Split(':');
        if (parts.Length < 2 || !int.TryParse(parts[1], out var bookingId))
        {
            await api.AnswerCallbackQueryAsync(cb.Id, ct: ct);
            return;
        }

        var booking = await db.Bookings.AsNoTracking()
            .Include(b => b.Client).Include(b => b.Service).Include(b => b.Artist)
            .FirstOrDefaultAsync(b => b.Id == bookingId, ct);
        // Someone else's (or unknown) booking: say nothing.
        if (booking == null || booking.Client.TelegramUserId != cb.From.Id)
        {
            await api.AnswerCallbackQueryAsync(cb.Id, ct: ct);
            return;
        }

        var salon = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == booking.SalonId, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(salon.TimeZoneId);
        var lang = booking.Client.Language;
        var chatId = cb.Message!.Chat.Id;
        await api.AnswerCallbackQueryAsync(cb.Id, ct: ct);

        if (!Booking.ActiveStatuses.Contains(booking.Status))
        {
            await Show(chatId, cb, parts[0] == "rs", ClientMessages.RescheduleNotActive(lang), null, ct);
            return;
        }
        if (!bookings.ClientCanChange(booking, salon))
        {
            await Show(chatId, cb, parts[0] == "rs", ClientMessages.RescheduleTooLate(lang, salon.ClientChangeNoticeHours, salon.Phone), null, ct);
            return;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, zone));
        switch (parts[0])
        {
            case "rs":
                // New message, so the confirmation above stays as it was.
                var (text, markup) = await DaysAsync(booking, salon, zone, today, ct);
                await api.SendMessageAsync(chatId, text, replyMarkup: markup, ct: ct);
                break;
            case "rsp" when parts.Length == 3 && TryDay(parts[2], out var from):
                var page = await DaysAsync(booking, salon, zone, from < today ? today : from, ct);
                await Edit(chatId, cb, page.Text, page.Markup, ct);
                break;
            case "rsd" when parts.Length == 3 && TryDay(parts[2], out var day):
                var times = await TimesAsync(booking, zone, day, today, null, ct);
                await Edit(chatId, cb, times.Text, times.Markup, ct);
                break;
            case "rsy" when parts.Length == 3 && TryStart(parts[2], out var start):
                var when = Local(start, zone).ToString(WhenFormat(lang), Culture(lang));
                await Edit(chatId, cb, ClientMessages.RescheduleConfirm(lang, when), new
                {
                    inline_keyboard = new[]
                    {
                        new[]
                        {
                            Button(ClientMessages.RescheduleYes(lang), $"rsk:{booking.Id}:{parts[2]}"),
                            Button(ClientMessages.RescheduleBack(lang), $"rsd:{booking.Id}:{DayKey(DateOnly.FromDateTime(Local(start, zone)))}"),
                        },
                    },
                }, ct);
                break;
            case "rsk" when parts.Length == 3 && TryStart(parts[2], out var newStart):
                var result = await bookings.RescheduleAsync(booking.Id, newStart, null, staff: false, ct);
                if (result.Ok)
                {
                    await Edit(chatId, cb, ClientMessages.RescheduleRequested(result.Booking!, salon), null, ct);
                }
                else if (result.Error == BookingError.SlotTaken)
                {
                    var retry = await TimesAsync(booking, zone, DateOnly.FromDateTime(Local(newStart, zone)), today,
                        ClientMessages.RescheduleSlotGone(lang), ct);
                    await Edit(chatId, cb, retry.Text, retry.Markup, ct);
                }
                else
                {
                    await Edit(chatId, cb, result.Error == BookingError.TooLateToChange
                        ? ClientMessages.RescheduleTooLate(lang, salon.ClientChangeNoticeHours, salon.Phone)
                        : ClientMessages.RescheduleNotActive(lang), null, ct);
                }
                break;
        }
    }

    /// <summary>Days with free time for the same artist, starting at <paramref name="from"/>.</summary>
    private async Task<(string Text, object? Markup)> DaysAsync(Booking b, Salon salon, TimeZoneInfo zone, DateOnly from, CancellationToken ct)
    {
        var lang = b.Client.Language;
        var to = from.AddDays(DaysPerPage - 1);
        var slots = (await bookings.GetRescheduleSlotsAsync(b.Id, b.ArtistId, from, to, ct: ct)).SelectMany(a => a.SlotsUtc);
        var days = slots.Select(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(s, zone))).Distinct().Order().ToList();

        var rows = days.Take(MaxDayButtons)
            .Select(d => Button(d.ToString("ddd d MMM", Culture(lang)), $"rsd:{b.Id}:{DayKey(d)}"))
            .Chunk(2).Select(r => r.ToArray()).ToList();

        // "Later" continues after the last day shown (or after this window), within the booking horizon.
        var next = days.Count > MaxDayButtons ? days[MaxDayButtons] : to.AddDays(1);
        var horizon = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(clock.GetUtcNow().UtcDateTime, zone)).AddDays(salon.MaxBookingDaysAhead);
        if (next <= horizon) rows.Add([Button(ClientMessages.RescheduleLater(lang), $"rsp:{b.Id}:{DayKey(next)}")]);

        var text = ClientMessages.ReschedulePickDay(b, salon);
        if (days.Count == 0) text += "\n\n" + ClientMessages.RescheduleNoSlots(lang);
        return (text, new { inline_keyboard = rows });
    }

    private async Task<(string Text, object? Markup)> TimesAsync(
        Booking b, TimeZoneInfo zone, DateOnly day, DateOnly today, string? note, CancellationToken ct)
    {
        var lang = b.Client.Language;
        var slots = (await bookings.GetRescheduleSlotsAsync(b.Id, b.ArtistId, day, day, ct: ct)).SelectMany(a => a.SlotsUtc).Order();
        var rows = slots
            .Select(s => Button(TimeZoneInfo.ConvertTimeFromUtc(s, zone).ToString("HH:mm", CultureInfo.InvariantCulture),
                $"rsy:{b.Id}:{UnixMinutes(s)}"))
            .Chunk(4).Select(r => r.ToArray()).ToList();
        rows.Add([Button(ClientMessages.RescheduleBack(lang), $"rsp:{b.Id}:{DayKey(today)}")]);

        var text = ClientMessages.ReschedulePickTime(lang, day.ToString("dddd d MMMM", Culture(lang)));
        if (note != null) text = note + "\n" + text;
        return (text, new { inline_keyboard = rows });
    }

    private async Task Show(long chatId, CallbackQuery cb, bool asNew, string text, object? markup, CancellationToken ct)
    {
        if (asNew) await api.SendMessageAsync(chatId, text, replyMarkup: markup, ct: ct);
        else await Edit(chatId, cb, text, markup, ct);
    }

    private Task Edit(long chatId, CallbackQuery cb, string text, object? markup, CancellationToken ct) =>
        api.EditMessageTextAsync(chatId, cb.Message!.MessageId, text, replyMarkup: markup, ct: ct);

    private static object Button(string text, string data) => new { text, callback_data = data };

    private static string DayKey(DateOnly d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static bool TryDay(string s, out DateOnly d) =>
        DateOnly.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

    private static long UnixMinutes(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds() / 60;

    private static bool TryStart(string s, out DateTime utc)
    {
        utc = default;
        if (!long.TryParse(s, out var minutes)) return false;
        utc = DateTimeOffset.FromUnixTimeSeconds(minutes * 60).UtcDateTime;
        return true;
    }

    private static DateTime Local(DateTime utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTimeFromUtc(utc, zone);

    private static CultureInfo Culture(string lang) =>
        CultureInfo.GetCultureInfo(Languages.Normalize(lang) == Languages.Russian ? "ru-RU" : "en-GB");

    private static string WhenFormat(string lang) =>
        Languages.Normalize(lang) == Languages.Russian ? "dddd, d MMMM, HH:mm" : "dddd d MMMM, HH:mm";
}
