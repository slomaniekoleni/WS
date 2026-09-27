using Microsoft.EntityFrameworkCore;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Notifications;
using Ws.Core.Scheduling;

namespace Ws.Api.Telegram;

/// <summary>
/// Tells a client about a decision on their booking, on the messenger they used (Telegram for now).
/// Clients who booked on the website have no messenger; staff call them. Does nothing when Telegram is off.
/// </summary>
public sealed class ClientNotifier(WsDbContext db, BookingService bookings, IServiceProvider services)
{
    private readonly TelegramApi? _telegram = services.GetService<TelegramApi>();

    /// <summary>Booking must have Client, Service and Artist loaded. Returns true if the client was messaged.</summary>
    public async Task<bool> BookingDecidedAsync(Booking booking, CancellationToken ct)
    {
        if (_telegram == null || booking.Client.TelegramUserId is not { } chatId) return false;

        var salon = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == booking.SalonId, ct);
        var text = booking.Status switch
        {
            BookingStatus.Confirmed => ClientMessages.Confirmed(booking, salon),
            BookingStatus.Declined => ClientMessages.Declined(booking, salon),
            BookingStatus.Cancelled => ClientMessages.Cancelled(booking, salon),
            _ => null, // completed / no-show: nothing to tell
        };
        if (text == null) return false;

        // Confirmed and still far enough ahead: the client can move it with a button.
        var markup = booking.Status == BookingStatus.Confirmed && bookings.ClientCanChange(booking, salon)
            ? RescheduleFlow.StartButton(booking)
            : null;
        return (await _telegram.SendMessageAsync(chatId, text, replyMarkup: markup, ct: ct)).Ok;
    }

    /// <summary>Staff moved the booking. Returns true if the client was messaged.</summary>
    public async Task<bool> BookingMovedAsync(Booking booking, CancellationToken ct)
    {
        if (_telegram == null || booking.Client.TelegramUserId is not { } chatId) return false;
        var salon = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == booking.SalonId, ct);
        return (await _telegram.SendMessageAsync(chatId, ClientMessages.Moved(booking, salon), ct: ct)).Ok;
    }
}
