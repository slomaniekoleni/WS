namespace Ws.Api;

/// <summary>Which client channels are live, so the website only shows what works (chat widget, Telegram link).</summary>
public sealed class ChannelInfo
{
    public bool ChatEnabled { get; init; }
    /// <summary>The bot's @username, filled in by the Telegram bot once it has asked Telegram (getMe).</summary>
    public string? TelegramBot { get; set; }
}
