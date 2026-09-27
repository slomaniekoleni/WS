namespace Ws.Core.Domain;

/// <summary>
/// One salon. Phase 1 has a single row, but every salon-owned table carries SalonId
/// so Phase 2 (multi-salon) is a matter of scoping queries, not reshaping data.
/// </summary>
public sealed class Salon
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    /// <summary>IANA time zone id, e.g. "Europe/Warsaw". Working hours are local to it.</summary>
    public string TimeZoneId { get; set; } = "UTC";
    public string Currency { get; set; } = "EUR";
    /// <summary>ISO 3166 country code; phone numbers without a country code are read as local to it.</summary>
    public string Country { get; set; } = "BY";
    public LocalizedText Address { get; set; } = new();
    public string Phone { get; set; } = "";
    public string? Instagram { get; set; }
    /// <summary>Big photo on the home page.</summary>
    public string? CoverImageUrl { get; set; }
    public LocalizedText About { get; set; } = new();
    /// <summary>Policies and FAQ (markdown): deposits, cancellation, aftercare, pain, healing... Fed to the AI receptionist.</summary>
    public LocalizedText Policies { get; set; } = new();

    /// <summary>Clients this age or older may book with a parent/guardian present.</summary>
    public int MinAgeWithGuardian { get; set; } = 16;
    /// <summary>Clients this age or older may book on their own.</summary>
    public int MinAgeSolo { get; set; } = 18;

    /// <summary>Earliest a client can book ahead of now, so staff aren't surprised.</summary>
    public int MinBookingLeadMinutes { get; set; } = 120;
    /// <summary>How far ahead slots are offered.</summary>
    public int MaxBookingDaysAhead { get; set; } = 60;
    /// <summary>Clients can move (reschedule) a booking themselves up to this many hours before it; later they contact the salon.</summary>
    public int ClientChangeNoticeHours { get; set; } = 24;
    /// <summary>Grid for offered start times.</summary>
    public int SlotStepMinutes { get; set; } = 30;

    public List<SalonHours> OpeningHours { get; set; } = [];
    public List<Room> Rooms { get; set; } = [];
}

/// <summary>Opening hours shown on the website. Bookable time comes from each artist's own hours.</summary>
public sealed class SalonHours
{
    public int Id { get; set; }
    public int SalonId { get; set; }
    public DayOfWeek Day { get; set; }
    public TimeOnly Open { get; set; }
    public TimeOnly Close { get; set; }
}

/// <summary>
/// A room with a number of workstations. Normal procedures share the room (up to Workstations at once);
/// a procedure that needs privacy (intimate piercing/tattoo) takes the whole room.
/// </summary>
public sealed class Room
{
    public int Id { get; set; }
    public int SalonId { get; set; }
    public string Name { get; set; } = "";
    public int Workstations { get; set; } = 1;
}
