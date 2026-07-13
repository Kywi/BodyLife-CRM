using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Memberships;

public sealed class MembershipOpeningStateCommandContractsTests
{
    private static readonly Guid MembershipId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly OpeningAsOfDate = new(2026, 7, 13);
    private static readonly DateTimeOffset OccurredAt = new(
        2026,
        7,
        13,
        9,
        30,
        0,
        TimeSpan.Zero);

    [Fact]
    public void CommandCarriesCommonEnvelopeAndBackfillMetadata()
    {
        var envelope = CreateEnvelope();
        var entryBatchId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var command = new CreateMembershipOpeningStateCommand(
            envelope,
            MembershipId,
            OpeningAsOfDate,
            DeclaredRemainingVisits: -2,
            KnownEffectiveEndDate: new DateOnly(2026, 8, 3),
            KnownExtensionDays: 4,
            SourceReference: "Paper register 2026, page 12",
            entryBatchId);

        Assert.IsAssignableFrom<IBodyLifeCommand>(command);
        Assert.Same(envelope, command.Envelope);
        Assert.Equal(EntryOrigin.ManualBackfill, command.Envelope.EntryOrigin);
        Assert.Equal(OccurredAt, command.Envelope.OccurredAt);
        Assert.Equal("opening-state-contract-key", command.Envelope.IdempotencyKey);
        Assert.Equal(
            "Active membership history before launch is incomplete",
            command.Envelope.Reason);
        Assert.Equal("Paper register 2026, page 12", command.SourceReference);
        Assert.Equal(entryBatchId, command.EntryBatchId);
    }

    [Fact]
    public void CommandCarriesDeclaredStateWithoutDuplicatingDerivedNegativeBalance()
    {
        var command = CreateCommand(
            declaredRemainingVisits: -2,
            knownEffectiveEndDate: new DateOnly(2026, 8, 3),
            knownExtensionDays: 4);

        Assert.Equal(MembershipId, command.MembershipId);
        Assert.Equal(OpeningAsOfDate, command.OpeningAsOfDate);
        Assert.Equal(-2, command.DeclaredRemainingVisits);
        Assert.Equal(new DateOnly(2026, 8, 3), command.KnownEffectiveEndDate);
        Assert.Equal(4, command.KnownExtensionDays);
        Assert.DoesNotContain(
            typeof(CreateMembershipOpeningStateCommand).GetProperties(),
            property => property.Name == "DeclaredNegativeBalance");
    }

    [Fact]
    public void KnownStateAndFutureBatchReferenceRemainOptional()
    {
        var command = CreateCommand(
            declaredRemainingVisits: 1,
            knownEffectiveEndDate: null,
            knownExtensionDays: null,
            entryBatchId: null);

        Assert.Null(command.KnownEffectiveEndDate);
        Assert.Null(command.KnownExtensionDays);
        Assert.Null(command.EntryBatchId);
    }

    [Fact]
    public void CommandDeclaresStableAdminOrOwnerPermissionIntent()
    {
        Assert.Equal(
            "memberships.create_opening_state",
            MembershipActionKeys.CreateOpeningState);
        Assert.Equal("BodyLife.AdminOrOwner", MembershipActionKeys.AdminOrOwnerPolicy);
    }

    [Fact]
    public void CommandTargetsCanonicalMembershipReread()
    {
        var command = CreateCommand();

        Assert.Equal(
            new EntityId("membership", MembershipId),
            command.CanonicalRereadTargetId);
        Assert.Equal(
            "membership",
            CreateMembershipOpeningStateCommand.CanonicalRereadEntityType);
    }

    private static CreateMembershipOpeningStateCommand CreateCommand(
        int declaredRemainingVisits = 2,
        DateOnly? knownEffectiveEndDate = null,
        int? knownExtensionDays = null,
        Guid? entryBatchId = null)
    {
        return new CreateMembershipOpeningStateCommand(
            CreateEnvelope(),
            MembershipId,
            OpeningAsOfDate,
            declaredRemainingVisits,
            knownEffectiveEndDate,
            knownExtensionDays,
            "Paper register 2026, page 12",
            entryBatchId);
    }

    private static CommandEnvelope CreateEnvelope()
    {
        var actor = new ActorContext(
            AccountId.New(),
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            SessionId.New(),
            "reception tablet");

        return new CommandEnvelope(
            actor,
            new RequestCorrelationId("opening-state-contract"),
            EntryOrigin.ManualBackfill,
            OccurredAt,
            IdempotencyKey: "opening-state-contract-key",
            Reason: "Active membership history before launch is incomplete",
            Comment: "Source entered during launch backfill");
    }
}
