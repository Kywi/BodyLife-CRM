using BodyLife.Crm.Modules.Clients.Search;

namespace BodyLife.Crm.Tests.Modules.Clients.Search;

public sealed class ClientSearchNormalizerTests
{
    [Theory]
    [InlineData(" bl - 1001 ", "BL-1001")]
    [InlineData(" 00123 ", "00123")]
    [InlineData("ab/01", "AB/01")]
    [InlineData("\uff42\uff4c\uff0d\uff11\uff10\uff10\uff11", "BL-1001")]
    public void CardNormalizationIsWhitespaceInsensitiveAndCultureInvariant(
        string input,
        string expected)
    {
        Assert.Equal(expected, ClientSearchNormalizer.NormalizeCardNumber(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void CardNormalizationRejectsMissingValues(string? input)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ClientSearchNormalizer.NormalizeCardNumber(input));

        Assert.Equal("cardNumber", exception.ParamName);
    }

    [Theory]
    [InlineData("+38 (067) 123-45-67", "380671234567")]
    [InlineData("067.123.45.67", "0671234567")]
    [InlineData("\uff0b\uff13\uff18 \uff10\uff16\uff17 \uff11\uff12\uff13 \uff14\uff15 \uff16\uff17", "380671234567")]
    public void PhoneNormalizationRemovesFormattingOnly(string input, string expected)
    {
        Assert.Equal(expected, ClientSearchNormalizer.NormalizePhone(input));
    }

    [Fact]
    public void PhoneNormalizationDoesNotInferCountryCode()
    {
        var local = ClientSearchNormalizer.NormalizePhone("067 123 45 67");
        var international = ClientSearchNormalizer.NormalizePhone("+38 067 123 45 67");

        Assert.Equal("0671234567", local);
        Assert.Equal("380671234567", international);
        Assert.NotEqual(local, international);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("067ABC1234")]
    [InlineData("067+1234")]
    [InlineData("++380671234567")]
    public void PhoneNormalizationRejectsInvalidInput(string input)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ClientSearchNormalizer.NormalizePhone(input));

        Assert.Equal("phone", exception.ParamName);
    }

    [Fact]
    public void LastFourExtractionUsesExactNormalizedSuffix()
    {
        var normalizedPhone = ClientSearchNormalizer.NormalizePhone("+38 (067) 123-45-67");

        Assert.Equal("4567", ClientSearchNormalizer.ExtractPhoneLastFour(normalizedPhone));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("067-1234")]
    public void LastFourExtractionRejectsNonNormalizedInput(string input)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => ClientSearchNormalizer.ExtractPhoneLastFour(input));

        Assert.Equal("normalizedPhone", exception.ParamName);
    }

    [Theory]
    [InlineData("  anna\t maria  ", "ANNA MARIA")]
    [InlineData("o\u2019brien", "O'BRIEN")]
    [InlineData("anna\u2013maria", "ANNA-MARIA")]
    public void NamePartNormalizationPreservesIdentityWithoutFuzzyMatching(
        string input,
        string expected)
    {
        Assert.Equal(expected, ClientSearchNormalizer.NormalizeNamePart(input));
    }

    [Fact]
    public void NameNormalizationUsesInvariantCasingForUkrainianLetters()
    {
        Assert.Equal(
            "\u0406\u0412\u0410\u041d",
            ClientSearchNormalizer.NormalizeNamePart("\u0456\u0432\u0430\u043d"));
    }

    [Fact]
    public void FullNameNormalizationUsesStableSurnameNamePatronymicOrder()
    {
        Assert.Equal(
            "IVANENKO IVAN PETROVYCH",
            ClientSearchNormalizer.NormalizeFullName(
                " ivanenko ",
                "ivan",
                "  petrovych "));
        Assert.Equal(
            "IVANENKO IVAN",
            ClientSearchNormalizer.NormalizeFullName("ivanenko", "ivan", "   "));
    }

    [Fact]
    public void FullNameNormalizationRejectsMissingRequiredParts()
    {
        var surnameException = Assert.Throws<ArgumentException>(
            () => ClientSearchNormalizer.NormalizeFullName(" ", "Ivan", null));
        var nameException = Assert.Throws<ArgumentException>(
            () => ClientSearchNormalizer.NormalizeFullName("Ivanenko", null, null));

        Assert.Equal("surname", surnameException.ParamName);
        Assert.Equal("name", nameException.ParamName);
    }
}
