namespace BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

public sealed record OwnerCredentialsBootstrapResult(
    OwnerCredentialsBootstrapStatus Status,
    Guid? AccountId,
    string Message)
{
    public static OwnerCredentialsBootstrapResult Updated(Guid accountId)
    {
        return new OwnerCredentialsBootstrapResult(
            OwnerCredentialsBootstrapStatus.Updated,
            accountId,
            "Owner credentials updated.");
    }

    public static OwnerCredentialsBootstrapResult OwnerMissing()
    {
        return new OwnerCredentialsBootstrapResult(
            OwnerCredentialsBootstrapStatus.OwnerMissing,
            null,
            "Owner account must be bootstrapped before credentials can be set.");
    }

    public static OwnerCredentialsBootstrapResult ValidationFailed(string message)
    {
        return new OwnerCredentialsBootstrapResult(
            OwnerCredentialsBootstrapStatus.ValidationFailed,
            null,
            message);
    }
}
