using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public sealed record MembershipIssuePayment(
    Money Amount,
    PaymentContext PaymentContext);
