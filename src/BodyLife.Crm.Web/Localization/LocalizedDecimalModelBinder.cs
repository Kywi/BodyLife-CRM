using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Localization;

namespace BodyLife.Crm.Web.Localization;

public sealed class LocalizedDecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var value = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (value == ValueProviderResult.None)
        {
            return Task.CompletedTask;
        }

        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, value);
        var raw = value.FirstValue;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Task.CompletedTask;
        }

        // Browsers can submit a canonical dot under Ukrainian, so uk-UA accepts
        // its native comma and that dot fallback. en-US accepts only its native
        // dot. Mixed separators and grouping remain invalid to avoid silently
        // changing a money amount.
        var trimmed = raw.Trim();
        var hasComma = trimmed.Contains(',', StringComparison.Ordinal);
        var hasDot = trimmed.Contains('.', StringComparison.Ordinal);
        var commaIsAccepted = CultureInfo.CurrentCulture.Name == WebCultures.Ukrainian;
        var separatorsAreValid = !(hasComma && hasDot) && (!hasComma || commaIsAccepted);
        var normalized = commaIsAccepted ? trimmed.Replace(',', '.') : trimmed;
        if (separatorsAreValid
            && decimal.TryParse(
                normalized,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var parsed))
        {
            bindingContext.Result = ModelBindingResult.Success(parsed);
        }
        else
        {
            var localizer = bindingContext.HttpContext.RequestServices
                .GetRequiredService<IStringLocalizer<Validation>>();
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                localizer["Decimal.Invalid"]);
        }

        return Task.CompletedTask;
    }
}

public sealed class LocalizedDecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.ModelType is var type && (type == typeof(decimal) || type == typeof(decimal?))
            ? new LocalizedDecimalModelBinder()
            : null;
}
