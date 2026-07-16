using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Payments;

public sealed record PaymentReplacement(
    Guid? MembershipId,
    Money Amount,
    PaymentContext PaymentContext,
    DateTimeOffset OccurredAt,
    string? Comment);
