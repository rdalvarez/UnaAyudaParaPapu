using System.Globalization;
using PapaPersonas.Core.Stock.Paso4;

namespace PapaPersonas.Tests.Stock.Paso4;

public sealed class Paso4DateFormattingTests
{
    [Theory]
    [InlineData("es-AR")]
    [InlineData("en-US")]
    public void IsoDateFormatter_IsCultureInvariant_ForProcess4DateDisplay(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            var date = new DateOnly(2026, 8, 10);
            var isoText = Paso4ProcessUiLogic.FormatDateIso(date);
            Assert.Equal("2026-08-10", isoText);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}
