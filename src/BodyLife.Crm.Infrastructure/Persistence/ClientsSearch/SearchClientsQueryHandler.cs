using System.Globalization;
using BodyLife.Crm.Application.Queries;
using BodyLife.Crm.Modules.Clients.Search;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.ClientsSearch;

public sealed class SearchClientsQueryHandler(
    BodyLifeDbContext dbContext,
    TimeProvider timeProvider)
    : IBodyLifeQueryHandler<SearchClientsQuery, SearchClientsResult>
{
    private const int MaxLimit = 50;
    private const int MaxCursorOffset = 10_000;
    private const int MaxSearchTextLength = 200;
    private const int ExactCardPriority = 0;
    private const int ExactPhonePriority = 10;
    private const int PhoneLastFourPriority = 20;
    private const int ExactNamePriority = 30;
    private const int PartialCardPriority = 40;
    private const int PartialPhonePriority = 50;
    private const int PartialNamePriority = 60;

    public async Task<SearchClientsResult> ExecuteAsync(
        SearchClientsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!await ClientQuerySupport.IsActorAuthorizedAsync(
                dbContext,
                query.Actor,
                timeProvider.GetUtcNow(),
                cancellationToken))
        {
            return SearchClientsResult.Denied();
        }

        var validationResult = ValidateAndNormalize(query, out var normalizedCriteria);

        if (validationResult is not null)
        {
            return validationResult;
        }

        var criteria = normalizedCriteria!;
        var currentCards = dbContext.Set<ClientCardAssignmentRecord>()
            .AsNoTracking()
            .Where(assignment => assignment.IsCurrent);
        var clients = dbContext.Set<ClientRecord>()
            .AsNoTracking()
            .Where(client => criteria.IncludeInactive
                || client.OperationalStatus == ClientQuerySupport.ActiveOperationalStatus);
        var candidates =
            from client in clients
            join card in currentCards on client.Id equals card.ClientId into currentCardGroup
            from currentCard in currentCardGroup.DefaultIfEmpty()
            let exactCard = criteria.CardTerm != null
                && currentCard != null
                && currentCard.CardNumberNormalized == criteria.CardTerm
            let exactPhone = criteria.PhoneTerm != null
                && client.PhoneNormalized == criteria.PhoneTerm
            let lastFour = criteria.LastFourTerm != null
                && client.PhoneLastFour == criteria.LastFourTerm
            let exactName = criteria.NameTerm != null
                && client.NormalizedFullName == criteria.NameTerm
            let partialCard = criteria.CardTerm != null
                && currentCard != null
                && currentCard.CardNumberNormalized.Contains(criteria.CardTerm)
            let partialPhone = criteria.PhoneTerm != null
                && client.PhoneNormalized != null
                && client.PhoneNormalized!.Contains(criteria.PhoneTerm!)
            let partialName = criteria.NameTerm != null
                && client.NormalizedFullName.Contains(criteria.NameTerm)
            where exactCard
                || exactPhone
                || lastFour
                || exactName
                || partialCard
                || partialPhone
                || partialName
            select new
            {
                ClientId = client.Id,
                client.Surname,
                client.Name,
                client.Patronymic,
                client.NormalizedFullName,
                Phone = client.PhoneRaw,
                CurrentCardNumber = currentCard == null ? null : currentCard.CardNumberRaw,
                client.OperationalStatus,
                IsExactCard = exactCard,
                MatchPriority = exactCard
                    ? ExactCardPriority
                    : exactPhone
                        ? ExactPhonePriority
                        : lastFour
                            ? PhoneLastFourPriority
                            : exactName
                                ? ExactNamePriority
                                : partialCard
                                    ? PartialCardPriority
                                    : partialPhone
                                        ? PartialPhonePriority
                                        : PartialNamePriority,
            };

        Guid? autoOpenClientId = null;

        if (criteria.CardTerm is not null)
        {
            var exactCardIds = await candidates
                .Where(candidate => candidate.IsExactCard)
                .Select(candidate => candidate.ClientId)
                .Take(2)
                .ToArrayAsync(cancellationToken);

            if (exactCardIds.Length == 1)
            {
                autoOpenClientId = exactCardIds[0];
            }
        }

        var page = await candidates
            .OrderBy(candidate => candidate.MatchPriority)
            .ThenBy(candidate => candidate.OperationalStatus == ClientQuerySupport.ActiveOperationalStatus ? 0 : 1)
            .ThenBy(candidate => candidate.NormalizedFullName)
            .ThenBy(candidate => candidate.ClientId)
            .Skip(criteria.Offset)
            .Take(criteria.Limit + 1)
            .ToArrayAsync(cancellationToken);
        var hasNextPage = page.Length > criteria.Limit;
        var items = page
            .Take(criteria.Limit)
            .Select(candidate => new ClientSearchResult(
                candidate.ClientId,
                ClientQuerySupport.BuildDisplayName(
                    candidate.Surname,
                    candidate.Name,
                    candidate.Patronymic),
                candidate.Phone,
                candidate.CurrentCardNumber,
                ClientQuerySupport.MapOperationalStatus(candidate.OperationalStatus),
                MapMatchType(candidate.MatchPriority),
                candidate.MatchPriority,
                CurrentMembership: null,
                ClientQuerySupport.BuildWarnings(
                    candidate.OperationalStatus,
                    candidate.CurrentCardNumber)))
            .ToArray();
        var nextPageCursor = hasNextPage
            ? (criteria.Offset + criteria.Limit).ToString(CultureInfo.InvariantCulture)
            : null;

        return SearchClientsResult.Succeeded(items, autoOpenClientId, nextPageCursor);
    }

    private static SearchClientsResult? ValidateAndNormalize(
        SearchClientsQuery query,
        out NormalizedSearchCriteria? normalizedCriteria)
    {
        normalizedCriteria = null;
        var searchText = query.SearchText?.Trim();

        if (string.IsNullOrWhiteSpace(searchText))
        {
            return SearchClientsResult.Invalid("Search text is required.", "searchText");
        }

        if (searchText.Length > MaxSearchTextLength)
        {
            return SearchClientsResult.Invalid(
                $"Search text must be {MaxSearchTextLength} characters or fewer.",
                "searchText");
        }

        if (!Enum.IsDefined(query.Mode))
        {
            return SearchClientsResult.Invalid("Search mode is invalid.", "mode");
        }

        var limit = query.Limit;

        if (limit is < 1 or > MaxLimit)
        {
            return SearchClientsResult.Invalid(
                $"Limit must be between 1 and {MaxLimit}.",
                "limit");
        }

        var cursor = query.PageCursor?.Trim();
        var offset = 0;

        if (!string.IsNullOrEmpty(cursor)
            && (!int.TryParse(
                    cursor,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out offset)
                || offset < 0
                || offset > MaxCursorOffset))
        {
            return SearchClientsResult.Invalid(
                $"Page cursor must be an offset between 0 and {MaxCursorOffset}.",
                "pageCursor");
        }

        string? cardTerm = null;
        string? nameTerm = null;
        string? phoneTerm = null;
        string? lastFourTerm = null;

        try
        {
            switch (query.Mode)
            {
                case ClientSearchMode.Auto:
                    cardTerm = ClientSearchNormalizer.NormalizeCardNumber(searchText);
                    nameTerm = ClientSearchNormalizer.NormalizeNamePart(searchText);

                    try
                    {
                        phoneTerm = ClientSearchNormalizer.NormalizePhone(searchText);

                        if (phoneTerm.Length == 4)
                        {
                            lastFourTerm = phoneTerm;
                        }
                    }
                    catch (ArgumentException)
                    {
                        phoneTerm = null;
                    }

                    break;
                case ClientSearchMode.Card:
                    cardTerm = ClientSearchNormalizer.NormalizeCardNumber(searchText);
                    break;
                case ClientSearchMode.Name:
                    nameTerm = ClientSearchNormalizer.NormalizeNamePart(searchText);
                    break;
                case ClientSearchMode.Phone:
                    phoneTerm = ClientSearchNormalizer.NormalizePhone(searchText);
                    break;
                case ClientSearchMode.LastFour:
                    var normalizedPhone = ClientSearchNormalizer.NormalizePhone(searchText);

                    if (normalizedPhone.Length != 4)
                    {
                        return SearchClientsResult.Invalid(
                            "Last-four search requires exactly four digits.",
                            "searchText");
                    }

                    lastFourTerm = normalizedPhone;
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported search mode '{query.Mode}'.");
            }
        }
        catch (ArgumentException exception)
        {
            return SearchClientsResult.Invalid(exception.Message, "searchText");
        }

        normalizedCriteria = new NormalizedSearchCriteria(
            cardTerm,
            nameTerm,
            phoneTerm,
            lastFourTerm,
            query.IncludeInactive,
            limit,
            offset);
        return null;
    }

    private static ClientSearchMatchType MapMatchType(int matchPriority)
    {
        return matchPriority switch
        {
            ExactCardPriority => ClientSearchMatchType.ExactCard,
            ExactPhonePriority => ClientSearchMatchType.ExactPhone,
            PhoneLastFourPriority => ClientSearchMatchType.PhoneLastFour,
            ExactNamePriority => ClientSearchMatchType.ExactName,
            PartialCardPriority => ClientSearchMatchType.PartialCard,
            PartialPhonePriority => ClientSearchMatchType.PartialPhone,
            PartialNamePriority => ClientSearchMatchType.PartialName,
            _ => throw new InvalidOperationException(
                $"Unsupported client search match priority '{matchPriority}'."),
        };
    }

    private sealed record NormalizedSearchCriteria(
        string? CardTerm,
        string? NameTerm,
        string? PhoneTerm,
        string? LastFourTerm,
        bool IncludeInactive,
        int Limit,
        int Offset);
}
