using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.Web.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BodyLife.Crm.Web.Pages.Owner;

public sealed class NonWorkingDaysModel(
    IBodyLifeRequestContextResolver requestContextResolver,
    IBodyLifeQueryHandler<
        PreviewNonWorkingDayImpactQuery,
        PreviewNonWorkingDayImpactResult> previewImpact,
    TimeProvider timeProvider)
    : PageModel
{
    public NonWorkingDayPreviewWorkspaceViewModel Workspace { get; private set; }
        = NonWorkingDayPreviewWorkspaceViewModel.Empty;

    public void OnGet()
    {
        Workspace = NonWorkingDayPreviewWorkspaceViewModel.New(CurrentDate());
    }

    public async Task<IActionResult> OnPostPreviewAsync(
        NonWorkingDayPreviewFormInput form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        var result = await previewImpact.ExecuteAsync(
            new PreviewNonWorkingDayImpactQuery(
                requestContextResolver.Require().Actor,
                form.ProposedStartDate ?? default,
                form.ProposedEndDate ?? default,
                form.ReasonCode,
                form.ReasonComment),
            cancellationToken);

        if (result.Status == PreviewNonWorkingDayImpactStatus.PermissionDenied)
        {
            return Forbid();
        }

        Workspace = NonWorkingDayPreviewWorkspaceViewModel.FromResult(form, result);
        return IsHtmxRequest()
            ? Partial("_NonWorkingDayPreviewWorkspace", Workspace)
            : Page();
    }

    private DateOnly CurrentDate()
    {
        return DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
    }

    private bool IsHtmxRequest()
    {
        return string.Equals(
            Request.Headers["HX-Request"].ToString(),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }
}
