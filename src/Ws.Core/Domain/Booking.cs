namespace Ws.Core.Domain;

public enum Channel
{
    Website,
    WebChat,
    Telegram,
    Admin,
}

public enum BookingStatus
{
    /// <summary>Waiting for the artist to approve. Already holds the slot.</summary>
    Pending,
    Confirmed,
    /// <summary>Artist declined the request.</summary>
    Declined,
    Cancelled,
    Completed,
    NoShow,
}

public sealed class Client
{
    public int Id { get; set; }
    public int SalonId { get; set; }
    public string Name { get; set; } = "";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public long? TelegramUserId { get; set; }
    public string Language { get; set; } = Languages.English;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class Booking
{
    public int Id { get; set; }
    public int SalonId { get; set; }
    public int ArtistId { get; set; }
    public int ServiceId { get; set; }
    public int ClientId { get; set; }
    public int RoomId { get; set; }

    public DateTime StartUtc { get; set; }
    /// <summary>End of the procedure itself (what the client sees).</summary>
    public DateTime EndUtc { get; set; }
    /// <summary>End + buffer: until when the artist and the workstation are busy.</summary>
    public DateTime BlockedUntilUtc { get; set; }
    /// <summary>Copied from the service at booking time so later service edits don't move existing bookings.</summary>
    public bool NeedsPrivateRoom { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public Channel Source { get; set; }
    /// <summary>Client is 16-17 and comes with a parent/guardian.</summary>
    public bool WithGuardian { get; set; }
    public TattooDetails? Tattoo { get; set; }
    public string? ClientNotes { get; set; }
    public string? StaffNotes { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
    public string? DeclineOrCancelReason { get; set; }
    public DateTime? Reminder24hSentAtUtc { get; set; }
    public DateTime? Reminder2hSentAtUtc { get; set; }

    public Artist Artist { get; set; } = null!;
    public Service Service { get; set; } = null!;
    public Client Client { get; set; } = null!;

    /// <summary>Statuses that occupy the calendar.</summary>
    public static readonly BookingStatus[] ActiveStatuses = [BookingStatus.Pending, BookingStatus.Confirmed];
}

/// <summary>What the client wants tattooed; collected before a consultation.</summary>
public sealed class TattooDetails
{
    public string? Idea { get; set; }
    public string? Placement { get; set; }
    /// <summary>Free text, e.g. "about 10x5 cm" or "palm-sized".</summary>
    public string? Size { get; set; }
    public string? Style { get; set; }
    public bool? ColorWork { get; set; }
    public List<string> ReferenceImageUrls { get; set; } = [];
}
