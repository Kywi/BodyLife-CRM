using System.Globalization;
using System.Text.Json;
using BodyLife.Crm.Modules.Audit;

namespace BodyLife.Crm.Web.Pages.Audit;

internal static class NonWorkingDayAuditExplanationFactory
{
    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);

    internal static AuditEntryExplanationViewModel CreateCorrection(
        AuditTimelineEntry entry,
        JsonElement before,
        JsonElement after)
    {
        var summary = ReadSummary(entry, before, after, expectedOriginalStatus: "corrected");
        var replacement = summary.ReplacementPeriod
            ?? throw new JsonException("A corrected period requires a replacement period.");
        if (summary.Mode is not ("replace_range" or "replace_reason")
            || summary.Cancellation is not null
            || replacement.Status != "active"
            || replacement.PeriodId == summary.BeforePeriod.PeriodId
            || replacement.CreatedAt != entry.RecordedAt
            || summary.ReplacementApplications.Any(application =>
                application.StartDate != replacement.StartDate
                || application.EndDate != replacement.EndDate))
        {
            throw new JsonException("Non-working day correction summary is inconsistent.");
        }

        var rangeChanged = summary.BeforePeriod.RangeValues()
            != replacement.RangeValues();
        var reasonChanged = summary.BeforePeriod.ReasonValues()
            != replacement.ReasonValues();
        var scopeChanged = !ScopeEquals(
            summary.BeforeApplications,
            summary.ReplacementApplications);
        if ((summary.Mode == "replace_range" && !rangeChanged)
            || (summary.Mode == "replace_reason"
                && (rangeChanged || !reasonChanged || scopeChanged)))
        {
            throw new JsonException("Non-working day correction mode is inconsistent.");
        }

        var changedFields = new List<string>();
        AddChangedField(changedFields, rangeChanged, "Date range");
        AddChangedField(changedFields, reasonChanged, "Reason");
        AddChangedField(changedFields, scopeChanged, "Affected scope");
        if (changedFields.Count == 0)
        {
            throw new JsonException("Non-working day correction did not change business values.");
        }

        return new AuditEntryExplanationViewModel(
            "non-working-day-corrected",
            "Original non-working period preserved; replacement added",
            "The original period remains in history with Corrected status. A new active period and application snapshot replace it, and the stored affected union was recalculated successfully.",
            "Original period",
            "Replacement period",
            PeriodFacts(summary.BeforePeriod, summary.OldAffectedCount, "Active"),
            [
                Fact("Correction type", CorrectionModeLabel(summary.Mode)),
                Fact("Original status", "Corrected"),
                .. PeriodFacts(replacement, summary.NewAffectedCount, "Active"),
                Fact("Recalculated memberships", RecalculationLabel(summary.Recalculation)),
            ],
            string.Join(", ", changedFields),
            IsAvailable: true);
    }

    internal static AuditEntryExplanationViewModel CreateCancellation(
        AuditTimelineEntry entry,
        JsonElement before,
        JsonElement after)
    {
        var summary = ReadSummary(entry, before, after, expectedOriginalStatus: "canceled");
        var cancellation = summary.Cancellation
            ?? throw new JsonException("A canceled period requires a cancellation fact.");
        if (summary.Mode != "cancel"
            || summary.ReplacementPeriod is not null
            || summary.ReplacementApplications.Count != 0
            || summary.NewAffectedCount != 0
            || cancellation.NonWorkingPeriodId != summary.BeforePeriod.PeriodId
            || cancellation.Reason != entry.Reason
            || cancellation.RecordedAt != entry.RecordedAt)
        {
            throw new JsonException("Non-working day cancellation summary is inconsistent.");
        }

        return new AuditEntryExplanationViewModel(
            "non-working-day-canceled",
            "Original non-working period preserved; cancellation added",
            "The original period and application records remain in history. A separate cancellation fact deactivates the stored scope, and the affected Memberships were recalculated successfully.",
            "Original period",
            "After cancellation",
            PeriodFacts(summary.BeforePeriod, summary.OldAffectedCount, "Active"),
            [
                Fact("Original fact", "Preserved"),
                Fact("Period", DateRangeLabel(summary.BeforePeriod)),
                Fact("Status", "Canceled"),
                Fact("Active applications", "0"),
                Fact("Recalculated memberships", RecalculationLabel(summary.Recalculation)),
                Fact("Canceled", TimelineModel.TimestampLabel(cancellation.RecordedAt)),
            ],
            ChangedFields: "Period status, Active affected scope",
            IsAvailable: true);
    }

    private static NonWorkingDaySummary ReadSummary(
        AuditTimelineEntry entry,
        JsonElement before,
        JsonElement after,
        string expectedOriginalStatus)
    {
        var beforeDto = Deserialize<BeforeSummaryDto>(before);
        var afterDto = Deserialize<AfterSummaryDto>(after);
        var preview = beforeDto.Preview
            ?? throw new JsonException("The non-working day preview summary is required.");
        var beforePeriod = ReadSourcePeriod(beforeDto.Period);
        var beforeApplications = ReadBeforeApplications(beforeDto.Applications);
        var afterOriginalPeriod = ReadSourcePeriod(afterDto.OriginalPeriod);
        var replacementPeriod = afterDto.ReplacementPeriod is null
            ? null
            : ReadReplacementPeriod(afterDto.ReplacementPeriod);
        var replacementApplications = ReadReplacementApplications(
            afterDto.ReplacementApplications);
        var cancellation = afterDto.Cancellation is null
            ? null
            : ReadCancellation(afterDto.Cancellation);
        var recalculation = ReadRecalculation(afterDto.Recalculation);
        var affectedMembershipIds = beforeApplications
            .Select(application => application.MembershipId)
            .Concat(replacementApplications.Select(application => application.MembershipId))
            .ToHashSet();

        if (preview.OldAffectedCount < 0
            || preview.NewAffectedCount < 0
            || afterDto.OldAffectedCount < 0
            || afterDto.NewAffectedCount < 0
            || afterDto.AffectedUnionCount < 0
            || beforePeriod.PeriodId != entry.EntityId
            || afterOriginalPeriod.PeriodId != entry.EntityId
            || beforePeriod.Status != "active"
            || afterOriginalPeriod.Status != expectedOriginalStatus
            || (afterOriginalPeriod with { Status = "active" }) != beforePeriod
            || beforeApplications.Any(application =>
                application.StartDate != beforePeriod.StartDate
                || application.EndDate != beforePeriod.EndDate)
            || preview.OldAffectedCount != beforeApplications.Count
            || afterDto.OldAffectedCount != beforeApplications.Count
            || preview.NewAffectedCount != replacementApplications.Count
            || afterDto.NewAffectedCount != replacementApplications.Count
            || afterDto.AffectedUnionCount != affectedMembershipIds.Count
            || recalculation.RequestedCount != afterDto.AffectedUnionCount
            || recalculation.SucceededCount != afterDto.AffectedUnionCount
            || !affectedMembershipIds.SetEquals(recalculation.MembershipIds))
        {
            throw new JsonException("Non-working day affected scope is inconsistent.");
        }

        return new NonWorkingDaySummary(
            beforePeriod,
            beforeApplications,
            RequireText(afterDto.Mode, "mode"),
            replacementPeriod,
            replacementApplications,
            cancellation,
            afterDto.OldAffectedCount,
            afterDto.NewAffectedCount,
            recalculation);
    }

    private static SourcePeriodSnapshot ReadSourcePeriod(SourcePeriodDto? period)
    {
        period = period
            ?? throw new JsonException("The source period summary is required.");
        var snapshot = new SourcePeriodSnapshot(
            RequireId(period.PeriodId, "periodId"),
            period.StartDate,
            period.EndDate,
            RequirePositive(period.InclusiveDays, "inclusiveDays"),
            RequireText(period.ReasonCode, "reasonCode"),
            NormalizeOptionalText(period.ReasonComment),
            RequireTimestamp(period.CreatedAt, "createdAt"),
            RequireId(period.CreatedByAccountId, "createdByAccountId"),
            RequireId(period.SessionId, "sessionId"),
            RequireText(period.Status, "status"));
        EnsureValidRange(snapshot.StartDate, snapshot.EndDate);
        return snapshot;
    }

    private static SourcePeriodSnapshot ReadReplacementPeriod(ReplacementPeriodDto period)
    {
        var snapshot = new SourcePeriodSnapshot(
            RequireId(period.PeriodId, "periodId"),
            period.StartDate,
            period.EndDate,
            RequirePositive(period.InclusiveDays, "inclusiveDays"),
            RequireText(period.ReasonCode, "reasonCode"),
            NormalizeOptionalText(period.ReasonComment),
            RequireTimestamp(period.CreatedAt, "createdAt"),
            CreatedByAccountId: null,
            SessionId: null,
            RequireText(period.Status, "status"));
        EnsureValidRange(snapshot.StartDate, snapshot.EndDate);
        return snapshot;
    }

    private static IReadOnlyList<ApplicationScopeSnapshot> ReadBeforeApplications(
        IReadOnlyList<BeforeApplicationDto?>? applications)
    {
        applications = applications
            ?? throw new JsonException("Original applications are required.");
        var result = new List<ApplicationScopeSnapshot>(applications.Count);
        var applicationIds = new HashSet<Guid>();
        var membershipIds = new HashSet<Guid>();
        foreach (var application in applications)
        {
            if (application is null)
            {
                throw new JsonException("An original application is required.");
            }

            var applicationId = RequireId(application.ApplicationId, "applicationId");
            var membershipId = RequireId(application.MembershipId, "membershipId");
            if (!applicationIds.Add(applicationId)
                || !membershipIds.Add(membershipId)
                || RequireText(application.Status, "status") != "active"
                || RequireTimestamp(application.ConfirmedAt, "confirmedAt")
                    < RequireTimestamp(application.PreviewedAt, "previewedAt"))
            {
                throw new JsonException("An original application is inconsistent.");
            }

            EnsureValidRange(application.StartDate, application.EndDate);
            result.Add(new ApplicationScopeSnapshot(
                membershipId,
                RequireId(application.ClientId, "clientId"),
                application.StartDate,
                application.EndDate));
        }

        return result;
    }

    private static IReadOnlyList<ApplicationScopeSnapshot> ReadReplacementApplications(
        IReadOnlyList<ReplacementApplicationDto?>? applications)
    {
        applications = applications
            ?? throw new JsonException("Replacement applications are required.");
        var result = new List<ApplicationScopeSnapshot>(applications.Count);
        var applicationIds = new HashSet<Guid>();
        var membershipIds = new HashSet<Guid>();
        foreach (var application in applications)
        {
            if (application is null)
            {
                throw new JsonException("A replacement application is required.");
            }

            var applicationId = RequireId(application.ApplicationId, "applicationId");
            var membershipId = RequireId(application.MembershipId, "membershipId");
            if (!applicationIds.Add(applicationId) || !membershipIds.Add(membershipId))
            {
                throw new JsonException("A replacement application is duplicated.");
            }

            EnsureValidRange(application.AppliedStartDate, application.AppliedEndDate);
            result.Add(new ApplicationScopeSnapshot(
                membershipId,
                RequireId(application.ClientId, "clientId"),
                application.AppliedStartDate,
                application.AppliedEndDate));
        }

        return result;
    }

    private static CancellationSnapshot ReadCancellation(CancellationDto cancellation)
    {
        return new CancellationSnapshot(
            RequireId(cancellation.CancellationId, "cancellationId"),
            RequireId(cancellation.NonWorkingPeriodId, "nonWorkingPeriodId"),
            RequireText(cancellation.Reason, "reason"),
            RequireTimestamp(cancellation.RecordedAt, "recordedAt"));
    }

    private static RecalculationSnapshot ReadRecalculation(RecalculationDto? recalculation)
    {
        recalculation = recalculation
            ?? throw new JsonException("The recalculation summary is required.");
        var membershipIds = recalculation.MembershipIds
            ?? throw new JsonException("Recalculated Membership ids are required.");
        if (recalculation.RequestedCount < 0
            || recalculation.SucceededCount < 0
            || membershipIds.Any(id => id == Guid.Empty)
            || membershipIds.Distinct().Count() != membershipIds.Count)
        {
            throw new JsonException("The recalculation summary is inconsistent.");
        }

        return new RecalculationSnapshot(
            recalculation.RequestedCount,
            recalculation.SucceededCount,
            membershipIds.ToHashSet());
    }

    private static IReadOnlyList<AuditEntryExplanationFactViewModel> PeriodFacts(
        SourcePeriodSnapshot period,
        int affectedCount,
        string statusLabel)
    {
        return
        [
            Fact("Period", DateRangeLabel(period)),
            Fact("Inclusive days", period.InclusiveDays.ToString(CultureInfo.InvariantCulture)),
            Fact("Reason code", ReasonCodeLabel(period.ReasonCode)),
            Fact("Reason comment", period.ReasonComment ?? "None"),
            Fact("Affected memberships", affectedCount.ToString(CultureInfo.InvariantCulture)),
            Fact("Status", statusLabel),
        ];
    }

    private static T Deserialize<T>(JsonElement element)
    {
        return element.Deserialize<T>(AuditJsonOptions)
            ?? throw new JsonException("The audit summary is required.");
    }

    private static void AddChangedField(
        ICollection<string> changedFields,
        bool changed,
        string label)
    {
        if (changed)
        {
            changedFields.Add(label);
        }
    }

    private static bool ScopeEquals(
        IReadOnlyList<ApplicationScopeSnapshot> left,
        IReadOnlyList<ApplicationScopeSnapshot> right)
    {
        return left.Count == right.Count && left.ToHashSet().SetEquals(right);
    }

    private static Guid RequireId(Guid value, string propertyName)
    {
        return value != Guid.Empty
            ? value
            : throw new JsonException($"Audit summary property '{propertyName}' is required.");
    }

    private static int RequirePositive(int value, string propertyName)
    {
        return value > 0
            ? value
            : throw new JsonException($"Audit summary property '{propertyName}' is required.");
    }

    private static string RequireText(string? value, string propertyName)
    {
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new JsonException($"Audit summary property '{propertyName}' is required.");
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return value switch
        {
            null => null,
            { } text when !string.IsNullOrWhiteSpace(text) => text,
            _ => throw new JsonException("An optional audit summary value is invalid."),
        };
    }

    private static DateTimeOffset RequireTimestamp(
        DateTimeOffset value,
        string propertyName)
    {
        return value != default
            ? value
            : throw new JsonException($"Audit summary property '{propertyName}' is required.");
    }

    private static void EnsureValidRange(DateOnly startDate, DateOnly endDate)
    {
        if (startDate == default || endDate == default || startDate > endDate)
        {
            throw new JsonException("Non-working day period range is invalid.");
        }
    }

    private static string DateRangeLabel(SourcePeriodSnapshot period) =>
        $"{period.StartDate:yyyy-MM-dd} to {period.EndDate:yyyy-MM-dd}";

    private static string RecalculationLabel(RecalculationSnapshot recalculation) =>
        $"{recalculation.SucceededCount.ToString(CultureInfo.InvariantCulture)} of {recalculation.RequestedCount.ToString(CultureInfo.InvariantCulture)}";

    private static string CorrectionModeLabel(string mode) => mode switch
    {
        "replace_range" => "Date range replaced",
        "replace_reason" => "Reason replaced",
        _ => throw new JsonException("Correction mode is not supported."),
    };

    private static string ReasonCodeLabel(string reasonCode) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(reasonCode.Replace('_', ' '));

    private static AuditEntryExplanationFactViewModel Fact(string label, string value) =>
        new(label, value);

    private sealed record NonWorkingDaySummary(
        SourcePeriodSnapshot BeforePeriod,
        IReadOnlyList<ApplicationScopeSnapshot> BeforeApplications,
        string Mode,
        SourcePeriodSnapshot? ReplacementPeriod,
        IReadOnlyList<ApplicationScopeSnapshot> ReplacementApplications,
        CancellationSnapshot? Cancellation,
        int OldAffectedCount,
        int NewAffectedCount,
        RecalculationSnapshot Recalculation);

    private sealed record SourcePeriodSnapshot(
        Guid PeriodId,
        DateOnly StartDate,
        DateOnly EndDate,
        int InclusiveDays,
        string ReasonCode,
        string? ReasonComment,
        DateTimeOffset CreatedAt,
        Guid? CreatedByAccountId,
        Guid? SessionId,
        string Status)
    {
        internal PeriodRangeValues RangeValues() => new(StartDate, EndDate, InclusiveDays);

        internal PeriodReasonValues ReasonValues() => new(ReasonCode, ReasonComment);
    }

    private sealed record PeriodRangeValues(
        DateOnly StartDate,
        DateOnly EndDate,
        int InclusiveDays);

    private sealed record PeriodReasonValues(string ReasonCode, string? ReasonComment);

    private sealed record ApplicationScopeSnapshot(
        Guid MembershipId,
        Guid ClientId,
        DateOnly StartDate,
        DateOnly EndDate);

    private sealed record CancellationSnapshot(
        Guid CancellationId,
        Guid NonWorkingPeriodId,
        string Reason,
        DateTimeOffset RecordedAt);

    private sealed record RecalculationSnapshot(
        int RequestedCount,
        int SucceededCount,
        IReadOnlySet<Guid> MembershipIds);

    private sealed class BeforeSummaryDto
    {
        public required SourcePeriodDto? Period { get; init; }

        public required List<BeforeApplicationDto?>? Applications { get; init; }

        public required PreviewDto? Preview { get; init; }
    }

    private sealed class AfterSummaryDto
    {
        public required string? Mode { get; init; }

        public required SourcePeriodDto? OriginalPeriod { get; init; }

        public required ReplacementPeriodDto? ReplacementPeriod { get; init; }

        public required List<ReplacementApplicationDto?>? ReplacementApplications { get; init; }

        public required CancellationDto? Cancellation { get; init; }

        public required int OldAffectedCount { get; init; }

        public required int NewAffectedCount { get; init; }

        public required int AffectedUnionCount { get; init; }

        public required RecalculationDto? Recalculation { get; init; }
    }

    private sealed class SourcePeriodDto
    {
        public required Guid PeriodId { get; init; }

        public required DateOnly StartDate { get; init; }

        public required DateOnly EndDate { get; init; }

        public required int InclusiveDays { get; init; }

        public required string? ReasonCode { get; init; }

        public required string? ReasonComment { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public required Guid CreatedByAccountId { get; init; }

        public required Guid SessionId { get; init; }

        public required string? Status { get; init; }
    }

    private sealed class ReplacementPeriodDto
    {
        public required Guid PeriodId { get; init; }

        public required DateOnly StartDate { get; init; }

        public required DateOnly EndDate { get; init; }

        public required int InclusiveDays { get; init; }

        public required string? ReasonCode { get; init; }

        public required string? ReasonComment { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }

        public required string? Status { get; init; }
    }

    private sealed class BeforeApplicationDto
    {
        public required Guid ApplicationId { get; init; }

        public required Guid MembershipId { get; init; }

        public required Guid ClientId { get; init; }

        public required DateOnly StartDate { get; init; }

        public required DateOnly EndDate { get; init; }

        public required DateTimeOffset PreviewedAt { get; init; }

        public required DateTimeOffset ConfirmedAt { get; init; }

        public required string? Status { get; init; }
    }

    private sealed class ReplacementApplicationDto
    {
        public required Guid ApplicationId { get; init; }

        public required Guid MembershipId { get; init; }

        public required Guid ClientId { get; init; }

        public required DateOnly AppliedStartDate { get; init; }

        public required DateOnly AppliedEndDate { get; init; }
    }

    private sealed class PreviewDto
    {
        public required int OldAffectedCount { get; init; }

        public required int NewAffectedCount { get; init; }
    }

    private sealed class CancellationDto
    {
        public required Guid CancellationId { get; init; }

        public required Guid NonWorkingPeriodId { get; init; }

        public required string? Reason { get; init; }

        public required DateTimeOffset RecordedAt { get; init; }
    }

    private sealed class RecalculationDto
    {
        public required int RequestedCount { get; init; }

        public required int SucceededCount { get; init; }

        public required List<Guid>? MembershipIds { get; init; }
    }
}
