using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Visits;

public sealed class MarkVisitCommandContractsTests
{
    private static readonly Guid ClientId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid MembershipId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid EntryBatchId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");
    private static readonly Guid FreezeId = Guid.Parse(
        "44444444-4444-4444-4444-444444444444");

    [Fact]
    public void CommandCarriesEnvelopeExplicitContextAcknowledgementsAndBatchReference()
    {
        var envelope = CreateEnvelope(
            EntryOrigin.PaperFallback,
            occurredAt: new DateTimeOffset(2026, 7, 14, 9, 30, 0, TimeSpan.Zero),
            reason: "Reception paper register reconciliation");
        MembershipVisitAcknowledgement[] acknowledgements =
        [
            MembershipVisitAcknowledgement.Expired,
            MembershipVisitAcknowledgement.ZeroRemaining,
        ];

        var command = new MarkVisitCommand(
            envelope,
            ClientId,
            VisitKind.Membership,
            MembershipId,
            acknowledgements,
            EntryBatchId);

        Assert.IsAssignableFrom<IBodyLifeCommand>(command);
        Assert.Same(envelope, command.Envelope);
        Assert.Equal(ClientId, command.ClientId);
        Assert.Equal(VisitKind.Membership, command.VisitKind);
        Assert.Equal(MembershipId, command.MembershipId);
        Assert.Same(acknowledgements, command.Acknowledgements);
        Assert.Equal(EntryBatchId, command.EntryBatchId);
        Assert.Equal("mark-visit-key", command.Envelope.IdempotencyKey);
        Assert.Equal(EntryOrigin.PaperFallback, command.Envelope.EntryOrigin);
        Assert.Equal("Reception note", command.Envelope.Comment);
    }

    [Theory]
    [InlineData(VisitKind.OneOff)]
    [InlineData(VisitKind.Trial)]
    public void NonMembershipCommandMayOmitMembershipAndBatchReference(VisitKind visitKind)
    {
        var command = new MarkVisitCommand(
            CreateEnvelope(),
            ClientId,
            visitKind,
            MembershipId: null,
            Acknowledgements: []);

        Assert.Null(command.MembershipId);
        Assert.Empty(command.Acknowledgements);
        Assert.Null(command.EntryBatchId);
    }

    [Fact]
    public void CommandDoesNotAcceptClientSuppliedMembershipFormulaState()
    {
        var propertyNames = typeof(MarkVisitCommand)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("RemainingVisits", propertyNames);
        Assert.DoesNotContain("NegativeBalance", propertyNames);
        Assert.DoesNotContain("EffectiveEndDate", propertyNames);
        Assert.DoesNotContain("RequiredAcknowledgements", propertyNames);
        Assert.DoesNotContain("MembershipState", propertyNames);
        Assert.DoesNotContain("MembershipEligibility", propertyNames);
    }

    [Fact]
    public void SuccessfulResultTargetsCanonicalClientAndCanRelateSelectedMembership()
    {
        var visitId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var command = new MarkVisitCommand(
            CreateEnvelope(),
            ClientId,
            VisitKind.Membership,
            MembershipId,
            Acknowledgements: []);
        var result = CommandResult.Success(
            new EntityId(MarkVisitCommand.PrimaryEntityType, visitId),
            command.CanonicalRereadTargetId,
            relatedEntityIds: [new EntityId("membership", MembershipId)]);

        Assert.Equal(new EntityId("visit", visitId), result.PrimaryEntityId);
        Assert.Equal(new EntityId("client", ClientId), result.RereadTargetId);
        Assert.Equal(
            new EntityId("membership", MembershipId),
            Assert.Single(result.RelatedEntityIds));
        Assert.Equal("visit", MarkVisitCommand.PrimaryEntityType);
        Assert.Equal("client", MarkVisitCommand.CanonicalRereadEntityType);
    }

    [Theory]
    [InlineData(CommandErrorCode.MembershipNotEligible)]
    [InlineData(CommandErrorCode.VisitDuringFreeze)]
    [InlineData(CommandErrorCode.WarningAcknowledgementRequired)]
    public void ResultContractIncludesDocumentedVisitErrors(CommandErrorCode errorCode)
    {
        var result = CommandResult.Error(
        [
            new CommandError(errorCode, "Visit cannot be marked.", "membershipId"),
        ]);

        var error = Assert.Single(result.Errors);
        Assert.Equal(errorCode, error.Code);
        Assert.Equal("membershipId", error.Field);
        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
    }

