namespace Ws.Core.Domain;

public enum ServiceKind
{
    TattooConsultation,
    TattooSession,
    TattooTouchUp,
    Piercing,
    JewelryChange,
}

public sealed class Service
{
    public int Id { get; set; }
    public int SalonId { get; set; }
    public ServiceKind Kind { get; set; }
    public LocalizedText Name { get; set; } = new();
    public LocalizedText Description { get; set; } = new();
    public int DurationMinutes { get; set; }
    /// <summary>Cleanup/setup time after the procedure; the artist and workstation stay busy.</summary>
    public int BufferMinutes { get; set; }
    /// <summary>Null = free. PriceTo null = fixed price, otherwise a "from-to" range.</summary>
    public decimal? PriceFrom { get; set; }
    public decimal? PriceTo { get; set; }
    /// <summary>Intimate procedures: nobody else can work in the room at the same time.</summary>
    public bool NeedsPrivateRoom { get; set; }
    /// <summary>18+ only, even with a guardian.</summary>
    public bool AdultsOnly { get; set; }
    /// <summary>
    /// Clients can book it themselves (website/AI). Tattoo sessions are false: the artist sizes them
    /// after a consultation and books them from the admin panel.
    /// </summary>
    public bool BookableOnline { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public List<ArtistService> Artists { get; set; } = [];
}

/// <summary>Which artist performs which service, with optional per-artist price/duration.</summary>
public sealed class ArtistService
{
    public int ArtistId { get; set; }
    public int ServiceId { get; set; }
    public int? DurationMinutes { get; set; }
    public decimal? PriceFrom { get; set; }
    public decimal? PriceTo { get; set; }

    public Artist Artist { get; set; } = null!;
    public Service Service { get; set; } = null!;
}
