using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.NonWorkingDays;

public sealed class AddNonWorkingDayCommandContractsTests
{
    private static readonly DateRange Period = new(
        new DateOnly(2026, 7, 20),
        new DateOnly(2026, 7, 22));

    [Fact]
    public void CommandCarriesExactPreviewInputTokenAndOperationalEnvelope()
    {
        var envelope = CreateEnvelope();

        var command = new AddNonWorkingDayCommand(
            envelope,
            Period,
            "weather_closure",
            "Severe weather",
            "v1.preview.signature");

        Assert.IsAssignableFrom<IBodyLifeCommand>(command);
        Assert.Same(envelope, command.Envelope);
        Assert.Equal(Period, command.Period);
        Assert.Equal("weather_closure", command.ReasonCode);
        Assert.Equal("Severe weather", command.ReasonComment);
        Assert.Equal("v1.preview.signature", command.ConfirmationToken);
    }

    [Fact]
    public void SuccessfulResultTargetsPeriodAndAffectedMembershipRereads()
    {
        var periodId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var membershipId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var auditEntryId = AuditEntryId.New();
        var periodEntityId = new EntityId(
            AddNonWorkingDayCommand.PrimaryEntityType,
            periodId);

        var result = CommandResult.Success(
            periodEntityId,
            periodEntityId,
            [new EntityId(AddNonWorkingDayCommand.MembershipEntityType, membershipId)],
            auditEntryId: auditEntryId);

        Assert.Equal(new EntityId("non_working_period", periodId), result.PrimaryEntityId);
        Assert.Equal(result.PrimaryEntityId, result.RereadTargetId);
        Assert.Equal(
            new EntityId("membership", membershipId),
            Assert.Single(result.RelatedEntityIds));
        Assert.Equal(auditEntryId, result.AuditEntryId);
        Assert.Equal(
            "non_working_period",
            AddNonWorkingDayCommand.CanonicalRereadEntityType);
    }

    [Theory]
    [InlineData(CommandErrorCode.PreviewExpired)]
    [InlineData(CommandErrorCode.AffectedScopeChanged)]
    public void ResultTaxonomyIncludesPreviewRevalidationErrors(CommandErrorCode code)
    {
        var result = CommandResult.Error(
        [
            new CommandError(code, "Create and confirm a new preview.", "confirmationToken"),
        ]);

        var error = Assert.Single(result.Errors);
        Assert.Equal(code, error.Code);
        Assert.Equal("confirmationToken", error.Field);
        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
    }

    private static CommandEnvelope CreateEnvelope()
    {
        return new CommandEnvelope(
            new ActorContext(
                AccountId.New(),
                ActorRole.Owner,
                AccountKind.Owner,
                SessionId.New(),
                "Owner laptop"),
            new RequestCorrelationId("correlation-add-non-working-day"),
            EntryOrigin.Normal,
            OccurredAt: null,
            "add-non-working-day-key",
            "Owner confirmed closure",
            "Schedule source");
    }
}
