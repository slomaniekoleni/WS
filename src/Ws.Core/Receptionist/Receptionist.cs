using System.Globalization;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ws.Core.Data;
using Ws.Core.Domain;
using Ws.Core.Notifications;

namespace Ws.Core.Receptionist;

public sealed record ReceptionistReply(int ConversationId, string Text, bool NeedsHuman);

/// <summary>
/// The AI receptionist. Channel-agnostic: web chat, Telegram (and later Instagram/WhatsApp) all call ReplyAsync
/// with their own channel + user id. Conversation history lives in the DB and is replayed to Claude every turn.
/// </summary>
public sealed class Receptionist(
    WsDbContext db,
    AnthropicClient claude,
    ReceptionistTools tools,
    TimeProvider clock,
    IOptions<ReceptionistOptions> options,
    ILogger<Receptionist> log,
    IStaffNotifier? notifier = null)
{
    private readonly IStaffNotifier _notifier = notifier ?? NullStaffNotifier.Instance;
    private readonly ReceptionistOptions _opt = options.Value;

    private static readonly List<BetaToolUnion> ToolDefinitions = ReceptionistTools.Specs
        .Select(s => (BetaToolUnion)new BetaTool
        {
            Name = s.Name,
            Description = s.Description,
            InputSchema = new()
            {
                Properties = s.Properties.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value)),
                Required = s.Required,
            },
        })
        .ToList();

    public async Task<ReceptionistReply> ReplyAsync(
        int salonId, Channel channel, string externalId, string userText, string? language, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var conversation = await db.Conversations
            .Include(c => c.Messages.OrderBy(m => m.Id))
            .FirstOrDefaultAsync(c => c.SalonId == salonId && c.Channel == channel && c.ExternalId == externalId, ct);
        if (conversation == null)
        {
            conversation = new Conversation { SalonId = salonId, Channel = channel, ExternalId = externalId, CreatedAtUtc = now };
            db.Conversations.Add(conversation);
        }
        if (language != null) conversation.Language = Languages.Normalize(language);
        var wasNeedsHuman = conversation.NeedsHuman;

        userText = userText.Trim();
        if (userText.Length > _opt.MaxUserMessageLength) userText = userText[.._opt.MaxUserMessageLength];

        // Everything produced this turn is saved only if the turn completes, so a failed call never
        // leaves half a tool exchange in the history.
        var turn = new List<ConversationMessage>();

        if (conversation.Messages.Count >= _opt.MaxMessagesPerConversation)
        {
            conversation.NeedsHuman = true;
            conversation.HandoffReason ??= "Conversation too long for the AI";
            var text = conversation.Language == Languages.Russian
                ? "Мы уже долго общаемся, поэтому я передам диалог администратору, он скоро ответит."
                : "This chat has gotten long, so I'm passing it to our team; they'll reply soon.";
            turn.Add(Plain(MessageRole.User, userText, now));
            turn.Add(Plain(MessageRole.Assistant, text, now));
            await SaveAsync(conversation, turn, now, wasNeedsHuman, ct);
            return new ReceptionistReply(conversation.Id, text, true);
        }

        var salon = await db.Salons.AsNoTracking().SingleAsync(s => s.Id == salonId, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(salon.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, zone);
        var context = $"[Context: now {localNow.ToString("dddd yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} salon time; " +
                      $"channel {channel}; site language {conversation.Language}]";

        turn.Add(new ConversationMessage
        {
            Role = MessageRole.User,
            Text = userText,
            ContentJson = StoredBlock.Serialize([StoredBlock.OfText(context), StoredBlock.OfText(userText)]),
            CreatedAtUtc = now,
        });

        var system = await ReceptionistPrompt.BuildAsync(db, salonId, ct);
        string replyText;

        for (var round = 0; ; round++)
        {
            var messages = conversation.Messages.Concat(turn).Where(m => m.ContentJson != null).Select(ToParam).ToList();
            var lastRound = round >= _opt.MaxToolRounds;
            var response = await CallAsync(system, messages, allowTools: !lastRound, ct);

            log.LogInformation(
                "Claude {Model} stop={Stop} in={In} cache_read={CacheRead} cache_write={CacheWrite} out={Out} conv={Conv}",
                response.Model, response.StopReason?.ToString(), response.Usage.InputTokens, response.Usage.CacheReadInputTokens,
                response.Usage.CacheCreationInputTokens, response.Usage.OutputTokens, conversation.Id);

            var stop = response.StopReason?.ToString() ?? "";
            if (stop.Contains("refusal", StringComparison.OrdinalIgnoreCase))
            {
                conversation.NeedsHuman = true;
                conversation.HandoffReason ??= "AI declined to answer";
                replyText = conversation.Language == Languages.Russian
                    ? "С этим вопросом лучше поможет администратор, я передал ему диалог."
                    : "Our team can help with this better; I've passed the conversation to them.";
                turn.Add(Plain(MessageRole.Assistant, replyText, now));
                break;
            }

            var blocks = StoredBlock.FromResponse(response.Content);
            var toolUses = response.Content.Select(b => b.TryPickToolUse(out var t) ? t : null).OfType<BetaToolUseBlock>().ToList();
            var text = string.Join("\n\n", response.Content.Select(b => b.TryPickText(out var t) ? t.Text : null).OfType<string>()).Trim();

            if (toolUses.Count == 0 || stop.Contains("max_tokens", StringComparison.OrdinalIgnoreCase))
            {
                // A reply cut off mid tool call can't be replayed; keep only its text.
                if (toolUses.Count > 0) blocks = blocks.Where(b => b.Type is "text").ToList();
                replyText = text.Length > 0 ? text : Fallback(conversation.Language);
                turn.Add(new ConversationMessage
                {
                    Role = MessageRole.Assistant,
                    Text = replyText,
                    ContentJson = StoredBlock.Serialize(blocks.Count > 0 ? blocks : [StoredBlock.OfText(replyText)]),
                    CreatedAtUtc = now,
                });
                break;
            }

            turn.Add(new ConversationMessage
            {
                Role = MessageRole.Assistant,
                Text = text,
                ContentJson = StoredBlock.Serialize(blocks),
                CreatedAtUtc = now,
            });

            // All results for one assistant message go back in a single user message.
            var results = new List<StoredBlock>();
            foreach (var use in toolUses)
            {
                var outcome = await tools.ExecuteAsync(use.Name, use.Input, conversation, ct);
                log.LogInformation("Tool {Tool} error={IsError} conv={Conv}", use.Name, outcome.IsError, conversation.Id);
                results.Add(StoredBlock.OfToolResult(use.ID, outcome.Content, outcome.IsError));
            }
            turn.Add(new ConversationMessage
            {
                Role = MessageRole.ToolResult,
                ContentJson = StoredBlock.Serialize(results),
                CreatedAtUtc = now,
            });
        }

        await SaveAsync(conversation, turn, now, wasNeedsHuman, ct);
        // Text written between tool calls ("let me check...") is part of the reply the client sees.
        var shown = string.Join("\n\n", turn.Where(m => m.Role == MessageRole.Assistant && m.Text.Length > 0).Select(m => m.Text));
        return new ReceptionistReply(conversation.Id, shown.Length > 0 ? shown : replyText, conversation.NeedsHuman);
    }

    private async Task<BetaMessage> CallAsync(string system, List<BetaMessageParam> messages, bool allowTools, CancellationToken ct)
    {
        var parameters = new MessageCreateParams
        {
            Model = _opt.Model,
            MaxTokens = 16000,
            // Refused requests are re-served by a fallback model instead of just stopping.
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            OutputConfig = new BetaOutputConfig { Effort = _opt.Effort },
            // Cached: the system prompt (instructions + catalog) and, via the top-level breakpoint, the growing history.
            System = new List<BetaTextBlockParam> { new() { Text = system, CacheControl = new BetaCacheControlEphemeral() } },
            CacheControl = new BetaCacheControlEphemeral(),
            Tools = ToolDefinitions,
            ToolChoice = allowTools ? new BetaToolChoiceAuto() : new BetaToolChoiceNone(),
            Messages = messages,
        };
        return await claude.Beta.Messages.Create(parameters, cancellationToken: ct);
    }

    private static BetaMessageParam ToParam(ConversationMessage m) => new()
    {
        Role = m.Role == MessageRole.Assistant ? Role.Assistant : Role.User,
        Content = StoredBlock.Deserialize(m.ContentJson!).Select(b => b.ToParam()).ToList(),
    };

    private async Task SaveAsync(
        Conversation conversation, List<ConversationMessage> turn, DateTime now, bool wasNeedsHuman, CancellationToken ct)
    {
        conversation.Messages.AddRange(turn);
        conversation.LastMessageAtUtc = now;
        await db.SaveChangesAsync(ct);

        // Newly handed off to a human: tell the team once (not on every later message).
        if (conversation.NeedsHuman && !wasNeedsHuman) await NotifyHandoffAsync(conversation, ct);
    }

    private async Task NotifyHandoffAsync(Conversation conversation, CancellationToken ct)
    {
        var client = conversation.ClientId == null
            ? null
            : await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conversation.ClientId, ct);
        var lastMessages = conversation.Messages
            .Where(m => m.Role == MessageRole.User && m.Text.Length > 0)
            .TakeLast(3)
            .Select(m => m.Text)
            .ToList();
        _notifier.HandoffRequested(new HandoffNotice(
            conversation.Id, conversation.Channel, conversation.ExternalId, client?.Name, client?.Phone,
            conversation.HandoffReason, lastMessages));
    }

    private static ConversationMessage Plain(MessageRole role, string text, DateTime now) => new()
    {
        Role = role,
        Text = text,
        ContentJson = StoredBlock.Serialize([StoredBlock.OfText(text)]),
        CreatedAtUtc = now,
    };

    private static string Fallback(string language) => language == Languages.Russian
        ? "Извините, не получилось ответить. Попробуйте ещё раз или позвоните нам."
        : "Sorry, I couldn't answer that. Please try again or call us.";
}
