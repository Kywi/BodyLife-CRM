namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

public static class MembershipAuditActions
{
    public const string MembershipEntityType = "membership";
    public const string Issued = "membership.issued";
    public const string Replaced = "membership.replaced";
    public const string SaleCanceled = "membership.sale_canceled";
    public const string OpeningStateEntityType = "membership_opening_state";
    public const string OpeningStateCreated = "membership_opening_state.created";
}
