using Microsoft.EntityFrameworkCore;
using Ws.Core.Data;
using Ws.Core.Domain;

namespace Ws.Core.Notifications;

public enum ReminderKind
{
    DayBefore,
    TwoHoursBefore,
}

public sealed record DueReminder(Booking Booking, ReminderKind Kind);

/// <summary>Finds confirmed bookings whose 24h / 2h reminder is due and records that it was sent.</summary>
public sealed class Reminders(WsDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Due now: the 24h reminder once the start is within 24h (skipped for bookings made less than 24h ahead,
    /// they get only the 2h one), the 2h reminder once the start is within 2h. Only clients reachable on a
    /// messenger (Telegram for now) are returned.
    /// </summary>
    public async Task<List<DueReminder>> GetDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var in24h = now.AddHours(24);
        var in2h = now.AddHours(2);

        var candidates = await db.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed && b.StartUtc > now && b.StartUtc <= in24h)
            .Where(b => b.Client.TelegramUserId != null)
            .Where(b => b.Reminder24hSentAtUtc == null || b.Reminder2hSentAtUtc == null)
            .Include(b => b.Client).Include(b => b.Artist).Include(b => b.Service)
            .ToListAsync(ct);

        var due = new List<DueReminder>();
        foreach (var b in candidates)
        {
            if (b.Reminder2hSentAtUtc == null && b.StartUtc <= in2h)
                due.Add(new DueReminder(b, ReminderKind.TwoHoursBefore));
            else if (b.Reminder24hSentAtUtc == null && b.StartUtc > in2h && b.CreatedAtUtc <= b.StartUtc.AddHours(-24))
                due.Add(new DueReminder(b, ReminderKind.DayBefore));
        }
        return due;
    }

    public async Task MarkSentAsync(DueReminder reminder, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (reminder.Kind == ReminderKind.DayBefore) reminder.Booking.Reminder24hSentAtUtc = now;
        else
        {
            reminder.Booking.Reminder2hSentAtUtc = now;
            // A 2h reminder makes a pending 24h one pointless.
            reminder.Booking.Reminder24hSentAtUtc ??= now;
        }
        await db.SaveChangesAsync(ct);
    }
}
