using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Application;

public sealed class CommandResultTests
{
    [Fact]
    public void SuccessCarriesCanonicalRereadTarget()
    {
        var primaryEntityId = new EntityId("membership", Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var rereadTargetId = new EntityId("client", Guid.Parse("22222222-2222-2222-2222-222222222222"));
        var relatedEntityId = new EntityId("visit", Guid.Parse("33333333-3333-3333-3333-333333333333"));

        var result = CommandResult.Success(
            primaryEntityId,
            rereadTargetId,
            relatedEntityIds: [relatedEntityId],
            warnings: ["low_remaining"]);

        Assert.Equal(CommandStatus.Success, result.Status);
        Assert.Equal(primaryEntityId, result.PrimaryEntityId);
        Assert.Equal(rereadTargetId, result.RereadTargetId);
        Assert.Equal([relatedEntityId], result.RelatedEntityIds);
        Assert.Equal(["low_remaining"], result.Warnings);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ErrorCarriesNoBusinessMutationIds()
    {
        var error = new CommandError(CommandErrorCode.ValidationFailed, "Name is required.", "name");

        var result = CommandResult.Error([error]);

        Assert.Equal(CommandStatus.Error, result.Status);
        Assert.Null(result.PrimaryEntityId);
        Assert.Null(result.RereadTargetId);
        Assert.Null(result.AuditEntryId);
        Assert.Empty(result.RelatedEntityIds);
        Assert.Empty(result.Warnings);
        Assert.Equal([error], result.Errors);
    }
}
