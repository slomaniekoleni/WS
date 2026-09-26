using PhoneNumbers;

namespace Ws.Core.Domain;

public static class PhoneFormat
{
    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    /// <summary>
    /// Parses a phone number typed by a client ("+375 29 123-45-67", "80291234567", "29 123 45 67"...)
    /// and returns it in E.164 ("+375291234567"), or null if it isn't a valid number.
    /// Numbers without a country code are read as <paramref name="defaultRegion"/> (the salon's country).
    /// </summary>
    public static string? Normalize(string? input, string defaultRegion)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        try
        {
            var number = Util.Parse(input, defaultRegion);
            return Util.IsValidNumber(number) ? Util.Format(number, PhoneNumberFormat.E164) : null;
        }
        catch (NumberParseException)
        {
            return null;
        }
    }
}
