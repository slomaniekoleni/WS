using Ws.Core.Domain;

namespace Ws.Tests;

public class PhoneFormatTests
{
    [Theory]
    [InlineData("+375 29 123-45-67", "+375291234567")]
    [InlineData("+375291234567", "+375291234567")]
    [InlineData("80291234567", "+375291234567")]     // domestic prefix 80
    [InlineData("8 (044) 123-45-67", "+375441234567")]
    [InlineData("+375 17 123-45-67", "+375171234567")] // Minsk landline
    [InlineData("+7 916 123-45-67", "+79161234567")]   // foreign numbers are fine too
    [InlineData("+48 512 345 678", "+48512345678")]
    public void Valid_numbers_are_normalized(string input, string expected)
    {
        Assert.Equal(expected, PhoneFormat.Normalize(input, "BY"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("+375 29 123")]
    [InlineData("+375 99 123-45-67")] // no such operator code
    [InlineData("not a phone")]
    public void Invalid_numbers_are_rejected(string input)
    {
        Assert.Null(PhoneFormat.Normalize(input, "BY"));
    }
}
