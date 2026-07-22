using System.Globalization;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.Web.Pages.Owner;
using BodyLife.Crm.Web.Tests.Localization;

namespace BodyLife.Crm.Web.Tests.Pages.Owner;

[Collection(nameof(LocalizationCollection))]
public sealed class NonWorkingDayCorrectionWorkspaceViewModelTests
{
    [Theory]
    [InlineData("en-US", CommandErrorCode.ValidationFailed)]
    [InlineData("en-US", CommandErrorCode.ReasonRequired)]
    [InlineData("uk-UA", CommandErrorCode.ValidationFailed)]
    [InlineData("uk-UA", CommandErrorCode.ReasonRequired)]
    public void ConfirmationAdapterPreservesOnlyItsLocalizedValidationMessages(
        string culture,
        CommandErrorCode code)
    {
        const string localizedMessage = "Локалізоване повідомлення форми.";
        using var cultureScope = new CultureScope(culture);

        var viewModel = NonWorkingDayCorrectionWorkspaceViewModel
            .FromConfirmationFailure(
                GetActiveNonWorkingDaysForCorrectionResult.Succeeded([]),
                new NonWorkingDayCorrectionConfirmationFormInput(),
                [new CommandError(code, localizedMessage, "reason")],
                preserveLocalizedAdapterMessages: true);

        var error = Assert.Single(viewModel.Errors);
        Assert.Equal("reason", error.Field);
        Assert.Equal(localizedMessage, error.Message);
    }

    [Fact]
    public void ConfirmationAdapterRejectsMessagesFromNonAdapterErrorCodes()
    {
        Assert.Throws<InvalidOperationException>(() =>
            NonWorkingDayCorrectionWorkspaceViewModel.FromConfirmationFailure(
                GetActiveNonWorkingDaysForCorrectionResult.Succeeded([]),
                new NonWorkingDayCorrectionConfirmationFormInput(),
                [new CommandError(
                    CommandErrorCode.NotFound,
                    "Untrusted application message")],
                preserveLocalizedAdapterMessages: true));
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previousCulture = CultureInfo.CurrentCulture;
        private readonly CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(string culture)
        {
            var selected = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentCulture = selected;
            CultureInfo.CurrentUICulture = selected;
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
