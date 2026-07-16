namespace BodyLife.Crm.Infrastructure.Persistence.Payments;

public static class PaymentAuditActions
{
    public const string Created = "payment.created";
    public const string Corrected = "payment.corrected";
    public const string Canceled = "payment.canceled";
    public const string EntityType = "payment";
    public const string MembershipEntityType = "membership";
}
