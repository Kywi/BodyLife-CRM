using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Modules.Visits;

public static class MarkVisitPreparationPolicy
{
    public static MarkVisitPreparation Prepare(
        Guid clientId,
        VisitKind visitKind,
        Guid? membershipId,
        IEnumerable<MembershipVisitAcknowledgement>? acknowledgements,
        MembershipVisitEligibility? membershipEligibility = null)
    {
        if (clientId == Guid.Empty)
        {
            throw new ArgumentException("Client id is required.", nameof(clientId));
        }

        if (!Enum.IsDefined(visitKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(visitKind),
                visitKind,
                "Visit kind is not supported.");
        }

        var acceptedAcknowledgements = ValidateAcknowledgements(acknowledgements);

        return visitKind == VisitKind.Membership
            ? PrepareMembershipVisit(
                clientId,
                membershipId,
                acceptedAcknowledgements,
                membershipEligibility)
            : PrepareNonMembershipVisit(
                clientId,
                visitKind,
                membershipId,
                acceptedAcknowledgements,
                membershipEligibility);
    }

    private static MarkVisitPreparation PrepareMembershipVisit(
        Guid clientId,
        Guid? membershipId,
        MembershipVisitAcknowledgement[] acknowledgements,
        MembershipVisitEligibility? membershipEligibility)
    {
        if (membershipId is null || membershipId == Guid.Empty)
        {
            throw new ArgumentException(
                "Membership id is required for a membership Visit.",
                nameof(membershipId));
        }

        ArgumentNullException.ThrowIfNull(membershipEligibility);

        if (membershipEligibility.MembershipId != membershipId.Value)
        {
            throw new ArgumentException(
                "Membership eligibility must belong to the selected membership.",
                nameof(membershipEligibility));
        }

        if (!membershipEligibility.IsEligible)
        {
            throw new ArgumentException(
                $"The selected membership is not eligible: {membershipEligibility.ErrorCode}.",
                nameof(membershipEligibility));
        }

        var requiredAcknowledgements = membershipEligibility.RequiredAcknowledgements
            .OrderBy(acknowledgement => acknowledgement)
            .ToArray();

        if (!acknowledgements.SequenceEqual(requiredAcknowledgements))
        {
            throw new ArgumentException(
                "Acknowledgements must exactly match the current Memberships requirements.",
                nameof(acknowledgements));
        }

        return new MarkVisitPreparation(
            clientId,
            VisitKind.Membership,
            membershipId,
            requiredAcknowledgements,
            acknowledgements);
    }

    private static MarkVisitPreparation PrepareNonMembershipVisit(
        Guid clientId,
        VisitKind visitKind,
        Guid? membershipId,
        MembershipVisitAcknowledgement[] acknowledgements,
        MembershipVisitEligibility? membershipEligibility)
    {
        if (membershipId is not null)
        {
            throw new ArgumentException(
                "One-off and trial Visits cannot select a membership.",
                nameof(membershipId));
        }

        if (membershipEligibility is not null)
        {
            throw new ArgumentException(
                "One-off and trial Visits cannot carry membership eligibility.",
                nameof(membershipEligibility));
        }

        if (acknowledgements.Length > 0)
        {
            throw new ArgumentException(
                "One-off and trial Visits cannot carry membership acknowledgements.",
                nameof(acknowledgements));
        }

        return new MarkVisitPreparation(
            clientId,
            visitKind,
            membershipId: null,
            requiredAcknowledgements: [],
            acknowledgements);
    }

    private static MembershipVisitAcknowledgement[] ValidateAcknowledgements(
        IEnumerable<MembershipVisitAcknowledgement>? acknowledgements)
    {
        ArgumentNullException.ThrowIfNull(acknowledgements);

        var acceptedAcknowledgements = acknowledgements.ToArray();

        if (acceptedAcknowledgements.Any(acknowledgement => !Enum.IsDefined(acknowledgement)))
        {
            throw new ArgumentException(
                "Membership acknowledgement is not supported.",
                nameof(acknowledgements));
        }

        if (acceptedAcknowledgements.Distinct().Count() != acceptedAcknowledgements.Length)
        {
            throw new ArgumentException(
                "Each membership acknowledgement can be supplied only once.",
                nameof(acknowledgements));
        }

        return acceptedAcknowledgements
            .OrderBy(acknowledgement => acknowledgement)
            .ToArray();
    }
}
