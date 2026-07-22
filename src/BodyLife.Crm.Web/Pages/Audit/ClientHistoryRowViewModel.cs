using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Modules.Audit;
using BodyLife.Crm.Modules.Reports;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Web.Pages.Audit;

public sealed record ClientHistoryRowViewModel(
    AuditEntryId AuditEntryId,
    Guid SourceEntityId,
    ClientHistorySourceKind Kind,
    string GroupLabel,
    string GroupClass,
    string Title,
    string StatusLabel,
    string StatusClass,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    EntryOrigin EntryOrigin,
    AccountId ActorAccountId,
    AccountKind ActorAccountKind,
    ActorRole ActorRole,
    SessionId SessionId,
    string DeviceLabel,
    bool ChangedAfterClose,
    string? AuditReason,
    string? AuditComment,
    IReadOnlyList<ClientHistoryFactViewModel> Facts,
    ClientHistoryChangeViewModel? Change,
    string? Narrative,
    IReadOnlyList<ClientHistoryFactViewModel> Identifiers);

public sealed record ClientHistoryFactViewModel(string Label, string Value);

public sealed record ClientHistoryChangeViewModel(
    string Label,
    string Reason,
    string? Comment,
    IReadOnlyList<ClientHistoryFactViewModel> Details);
