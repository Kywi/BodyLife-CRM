using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Infrastructure.Persistence.Memberships;

internal sealed record MembershipCanonicalStateCalculation(
    MembershipIssueTerms IssueTerms,
    MembershipCalculatedState State,
    MembershipExtensionCalculation? ExtensionCalculation);