    [Fact]
    public void MembershipPreparationAcceptsExactlyCurrentMembershipsRequirements()
    {
        var eligibility = CreateEligibility(
            remainingVisits: 0,
            visitDate: new DateOnly(2026, 8, 1));

        var preparation = MarkVisitPreparationPolicy.Prepare(
            ClientId,
            VisitKind.Membership,
            MembershipId,
            acknowledgements:
            [
                MembershipVisitAcknowledgement.ZeroRemaining,
                MembershipVisitAcknowledgement.Expired,
            ],
            eligibility);

        MembershipVisitAcknowledgement[] expectedAcknowledgements =
        [
            MembershipVisitAcknowledgement.Expired,
            MembershipVisitAcknowledgement.ZeroRemaining,
        ];
        Assert.Equal(ClientId, preparation.ClientId);
        Assert.Equal(VisitKind.Membership, preparation.VisitKind);
        Assert.Equal(MembershipId, preparation.MembershipId);
        Assert.Equal(expectedAcknowledgements, preparation.RequiredAcknowledgements);
        Assert.Equal(expectedAcknowledgements, preparation.AcceptedAcknowledgements);
        Assert.True(preparation.CreatesMembershipConsumption);
        Assert.True(preparation.RequiresMembershipRecalculation);
        Assert.All(
            typeof(MarkVisitPreparation).GetProperties(),
            property => Assert.Null(property.SetMethod));

        var accepted = Assert.IsAssignableFrom<IList<MembershipVisitAcknowledgement>>(
            preparation.AcceptedAcknowledgements);
        Assert.True(accepted.IsReadOnly);
        Assert.Throws<NotSupportedException>(() =>
            accepted.Add(MembershipVisitAcknowledgement.NegativeRemaining));
    }

    [Theory]
    [InlineData(VisitKind.OneOff)]
    [InlineData(VisitKind.Trial)]
    public void NonMembershipPreparationCreatesNoConsumptionOrRecalculation(
        VisitKind visitKind)
    {
        var preparation = MarkVisitPreparationPolicy.Prepare(
            ClientId,
            visitKind,
            membershipId: null,
            acknowledgements: []);

        Assert.Equal(visitKind, preparation.VisitKind);
        Assert.Null(preparation.MembershipId);
        Assert.Empty(preparation.RequiredAcknowledgements);
        Assert.Empty(preparation.AcceptedAcknowledgements);
        Assert.False(preparation.CreatesMembershipConsumption);
        Assert.False(preparation.RequiresMembershipRecalculation);
    }

