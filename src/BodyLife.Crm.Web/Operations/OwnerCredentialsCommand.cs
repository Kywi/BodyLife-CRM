using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

namespace BodyLife.Crm.Web.Operations;

internal static class OwnerCredentialsCommand
{
    public const string CommandName = "set-owner-credentials";
    public const string LoginNameEnvironmentVariable = "BODYLIFE_OWNER_LOGIN_NAME";
    public const string PasswordEnvironmentVariable = "BODYLIFE_OWNER_PASSWORD";
    private const string LoginNameConfigurationKey = "BodyLife:Bootstrap:OwnerLoginName";
    private const string PasswordConfigurationKey = "BodyLife:Bootstrap:OwnerPassword";

    public static bool IsRequested(string[] args)
    {
        return args.Any(argument =>
            string.Equals(argument, CommandName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, "--set-owner-credentials", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<int> ExecuteAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var loginName = configuration[LoginNameConfigurationKey];
        var password = configuration[PasswordConfigurationKey];

        if (string.IsNullOrWhiteSpace(loginName))
        {
            loginName = Environment.GetEnvironmentVariable(LoginNameEnvironmentVariable);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            password = Environment.GetEnvironmentVariable(PasswordEnvironmentVariable);
        }

        await using var scope = services.CreateAsyncScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<OwnerCredentialsBootstrapper>();
        var result = await bootstrapper.SetOwnerCredentialsAsync(loginName, password, cancellationToken);

        return result.Status switch
        {
            OwnerCredentialsBootstrapStatus.Updated => ReportSuccess(logger, result),
            OwnerCredentialsBootstrapStatus.OwnerMissing => ReportFailure(logger, result, 66),
            OwnerCredentialsBootstrapStatus.ValidationFailed => ReportFailure(logger, result, 64),
            _ => throw new InvalidOperationException($"Unsupported owner credentials status: {result.Status}."),
        };
    }

    private static int ReportSuccess(ILogger logger, OwnerCredentialsBootstrapResult result)
    {
        logger.LogInformation(
            "Owner credentials bootstrap completed with status {OwnerCredentialsBootstrapStatus} for account {AccountId}.",
            result.Status,
            result.AccountId);

        Console.WriteLine(result.Message);

        return 0;
    }

    private static int ReportFailure(ILogger logger, OwnerCredentialsBootstrapResult result, int exitCode)
    {
        logger.LogWarning(
            "Owner credentials bootstrap failed with status {OwnerCredentialsBootstrapStatus}.",
            result.Status);

        Console.Error.WriteLine(result.Message);
        Console.Error.WriteLine(
            $"Set {LoginNameEnvironmentVariable} and {PasswordEnvironmentVariable}, or matching configuration keys.");

        return exitCode;
    }
}
