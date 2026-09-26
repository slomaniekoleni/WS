namespace Ws.Core.Domain;

public enum ArtistSpecialty
{
    Tattoo,
    Piercing,
}

public sealed class Artist
{
    public int Id { get; set; }
    public int SalonId { get; set; }
    public string Name { get; set; } = "";
    public ArtistSpecialty Specialty { get; set; }
    public LocalizedText Bio { get; set; } = new();
    /// <summary>Style tags used for matching clients to artists, e.g. "fine-line", "realism".</summary>
    public List<string> Styles { get; set; } = [];
    public string? PhotoUrl { get; set; }
    public string? InstagramHandle { get; set; }
    /// <summary>Telegram username for mentions in the staff group (booking approvals).</summary>
    public string? TelegramUsername { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public List<PortfolioImage> Portfolio { get; set; } = [];
    public List<WorkingHours> WorkingHours { get; set; } = [];
    public List<TimeOff> TimeOff { get; set; } = [];
    public List<ArtistService> Services { get; set; } = [];
}

public sealed class PortfolioImage
{
    public int Id { get; set; }
    public int ArtistId { get; set; }
    public string Url { get; set; } = "";
    public string? Style { get; set; }
    public LocalizedText Caption { get; set; } = new();
    public int SortOrder { get; set; }
}

/// <summary>Weekly schedule, local salon time. Several rows per day allow split shifts.</summary>
public sealed class WorkingHours
{
    public int Id { get; set; }
    public int ArtistId { get; set; }
    public DayOfWeek Day { get; set; }
    public TimeOnly Start { get; set; }
    public TimeOnly End { get; set; }
}

/// <summary>Days off, vacations, sick leave (UTC).</summary>
public sealed class TimeOff
{
    public int Id { get; set; }
    public int ArtistId { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string? Reason { get; set; }
}
