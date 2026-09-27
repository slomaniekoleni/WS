using System.Globalization;
using Ws.Core.Domain;

namespace Ws.Core.Notifications;

/// <summary>Plain-text messages to clients (any messenger), in the client's language.</summary>
public static class ClientMessages
{
    public static string Confirmed(Booking b, Salon salon) => Ru(b)
        ? $"Запись подтверждена ✅\n{Summary(b, salon)}\nАдрес: {salon.Address.Ru}\nВозьмите, пожалуйста, документ с фото. До встречи в {salon.Name}!"
        : $"Your booking is confirmed ✅\n{Summary(b, salon)}\nAddress: {salon.Address.En}\nPlease bring a photo ID. See you at {salon.Name}!";

    public static string Declined(Booking b, Salon salon) => Ru(b)
        ? $"К сожалению, мастер не сможет принять вас в это время:\n{Summary(b, salon)}\nНапишите, и я подберу другое время."
        : $"Unfortunately the artist can't take you at this time:\n{Summary(b, salon)}\nWrite to me and I'll find another time.";

    public static string Cancelled(Booking b, Salon salon) => Ru(b)
        ? $"Ваша запись отменена:\n{Summary(b, salon)}\nЕсли это неожиданно или хотите другое время, напишите сюда."
        : $"Your booking was cancelled:\n{Summary(b, salon)}\nIf that's unexpected or you'd like another time, write here.";

    /// <summary>Staff moved the booking.</summary>
    public static string Moved(Booking b, Salon salon) => Ru(b)
        ? $"Ваша запись перенесена 🔁\n{Summary(b, salon)}\nАдрес: {salon.Address.Ru}\nЕсли новое время не подходит, напишите сюда."
        : $"Your booking was moved 🔁\n{Summary(b, salon)}\nAddress: {salon.Address.En}\nIf the new time doesn't suit you, write here.";

    /// <summary>The client moved the booking; the artist still has to confirm the new time.</summary>
    public static string RescheduleRequested(Booking b, Salon salon) => Ru(b)
        ? $"Запрос на перенос отправлен 🔁\n{Summary(b, salon)}\nМастер подтвердит новое время, я сообщу."
        : $"Reschedule request sent 🔁\n{Summary(b, salon)}\nThe artist will confirm the new time, I'll let you know.";

    // Reschedule picker (Telegram buttons). Language = the client's booking language.
    public static string RescheduleButton(string language) => IsRu(language) ? "🔁 Перенести" : "🔁 Reschedule";
    public static string RescheduleYes(string language) => IsRu(language) ? "✅ Да, перенести" : "✅ Yes, move it";
    public static string RescheduleBack(string language) => IsRu(language) ? "‹ Назад" : "‹ Back";
    public static string RescheduleLater(string language) => IsRu(language) ? "Позже ›" : "Later ›";

    public static string ReschedulePickDay(Booking b, Salon salon) => Ru(b)
        ? $"Перенос записи:\n{Summary(b, salon)}\n\nВыберите новый день:"
        : $"Moving your booking:\n{Summary(b, salon)}\n\nPick a new day:";

    public static string ReschedulePickTime(string language, string day) =>
        IsRu(language) ? $"Свободное время на {day}:" : $"Free times on {day}:";

    public static string RescheduleConfirm(string language, string when) => IsRu(language)
        ? $"Перенести запись на {when}?\nМастер подтвердит новое время."
        : $"Move your booking to {when}?\nThe artist will confirm the new time.";

    public static string RescheduleNoSlots(string language) => IsRu(language)
        ? "В эти дни свободного времени нет. Нажмите «Позже ›» или напишите, и я подберу вариант."
        : "No free times on these days. Tap \"Later ›\" or write to me and I'll find an option.";

    public static string RescheduleSlotGone(string language) => IsRu(language)
        ? "Это время только что заняли. Выберите другое:"
        : "That time was just taken. Please pick another:";

    public static string RescheduleTooLate(string language, int hours, string phone) => IsRu(language)
        ? $"Перенести запись самостоятельно можно не позже чем за {hours} ч. Позвоните нам: {phone}."
        : $"Bookings can be moved online up to {hours} h before. Please call us: {phone}.";

    public static string RescheduleNotActive(string language) => IsRu(language)
        ? "Эта запись уже неактуальна."
        : "This booking is no longer active.";

    public static string Reminder(Booking b, Salon salon, ReminderKind kind) => (Ru(b), kind) switch
    {
        (true, ReminderKind.DayBefore) =>
            $"Напоминаем о записи завтра:\n{Summary(b, salon)}\nАдрес: {salon.Address.Ru}\nЕсли планы изменились, напишите сюда.",
        (true, _) =>
            $"Ждём вас через 2 часа:\n{Summary(b, salon)}\nАдрес: {salon.Address.Ru}",
        (false, ReminderKind.DayBefore) =>
            $"A reminder about your appointment tomorrow:\n{Summary(b, salon)}\nAddress: {salon.Address.En}\nIf your plans changed, just write here.",
        _ =>
            $"See you in 2 hours:\n{Summary(b, salon)}\nAddress: {salon.Address.En}",
    };

    public static string Welcome(string language, string salonName) => Languages.Normalize(language) == Languages.Russian
        ? $"Привет! Это {salonName}, тату и пирсинг студия. Спросите о ценах, мастерах или уходе, или я запишу вас. Что планируете?"
        : $"Hi! This is {salonName}, a tattoo & piercing studio. Ask about prices, artists or aftercare, or I can book you in. What are you planning?";

    public static string TextOnly(string language) => Languages.Normalize(language) == Languages.Russian
        ? "Пока я понимаю только текст. Референсы можно принести на консультацию."
        : "For now I can only read text. You can bring reference images to the consultation.";

    public static string Busy(string language) => Languages.Normalize(language) == Languages.Russian
        ? "Слишком много сообщений подряд. Подождите минуту, пожалуйста."
        : "Too many messages in a row. Please wait a minute.";

    public static string Unavailable(string language) => Languages.Normalize(language) == Languages.Russian
        ? "Не получилось ответить. Попробуйте ещё раз чуть позже."
        : "I couldn't answer just now. Please try again a bit later.";

    private static bool Ru(Booking b) => IsRu(b.Client.Language);
    private static bool IsRu(string language) => Languages.Normalize(language) == Languages.Russian;

    private static string Summary(Booking b, Salon salon)
    {
        var ru = Ru(b);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(salon.TimeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(b.StartUtc, zone);
        var culture = CultureInfo.GetCultureInfo(ru ? "ru-RU" : "en-GB");
        var when = local.ToString(ru ? "dddd, d MMMM, HH:mm" : "dddd d MMMM, HH:mm", culture);
        var service = b.Service.Name.Get(b.Client.Language);
        return ru ? $"{service}, мастер {b.Artist.Name}, {when}" : $"{service} with {b.Artist.Name}, {when}";
    }
}
