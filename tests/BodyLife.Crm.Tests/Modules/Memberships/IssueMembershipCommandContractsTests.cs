using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.MembershipTypes;
using BodyLife.Crm.Modules.Payments;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class IssueMembershipCommandContractsTests
{
    private static readonly Guid ClientId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid MembershipTypeId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid EntryBatchId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");
    private static readonly DateOnly StartDate = new(2026, 7, 1);
    private static readonly DateTimeOffset CatalogTimestamp = new(
        2026,
        7,
        13,
        20,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public void CommandCarriesEnvelopeSelectorsDecisionBatchAndOptionalPayment()
    {
        var envelope = CreateEnvelope(
            EntryOrigin.ManualBackfill,
            occurredAt: new DateTimeOffset(
                2026,
                7,
                1,
                9,
                30,
                0,
                TimeSpan.Zero),
            reason: "Launch backfill from paper register");

        var command = new IssueMembershipCommand(
            envelope,
            ClientId,
            MembershipTypeId,
            StartDate,
            MembershipNegativeHandlingDecision.LeaveVisible,
            EntryBatchId,
            new MembershipIssuePayment(
                new Money(1000m, "uah"),
                PaymentContext.MembershipSale));

        Assert.IsAssignableFrom<IBodyLifeCommand>(command);
        Assert.Same(envelope, command.Envelope);
        Assert.Equal(ClientId, command.ClientId);
        Assert.Equal(MembershipTypeId, command.MembershipTypeId);
        Assert.Equal(StartDate, command.StartDate);
        Assert.Equal(
            MembershipNegativeHandlingDecision.LeaveVisible,
            command.NegativeHandlingDecision);
        Assert.Equal(EntryBatchId, command.EntryBatchId);
        Assert.Equal(
            new MembershipIssuePayment(
                new Money(1000m, "UAH"),
                PaymentContext.MembershipSale),
            command.Payment);
        Assert.Equal("issue-membership-key", command.Envelope.IdempotencyKey);
        Assert.Equal(EntryOrigin.ManualBackfill, command.Envelope.EntryOrigin);
        Assert.Equal("Reception note", command.Envelope.Comment);
    }

    [Fact]
    public void OrdinaryCommandMayOmitNegativeDecisionAndBatchReference()
    {
        var command = new IssueMembershipCommand(
            CreateEnvelope(),
            ClientId,
            MembershipTypeId,
            StartDate);

        Assert.Null(command.NegativeHandlingDecision);
        Assert.Null(command.EntryBatchId);
        Assert.Null(command.Payment);
    }

    [Fact]
    public void CommandUsesCanonicalCatalogSelectorsInsteadOfClientSuppliedDerivedState()
    {
        var propertyNames = typeof(IssueMembershipCommand)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Snapshot", propertyNames);
        Assert.DoesNotContain("BaseEndDate", propertyNames);
        Assert.DoesNotContain("ExpectedInitialState", propertyNames);
    }

    [Fact]
    public void SuccessfulResultContractTargetsCanonicalClientReread()
    {
        var membershipId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var command = new IssueMembershipCommand(
            CreateEnvelope(),
            ClientId,
            MembershipTypeId,
            StartDate);
        var result = CommandResult.Success(
            new EntityId(IssueMembershipCommand.PrimaryEntityType, membershipId),
            command.CanonicalRereadTargetId);

        Assert.Equal(
            new EntityId("membership", membershipId),
            result.PrimaryEntityId);
        Assert.Equal(new EntityId("client", ClientId), result.RereadTargetId);
        Assert.Equal("membership", IssueMembershipCommand.PrimaryEntityType);
        Assert.Equal("client", IssueMembershipCommand.CanonicalRereadEntityType);
    }

    [Fact]
    public void ResultContractIncludesDocumentedNegativeDecisionError()
    {
        var result = CommandResult.Error(
        [
            new CommandError(
                CommandErrorCode.NegativeDecisionRequired,
                "An explicit negative handling decision is required.",
                "negativeHandlingDecision"),
        ]);

        var error = Assert.Single(result.Errors);
        Assert.Equal(CommandErrorCode.NegativeDecisionRequired, error.Code);
        Assert.Equal("negativeHandlingDecision", error.Field);
        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
    }

    [Fact]
    public void PreparationCopiesImmutableSnapshotAndInclusiveInitialState()
    {
        var preparation = MembershipIssuePreparationPolicy.Prepare(
            ClientId,
            CreateMembershipType(),
            StartDate);

        Assert.Equal(ClientId, preparation.ClientId);
        Assert.Equal(MembershipTypeId, preparation.MembershipTypeId);
        Assert.Equal("Eight visits", preparation.Snapshot.TypeName);
        Assert.Equal(30, preparation.Snapshot.DurationDays);
        Assert.Equal(8, preparation.Snapshot.VisitsLimit);
        Assert.Equal(new Money(1000m, "UAH"), preparation.Snapshot.Price);
        Assert.Equal(StartDate, preparation.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 30), preparation.BaseEndDate);
        Assert.Equal(0, preparation.ExpectedInitialState.CountedVisits);
        Assert.Equal(8, preparation.ExpectedInitialState.RemainingVisits);
        Assert.Equal(0, preparation.ExpectedInitialState.NegativeBalance);
        Assert.Null(preparation.ExpectedInitialState.FirstNegativeVisitDate);
        Assert.Equal(0, preparation.ExpectedInitialState.ExtensionDays);
        Assert.Equal(
            preparation.BaseEndDate,
            preparation.ExpectedInitialState.EffectiveEndDate);
        Assert.Null(preparation.ExistingNegativeState);
        Assert.Null(preparation.NegativeHandlingDecision);
        Assert.Empty(preparation.Warnings);
        Assert.All(
            typeof(MembershipIssuePreparation).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void PreparationSnapshotDoesNotFollowLaterCatalogValues()
    {
        var original = CreateMembershipType();
        var preparation = MembershipIssuePreparationPolicy.Prepare(
            ClientId,
            original,
            StartDate);

        var editedCatalogValue = original with
        {
            Name = "Twelve visits",
            DurationDays = 45,
            VisitsLimit = 12,
            Price = new Money(1500m, "UAH"),
            UpdatedAt = CatalogTimestamp.AddMinutes(1),
        };

        Assert.Equal("Twelve visits", editedCatalogValue.Name);
        Assert.Equal("Eight visits", preparation.Snapshot.TypeName);
        Assert.Equal(30, preparation.Snapshot.DurationDays);
        Assert.Equal(8, preparation.Snapshot.VisitsLimit);
        Assert.Equal(new Money(1000m, "UAH"), preparation.Snapshot.Price);
    }

    [Fact]
    public void ExistingNegativeRequiresExplicitDecision()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipIssuePreparationPolicy.Prepare(
                ClientId,
                CreateMembershipType(),
                StartDate,
                CreateNegativeContext()));

        Assert.Equal("negativeHandlingDecision", exception.ParamName);
        Assert.Contains(
            "explicit negative handling decision is required",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LeaveVisiblePreservesExistingNegativeStateAndWarning()
    {
        var negativeState = CreateNegativeContext();

        var preparation = MembershipIssuePreparationPolicy.Prepare(
            ClientId,
            CreateMembershipType(),
            StartDate,
            negativeState,
            MembershipNegativeHandlingDecision.LeaveVisible);

        Assert.Same(negativeState, preparation.ExistingNegativeState);
        Assert.Equal(2, preparation.ExistingNegativeState!.NegativeBalance);
        Assert.Equal(
            new DateOnly(2026, 6, 28),
            preparation.ExistingNegativeState.FirstNegativeVisitDate);
        Assert.Equal(
            MembershipNegativeHandlingDecision.LeaveVisible,
            preparation.NegativeHandlingDecision);
        Assert.Equal(0, preparation.ExpectedInitialState.NegativeBalance);
        Assert.Equal(8, preparation.ExpectedInitialState.RemainingVisits);
        var warning = Assert.Single(preparation.Warnings);
        Assert.Equal(MembershipWarningCodes.NegativeBalance, warning.Code);

        var warningList = Assert.IsAssignableFrom<IList<MembershipWarning>>(
            preparation.Warnings);
        Assert.True(warningList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => warningList.Add(warning));
    }

    [Theory]
    [InlineData(MembershipNegativeHandlingDecision.CoverWithNewMembership)]
    [InlineData(MembershipNegativeHandlingDecision.RecordExplicitClosure)]
    public void DeferredNegativeDecisionsAreRejected(
        MembershipNegativeHandlingDecision decision)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            MembershipIssuePreparationPolicy.Prepare(
                ClientId,
                CreateMembershipType(),
                StartDate,
                CreateNegativeContext(),
                decision));

        Assert.Equal("negativeHandlingDecision", exception.ParamName);
        Assert.Contains(
            "not available",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreparationReusesPreviewValidationAndCalendarGuards()
    {
        Assert.Equal(
            "clientId",
            Assert.Throws<ArgumentException>(() =>
                MembershipIssuePreparationPolicy.Prepare(
                    Guid.Empty,
                    CreateMembershipType(),
                    StartDate)).ParamName);
        Assert.Equal(
            "negativeHandlingDecision",
            Assert.Throws<ArgumentException>(() =>
                MembershipIssuePreparationPolicy.Prepare(
                    ClientId,
                    CreateMembershipType(),
                    StartDate,
                    existingNegativeState: null,
                    MembershipNegativeHandlingDecision.LeaveVisible)).ParamName);
        Assert.Equal(
            "durationDays",
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                MembershipIssuePreparationPolicy.Prepare(
                    ClientId,
                    CreateMembershipType(durationDays: 2),
                    DateOnly.MaxValue)).ParamName);
    }

    private static MembershipIssueNegativeContext CreateNegativeContext()
    {
        return new MembershipIssueNegativeContext(
            negativeBalance: 2,
            firstNegativeVisitDate: new DateOnly(2026, 6, 28));
    }

    private static MembershipTypeCatalogItem CreateMembershipType(int durationDays = 30)
    {
        return new MembershipTypeCatalogItem(
            MembershipTypeId,
            "Eight visits",
            durationDays,
            VisitsLimit: 8,
            new Money(1000m, "UAH"),
            IsActive: true,
            Comment: null,
            CreatedAt: CatalogTimestamp.AddDays(-1),
            UpdatedAt: CatalogTimestamp,
            DeactivatedAt: null);
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
            new RequestCorrelationId("issue-membership-contract"),
            entryOrigin,
            occurredAt,
            IdempotencyKey: "issue-membership-key",
            reason,
            Comment: "Reception note");
    }
}
