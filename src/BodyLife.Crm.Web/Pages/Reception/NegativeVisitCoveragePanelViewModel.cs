using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Clients.Search;
using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Web.Pages.Reception;

/// <summary>Presentation-only adapter for the Memberships-owned negative coverage reads.</summary>
public sealed record NegativeVisitCoveragePanelViewModel(
    GetClientNegativeVisitCoverageResult CoverageResult,
    NegativeVisitCoverageFormInput CloseInput,
    IReadOnlyList<NegativeVisitCoverageCorrectionFormInput> CorrectionInputs,
    PreviewCloseNegativeVisitsOneOffResult? ClosePreview,
    IReadOnlyDictionary<Guid, PreviewCorrectNegativeVisitCoverageResult> CorrectionPreviews,
    IReadOnlyList<CommandError> Errors)
{
    public bool IsSafe => CoverageResult.Status == GetClientNegativeVisitCoverageStatus.Success
        && CoverageResult.Coverage is not null;

    public bool HasErrors => Errors.Count > 0;

    public static NegativeVisitCoveragePanelViewModel FromCanonical(
        GetClientNegativeVisitCoverageResult result,
        ReceptionSearchContext context,
        DateTime? correctionOccurredAtLocal = null) => new(
        result,
        new NegativeVisitCoverageFormInput
        {
            ClientId = result.Coverage?.ClientId ?? Guid.Empty,
            Lines = result.Coverage?.ActiveOneOffTypes.Select(type => new NegativeVisitCoverageLineInput
            {
                MembershipTypeId = type.MembershipTypeId,
                ExpectedMembershipTypeUpdatedAt = type.UpdatedAt,
                Quantity = 0,
            }).ToList() ?? [],
            ExpectedOldestOpenNegativeVisitId = result.Coverage?.OpenConcreteVisits.FirstOrDefault()?.VisitId,
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            SearchQuery = context.Query,
            SearchMode = context.Mode,
            SearchIncludeInactive = context.IncludeInactive,
            SearchPageCursor = context.PageCursor,
        },
        result.Coverage?.ActiveClosures.Select(closure =>
            NegativeVisitCoverageCorrectionFormInput.Initial(
                result.Coverage.ClientId,
                closure,
                result.Coverage.ActiveOneOffTypes,
                context,
                correctionOccurredAtLocal)).ToArray() ?? [],
        null,
        new Dictionary<Guid, PreviewCorrectNegativeVisitCoverageResult>(),
        []);
}

public sealed class NegativeVisitCoverageLineInput
{
    public Guid MembershipTypeId { get; set; }
    public DateTimeOffset ExpectedMembershipTypeUpdatedAt { get; set; }
    public int Quantity { get; set; }
}

public sealed class NegativeVisitCoverageFormInput
{
    public Guid ClientId { get; set; }
    public Guid? ExpectedOldestOpenNegativeVisitId { get; set; }
    public List<NegativeVisitCoverageLineInput>? Lines { get; set; }
    public bool Confirmed { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? SearchQuery { get; set; }
    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;
    public bool SearchIncludeInactive { get; set; }
    public string? SearchPageCursor { get; set; }
}

public sealed class NegativeVisitCoverageCorrectionFormInput
{
    public Guid ClientId { get; set; }
    public Guid OriginalNegativeClosureId { get; set; }
    public NegativeVisitCoverageCorrectionMode? Mode { get; set; }
    public string? Reason { get; set; }
    public DateTime? OccurredAtLocal { get; set; }
    public List<NegativeVisitCoverageLineInput>? ReplacementOneOffLines { get; set; }
    public int? ReplacementNewMembershipCoverageCount { get; set; }
    public Guid? ExpectedOldestOpenNegativeVisitId { get; set; }
    public bool Confirmed { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? SearchQuery { get; set; }
    public ClientSearchMode SearchMode { get; set; } = ClientSearchMode.Auto;
    public bool SearchIncludeInactive { get; set; }
    public string? SearchPageCursor { get; set; }

    public static NegativeVisitCoverageCorrectionFormInput Initial(
        Guid clientId,
        NegativeVisitCoverageClosureReadModel closure,
        IReadOnlyList<OneOffMembershipTypeReadModel> activeOneOffTypes,
        ReceptionSearchContext context,
        DateTime? occurredAtLocal = null) => new()
        {
            ClientId = clientId,
            OriginalNegativeClosureId = closure.ClosureId,
            OccurredAtLocal = occurredAtLocal,
            ExpectedOldestOpenNegativeVisitId = closure.OldestOpenNegativeVisitId,
            ReplacementOneOffLines = closure.ClosureType == "one_off"
            ? activeOneOffTypes.Select(type => new NegativeVisitCoverageLineInput
            {
                MembershipTypeId = type.MembershipTypeId,
                ExpectedMembershipTypeUpdatedAt = type.UpdatedAt,
                Quantity = 0,
            }).ToList()
            : [],
            IdempotencyKey = Guid.NewGuid().ToString("N"),
            SearchQuery = context.Query,
            SearchMode = context.Mode,
            SearchIncludeInactive = context.IncludeInactive,
            SearchPageCursor = context.PageCursor,
        };
}
