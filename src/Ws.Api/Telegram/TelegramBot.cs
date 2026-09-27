using System.Collections.Concurrent;
using Anthropic.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Notifications;
using Ws.Core.Scheduling;
using Ws.Core.Receptionist;
using static Ws.Api.Telegram.TelegramApi;

namespace Ws.Api.Telegram;

/// <summary>
/// The salon's Telegram bot, via long polling (works behind NAT, no public URL needed):
/// private chats go to the AI receptionist; the staff group approves/declines booking requests with buttons.
/// </summary>
public sealed class TelegramBot(
    TelegramApi api,
    IServiceScopeFactory scopes,
    IOptions<TelegramOptions> telegramOptions,
    IOptions<WsOptions> wsOptions,
    ChannelInfo channels,
    ILogger<TelegramBot> log) : BackgroundService
{
    private readonly TelegramOptions _opt = telegramOptions.Value;
    private readonly int _salonId = wsOptions.Value.SalonId;

    // One update at a time per chat keeps a client's messages in order; different chats run in parallel.
    private readonly ConcurrentDictionary<long, SemaphoreSlim> _chatLocks = new();
    private readonly ConcurrentDictionary<long, Queue<DateTime>> _recentMessages = new();
    private readonly ConcurrentDictionary<long, bool> _unknownGroupsLogged = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Telegram bot started (staff chat {StaffChat})", _opt.StaffChatId?.ToString() ?? "not set");
        // For the website's "write us in Telegram" link.
        channels.TelegramBot = (await api.GetMeAsync(stoppingToken))?.Username;
        long offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await api.GetUpdatesAsync(offset, stoppingToken);
                foreach (var update in updates)
                {
                    offset = update.UpdateId + 1;
                    _ = HandleSafelyAsync(update, stoppingToken);
                }
                if (updates.Count == 0) await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Telegram polling failed; retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleSafelyAsync(Update update, CancellationToken ct)
    {
        var chatId = update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id ?? 0;
        var gate = _chatLocks.GetOrAdd(chatId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (update.CallbackQuery is { } cb) await HandleCallbackAsync(cb, ct);
            else if (update.Message is { } msg) await HandleMessageAsync(msg, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError(ex, "Failed to handle Telegram update {UpdateId}", update.UpdateId);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task HandleMessageAsync(Message msg, CancellationToken ct)
    {
        var text = msg.Text?.Trim();

        if (msg.Chat.Type != "private")
        {
            // Groups: only a helper to find the staff group's id.
            if (text != null && (text == "/chatid" || text.StartsWith("/chatid@")))
            {
                await api.SendMessageAsync(msg.Chat.Id, $"chat id: {msg.Chat.Id}", ct: ct);
            }
            else if (msg.Chat.Id != _opt.StaffChatId && _unknownGroupsLogged.TryAdd(msg.Chat.Id, true))
            {
                log.LogInformation("Bot is in group {Title} ({ChatId}); set Telegram:StaffChatId to use it for staff messages",
                    msg.Chat.Title, msg.Chat.Id);
            }
            return;
        }

        var language = msg.From?.LanguageCode;
        if (text == null)
        {
            await api.SendMessageAsync(msg.Chat.Id, ClientMessages.TextOnly(language ?? "ru"), ct: ct);
            return;
        }

        if (text.StartsWith("/start"))
        {
            using var scope = scopes.CreateScope();
            var salonName = await scope.ServiceProvider.GetRequiredService<WsDbContext>()
                .Salons.Where(s => s.Id == _salonId).Select(s => s.Name).SingleAsync(ct);
            await api.SendMessageAsync(msg.Chat.Id, ClientMessages.Welcome(language ?? "ru", salonName), ct: ct);
            return;
        }

        if (!AllowClientMessage(msg.Chat.Id))
        {
            await api.SendMessageAsync(msg.Chat.Id, ClientMessages.Busy(language ?? "ru"), ct: ct);
            return;
        }

        using (var scope = scopes.CreateScope())
        {
            var receptionist = scope.ServiceProvider.GetService<Receptionist>();
            if (receptionist == null)
            {
                await api.SendMessageAsync(msg.Chat.Id, ClientMessages.Unavailable(language ?? "ru"), ct: ct);
                return;
            }

            await api.SendTypingAsync(msg.Chat.Id, ct);
            try
            {
                var reply = await receptionist.ReplyAsync(_salonId, Channel.Telegram, msg.Chat.Id.ToString(), text, language, ct);
                await api.SendMessageAsync(msg.Chat.Id, reply.Text, ct: ct);
            }
            catch (AnthropicApiException ex)
            {
                log.LogError(ex, "Claude API error in Telegram chat");
                await api.SendMessageAsync(msg.Chat.Id, ClientMessages.Unavailable(language ?? "ru"), ct: ct);
            }
        }
    }

    private async Task HandleCallbackAsync(CallbackQuery cb, CancellationToken ct)
    {
        if (cb.Message == null || cb.Message.Chat.Id != _opt.StaffChatId || cb.Data == null)
        {
            await api.AnswerCallbackQueryAsync(cb.Id, ct: ct);
            return;
        }

        var approve = cb.Data.StartsWith(StaffMessages.ApprovePrefix);
        var idText = cb.Data[(approve ? StaffMessages.ApprovePrefix : StaffMessages.DeclinePrefix).Length..];
        if (!int.TryParse(idText, out var bookingId) || !(approve || cb.Data.StartsWith(StaffMessages.DeclinePrefix)))
        {
            await api.AnswerCallbackQueryAsync(cb.Id, ct: ct);
            return;
        }

        using var scope = scopes.CreateScope();
        var bookings = scope.ServiceProvider.GetRequiredService<BookingService>();
        var db = scope.ServiceProvider.GetRequiredService<WsDbContext>();

        var who = cb.From.Display;
        var result = approve
            ? await bookings.ApproveAsync(bookingId, ct)
            : await bookings.DeclineAsync(bookingId, $"Declined by {who} in Telegram", ct);
        if (result.Booking == null)
        {
            await api.AnswerCallbackQueryAsync(cb.Id, "Заявка не найдена", ct);
            return;
        }

        var booking = result.Booking;
        var salon = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == booking.SalonId, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(salon.TimeZoneId);
        var clientTelegram = booking.Client.TelegramUserId;

        // Rebuild the request text with the outcome; no markup = the buttons disappear.
        var statusLine = result.Ok
            ? StaffMessages.Decision(booking.Status, who, clientTelegram != null)
            : StaffMessages.Decision(booking.Status, "уже обработана", clientTelegram != null);
        await api.EditMessageTextAsync(cb.Message.Chat.Id, cb.Message.MessageId,
            StaffMessages.BookingRequest(BookingNotice.From(booking, zone)) + "\n\n" + statusLine, html: true, ct: ct);
        await api.AnswerCallbackQueryAsync(cb.Id, result.Ok ? (approve ? "Подтверждено" : "Отклонено") : "Заявка уже обработана", ct);

        if (result.Ok) await scope.ServiceProvider.GetRequiredService<ClientNotifier>().BookingDecidedAsync(booking, ct);
        log.LogInformation("Booking {BookingId} {Decision} by {Who} (ok={Ok})", bookingId, approve ? "approved" : "declined", who, result.Ok);
    }

    /// <summary>Sliding one-minute window per chat, so one client can't run up the Claude bill.</summary>
    private bool AllowClientMessage(long chatId)
    {
        var now = DateTime.UtcNow;
        var recent = _recentMessages.GetOrAdd(chatId, _ => new Queue<DateTime>());
        lock (recent)
        {
            while (recent.Count > 0 && now - recent.Peek() > TimeSpan.FromMinutes(1)) recent.Dequeue();
            if (recent.Count >= _opt.ClientMessagesPerMinute) return false;
            recent.Enqueue(now);
            return true;
        }
    }
}
