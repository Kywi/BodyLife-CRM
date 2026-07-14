using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Visits;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.Visits;

public sealed class CancelVisitCommandContractsTests
{
    private static readonly Guid VisitId = Guid.Parse(
        "11111111-1111-1111-1111-111111111111");
    private static readonly Guid ClientId = Guid.Parse(
        "22222222-2222-2222-2222-222222222222");
    private static readonly Guid ConsumptionId = Guid.Parse(
        "33333333-3333-3333-3333-333333333333");
    private static readonly Guid MembershipId = Guid.Parse(
        "44444444-4444-4444-4444-444444444444");
    private static readonly Guid EntryBatchId = Guid.Parse(
        "55555555-5555-5555-5555-555555555555");
    private static readonly Guid RecordedByAccountId = Guid.Parse(
        "66666666-6666-6666-6666-666666666666");
    private static readonly Guid RecordedSessionId = Guid.Parse(
        "77777777-7777-7777-7777-777777777777");

    [Fact]
    public void CommandCarriesCommonEnvelopeVisitAndOptionalBatchReference()
    {
        var envelope = CreateEnvelope(
            entryOrigin: EntryOrigin.PaperFallback,
            reason: "Paper sheet contains a mistaken Visit");

        var command = new CancelVisitCommand(envelope, VisitId, EntryBatchId);

        Assert.IsAssignableFrom<IBodyLifeCommand>(command);
        Assert.Same(envelope, command.Envelope);
        Assert.Equal(VisitId, command.VisitId);
        Assert.Equal(EntryBatchId, command.EntryBatchId);
        Assert.Equal("cancel-visit-key", command.Envelope.IdempotencyKey);
        Assert.Equal("visit_cancellation", CancelVisitCommand.PrimaryEntityType);
        Assert.Equal("visit", CancelVisitCommand.SourceVisitEntityType);
        Assert.Equal("client", CancelVisitCommand.CanonicalRereadEntityType);
    }

    [Fact]
    public void SuccessfulResultTargetsCancellationAndCanonicalClient()
    {
        var cancellationId = Guid.Parse(
            "88888888-8888-8888-8888-888888888888");
        var auditEntryId = AuditEntryId.New();
        var preparation = CancelVisitPreparationPolicy.Prepare(
            CreateCommand(),
            CreateSource(),
            changedAfterClose: true);

        var result = CommandResult.Success(
            new EntityId(CancelVisitCommand.PrimaryEntityType, cancellationId),
            preparation.CanonicalRereadTargetId,
            relatedEntityIds:
            [
                preparation.SourceVisitEntityId,
                new EntityId("membership", MembershipId),
            ],
            auditEntryId: auditEntryId,
            changedAfterClose: preparation.ChangedAfterClose);

        Assert.Equal(
            new EntityId("visit_cancellation", cancellationId),
            result.PrimaryEntityId);
        Assert.Equal(new EntityId("client", ClientId), result.RereadTargetId);
        Assert.Contains(new EntityId("visit", VisitId), result.RelatedEntityIds);
        Assert.Contains(
            new EntityId("membership", MembershipId),
            result.RelatedEntityIds);
        Assert.Equal(auditEntryId, result.AuditEntryId);
        Assert.True(result.ChangedAfterClose);
    }

    [Theory]
    [InlineData(CommandErrorCode.NotFound)]
    [InlineData(CommandErrorCode.AlreadyCanceled)]
    [InlineData(CommandErrorCode.ReasonRequired)]
    [InlineData(CommandErrorCode.DayClosedRequiresOwner)]
    [InlineData(CommandErrorCode.DuplicateSubmission)]
    [InlineData(CommandErrorCode.RecalculationFailed)]
    [InlineData(CommandErrorCode.ConcurrencyConflict)]
    public void ResultContractIncludesDocumentedCancellationErrors(
        CommandErrorCode errorCode)
    {
        var result = CommandResult.Error(
        [
            new CommandError(errorCode, "Visit cannot be canceled.", "visitId"),
        ]);

        Assert.Equal(errorCode, Assert.Single(result.Errors).Code);
        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
        Assert.False(result.ChangedAfterClose);
    }

