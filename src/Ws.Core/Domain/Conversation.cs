namespace Ws.Core.Domain;

/// <summary>A chat with the AI receptionist on any channel.</summary>
public sealed class Conversation
{
    public int Id { get; set; }
    public int SalonId { get; set; }
    public Channel Channel { get; set; }
    /// <summary>Channel-specific user id: Telegram chat id, web-chat session id, ...</summary>
    public string ExternalId { get; set; } = "";
    public int? ClientId { get; set; }
    public string Language { get; set; } = Languages.English;
    /// <summary>AI asked for a human; staff should take over.</summary>
    public bool NeedsHuman { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastMessageAtUtc { get; set; }

    public List<ConversationMessage> Messages { get; set; } = [];
}

public enum MessageRole
{
    User,
    Assistant,
    /// <summary>Written by a staff member who took over the chat.</summary>
    Staff,
}

public sealed class ConversationMessage
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public MessageRole Role { get; set; }
    /// <summary>Plain text shown in transcripts.</summary>
    public string Text { get; set; } = "";
    /// <summary>Raw Claude content blocks (JSON), incl. tool calls/results, so the conversation can be replayed.</summary>
    public string? ContentJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
