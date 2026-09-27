using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Ws.Core.Notifications;

namespace Ws.Api.Telegram;

/// <summary>
/// Posts booking requests (with approve/decline buttons) and AI handoffs to the staff group.
/// Callers only enqueue, so a slow or failing Telegram never delays a booking.
/// </summary>
public sealed class TelegramStaffNotifier(TelegramApi api, IOptions<TelegramOptions> options, ILogger<TelegramStaffNotifier> log)
    : BackgroundService, IStaffNotifier
{
    private sealed record Outgoing(string Html, object? Markup, string What);

    private readonly Channel<Outgoing> _queue = Channel.CreateUnbounded<Outgoing>();
    private readonly long? _staffChatId = options.Value.StaffChatId;

    public void BookingRequested(BookingNotice notice) =>
        _queue.Writer.TryWrite(new Outgoing(
            StaffMessages.BookingRequest(notice), StaffMessages.DecisionButtons(notice.BookingId), $"booking #{notice.BookingId}"));

    public void HandoffRequested(HandoffNotice notice) =>
        _queue.Writer.TryWrite(new Outgoing(StaffMessages.Handoff(notice), null, $"handoff conv #{notice.ConversationId}"));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_staffChatId == null)
            log.LogWarning("Telegram:StaffChatId is not set: staff notifications are only logged. Add the bot to the staff group and send /chatid");

        await foreach (var msg in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (_staffChatId == null)
            {
                log.LogInformation("Staff notification not sent ({What}): no staff chat configured", msg.What);
                continue;
            }

            // A few quick retries for network blips; after that the request is still in the DB for the admin panel.
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var result = await api.SendMessageAsync(_staffChatId.Value, msg.Html, html: true, msg.Markup, stoppingToken);
                if (result.Ok) break;
                if (result.ErrorCode is >= 400 and < 500 and not 429) break; // our fault (bad chat id, HTML): retrying won't help
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), stoppingToken);
            }
        }
    }
}
