using System.Globalization;
using System.Net;
using System.Text;
using Ws.Core.Domain;
using Ws.Core.Notifications;

namespace Ws.Api.Telegram;

/// <summary>Messages for the staff Telegram group (Russian, Telegram HTML).</summary>
public static class StaffMessages
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public const string ApprovePrefix = "approve:";
    public const string DeclinePrefix = "decline:";

    public static object DecisionButtons(int bookingId) => new
    {
        inline_keyboard = new[]
        {
            new[]
            {
                new { text = "✅ Подтвердить", callback_data = ApprovePrefix + bookingId },
                new { text = "❌ Отклонить", callback_data = DeclinePrefix + bookingId },
            },
        },
    };

    public static string BookingRequest(BookingNotice n)
    {
        var sb = new StringBuilder();
        sb.AppendLine(n.RescheduledFromLocal == null
            ? $"🆕 <b>Заявка #{n.BookingId}</b> · {E(n.ServiceName)}"
            : $"🔁 <b>Перенос записи #{n.BookingId}</b> · {E(n.ServiceName)}");
        var artist = n.ArtistTelegramUsername != null ? $"{E(n.ArtistName)} @{E(n.ArtistTelegramUsername)}" : E(n.ArtistName);
        sb.AppendLine($"Мастер: {artist}");
        if (n.RescheduledFromLocal is { } was) sb.AppendLine($"Было: <s>{E(was.ToString("ddd, d MMMM, HH:mm", Ru))}</s>");
        sb.AppendLine($"{(n.RescheduledFromLocal == null ? "Когда" : "Стало")}: {E(n.StartLocal.ToString("ddd, d MMMM, HH:mm", Ru))} ({n.DurationMinutes} мин)");
        sb.AppendLine($"Клиент: {E(n.ClientName)}{Contact(n.ClientPhone, n.ClientTelegramUserId)}");
        sb.AppendLine($"Источник: {Source(n.Source)}");
        if (n.WithGuardian) sb.AppendLine("⚠️ 16–17 лет, придёт с родителем/представителем");
        if (n.Tattoo is { } t)
        {
            if (t.Idea != null) sb.AppendLine($"Идея: {E(t.Idea)}");
            var parts = new[] { ("Место", t.Placement), ("Размер", t.Size), ("Стиль", t.Style) }
                .Where(p => p.Item2 != null).Select(p => $"{p.Item1}: {E(p.Item2!)}");
            var line = string.Join(" · ", parts);
            if (line.Length > 0) sb.AppendLine(line);
        }
        if (n.Notes != null) sb.AppendLine($"Комментарий: {E(n.Notes)}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Status line appended to a request once someone decided on it.</summary>
    public static string Decision(BookingStatus status, string who, bool clientOnTelegram) => status switch
    {
        BookingStatus.Confirmed => $"✅ <b>Подтверждено</b> · {E(who)}" + (clientOnTelegram
            ? "\nКлиенту отправлено подтверждение в Telegram."
            : "\n📞 Клиент записался на сайте: сообщите ему по телефону."),
        BookingStatus.Declined => $"❌ <b>Отклонено</b> · {E(who)}" + (clientOnTelegram
            ? "\nКлиенту предложено выбрать другое время."
            : "\n📞 Клиент записался на сайте: сообщите ему по телефону."),
        _ => $"ℹ️ Статус: {status}",
    };

    public static string Handoff(HandoffNotice n)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"🙋 <b>Нужен администратор</b> · чат #{n.ConversationId} ({Source(n.Channel)})");
        if (n.Reason != null) sb.AppendLine($"Причина: {E(n.Reason)}");
        var who = n.ClientName != null ? E(n.ClientName) : "клиент";
        var contact = Contact(n.ClientPhone, n.Channel == Channel.Telegram && long.TryParse(n.ExternalId, out var id) ? id : null);
        sb.AppendLine($"Кто: {who}{contact}");
        if (n.LastClientMessages.Count > 0)
        {
            sb.AppendLine("Последние сообщения:");
            foreach (var m in n.LastClientMessages) sb.AppendLine($"» {E(Shorten(m, 300))}");
        }
        if (n.Channel == Channel.WebChat && n.ClientPhone == null)
            sb.AppendLine("Клиент на сайте без телефона: ответить пока можно только когда он оставит контакт.");
        return sb.ToString().TrimEnd();
    }

    private static string Contact(string? phone, long? telegramUserId)
    {
        var parts = new List<string>();
        if (phone != null) parts.Add(E(phone));
        if (telegramUserId != null) parts.Add($"<a href=\"tg://user?id={telegramUserId}\">Telegram</a>");
        return parts.Count > 0 ? ", " + string.Join(", ", parts) : "";
    }

    private static string Source(Channel c) => c switch
    {
        Channel.Website => "форма на сайте",
        Channel.WebChat => "чат на сайте",
        Channel.Telegram => "Telegram",
        Channel.Admin => "админка",
        _ => c.ToString(),
    };

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>Escapes user text for Telegram HTML.</summary>
    private static string E(string s) => WebUtility.HtmlEncode(s);
}
