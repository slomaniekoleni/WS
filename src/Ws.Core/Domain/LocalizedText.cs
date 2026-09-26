namespace Ws.Core.Domain;

/// <summary>Text shown to clients in every supported language. Stored as JSON on the owning row.</summary>
public sealed class LocalizedText
{
    public string En { get; set; } = "";
    public string Ru { get; set; } = "";

    public LocalizedText() { }

    public LocalizedText(string en, string ru)
    {
        En = en;
        Ru = ru;
    }

    /// <summary>Text for <paramref name="language"/> ("en"/"ru"), falling back to English when missing.</summary>
    public string Get(string? language) =>
        Languages.Normalize(language) == Languages.Russian && Ru.Length > 0 ? Ru : En;
}

public static class Languages
{
    public const string English = "en";
    public const string Russian = "ru";

    public static string Normalize(string? language) =>
        language != null && language.StartsWith(Russian, StringComparison.OrdinalIgnoreCase) ? Russian : English;
}
