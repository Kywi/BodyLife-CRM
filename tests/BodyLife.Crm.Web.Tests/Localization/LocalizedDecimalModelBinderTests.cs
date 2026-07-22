using System.Globalization;
using BodyLife.Crm.Web.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace BodyLife.Crm.Web.Tests.Localization;

[Collection(nameof(LocalizationCollection))]
public sealed class LocalizedDecimalModelBinderTests
{
    [Theory]
    [InlineData("en-US", "125.50")]
    [InlineData("uk-UA", "125.50")]
    [InlineData("uk-UA", "125,50")]
    public async Task CultureSpecificDecimalSeparatorsBindWithoutGrouping(
        string culture,
        string input)
    {
        using var cultureScope = new CultureScope(culture);
        var context = CreateContext(input);

        await new LocalizedDecimalModelBinder().BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        Assert.Equal(125.50m, Assert.IsType<decimal>(context.Result.Model));
        Assert.Empty(context.ModelState["amount"]!.Errors);
    }

    [Theory]
    [InlineData(
        "en-US",
        "1,234",
        "Enter a valid amount using a dot as the decimal separator and no grouping separators.")]
    [InlineData(
        "en-US",
        "12,34.56",
        "Enter a valid amount using a dot as the decimal separator and no grouping separators.")]
    [InlineData(
        "uk-UA",
        "12,34.56",
        "Вкажіть коректну суму, використовуючи кому або крапку як десятковий роздільник без розділювачів тисяч.")]
    public async Task UnsupportedOrAmbiguousSeparatorsAddLocalizedModelStateError(
        string culture,
        string input,
        string expectedError)
    {
        using var cultureScope = new CultureScope(culture);
        var context = CreateContext(input);

        await new LocalizedDecimalModelBinder().BindModelAsync(context);

        Assert.False(context.Result.IsModelSet);
        var error = Assert.Single(context.ModelState["amount"]!.Errors);
        Assert.Equal(expectedError, error.ErrorMessage);
    }

    [Theory]
    [InlineData(typeof(decimal), true)]
    [InlineData(typeof(decimal?), true)]
    [InlineData(typeof(int), false)]
    public void ProviderOnlyHandlesDecimalTypes(Type type, bool expected)
    {
        var metadataProvider = new EmptyModelMetadataProvider();
        var context = new TestModelBinderProviderContext(
            metadataProvider.GetMetadataForType(type));

        var binder = new LocalizedDecimalModelBinderProvider().GetBinder(context);

        Assert.Equal(expected, binder is LocalizedDecimalModelBinder);
    }

    private static ModelBindingContext CreateContext(string input)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBodyLifeLocalization();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());
        var valueProvider = new FormValueProvider(
            BindingSource.Form,
            new FormCollection(new Dictionary<string, StringValues>
            {
                ["amount"] = input,
            }),
            CultureInfo.CurrentCulture);
        var metadataProvider = new EmptyModelMetadataProvider();

        return DefaultModelBindingContext.CreateBindingContext(
            actionContext,
            valueProvider,
            metadataProvider.GetMetadataForType(typeof(decimal)),
            bindingInfo: null,
            modelName: "amount");
    }

    private sealed class TestModelBinderProviderContext(ModelMetadata metadata)
        : ModelBinderProviderContext
    {
        public override BindingInfo BindingInfo { get; } = new();

        public override ModelMetadata Metadata { get; } = metadata;

        public override IModelMetadataProvider MetadataProvider { get; } =
            new EmptyModelMetadataProvider();

        public override IModelBinder CreateBinder(ModelMetadata metadata) =>
            throw new NotSupportedException();
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
