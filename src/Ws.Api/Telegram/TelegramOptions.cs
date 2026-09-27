namespace Ws.Api.Telegram;

/// <summary>
/// Settings under "Telegram". BotToken in user-secrets locally (Telegram:BotToken), env Telegram__BotToken on servers.
/// Without a token the bot, staff notifications and reminders are off; the website works as before.
/// </summary>
public sealed class TelegramOptions
{
    public const string Section = "Telegram";

    public string? BotToken { get; set; }
    /// <summary>Staff group chat id (negative number). Add the bot to the group and send /chatid to find it.</summary>
    public long? StaffChatId { get; set; }
    /// <summary>Long-poll wait per getUpdates call.</summary>
    public int PollTimeoutSeconds { get; set; } = 50;
    /// <summary>Messages per minute a client may send to the AI in one chat.</summary>
    public int ClientMessagesPerMinute { get; set; } = 12;
}
