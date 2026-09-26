namespace Ws.Core.Receptionist;

/// <summary>Settings under "Receptionist" (env: Receptionist__Model=...). The API key lives under Claude:ApiKey.</summary>
public sealed class ReceptionistOptions
{
    public const string Section = "Receptionist";

    public string Model { get; set; } = "claude-opus-5";
    /// <summary>low / medium / high. Chat replies rarely need deep thinking; bookings need care, so medium.</summary>
    public string Effort { get; set; } = "medium";
    /// <summary>Safety stop for the tool loop within one client message.</summary>
    public int MaxToolRounds { get; set; } = 8;
    /// <summary>Longer conversations are politely closed (and handed to staff) to cap cost and abuse.</summary>
    public int MaxMessagesPerConversation { get; set; } = 80;
    public int MaxUserMessageLength { get; set; } = 1500;
}