    [Fact]
    public void PreparationNormalizesCancellationMetadataAndPreservesDayCloseMarker()
    {
        var occurredAt = new DateTimeOffset(
            2026,
            7,
            14,
            18,
            30,
            0,
            TimeSpan.FromHours(3));
        var command = CreateCommand(
            CreateEnvelope(
                entryOrigin: EntryOrigin.ManualBackfill,
                occurredAt: occurredAt,
                idempotencyKey: "  cancel-visit-42  ",
                reason: "  Wrong client selected  ",
                comment: "  Corrected from reception history  "),
            EntryBatchId);

        var preparation = CancelVisitPreparationPolicy.Prepare(
            command,
            CreateSource(),
            changedAfterClose: true);

        Assert.Same(command.Envelope.Actor, preparation.Envelope.Actor);
        Assert.Equal("cancel-visit-42", preparation.Envelope.IdempotencyKey);
        Assert.Equal("Wrong client selected", preparation.Envelope.Reason);
        Assert.Equal("Corrected from reception history", preparation.Envelope.Comment);
        Assert.Equal(occurredAt.ToUniversalTime(), preparation.Envelope.OccurredAt);
        Assert.Equal(EntryBatchId, preparation.EntryBatchId);
        Assert.Equal(ClientId, preparation.Source.ClientId);
        Assert.Equal(MembershipId, preparation.Source.MembershipId);
        Assert.True(preparation.RequiresMembershipRecalculation);
        Assert.True(preparation.ChangedAfterClose);
        Assert.All(
            typeof(CancelVisitPreparation).GetProperties(),
            property => Assert.Null(property.SetMethod));
    }

