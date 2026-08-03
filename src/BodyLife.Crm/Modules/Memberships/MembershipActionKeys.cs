namespace BodyLife.Crm.Modules.Memberships;

public static class MembershipActionKeys
{
    public const string CreateOpeningState = "memberships.create_opening_state";
    public const string Issue = "memberships.issue";
    public const string ReplaceIssuedSale = "memberships.replace_issued_sale";
    public const string CancelIssuedSale = "memberships.cancel_issued_sale";
    public const string CloseNegativeVisitsOneOff = "memberships.close_negative_visits_one_off";
    public const string CorrectNegativeVisitCoverage = "memberships.correct_negative_visit_coverage";
    public const string AdminOrOwnerPolicy = "BodyLife.AdminOrOwner";
}
