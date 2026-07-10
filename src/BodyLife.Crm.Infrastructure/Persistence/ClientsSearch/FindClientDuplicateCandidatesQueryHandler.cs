using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Clients.Search;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

public sealed class FindClientDuplicateCandidatesQueryHandler(BodyLifeDbContext dbContext)
    : IBodyLifeQueryHandler<FindClientDuplicateCandidatesQuery, IReadOnlyList<ClientDuplicateCandidate>>
{
    private const string ActiveOperationalStatus = "active";

    public async Task<IReadOnlyList<ClientDuplicateCandidate>> ExecuteAsync(
        FindClientDuplicateCandidatesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var normalizedFullName = ClientSearchNormalizer.NormalizeFullName(
            query.Surname,
            query.Name,
            query.Patronymic);
        var normalizedPhone = string.IsNullOrWhiteSpace(query.Phone)
            ? null
            : ClientSearchNormalizer.NormalizePhone(query.Phone);
        var excludedClientId = query.ExcludedClientId;

        var matches = await dbContext.Set<ClientRecord>()
            .AsNoTracking()
            .Where(client =>
                (excludedClientId == null || client.Id != excludedClientId)
                && (client.NormalizedFullName == normalizedFullName
                    || (normalizedPhone != null && client.PhoneNormalized == normalizedPhone)))
            .Select(client => new
            {
                client.Id,
                client.Surname,
                client.Name,
                client.Patronymic,
                Phone = client.PhoneRaw,
                IsActive = client.OperationalStatus == ActiveOperationalStatus,
                MatchesPhone = normalizedPhone != null && client.PhoneNormalized == normalizedPhone,
                MatchesName = client.NormalizedFullName == normalizedFullName,
            })
            .ToListAsync(cancellationToken);

        var candidates = new List<ClientDuplicateCandidate>(matches.Count * 2);

        foreach (var match in matches)
        {
            if (match.MatchesPhone)
            {
                candidates.Add(CreateCandidate(match.Id, ClientDuplicateWarningType.DuplicatePhone));
            }

            if (match.MatchesName)
            {
                candidates.Add(CreateCandidate(match.Id, ClientDuplicateWarningType.SimilarName));
            }

            ClientDuplicateCandidate CreateCandidate(
                Guid matchedClientId,
                ClientDuplicateWarningType warningType)
            {
                return new ClientDuplicateCandidate(
                    matchedClientId,
                    warningType,
                    match.Surname,
                    match.Name,
                    match.Patronymic,
                    match.Phone,
                    match.IsActive);
            }
        }

        return candidates
            .OrderBy(candidate => candidate.WarningType)
            .ThenBy(candidate => candidate.MatchedClientId)
            .ToArray();
    }
}
