using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ws.Core.Data;
using Ws.Core.Domain;

namespace Ws.Api.Controllers.Admin;

/// <summary>The service/price list as staff edit it.</summary>
[Route("api/admin/services")]
public sealed class AdminServicesController(WsDbContext db) : AdminControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<AdminServiceDto>> List(CancellationToken ct)
    {
        var salonId = SalonId;
        var services = await db.Services.AsNoTracking()
            .Where(s => s.SalonId == salonId).Include(s => s.Artists).OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

        return services.Select(s => new AdminServiceDto(
            s.Id, s.Kind, new LocalizedDto(s.Name.En, s.Name.Ru), new LocalizedDto(s.Description.En, s.Description.Ru),
            s.DurationMinutes, s.BufferMinutes, s.PriceFrom, s.PriceTo, s.NeedsPrivateRoom, s.AdultsOnly, s.BookableOnline,
            s.IsActive, s.SortOrder, s.Artists.Select(a => a.ArtistId).ToList()));
    }

    [HttpPost]
    public Task<ActionResult<IdDto>> Create(ServiceEdit edit, CancellationToken ct) => SaveAsync(null, edit, ct);

    [HttpPut("{id:int}")]
    public Task<ActionResult<IdDto>> Update(int id, ServiceEdit edit, CancellationToken ct) => SaveAsync(id, edit, ct);

    private async Task<ActionResult<IdDto>> SaveAsync(int? id, ServiceEdit edit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(edit.Name.En) && string.IsNullOrWhiteSpace(edit.Name.Ru)) return Invalid("name", "Name is required.");
        if (edit.DurationMinutes is < 5 or > 720) return Invalid("durationMinutes", "Duration must be 5-720 minutes.");
        if (edit.BufferMinutes is < 0 or > 240) return Invalid("bufferMinutes", "Buffer must be 0-240 minutes.");
        if (edit.PriceFrom < 0 || edit.PriceTo < edit.PriceFrom) return Invalid("priceTo", "Price range is invalid.");
        var salonId = SalonId;

        Service? service;
        if (id == null)
        {
            service = new Service { SalonId = salonId };
            db.Services.Add(service);
        }
        else
        {
            service = await db.Services.FirstOrDefaultAsync(s => s.Id == id && s.SalonId == salonId, ct);
            if (service == null) return NotFound();
        }

        service.Kind = edit.Kind;
        // Either language may be filled first; English falls back to Russian.
        var nameEn = edit.Name.En.Trim();
        var nameRu = edit.Name.Ru.Trim();
        service.Name = new LocalizedText(nameEn.Length > 0 ? nameEn : nameRu, nameRu);
        service.Description = new LocalizedText(edit.Description.En.Trim(), edit.Description.Ru.Trim());
        service.DurationMinutes = edit.DurationMinutes;
        service.BufferMinutes = edit.BufferMinutes;
        service.PriceFrom = edit.PriceFrom;
        service.PriceTo = edit.PriceTo;
        service.NeedsPrivateRoom = edit.NeedsPrivateRoom;
        service.AdultsOnly = edit.AdultsOnly;
        service.BookableOnline = edit.BookableOnline;
        service.IsActive = edit.IsActive;
        service.SortOrder = edit.SortOrder;
        await db.SaveChangesAsync(ct);
        return new IdDto(service.Id);
    }
}

public sealed record AdminServiceDto(
    int Id, ServiceKind Kind, LocalizedDto Name, LocalizedDto Description, int DurationMinutes, int BufferMinutes,
    decimal? PriceFrom, decimal? PriceTo, bool NeedsPrivateRoom, bool AdultsOnly, bool BookableOnline, bool IsActive,
    int SortOrder, List<int> ArtistIds);

public sealed record ServiceEdit(
    ServiceKind Kind, LocalizedDto Name, LocalizedDto Description, int DurationMinutes, int BufferMinutes,
    decimal? PriceFrom, decimal? PriceTo, bool NeedsPrivateRoom, bool AdultsOnly, bool BookableOnline, bool IsActive, int SortOrder);
