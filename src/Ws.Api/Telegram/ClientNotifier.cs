using Microsoft.EntityFrameworkCore;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Notifications;

namespace Ws.Api.Telegram;

/// <summary>
/// Tells a client about a decision on their booking, on the messenger they used (Telegram for now).
/// Clients who booked on the website have no messenger; staff call them. Does nothing when Telegram is off.
/// </summary>
public sealed class ClientNotifier(WsDbContext db, IServiceProvider services)
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
        return (await _telegram.SendMessageAsync(chatId, text, ct: ct)).Ok;
    }
}
