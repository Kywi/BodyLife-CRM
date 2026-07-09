using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;

namespace BodyLife.Crm.Web.Operations;

internal static class OwnerBootstrapCommand
{
    public const string CommandName = "bootstrap-owner";
    public const string DisplayNameEnvironmentVariable = "BODYLIFE_BOOTSTRAP_OWNER_DISPLAY_NAME";
    private const string DisplayNameConfigurationKey = "BodyLife:Bootstrap:OwnerDisplayName";

    public static bool IsRequested(string[] args)
    {
        return args.Any(argument =>
            string.Equals(argument, CommandName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(argument, "--bootstrap-owner", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<int> ExecuteAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var displayName = configuration[DisplayNameConfigurationKey];

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = Environment.GetEnvironmentVariable(DisplayNameEnvironmentVariable);
        }

        await using var scope = services.CreateAsyncScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<OwnerBootstrapper>();
        var result = await bootstrapper.BootstrapOwnerAsync(displayName, cancellationToken);

        return result.Status switch
        {
            OwnerBootstrapStatus.Created => ReportSuccess(logger, result),
            OwnerBootstrapStatus.AlreadyExists => ReportSuccess(logger, result),
            OwnerBootstrapStatus.ValidationFailed => ReportValidationFailure(logger, result),
            _ => throw new InvalidOperationException($"Unsupported owner bootstrap status: {result.Status}."),
        };
    }

    private static int ReportSuccess(ILogger logger, OwnerBootstrapResult result)
    {
        logger.LogInformation(
            "Owner bootstrap completed with status {OwnerBootstrapStatus} for account {AccountId}.",
            result.Status,
            result.AccountId);

        Console.WriteLine(result.Message);

        return 0;
    }

    private static int ReportValidationFailure(ILogger logger, OwnerBootstrapResult result)
    {
        logger.LogWarning(
            "Owner bootstrap failed validation with status {OwnerBootstrapStatus}.",
            result.Status);

        Console.Error.WriteLine(result.Message);
        Console.Error.WriteLine(
            $"Set {DisplayNameEnvironmentVariable} or configuration key {DisplayNameConfigurationKey}.");

        return 64;
    }
}
