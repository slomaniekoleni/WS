using Ws.Core.Domain;
using Ws.Core.Scheduling;

namespace Ws.Tests;

public class SlotFinderTests
{
    // Monday. Artist works 10:00-14:00, zone UTC unless stated otherwise.
    private static readonly DateOnly Day = new(2026, 10, 5);

    private static DateTime At(int hour, int minute = 0) => Day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc);

    private static SlotQuery Query(
        int duration = 60, int buffer = 0, bool needsPrivate = false, int workstations = 3,
        BusyInterval[]? artistBusy = null, BusyInterval[]? roomBusy = null, TimeZoneInfo? zone = null) => new()
    {
        Zone = zone ?? TimeZoneInfo.Utc,
        DurationMinutes = duration,
        BufferMinutes = buffer,
        NeedsPrivateRoom = needsPrivate,
        ArtistHours = [new WorkingHours { Day = DayOfWeek.Monday, Start = new(10, 0), End = new(14, 0) }],
        ArtistBusy = artistBusy ?? [],
        Rooms = [new RoomSchedule(1, workstations, roomBusy ?? [])],
        EarliestStartUtc = DateTime.MinValue,
        LatestStartUtc = DateTime.MaxValue,
        StepMinutes = 30,
    };

    [Fact]
    public void Slots_fit_inside_working_hours()
    {
        var slots = SlotFinder.FindSlots(Query(duration: 60), Day, Day);

        Assert.Equal(At(10), slots.First());
        Assert.Equal(At(13), slots.Last()); // 13:00-14:00 is the last one that ends by close
        Assert.Equal(7, slots.Count);
    }

    [Fact]
    public void No_slots_on_days_off()
    {
        Assert.Empty(SlotFinder.FindSlots(Query(), Day.AddDays(1), Day.AddDays(1)));
    }

    [Fact]
    public void Artist_booking_blocks_overlapping_slots_but_allows_back_to_back()
    {
        var q = Query(duration: 60, artistBusy: [new(At(11), At(12))]);
        var slots = SlotFinder.FindSlots(q, Day, Day);

        Assert.Contains(At(10), slots);      // ends exactly when the booking starts
        Assert.DoesNotContain(At(10, 30), slots);
        Assert.DoesNotContain(At(11, 30), slots);
        Assert.Contains(At(12), slots);      // starts exactly when it ends
    }

    [Fact]
    public void Buffer_keeps_artist_busy_after_the_procedure()
    {
        // 30 min + 15 min cleanup: 10:00 slot blocks the artist until 10:45, so an 11:00 booking is fine,
        // but a booking at 10:30 would collide with the new slot's cleanup.
        var q = Query(duration: 30, buffer: 15, artistBusy: [new(At(10, 30), At(11))]);
        Assert.Null(SlotFinder.FindRoom(q, At(10)));

        var q2 = Query(duration: 30, buffer: 15, artistBusy: [new(At(11), At(11, 30))]);
        Assert.NotNull(SlotFinder.FindRoom(q2, At(10)));
    }

    [Fact]
    public void Time_off_blocks_slots()
    {
        var q = Query(artistBusy: [new(At(0), At(12))]);
        Assert.Equal(At(12), SlotFinder.FindSlots(q, Day, Day).First());
    }

    [Fact]
    public void Room_full_when_all_workstations_are_taken()
    {
        BusyInterval[] others = [new(At(10), At(12)), new(At(10), At(12)), new(At(11), At(13))];

        Assert.Null(SlotFinder.FindRoom(Query(workstations: 3, roomBusy: others), At(11)));
        Assert.NotNull(SlotFinder.FindRoom(Query(workstations: 4, roomBusy: others), At(11)));
    }

    [Fact]
    public void Room_capacity_uses_peak_overlap_not_total_count()
    {
        // Three bookings overlap the 10:00-12:00 window, but never more than one at a time.
        BusyInterval[] others = [new(At(10), At(10, 30)), new(At(10, 30), At(11)), new(At(11), At(12))];
        Assert.NotNull(SlotFinder.FindRoom(Query(duration: 120, workstations: 2, roomBusy: others), At(10)));
    }

    [Fact]
    public void Private_booking_in_room_blocks_everyone()
    {
        var q = Query(workstations: 3, roomBusy: [new(At(11), At(11, 45), Private: true)]);
        var slots = SlotFinder.FindSlots(q, Day, Day);

        Assert.DoesNotContain(At(10, 30), slots);
        Assert.DoesNotContain(At(11), slots);
        Assert.Contains(At(10), slots);
        Assert.Contains(At(12), slots);
    }

    [Fact]
    public void Private_procedure_needs_an_empty_room()
    {
        var busy = new BusyInterval[] { new(At(11), At(12)) };

        Assert.Null(SlotFinder.FindRoom(Query(needsPrivate: true, roomBusy: busy), At(11)));
        Assert.NotNull(SlotFinder.FindRoom(Query(needsPrivate: false, roomBusy: busy), At(11)));
        Assert.NotNull(SlotFinder.FindRoom(Query(needsPrivate: true, roomBusy: busy), At(12)));
    }

    [Fact]
    public void Lead_time_and_horizon_are_respected()
    {
        var q = Query() with { EarliestStartUtc = At(11, 15), LatestStartUtc = At(12, 30) };
        var slots = SlotFinder.FindSlots(q, Day, Day);

        Assert.Equal([At(11, 30), At(12), At(12, 30)], slots);
    }

    [Fact]
    public void Working_hours_follow_local_time_across_dst()
    {
        var warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
        var hours = new WorkingHours[]
        {
            new() { Day = DayOfWeek.Saturday, Start = new(10, 0), End = new(12, 0) },
            new() { Day = DayOfWeek.Sunday, Start = new(10, 0), End = new(12, 0) },
        };
        var q = Query(zone: warsaw) with { ArtistHours = hours };

        // Clocks go back on Sun 2026-10-25: 10:00 local is 08:00 UTC on Saturday (CEST) and 09:00 UTC on Sunday (CET).
        Assert.Equal(new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc), SlotFinder.FindSlots(q, new(2026, 10, 24), new(2026, 10, 24)).First());
        Assert.Equal(new DateTime(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc), SlotFinder.FindSlots(q, new(2026, 10, 25), new(2026, 10, 25)).First());
    }
}
