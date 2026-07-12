using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

internal static class ClientQuerySupport
{
    internal const string ActiveOperationalStatus = "active";

    internal static Task<bool> IsActorAuthorizedAsync(
        BodyLifeDbContext dbContext,
        ActorContext? actor,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return actor is not null && ClientCommandSupport.IsAllowedActorShape(actor)
            ? ClientCommandSupport.IsCanonicalActorAuthorizedAsync(
                dbContext,
                actor,
                now,
                cancellationToken)
            : Task.FromResult(false);
    }

    internal static string BuildDisplayName(
        string surname,
        string name,
        string? patronymic)
    {
        return patronymic is null
            ? $"{surname} {name}"
            : $"{surname} {name} {patronymic}";
    }

    internal static IReadOnlyList<ClientWarning> BuildWarnings(
        string operationalStatus,
        string? currentCardNumber)
    {
        var warnings = new List<ClientWarning>(2);

        if (operationalStatus != ActiveOperationalStatus)
        {
            warnings.Add(new ClientWarning(
                "client_inactive",
                "Client is operationally inactive."));
        }

        if (currentCardNumber is null)
        {
            warnings.Add(new ClientWarning(
                "no_current_card",
                "Client has no current card."));
        }

        return warnings;
    }

    internal static ClientOperationalStatus MapOperationalStatus(string operationalStatus)
    {
        return operationalStatus switch
        {
            ActiveOperationalStatus => ClientOperationalStatus.Active,
            "inactive" => ClientOperationalStatus.Inactive,
            _ => throw new InvalidOperationException(
                $"Unsupported client operational status '{operationalStatus}'."),
        };
    }
}
