using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed record AddNonWorkingDayCommand(
    CommandEnvelope Envelope,
    DateRange Period,
    string? ReasonCode,
    string? ReasonComment,
    string? ConfirmationToken)
    : IBodyLifeCommand
{
    public const string PrimaryEntityType = "non_working_period";
    public const string MembershipEntityType = "membership";
    public const string CanonicalRereadEntityType = "non_working_period";
}
