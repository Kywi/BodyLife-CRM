using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Modules.Memberships;

public sealed record ClientNegativeVisitCoverageReadModel(
    Guid ClientId,
    int TotalNegativeBalance,
    int UnknownNegativeBalance,
    DateOnly? FirstNegativeVisitDate,
    IReadOnlyList<NegativeVisitCoverageCandidateReadModel> OpenConcreteVisits,
    IReadOnlyList<OneOffMembershipTypeReadModel> ActiveOneOffTypes,
    IReadOnlyList<NegativeVisitCoverageClosureReadModel> ActiveClosures);

public sealed record NegativeVisitCoverageCandidateReadModel(
    Guid VisitId,
    Guid SourceMembershipId,
    Guid OldConsumptionId,
    DateTimeOffset OccurredAt,
    DateTimeOffset ConsumptionRecordedAt,
    DateOnly BusinessDate);

public sealed record OneOffMembershipTypeReadModel(
    Guid MembershipTypeId,
    string Name,
    int DurationDays,
    int VisitsLimit,
    Money Price,
    DateTimeOffset UpdatedAt);

public sealed record NegativeVisitCoverageClosureReadModel(
    Guid ClosureId,
    string ClosureType,
    Guid? CoveringMembershipId,
    IssuedMembershipCoverageSnapshotReadModel? CoveringMembershipSnapshot,
    Guid OldestOpenNegativeVisitId,
    int VisitsCount,
    string? Comment,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    Guid RecordedByAccountId,
    Guid SessionId,
    string EntryOrigin,
    string Status,
    IReadOnlyList<NegativeVisitCoverageLineReadModel> Lines,
    IReadOnlyList<NegativeVisitCoverageItemReadModel> Items,
    NegativeVisitCoveragePaymentReadModel? Payment);

public sealed record IssuedMembershipCoverageSnapshotReadModel(
    Guid MembershipId,
    Guid MembershipTypeId,
    string TypeName,
    int DurationDays,
    int VisitsLimit,
    Money Price,
    DateOnly StartDate,
    DateOnly BaseEndDate,
    DateTimeOffset IssuedAt,
    string Status);

public sealed record NegativeVisitCoverageLineReadModel(
    Guid LineId,
    Guid MembershipTypeId,
    string TypeName,
    int DurationDays,
    int VisitsLimit,
    int Quantity,
    Money UnitPrice,
    Money LineTotal,
    int Sequence);

public sealed record NegativeVisitCoverageItemReadModel(
    Guid ItemId,
    int Sequence,
    Guid VisitId,
    DateTimeOffset VisitOccurredAt,
    DateOnly VisitBusinessDate,
    string VisitSource,
    Guid SourceMembershipId,
    Guid OldConsumptionId,
    Guid? CoveringMembershipId,
    Guid? NewConsumptionId,
    string Status);

public sealed record NegativeVisitCoveragePaymentReadModel(
    Guid PaymentId,
    Money Amount,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    string Status);