    [Fact]
    public void PreparationEnforcesMembershipIdByVisitKind()
    {
        var missingMembership = Assert.Throws<ArgumentException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                VisitKind.Membership,
                membershipId: null,
                acknowledgements: [],
                membershipEligibility: null));
        var emptyMembership = Assert.Throws<ArgumentException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                VisitKind.Membership,
                Guid.Empty,
                acknowledgements: [],
                membershipEligibility: null));
        var oneOffMembership = Assert.Throws<ArgumentException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                VisitKind.OneOff,
                MembershipId,
                acknowledgements: []));

        Assert.Equal("membershipId", missingMembership.ParamName);
        Assert.Equal("membershipId", emptyMembership.ParamName);
        Assert.Equal("membershipId", oneOffMembership.ParamName);
    }

    [Fact]
    public void MembershipPreparationRequiresEligibilityForTheSelectedMembership()
    {
        var missingEligibility = Assert.Throws<ArgumentNullException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                VisitKind.Membership,
                MembershipId,
                acknowledgements: []));
        var foreignEligibility = CreateEligibility(
            membershipId: Guid.Parse("66666666-6666-6666-6666-666666666666"));
        var mismatchedEligibility = Assert.Throws<ArgumentException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                VisitKind.Membership,
                MembershipId,
                acknowledgements: [],
                foreignEligibility));

        Assert.Equal("membershipEligibility", missingEligibility.ParamName);
        Assert.Equal("membershipEligibility", mismatchedEligibility.ParamName);
    }

    [Fact]
    public void MembershipPreparationPreservesMembershipsFreezeRejection()
    {
        var visitDate = new DateOnly(2026, 7, 14);
        var freeze = new MembershipVisitFreezeSource(
            MembershipId,
            FreezeId,
            new DateRange(visitDate, visitDate),
            isActive: true);
        var eligibility = CreateEligibility(visitDate: visitDate, freezeSources: [freeze]);

        var exception = Assert.Throws<ArgumentException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                VisitKind.Membership,
                MembershipId,
                acknowledgements: [],
                eligibility));

        Assert.Equal("membershipEligibility", exception.ParamName);
        Assert.Contains(
            MembershipVisitEligibilityErrorCodes.VisitDuringFreeze,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MembershipPreparationRejectsMissingExtraDuplicateOrUnknownAcknowledgements()
    {
        var eligibility = CreateEligibility(remainingVisits: 0);

        var missing = Assert.Throws<ArgumentException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                VisitKind.Membership,
                MembershipId,
                acknowledgements: [],
                eligibility));
        var extra = Assert.Throws<ArgumentException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                VisitKind.Membership,
                MembershipId,
                acknowledgements:
                [
                    MembershipVisitAcknowledgement.ZeroRemaining,
                    MembershipVisitAcknowledgement.Expired,
                ],
                eligibility));
        var duplicate = Assert.Throws<ArgumentException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                VisitKind.Membership,
                MembershipId,
                acknowledgements:
                [
                    MembershipVisitAcknowledgement.ZeroRemaining,
                    MembershipVisitAcknowledgement.ZeroRemaining,
                ],
                eligibility));
        var unknown = Assert.Throws<ArgumentException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                VisitKind.Membership,
                MembershipId,
                acknowledgements: [(MembershipVisitAcknowledgement)999],
                eligibility));

        Assert.Equal("acknowledgements", missing.ParamName);
        Assert.Equal("acknowledgements", extra.ParamName);
        Assert.Equal("acknowledgements", duplicate.ParamName);
        Assert.Equal("acknowledgements", unknown.ParamName);
    }

    [Fact]
    public void NonMembershipPreparationRejectsMembershipEligibilityAndAcknowledgements()
    {
        var eligibility = CreateEligibility();

        var withEligibility = Assert.Throws<ArgumentException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                VisitKind.Trial,
                membershipId: null,
                acknowledgements: [],
                eligibility));
        var withAcknowledgement = Assert.Throws<ArgumentException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                VisitKind.OneOff,
                membershipId: null,
                acknowledgements: [MembershipVisitAcknowledgement.Expired]));

        Assert.Equal("membershipEligibility", withEligibility.ParamName);
        Assert.Equal("acknowledgements", withAcknowledgement.ParamName);
    }

    [Fact]
    public void PreparationRejectsMissingOrUnsupportedCommonInputs()
    {
        var missingClient = Assert.Throws<ArgumentException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                Guid.Empty,
                VisitKind.OneOff,
                membershipId: null,
                acknowledgements: []));
        var unsupportedKind = Assert.Throws<ArgumentOutOfRangeException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                (VisitKind)999,
                membershipId: null,
                acknowledgements: []));
        var missingAcknowledgements = Assert.Throws<ArgumentNullException>(() =>
            MarkVisitPreparationPolicy.Prepare(
                ClientId,
                VisitKind.OneOff,
                membershipId: null,
                acknowledgements: null));

        Assert.Equal("clientId", missingClient.ParamName);
        Assert.Equal("visitKind", unsupportedKind.ParamName);
        Assert.Equal("acknowledgements", missingAcknowledgements.ParamName);
    }

    private static MembershipVisitEligibility CreateEligibility(
        Guid? membershipId = null,
        int remainingVisits = 5,
        DateOnly? visitDate = null,
        IReadOnlyList<MembershipVisitFreezeSource>? freezeSources = null)
    {
        var selectedMembershipId = membershipId ?? MembershipId;
        var snapshot = new IssuedMembershipSnapshot(
            "Eight visits",
            durationDays: 30,
            visitsLimit: 8,
            new Money(1000m, "UAH"));
        var issueTerms = MembershipIssueTerms.FromIssuedSnapshot(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            snapshot,
            new DateOnly(2026, 7, 1),
            new DateOnly(2026, 7, 30));
        var state = MembershipCalculatedState.FromStoredCache(
            issueTerms,
            countedVisits: snapshot.VisitsLimit - remainingVisits,
            remainingVisits,
            negativeBalance: Math.Max(0, -remainingVisits),
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            extensionDays: 0,
            effectiveEndDate: issueTerms.BaseEndDate,
            lastCountedVisitAt: null);

        return MembershipVisitEligibilityPolicy.Evaluate(
            selectedMembershipId,
            issueTerms,
            state,
            IssuedMembershipLifecycleStatus.Active,
            visitDate ?? new DateOnly(2026, 7, 14),
            freezeSources ?? []);
    }

    private static CommandEnvelope CreateEnvelope(
        EntryOrigin entryOrigin = EntryOrigin.Normal,
        DateTimeOffset? occurredAt = null,
        string? reason = null)
    {
        var actor = new ActorContext(
            AccountId.New(),
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            SessionId.New(),
            "reception tablet");

        return new CommandEnvelope(
            actor,
            new RequestCorrelationId("mark-visit-contract"),
            entryOrigin,
            occurredAt,
            IdempotencyKey: "mark-visit-key",
            reason,
            Comment: "Reception note");
    }
}
