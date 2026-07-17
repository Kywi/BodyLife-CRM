using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public interface INonWorkingDayPreviewTokenService
{
    NonWorkingDayPreviewConfirmation Issue(
        NonWorkingDayPreviewInput input,
        MembershipNonWorkingDayAffectedScope scope);

    NonWorkingDayPreviewTokenValidation Validate(
        string? confirmationToken,
        NonWorkingDayPreviewInput input,
        MembershipNonWorkingDayAffectedScope currentScope);
}
