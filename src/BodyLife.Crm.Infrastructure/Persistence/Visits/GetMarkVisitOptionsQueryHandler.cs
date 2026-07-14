using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Visits;

namespace BodyLife.Crm.Infrastructure.Persistence.Visits;

public sealed class GetMarkVisitOptionsQueryHandler(
    IBodyLifeQueryHandler<
        GetClientMembershipStatesQuery,
        GetClientMembershipStatesResult> getClientMembershipStates,
    IMembershipVisitFreezeSourceSnapshotProvider freezeSourceProvider,
    IMembershipVisitEligibilityEvaluator eligibilityEvaluator)
    : IBodyLifeQueryHandler<GetMarkVisitOptionsQuery, GetMarkVisitOptionsResult>
{
    public async Task<GetMarkVisitOptionsResult> ExecuteAsync(
        GetMarkVisitOptionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.ClientId == Guid.Empty)
        {
            return GetMarkVisitOptionsResult.Invalid(
                "Client id is required.",
                "clientId");
        }

        if (query.OccurredAt == default)
        {
            return GetMarkVisitOptionsResult.Invalid(
                "Occurred_at is required for Visit options.",
                "occurredAt");
        }

        var occurredAt = query.OccurredAt.ToUniversalTime();
        var visitDate = DateOnly.FromDateTime(occurredAt.DateTime);
        var membershipResult = await getClientMembershipStates.ExecuteAsync(
            new GetClientMembershipStatesQuery(
                query.Actor,
                query.ClientId,
                visitDate),
            cancellationToken);

        if (membershipResult.Status != GetClientMembershipStatesStatus.Success)
        {
            return MapFailure(membershipResult);
        }

        if (membershipResult.StateCollection is not { } stateCollection
            || stateCollection.ClientId != query.ClientId
            || stateCollection.AsOfDate != visitDate)
        {
            return GetMarkVisitOptionsResult.RecalculationFailed();
        }

        var membershipOptions = new List<MarkVisitMembershipOption>();

        foreach (var timelineItem in stateCollection.Timeline)
        {
            if (timelineItem.LifecycleStatus != IssuedMembershipLifecycleStatus.Active)
            {
                continue;
            }

            var state = timelineItem.State;
            var freezeSources = await freezeSourceProvider.GetSnapshotForVisitAsync(
                state.MembershipId,
                visitDate,
                cancellationToken);
            var eligibility = eligibilityEvaluator.Evaluate(
                state,
                timelineItem.LifecycleStatus,
                visitDate,
                freezeSources);

            membershipOptions.Add(new MarkVisitMembershipOption(
                state.MembershipId,
                state.Snapshot.TypeName,
                state.StartDate,
                state.EffectiveEndDate,
                state.RemainingVisits,
                eligibility.Status,
                eligibility.RequiredAcknowledgements,
                state.Warnings));
        }

        var suggestedMembershipId = stateCollection.ActiveCandidateSelection
            .SingleCandidate?
            .State
            .MembershipId;
        if (suggestedMembershipId is not null
            && membershipOptions.Single(option =>
                option.MembershipId == suggestedMembershipId.Value) is { CanSelect: false })
        {
            suggestedMembershipId = null;
        }

        return GetMarkVisitOptionsResult.Succeeded(
            new MarkVisitOptions(
                query.ClientId,
                occurredAt,
                visitDate,
                membershipOptions.AsReadOnly(),
                suggestedMembershipId));
    }

    private static GetMarkVisitOptionsResult MapFailure(
        GetClientMembershipStatesResult membershipResult)
    {
        return membershipResult.Status switch
        {
            GetClientMembershipStatesStatus.PermissionDenied
                => GetMarkVisitOptionsResult.Denied(),
            GetClientMembershipStatesStatus.NotFound
                => GetMarkVisitOptionsResult.MissingClient(),
            GetClientMembershipStatesStatus.ValidationFailed
                => GetMarkVisitOptionsResult.Invalid(
                    membershipResult.ErrorMessage ?? "Visit options request is invalid.",
                    membershipResult.ErrorField == "asOfDate"
                        ? "occurredAt"
                        : membershipResult.ErrorField),
            GetClientMembershipStatesStatus.RecalculationFailed
                => GetMarkVisitOptionsResult.RecalculationFailed(),
            _ => GetMarkVisitOptionsResult.RecalculationFailed(),
        };
    }
}
