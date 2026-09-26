using Ws.Core.Domain;

namespace Ws.Core.Scheduling;

/// <summary>Something that occupies time: a booking (incl. buffer) or time off.</summary>
public readonly record struct BusyInterval(DateTime StartUtc, DateTime EndUtc, bool Private = false)
{
    public bool Overlaps(DateTime startUtc, DateTime endUtc) => StartUtc < endUtc && startUtc < EndUtc;
}

public sealed record RoomSchedule(int RoomId, int Workstations, IReadOnlyList<BusyInterval> Busy);

/// <summary>Everything needed to decide free slots for one artist + one service. Pure data, no DB.</summary>
public sealed record SlotQuery
{
    public required TimeZoneInfo Zone { get; init; }
    public required int DurationMinutes { get; init; }
    public int BufferMinutes { get; init; }
    public bool NeedsPrivateRoom { get; init; }
    public required IReadOnlyList<WorkingHours> ArtistHours { get; init; }
    /// <summary>Artist's own bookings (up to BlockedUntil) and time off.</summary>
    public required IReadOnlyList<BusyInterval> ArtistBusy { get; init; }
    public required IReadOnlyList<RoomSchedule> Rooms { get; init; }
    /// <summary>No slots starting before this (now + lead time).</summary>
    public required DateTime EarliestStartUtc { get; init; }
    /// <summary>No slots starting after this (booking horizon).</summary>
    public required DateTime LatestStartUtc { get; init; }
    public int StepMinutes { get; init; } = 30;
}

public static class SlotFinder
{
    /// <summary>Free start times (UTC) for local dates <paramref name="from"/>..<paramref name="to"/> inclusive.</summary>
    public static List<DateTime> FindSlots(SlotQuery q, DateOnly from, DateOnly to)
    {
        var slots = new List<DateTime>();
        var step = TimeSpan.FromMinutes(Math.Max(5, q.StepMinutes));
        var duration = TimeSpan.FromMinutes(q.DurationMinutes);

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            foreach (var shift in q.ArtistHours.Where(h => h.Day == date.DayOfWeek).OrderBy(h => h.Start))
            {
                var shiftEnd = date.ToDateTime(shift.End);
                for (var local = date.ToDateTime(shift.Start); local + duration <= shiftEnd; local += step)
                {
                    if (q.Zone.IsInvalidTime(local)) continue; // skipped by a DST jump
                    var startUtc = TimeZoneInfo.ConvertTimeToUtc(local, q.Zone);
                    if (FindRoom(q, startUtc) != null) slots.Add(startUtc);
                }
            }
        }

        return slots.Distinct().Order().ToList();
    }

    /// <summary>
    /// Checks one start time against booking window, artist hours, artist calendar and room capacity.
    /// Returns the room to use, or null if the slot is not free.
    /// </summary>
    public static int? FindRoom(SlotQuery q, DateTime startUtc)
    {
        if (startUtc < q.EarliestStartUtc || startUtc > q.LatestStartUtc) return null;

        var endUtc = startUtc.AddMinutes(q.DurationMinutes);
        if (!WithinWorkingHours(q, startUtc, endUtc)) return null;

        // Buffer (cleanup) keeps the artist and the workstation busy after the procedure.
        var blockedUntil = endUtc.AddMinutes(q.BufferMinutes);
        if (q.ArtistBusy.Any(b => b.Overlaps(startUtc, blockedUntil))) return null;

        foreach (var room in q.Rooms)
        {
            if (RoomHasSpace(room, startUtc, blockedUntil, q.NeedsPrivateRoom)) return room.RoomId;
        }
        return null;
    }

    private static bool WithinWorkingHours(SlotQuery q, DateTime startUtc, DateTime endUtc)
    {
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc, q.Zone);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(endUtc, q.Zone);
        var date = DateOnly.FromDateTime(localStart);
        return q.ArtistHours.Any(h =>
            h.Day == date.DayOfWeek &&
            localStart >= date.ToDateTime(h.Start) &&
            localEnd <= date.ToDateTime(h.End));
    }

    private static bool RoomHasSpace(RoomSchedule room, DateTime startUtc, DateTime endUtc, bool needsPrivate)
    {
        var overlapping = room.Busy.Where(b => b.Overlaps(startUtc, endUtc)).ToList();
        if (overlapping.Count == 0) return room.Workstations > 0;
        if (needsPrivate || overlapping.Any(b => b.Private)) return false;
        return MaxConcurrent(overlapping, startUtc, endUtc) < room.Workstations;
    }

    /// <summary>Peak number of simultaneous intervals inside [start, end).</summary>
    private static int MaxConcurrent(List<BusyInterval> intervals, DateTime startUtc, DateTime endUtc)
    {
        var events = new List<(DateTime At, int Delta)>(intervals.Count * 2);
        foreach (var i in intervals)
        {
            events.Add((i.StartUtc < startUtc ? startUtc : i.StartUtc, +1));
            events.Add((i.EndUtc > endUtc ? endUtc : i.EndUtc, -1));
        }

        // Ends before starts at the same instant: back-to-back bookings don't overlap.
        int current = 0, peak = 0;
        foreach (var e in events.OrderBy(e => e.At).ThenBy(e => e.Delta))
        {
            current += e.Delta;
            peak = Math.Max(peak, current);
        }
        return peak;
    }
}
