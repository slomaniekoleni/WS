using Ws.Core.Domain;

namespace Ws.Core.Notifications;

/// <summary>
/// Tells the salon team about things that need a human: new booking requests (to approve) and chats the AI handed off.
/// Implementations must not throw or block the caller; they queue and deliver in the background.
/// </summary>
public interface IStaffNotifier
{
    void BookingRequested(BookingNotice notice);
    void HandoffRequested(HandoffNotice notice);
}

public sealed class NullStaffNotifier : IStaffNotifier
{
    public static readonly NullStaffNotifier Instance = new();
    public void BookingRequested(BookingNotice notice) { }
    public void HandoffRequested(HandoffNotice notice) { }
}

/// <summary>A snapshot of a booking for staff messages (no EF entities cross the background boundary).</summary>
public sealed record BookingNotice(
    int BookingId,
    BookingStatus Status,
    string ServiceName,
    string ArtistName,
    string? ArtistTelegramUsername,
    DateTime StartLocal,
    int DurationMinutes,
    string ClientName,
    string? ClientPhone,
    long? ClientTelegramUserId,
    Channel Source,
    bool WithGuardian,
    TattooDetails? Tattoo,
    string? Notes)
{
    /// <summary>Builds a notice from a booking with Service, Artist and Client loaded. Service name in Russian (staff language).</summary>
    public static BookingNotice From(Booking b, TimeZoneInfo zone) => new(
        b.Id,
        b.Status,
        b.Service.Name.Get(Languages.Russian),
        b.Artist.Name,
        b.Artist.TelegramUsername,
        TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, zone),
        (int)(b.EndUtc - b.StartUtc).TotalMinutes,
        b.Client.Name,
        b.Client.Phone,
        b.Client.TelegramUserId,
        b.Source,
        b.WithGuardian,
        b.Tattoo,
        b.ClientNotes);
}

public sealed record HandoffNotice(
    int ConversationId,
    Channel Channel,
    string ExternalId,
    string? ClientName,
    string? ClientPhone,
    string? Reason,
    IReadOnlyList<string> LastClientMessages);
