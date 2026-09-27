using Microsoft.EntityFrameworkCore;
using Ws.Core.Data;
using Ws.Core.Notifications;

namespace Ws.Api.Telegram;

/// <summary>Every few minutes sends due 24h / 2h reminders to clients on Telegram.</summary>
public sealed class ReminderWorker(TelegramApi api, IServiceScopeFactory scopes, ILogger<ReminderWorker> log) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await SendDueAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Reminder run failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SendDueAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var reminders = scope.ServiceProvider.GetRequiredService<Reminders>();
        var db = scope.ServiceProvider.GetRequiredService<WsDbContext>();

        var due = await reminders.GetDueAsync(ct);
        if (due.Count == 0) return;

        var salons = await db.Salons.AsNoTracking().ToDictionaryAsync(s => s.Id, ct);
        foreach (var reminder in due)
        {
            var b = reminder.Booking;
            var result = await api.SendMessageAsync(b.Client.TelegramUserId!.Value,
                ClientMessages.Reminder(b, salons[b.SalonId], reminder.Kind), ct: ct);

            // 403 = the client blocked the bot: retrying every 5 minutes would never succeed, so count it as done.
            if (result.Ok || result.ErrorCode == 403)
            {
                await reminders.MarkSentAsync(reminder, ct);
                log.LogInformation("Reminder {Kind} for booking {BookingId}: {Outcome}", reminder.Kind, b.Id, result.Ok ? "sent" : "client blocked the bot");
            }
        }
    }
}
