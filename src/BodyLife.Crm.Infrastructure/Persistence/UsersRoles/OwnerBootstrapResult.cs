namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

public sealed record OwnerBootstrapResult(
    OwnerBootstrapStatus Status,
    Guid? AccountId,
    string Message)
{
    public static OwnerBootstrapResult Created(Guid accountId)
    {
        return new OwnerBootstrapResult(
            OwnerBootstrapStatus.Created,
            accountId,
            "Owner account created.");
    }

    public static OwnerBootstrapResult AlreadyExists(Guid accountId)
    {
        return new OwnerBootstrapResult(
            OwnerBootstrapStatus.AlreadyExists,
            accountId,
            "Owner account already exists.");
    }

    public static OwnerBootstrapResult ValidationFailed(string message)
    {
        return new OwnerBootstrapResult(
            OwnerBootstrapStatus.ValidationFailed,
            null,
            message);
    }
}
