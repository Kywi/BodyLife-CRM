using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Application.Commands;

public sealed record CommandResult(
    CommandStatus Status,
    EntityId? PrimaryEntityId,
    IReadOnlyList<EntityId> RelatedEntityIds,
    EntityId? RereadTargetId,
    IReadOnlyList<string> Warnings,
    AuditEntryId? AuditEntryId,
    bool ChangedAfterClose,
    IReadOnlyList<CommandError> Errors)
{
    public static CommandResult Success(
        EntityId primaryEntityId,
        EntityId rereadTargetId,
        IReadOnlyList<EntityId>? relatedEntityIds = null,
        IReadOnlyList<string>? warnings = null,
        AuditEntryId? auditEntryId = null,
        bool changedAfterClose = false)
    {
        return new CommandResult(
            CommandStatus.Success,
            primaryEntityId,
            relatedEntityIds ?? [],
            rereadTargetId,
            warnings ?? [],
            auditEntryId,
            changedAfterClose,
            []);
    }

    public static CommandResult Error(IReadOnlyList<CommandError> errors)
    {
        return new CommandResult(
            CommandStatus.Error,
            null,
            [],
            null,
            [],
            null,
            false,
            errors);
    }
}
