namespace BodyLife.Crm.Modules.NonWorkingDays;

public static class NonWorkingDayCorrectionPolicy
{
    public static NonWorkingDayCorrectionScopeBehavior GetScopeBehavior(
        NonWorkingDayCorrectionMode mode)
    {
        return mode switch
        {
            NonWorkingDayCorrectionMode.ReplaceRange =>
                NonWorkingDayCorrectionScopeBehavior.RecomputeReplacement,
            NonWorkingDayCorrectionMode.ReplaceReason =>
                NonWorkingDayCorrectionScopeBehavior.PreserveConfirmedApplications,
            NonWorkingDayCorrectionMode.Cancel =>
                NonWorkingDayCorrectionScopeBehavior.NoReplacement,
            _ => throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                "NonWorkingDay correction mode is not supported."),
        };
    }
}
