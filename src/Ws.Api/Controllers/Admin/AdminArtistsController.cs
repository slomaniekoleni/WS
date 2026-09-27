using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ws.Core.Data;
using Ws.Core.Domain;

namespace Ws.Api.Controllers.Admin;

/// <summary>Artist profiles, weekly hours, services they do, and days off.</summary>
[Route("api/admin/artists")]
public sealed class AdminArtistsController(WsDbContext db, TimeProvider clock) : AdminControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<AdminArtistDto>> List(CancellationToken ct)
    {
        var salonId = SalonId;
        var now = clock.GetUtcNow().UtcDateTime;
        var artists = await db.Artists.AsNoTracking()
            .Where(a => a.SalonId == salonId)
            .Include(a => a.WorkingHours).Include(a => a.TimeOff).Include(a => a.Services)
            .OrderBy(a => a.SortOrder)
            .ToListAsync(ct);

        return artists.Select(a => new AdminArtistDto(
            a.Id, a.Name, a.Specialty, new LocalizedDto(a.Bio.En, a.Bio.Ru), a.Styles, a.PhotoUrl, a.InstagramHandle,
            a.TelegramUsername, a.IsActive, a.SortOrder,
            a.WorkingHours.OrderBy(h => ((int)h.Day + 6) % 7).ThenBy(h => h.Start).Select(h => new ShiftDto(h.Day, h.Start, h.End)).ToList(),
            a.TimeOff.Where(t => t.EndUtc > now).OrderBy(t => t.StartUtc).Select(t => new TimeOffDto(t.Id, t.StartUtc, t.EndUtc, t.Reason)).ToList(),
            a.Services.Select(s => s.ServiceId).ToList()));
    }

    [HttpPost]
    public Task<ActionResult<IdDto>> Create(ArtistEdit edit, CancellationToken ct) => SaveAsync(null, edit, ct);

    [HttpPut("{id:int}")]
    public Task<ActionResult<IdDto>> Update(int id, ArtistEdit edit, CancellationToken ct) => SaveAsync(id, edit, ct);

    /// <summary>Days off / vacations, entered in salon-local time.</summary>
    [HttpPost("{id:int}/time-off")]
    public async Task<ActionResult<TimeOffDto>> AddTimeOff(int id, TimeOffRequest req, CancellationToken ct)
    {
        var salonId = SalonId;
        if (!await db.Artists.AnyAsync(a => a.Id == id && a.SalonId == salonId, ct)) return NotFound();
        if (req.EndLocal <= req.StartLocal) return Invalid("endLocal", "End must be after start.");

        var zone = await SalonZoneAsync(db, ct);
        var off = new TimeOff
        {
            ArtistId = id,
            StartUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(req.StartLocal, DateTimeKind.Unspecified), zone),
            EndUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(req.EndLocal, DateTimeKind.Unspecified), zone),
            Reason = Blank(req.Reason),
        };
        db.TimeOff.Add(off);
        await db.SaveChangesAsync(ct);
        return new TimeOffDto(off.Id, off.StartUtc, off.EndUtc, off.Reason);
    }

    [HttpDelete("~/api/admin/time-off/{id:int}")]
    public async Task<IActionResult> DeleteTimeOff(int id, CancellationToken ct)
    {
        var salonId = SalonId;
        var off = await db.TimeOff.FirstOrDefaultAsync(t => t.Id == id && db.Artists.Any(a => a.Id == t.ArtistId && a.SalonId == salonId), ct);
        if (off == null) return NotFound();
        db.TimeOff.Remove(off);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ActionResult<IdDto>> SaveAsync(int? id, ArtistEdit edit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(edit.Name)) return Invalid("name", "Name is required.");
        if (edit.Hours?.Any(h => h.End <= h.Start) == true) return Invalid("hours", "Each shift must end after it starts.");
        var salonId = SalonId;

        Artist? artist;
        if (id == null)
        {
            artist = new Artist { SalonId = salonId };
            db.Artists.Add(artist);
        }
        else
        {
            artist = await db.Artists.Include(a => a.WorkingHours).Include(a => a.Services)
                .FirstOrDefaultAsync(a => a.Id == id && a.SalonId == salonId, ct);
            if (artist == null) return NotFound();
        }

        artist.Name = edit.Name.Trim();
        artist.Specialty = edit.Specialty;
        artist.Bio = new LocalizedText(edit.Bio.En.Trim(), edit.Bio.Ru.Trim());
        artist.Styles = edit.Styles?.Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().ToList() ?? [];
        artist.PhotoUrl = Blank(edit.PhotoUrl);
        artist.InstagramHandle = Blank(edit.InstagramHandle)?.TrimStart('@');
        artist.TelegramUsername = Blank(edit.TelegramUsername)?.TrimStart('@');
        artist.IsActive = edit.IsActive;
        artist.SortOrder = edit.SortOrder;

        if (edit.Hours != null)
        {
            db.WorkingHours.RemoveRange(artist.WorkingHours);
            artist.WorkingHours = edit.Hours.Select(h => new WorkingHours { Day = h.Day, Start = h.Start, End = h.End }).ToList();
        }

        if (edit.ServiceIds != null)
        {
            var valid = await db.Services.Where(s => s.SalonId == salonId && edit.ServiceIds.Contains(s.Id)).Select(s => s.Id).ToListAsync(ct);
            artist.Services.RemoveAll(s => !valid.Contains(s.ServiceId));
            foreach (var sid in valid.Where(v => artist.Services.All(s => s.ServiceId != v)))
                artist.Services.Add(new ArtistService { ServiceId = sid, Artist = artist });
        }

        await db.SaveChangesAsync(ct);
        return new IdDto(artist.Id);
    }
}

public sealed record IdDto(int Id);

public sealed record ShiftDto(DayOfWeek Day, TimeOnly Start, TimeOnly End);

public sealed record TimeOffDto(int Id, DateTime StartUtc, DateTime EndUtc, string? Reason);

public sealed record TimeOffRequest(DateTime StartLocal, DateTime EndLocal, string? Reason);

public sealed record AdminArtistDto(
    int Id, string Name, ArtistSpecialty Specialty, LocalizedDto Bio, List<string> Styles, string? PhotoUrl,
    string? InstagramHandle, string? TelegramUsername, bool IsActive, int SortOrder,
    List<ShiftDto> Hours, List<TimeOffDto> TimeOff, List<int> ServiceIds);

/// <summary>Hours / ServiceIds null = leave unchanged.</summary>
public sealed record ArtistEdit(
    string Name, ArtistSpecialty Specialty, LocalizedDto Bio, List<string>? Styles, string? PhotoUrl,
    string? InstagramHandle, string? TelegramUsername, bool IsActive, int SortOrder,
    List<ShiftDto>? Hours, List<int>? ServiceIds);