    [Theory]
    [InlineData(VisitKind.OneOff)]
    [InlineData(VisitKind.Trial)]
    public void NonMembershipPreparationRequiresNoConsumptionOrRecalculation(
        VisitKind visitKind)
    {
        var preparation = CancelVisitPreparationPolicy.Prepare(
            CreateCommand(),
            CreateSource(
                visitKind: visitKind,
                includeConsumption: false),
            changedAfterClose: false);

        Assert.Equal(visitKind, preparation.Source.VisitKind);
        Assert.Null(preparation.Source.ActiveConsumptionId);
        Assert.Null(preparation.Source.MembershipId);
        Assert.False(preparation.RequiresMembershipRecalculation);
        Assert.False(preparation.ChangedAfterClose);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PreparationRequiresExplicitReason(string? reason)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand(CreateEnvelope(reason: reason)),
                CreateSource(),
                changedAfterClose: false));

        Assert.Equal("reason", exception.ParamName);
    }

    [Fact]
    public void PreparationValidatesIdempotencyAndTextLengths()
    {
        var missingKey = Assert.Throws<ArgumentException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand(CreateEnvelope(idempotencyKey: "   ")),
                CreateSource(),
                changedAfterClose: false));
        var longKey = Assert.Throws<ArgumentException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand(CreateEnvelope(idempotencyKey: new string('k', 201))),
                CreateSource(),
                changedAfterClose: false));
        var longReason = Assert.Throws<ArgumentException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand(CreateEnvelope(reason: new string('r', 1001))),
                CreateSource(),
                changedAfterClose: false));
        var longComment = Assert.Throws<ArgumentException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand(CreateEnvelope(comment: new string('c', 1001))),
                CreateSource(),
                changedAfterClose: false));

        Assert.Equal("idempotencyKey", missingKey.ParamName);
        Assert.Equal("idempotencyKey", longKey.ParamName);
        Assert.Equal("reason", longReason.ParamName);
        Assert.Equal("comment", longComment.ParamName);
    }

    [Fact]
    public void PreparationValidatesOccurredAtEntryOriginAndBatchShape()
    {
        var missingOccurredAt = Assert.Throws<ArgumentException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand(CreateEnvelope() with { OccurredAt = null }),
                CreateSource(),
                changedAfterClose: false));
        var unknownOrigin = Assert.Throws<ArgumentException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand(CreateEnvelope(entryOrigin: (EntryOrigin)999)),
                CreateSource(),
                changedAfterClose: false));
        var emptyBatch = Assert.Throws<ArgumentException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand(entryBatchId: Guid.Empty),
                CreateSource(),
                changedAfterClose: false));
        var normalBatch = Assert.Throws<ArgumentException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand(entryBatchId: EntryBatchId),
                CreateSource(),
                changedAfterClose: false));

        Assert.Equal("occurredAt", missingOccurredAt.ParamName);
        Assert.Equal("entryOrigin", unknownOrigin.ParamName);
        Assert.Equal("entryBatchId", emptyBatch.ParamName);
        Assert.Equal("entryBatchId", normalBatch.ParamName);
    }

    [Fact]
    public void PreparationRequiresRequestedActiveUncanceledSource()
    {
        var mismatchedVisit = Assert.Throws<ArgumentException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand() with { VisitId = Guid.NewGuid() },
                CreateSource(),
                changedAfterClose: false));
        var canceledSource = Assert.Throws<InvalidOperationException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand(),
                CreateSource(status: VisitCancellationSourceStatus.Canceled),
                changedAfterClose: false));
        var cancellationExists = Assert.Throws<InvalidOperationException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand(),
                CreateSource(existingCancellationId: Guid.NewGuid()),
                changedAfterClose: false));

        Assert.Equal("source", mismatchedVisit.ParamName);
        Assert.Contains("active Visit", canceledSource.Message, StringComparison.Ordinal);
        Assert.Contains("existing cancellation", cancellationExists.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PreparationRejectsInconsistentConsumptionShape()
    {
        var missingConsumption = Assert.Throws<InvalidOperationException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand(),
                CreateSource(includeConsumption: false),
                changedAfterClose: false));
        var nonMembershipConsumption = Assert.Throws<InvalidOperationException>(() =>
            CancelVisitPreparationPolicy.Prepare(
                CreateCommand(),
                CreateSource(visitKind: VisitKind.OneOff),
                changedAfterClose: false));

        Assert.Contains(
            "active counted consumption",
            missingConsumption.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "cannot have a membership consumption",
            nonMembershipConsumption.Message,
            StringComparison.Ordinal);
    }

    private static CancelVisitCommand CreateCommand(
        CommandEnvelope? envelope = null,
        Guid? entryBatchId = null)
    {
        return new CancelVisitCommand(
            envelope ?? CreateEnvelope(),
            VisitId,
            entryBatchId);
    }

    private static VisitCancellationSource CreateSource(
        VisitKind visitKind = VisitKind.Membership,
        VisitCancellationSourceStatus status = VisitCancellationSourceStatus.Active,
        bool includeConsumption = true,
        Guid? activeConsumptionId = null,
        Guid? membershipId = null,
        Guid? existingCancellationId = null)
    {
        return new VisitCancellationSource(
            VisitId,
            ClientId,
            new DateTimeOffset(2026, 7, 14, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 14, 14, 1, 0, TimeSpan.Zero),
            RecordedByAccountId,
            RecordedSessionId,
            visitKind,
            EntryOrigin.Normal,
            entryBatchId: null,
            comment: "Original Visit note",
            status,
            includeConsumption ? activeConsumptionId ?? ConsumptionId : null,
            includeConsumption ? membershipId ?? MembershipId : null,
            existingCancellationId);
    }

    private static CommandEnvelope CreateEnvelope(
        EntryOrigin entryOrigin = EntryOrigin.Normal,
        DateTimeOffset? occurredAt = default,
        string? idempotencyKey = "cancel-visit-key",
        string? reason = "Mistaken Visit",
        string? comment = "Reception correction")
    {
        var actor = new ActorContext(
            AccountId.New(),
            ActorRole.Admin,
            AccountKind.NamedAdmin,
            SessionId.New(),
            "reception tablet");

        return new CommandEnvelope(
            actor,
            new RequestCorrelationId("cancel-visit-contract"),
            entryOrigin,
            occurredAt
                ?? new DateTimeOffset(2026, 7, 14, 15, 0, 0, TimeSpan.Zero),
            idempotencyKey,
            reason,
            comment);
    }
}
