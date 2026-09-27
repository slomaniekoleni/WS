using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Ws.Api.Telegram;

/// <summary>Result of a Bot API call. ErrorCode 403 = the user blocked the bot or never started it.</summary>
public readonly record struct TelegramResult(bool Ok, int? ErrorCode = null, long? MessageId = null);

/// <summary>Minimal Telegram Bot API client (only what the salon bot needs). Never throws for API errors.</summary>
public sealed class TelegramApi(IHttpClientFactory httpFactory, IOptions<TelegramOptions> options, ILogger<TelegramApi> log)
{
    public const string HttpClientName = "telegram";
    /// <summary>Telegram rejects longer texts.</summary>
    public const int MaxMessageLength = 4096;

    private readonly TelegramOptions _opt = options.Value;
    private HttpClient Http => httpFactory.CreateClient(HttpClientName);

    public static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<Update>> GetUpdatesAsync(long offset, CancellationToken ct)
    {
        var response = await CallAsync<List<Update>>("getUpdates", new
        {
            offset,
            timeout = _opt.PollTimeoutSeconds,
            allowed_updates = new[] { "message", "callback_query" },
        }, ct);
        return response.Result ?? [];
    }

    public async Task<User?> GetMeAsync(CancellationToken ct) =>
        (await CallAsync<User>("getMe", new { }, ct)).Result;

    public async Task<TelegramResult> SendMessageAsync(long chatId, string text, bool html = false, object? replyMarkup = null, CancellationToken ct = default)
    {
        var response = await CallAsync<Message>("sendMessage", new
        {
            chat_id = chatId,
            text = Truncate(text),
            parse_mode = html ? "HTML" : null,
            reply_markup = replyMarkup,
            link_preview_options = new { is_disabled = true },
        }, ct);
        return new TelegramResult(response.Ok, response.ErrorCode, response.Result?.MessageId);
    }

    /// <summary>Replaces a message's text; without replyMarkup its inline buttons are removed.</summary>
    public async Task<TelegramResult> EditMessageTextAsync(long chatId, long messageId, string text, bool html = false, object? replyMarkup = null, CancellationToken ct = default)
    {
        var response = await CallAsync<JsonElement>("editMessageText", new
        {
            chat_id = chatId,
            message_id = messageId,
            text = Truncate(text),
            parse_mode = html ? "HTML" : null,
            reply_markup = replyMarkup,
            link_preview_options = new { is_disabled = true },
        }, ct);
        return new TelegramResult(response.Ok, response.ErrorCode);
    }

    public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, CancellationToken ct = default) =>
        CallAsync<JsonElement>("answerCallbackQuery", new { callback_query_id = callbackQueryId, text }, ct);

    public Task SendTypingAsync(long chatId, CancellationToken ct = default) =>
        CallAsync<JsonElement>("sendChatAction", new { chat_id = chatId, action = "typing" }, ct);

    private async Task<ApiResponse<T>> CallAsync<T>(string method, object body, CancellationToken ct)
    {
        try
        {
            // The URL contains the bot token: never log it (System.Net.Http logs are at Warning in appsettings).
            using var response = await Http.PostAsJsonAsync($"https://api.telegram.org/bot{_opt.BotToken}/{method}", body, Json, ct);
            var result = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json, ct);
            if (result is { Ok: true }) return result;

            log.LogWarning("Telegram {Method} failed: {Code} {Description}", method, result?.ErrorCode ?? (int)response.StatusCode, result?.Description);
            return result ?? new ApiResponse<T>(false, default, (int)response.StatusCode, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Telegram {Method} failed", method);
            return new ApiResponse<T>(false, default, null, ex.Message);
        }
    }

    private static string Truncate(string text) =>
        text.Length <= MaxMessageLength ? text : text[..(MaxMessageLength - 1)] + "…";

    public sealed record ApiResponse<T>(
        [property: JsonPropertyName("ok")] bool Ok,
        [property: JsonPropertyName("result")] T? Result,
        [property: JsonPropertyName("error_code")] int? ErrorCode,
        [property: JsonPropertyName("description")] string? Description);

    public sealed record Update(
        [property: JsonPropertyName("update_id")] long UpdateId,
        [property: JsonPropertyName("message")] Message? Message,
        [property: JsonPropertyName("callback_query")] CallbackQuery? CallbackQuery);

    public sealed record Message(
        [property: JsonPropertyName("message_id")] long MessageId,
        [property: JsonPropertyName("chat")] Chat Chat,
        [property: JsonPropertyName("from")] User? From,
        [property: JsonPropertyName("text")] string? Text);

    public sealed record Chat(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("title")] string? Title);

    public sealed record User(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("first_name")] string FirstName,
        [property: JsonPropertyName("username")] string? Username,
        [property: JsonPropertyName("language_code")] string? LanguageCode)
    {
        public string Display => Username != null ? $"@{Username}" : FirstName;
    }

    public sealed record CallbackQuery(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("from")] User From,
        [property: JsonPropertyName("message")] Message? Message,
        [property: JsonPropertyName("data")] string? Data);
}
