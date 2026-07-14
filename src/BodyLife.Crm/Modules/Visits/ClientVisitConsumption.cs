namespace BodyLife.Crm.Modules.Visits;

public sealed record ClientVisitConsumption(
    Guid ConsumptionId,
    Guid MembershipId,
    string MembershipTypeNameSnapshot,
    ClientVisitConsumptionStatus Status);
