using BodyLife.Crm.Infrastructure.Persistence.Memberships;

namespace BodyLife.Crm.Web.Operations;

internal static class RebuildMembershipStateCachesCommand
{
    public const string CommandName = "rebuild-membership-state-caches";

    public static bool IsRequested(string[] args)
    {
        return args.Any(argument =>
            string.Equals(argument, CommandName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                argument,
                "--rebuild-membership-state-caches",
                StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<int> ExecuteAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var rebuilder = scope.ServiceProvider
                .GetRequiredService<MembershipStateCacheBulkRebuilder>();
            var result = await rebuilder.RebuildAllAsync(cancellationToken);

            return result.Status switch
            {
                MembershipStateCacheBulkRebuildStatus.Succeeded => ReportSuccess(logger, result),
                MembershipStateCacheBulkRebuildStatus.MissingSource => ReportMissingSource(logger, result),
                _ => ReportUnsupportedStatus(logger, result),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Membership state-cache rebuild failed before completion.");
            Console.Error.WriteLine("Membership state-cache rebuild failed. See application logs.");
            return 1;
        }
    }

    private static int ReportSuccess(
        ILogger logger,
        MembershipStateCacheBulkRebuildResult result)
    {
        logger.LogInformation(
            "Membership state-cache rebuild completed at version {RecalculationVersion}: total {Total}, created {Created}, repaired {Repaired}, verified {Verified}.",
            MembershipStateCacheRebuilder.CurrentRecalculationVersion,
            result.Total,
            result.Created,
            result.Repaired,
            result.Verified);
        Console.WriteLine(
            $"Membership state-cache rebuild version {MembershipStateCacheRebuilder.CurrentRecalculationVersion} completed: total={result.Total}, created={result.Created}, repaired={result.Repaired}, verified={result.Verified}.");
        return 0;
    }

    private static int ReportMissingSource(
        ILogger logger,
        MembershipStateCacheBulkRebuildResult result)
    {
        logger.LogError(
            "Membership state-cache rebuild stopped because issued Membership {MembershipId} was missing after enumeration; total {Total}, processed {Processed}, created {Created}, repaired {Repaired}, verified {Verified}.",
            result.MissingMembershipId,
            result.Total,
            result.Processed,
            result.Created,
            result.Repaired,
            result.Verified);
        Console.Error.WriteLine(
            $"Membership state-cache rebuild stopped: issued Membership {result.MissingMembershipId} is missing after {result.Processed}/{result.Total} Memberships.");
        return 1;
    }

    private static int ReportUnsupportedStatus(
        ILogger logger,
        MembershipStateCacheBulkRebuildResult result)
    {
        logger.LogError(
            "Membership state-cache rebuild returned unsupported status {Status}.",
            result.Status);
        Console.Error.WriteLine("Membership state-cache rebuild failed with an unsupported status.");
        return 1;
    }
}
