using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ws.Core.Data;
using Ws.Core.Domain;

namespace Ws.Api.Controllers.Admin;

/// <summary>AI chat transcripts from all channels; handed-off chats first.</summary>
[Route("api/admin/conversations")]
public sealed class AdminConversationsController(WsDbContext db) : AdminControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<ConversationSummaryDto>> List(bool? needsHuman, CancellationToken ct)
    {
        var salonId = SalonId;
        var query = db.Conversations.AsNoTracking().Where(c => c.SalonId == salonId);
        if (needsHuman == true) query = query.Where(c => c.NeedsHuman);

        return await query
            .OrderByDescending(c => c.NeedsHuman).ThenByDescending(c => c.LastMessageAtUtc)
            .Take(200)
            .Select(c => new ConversationSummaryDto(
                c.Id, c.Channel,
                db.Clients.Where(cl => cl.Id == c.ClientId).Select(cl => cl.Name).FirstOrDefault(),
                db.Clients.Where(cl => cl.Id == c.ClientId).Select(cl => cl.Phone).FirstOrDefault(),
                c.NeedsHuman, c.HandoffReason, c.LastMessageAtUtc,
                c.Messages.Where(m => m.Role == MessageRole.User && m.Text != "").OrderByDescending(m => m.Id).Select(m => m.Text).FirstOrDefault(),
                c.Messages.Count(m => m.Role != MessageRole.ToolResult)))
            .ToListAsync(ct);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ConversationDto>> Get(int id, CancellationToken ct)
    {
        var salonId = SalonId;
        var conv = await db.Conversations.AsNoTracking()
            .Include(c => c.Messages.OrderBy(m => m.Id))
            .FirstOrDefaultAsync(c => c.Id == id && c.SalonId == salonId, ct);
        if (conv == null) return NotFound();
        var client = conv.ClientId == null ? null : await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conv.ClientId, ct);

        return new ConversationDto(
            conv.Id, conv.Channel, conv.ExternalId, conv.Language, conv.NeedsHuman, conv.HandoffReason,
            client?.Name, client?.Phone, client?.TelegramUserId,
            conv.Messages
                .Where(m => m.Role != MessageRole.ToolResult && m.Text.Length > 0)
                .Select(m => new TranscriptMessageDto(m.Role, m.Text, m.CreatedAtUtc))
                .ToList());
    }

    /// <summary>Staff handled a handoff: it leaves the "needs a human" list.</summary>
    [HttpPost("{id:int}/resolve")]
    public async Task<IActionResult> Resolve(int id, CancellationToken ct)
    {
        var salonId = SalonId;
        var conv = await db.Conversations.FirstOrDefaultAsync(c => c.Id == id && c.SalonId == salonId, ct);
        if (conv == null) return NotFound();
        conv.NeedsHuman = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public sealed record ConversationSummaryDto(
    int Id, Channel Channel, string? ClientName, string? ClientPhone, bool NeedsHuman, string? HandoffReason,
    DateTime LastMessageAtUtc, string? LastMessage, int MessageCount);

public sealed record ConversationDto(
    int Id, Channel Channel, string ExternalId, string Language, bool NeedsHuman, string? HandoffReason,
    string? ClientName, string? ClientPhone, long? ClientTelegramUserId, List<TranscriptMessageDto> Messages);

public sealed record TranscriptMessageDto(MessageRole Role, string Text, DateTime AtUtc);
