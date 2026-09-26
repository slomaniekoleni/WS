using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic.Models.Beta.Messages;

namespace Ws.Core.Receptionist;

/// <summary>
/// Our own JSON shape for Claude content blocks, stored in ConversationMessage.ContentJson.
/// Keeps the DB independent of SDK types and keeps thinking signatures so history replays exactly.
/// </summary>
public sealed record StoredBlock
{
    public required string Type { get; init; }
    public string? Text { get; init; }
    public string? Signature { get; init; }
    public string? Data { get; init; }
    public string? Id { get; init; }
    public string? Name { get; init; }
    public Dictionary<string, JsonElement>? Input { get; init; }
    public string? ToolUseId { get; init; }
    public bool? IsError { get; init; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(List<StoredBlock> blocks) => JsonSerializer.Serialize(blocks, Json);
    public static List<StoredBlock> Deserialize(string json) => JsonSerializer.Deserialize<List<StoredBlock>>(json, Json) ?? [];

    public static StoredBlock OfText(string text) => new() { Type = "text", Text = text };

    public static StoredBlock OfToolResult(string toolUseId, string content, bool isError) =>
        new() { Type = "tool_result", ToolUseId = toolUseId, Text = content, IsError = isError ? true : null };

    public static List<StoredBlock> FromResponse(IReadOnlyList<BetaContentBlock> content)
    {
        var list = new List<StoredBlock>();
        foreach (var block in content)
        {
            if (block.TryPickText(out var text)) list.Add(OfText(text.Text));
            else if (block.TryPickThinking(out var thinking))
                list.Add(new() { Type = "thinking", Text = thinking.Thinking, Signature = thinking.Signature });
            else if (block.TryPickRedactedThinking(out var redacted))
                list.Add(new() { Type = "redacted_thinking", Data = redacted.Data });
            else if (block.TryPickToolUse(out var use))
                list.Add(new() { Type = "tool_use", Id = use.ID, Name = use.Name, Input = use.Input.ToDictionary() });
        }
        return list;
    }

    public BetaContentBlockParam ToParam() => Type switch
    {
        "text" => new BetaTextBlockParam { Text = Text ?? "" },
        "thinking" => new BetaThinkingBlockParam { Thinking = Text ?? "", Signature = Signature ?? "" },
        "redacted_thinking" => new BetaRedactedThinkingBlockParam { Data = Data ?? "" },
        "tool_use" => new BetaToolUseBlockParam { ID = Id!, Name = Name!, Input = Input ?? [] },
        "tool_result" => new BetaToolResultBlockParam { ToolUseID = ToolUseId!, Content = Text ?? "", IsError = IsError },
        _ => throw new InvalidOperationException($"Unknown stored block type {Type}"),
    };
}
