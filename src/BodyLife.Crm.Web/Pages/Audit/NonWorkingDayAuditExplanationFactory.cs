using System.Text.Json;
using BodyLife.Crm.Modules.Audit;

namespace BodyLife.Crm.Web.Pages.Audit;

public sealed class NonWorkingDayAuditExplanationFactory(AuditPresentation presentation)
{
    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);

    internal AuditEntryExplanationViewModel CreateAddition(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        var relatedDto = Deserialize<AdditionRelatedDto>(related);
        var beforeDto = Deserialize<AdditionBeforeSummaryDto>(before);
        var afterDto = Deserialize<AdditionAfterSummaryDto>(after);
        var preview = beforeDto.Preview
            ?? throw new JsonException("The non-working day preview summary is required.");
        var period = afterDto.Period is null
            ? throw new JsonException("The added non-working period is required.")
            : ReadReplacementPeriod(afterDto.Period);
        var applications = ReadReplacementApplications(afterDto.Applications);
        var recalculation = ReadRecalculation(afterDto.Recalculation);
        var relatedMembershipIds = ReadIdList(
            relatedDto.AffectedMembershipIds,
            "affectedMembershipIds",
            requireDistinct: true);
        var relatedClientIds = ReadIdList(
            relatedDto.AffectedClientIds,
            "affectedClientIds",
            requireDistinct: false);
        var scopeFingerprint = RequireText(
            preview.ScopeFingerprint,
            "scopeFingerprint");
        var previewIssuedAt = RequireTimestamp(
            preview.IssuedAt ?? default,
            "issuedAt");
        var previewExpiresAt = RequireTimestamp(
            preview.ExpiresAt ?? default,
            "expiresAt");
        var applicationMembershipIds = applications
            .Select(application => application.MembershipId)
            .ToArray();
        var applicationClientIds = applications
            .Select(application => application.ClientId)
            .ToArray();
        var applicationMembershipSet = applicationMembershipIds.ToHashSet();

        if (entry.EntityId == Guid.Empty
            || period.PeriodId != entry.EntityId
            || period.Status != "active"
            || period.CreatedAt != entry.RecordedAt
            || period.InclusiveDays != period.EndDate.DayNumber - period.StartDate.DayNumber + 1
            || preview.AffectedCount < 0
            || preview.AffectedCount != applications.Count
            || afterDto.AffectedMembershipCount != applications.Count
            || previewIssuedAt > entry.RecordedAt
            || previewExpiresAt < entry.RecordedAt
            || previewExpiresAt <= previewIssuedAt
            || applications.Any(application =>
                application.StartDate != period.StartDate
                || application.EndDate != period.EndDate)
            || !relatedMembershipIds.SequenceEqual(applicationMembershipIds)
            || !relatedClientIds.SequenceEqual(applicationClientIds)
            || recalculation.RequestedCount != applications.Count
            || recalculation.SucceededCount != applications.Count
            || !applicationMembershipSet.SetEquals(recalculation.MembershipIds))
        {
            throw new JsonException("Added non-working day scope is inconsistent.");
        }

        var applicationDetails = applications.Count == 0
            ? presentation.Value("None")
            : string.Join(
                "; ",
                applications.Select(application =>
                    presentation.Text(
                        "Template.NonWorkingApplicationDetail",
                        presentation.ShortId(application.MembershipId),
                        presentation.ShortId(application.ClientId),
                        presentation.Date(application.StartDate),
                        presentation.Date(application.EndDate))));
        var previewScopeLabel = scopeFingerprint.Length <= 12
            ? scopeFingerprint
            : scopeFingerprint[..12];

        return new AuditEntryExplanationViewModel(
            "non-working-day-added",
            presentation.Explanation("NonWorkingDayAdded.Title"),
            presentation.Explanation("NonWorkingDayAdded.Narrative"),
            presentation.Explanation("NonWorkingDayAdded.Before"),
            presentation.Explanation("NonWorkingDayAdded.After"),
            [
                Fact("Preview scope", previewScopeLabel),
                Fact("Preview issued", presentation.Timestamp(previewIssuedAt)),
                Fact("Preview expires", presentation.Timestamp(previewExpiresAt)),
                Fact(
                    "Affected memberships",
                    presentation.Number(preview.AffectedCount)),
            ],
            [
                Fact("Non-working period", presentation.ShortId(period.PeriodId)),
                .. PeriodFacts(period, applications.Count, "Active"),
                Fact("Application details", applicationDetails),
                Fact("Recalculated memberships", RecalculationLabel(recalculation)),
                Fact("Recorded", presentation.Timestamp(period.CreatedAt)),
            ],
            ChangedFields: JoinChanged("NonWorkingPeriod", "ConfirmedAffectedScope"),
            IsAvailable: true);
    }

    internal AuditEntryExplanationViewModel CreateCorrection(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        var summary = ReadSummary(
            entry,
            related,
            before,
            after,
            expectedOriginalStatus: "corrected");
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
        AddChangedField(changedFields, rangeChanged, "DateRange");
        AddChangedField(changedFields, reasonChanged, "Reason");
        AddChangedField(changedFields, scopeChanged, "AffectedScope");
        if (changedFields.Count == 0)
        {
            throw new JsonException("Non-working day correction did not change business values.");
        }

        return new AuditEntryExplanationViewModel(
            "non-working-day-corrected",
            presentation.Explanation("NonWorkingDayCorrected.Title"),
            presentation.Explanation("NonWorkingDayCorrected.Narrative"),
            presentation.Explanation("NonWorkingDayCorrected.Before"),
            presentation.Explanation("NonWorkingDayCorrected.After"),
            [
                .. PeriodFacts(summary.BeforePeriod, summary.OldAffectedCount, "Active"),
                Fact("Application details", ApplicationDetails(summary.BeforeApplications)),
            ],
            [
                Fact("Correction type", CorrectionModeLabel(summary.Mode)),
                Fact("Original status", presentation.Status("Corrected")),
                .. PeriodFacts(replacement, summary.NewAffectedCount, "Active"),
                Fact(
                    "Application details",
                    ApplicationDetails(summary.ReplacementApplications)),
                Fact("Recalculated memberships", RecalculationLabel(summary.Recalculation)),
            ],
            string.Join(", ", changedFields.Select(presentation.Changed)),
            IsAvailable: true);
    }

    internal AuditEntryExplanationViewModel CreateCancellation(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after)
    {
        var summary = ReadSummary(
            entry,
            related,
            before,
            after,
            expectedOriginalStatus: "canceled");
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
            presentation.Explanation("NonWorkingDayCanceled.Title"),
            presentation.Explanation("NonWorkingDayCanceled.Narrative"),
            presentation.Explanation("NonWorkingDayCanceled.Before"),
            presentation.Explanation("NonWorkingDayCanceled.After"),
            [
                .. PeriodFacts(summary.BeforePeriod, summary.OldAffectedCount, "Active"),
                Fact("Application details", ApplicationDetails(summary.BeforeApplications)),
            ],
            [
                Fact("Original fact", presentation.Value("Preserved")),
                Fact("Period", DateRangeLabel(summary.BeforePeriod)),
                Fact("Status", presentation.Status("Canceled")),
                Fact("Active applications", presentation.Number(0)),
                Fact("Recalculated memberships", RecalculationLabel(summary.Recalculation)),
                Fact("Canceled", presentation.Timestamp(cancellation.RecordedAt)),
            ],
            ChangedFields: JoinChanged("PeriodStatus", "ActiveAffectedScope"),
            IsAvailable: true);
    }

    private static NonWorkingDaySummary ReadSummary(
        AuditTimelineEntry entry,
        JsonElement related,
        JsonElement before,
        JsonElement after,
        string expectedOriginalStatus)
    {
        var relatedDto = Deserialize<CorrectionRelatedDto>(related);
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
        var oldMembershipIds = beforeApplications
            .Select(application => application.MembershipId)
            .ToArray();
        var newMembershipIds = replacementApplications
            .Select(application => application.MembershipId)
            .ToArray();
        var affectedMembershipIds = OrderedDistinctUnion(oldMembershipIds, newMembershipIds);
        var affectedClientIds = OrderedDistinctUnion(
            beforeApplications.Select(application => application.ClientId),
            replacementApplications.Select(application => application.ClientId));
        var relatedOldMembershipIds = ReadIdList(
            relatedDto.OldMembershipIds,
            "oldMembershipIds",
            requireDistinct: true);
        var relatedNewMembershipIds = ReadIdList(
            relatedDto.NewMembershipIds,
            "newMembershipIds",
            requireDistinct: true);
        var relatedAffectedMembershipIds = ReadIdList(
            relatedDto.AffectedMembershipIds,
            "affectedMembershipIds",
            requireDistinct: true);
        var relatedAffectedClientIds = ReadIdList(
            relatedDto.AffectedClientIds,
            "affectedClientIds",
            requireDistinct: true);
        var confirmationFingerprint = RequireText(
            preview.ConfirmationFingerprint,
            "confirmationFingerprint");
        var previewIssuedAt = RequireTimestamp(preview.IssuedAt ?? default, "issuedAt");
        var previewExpiresAt = RequireTimestamp(preview.ExpiresAt ?? default, "expiresAt");

        if (preview.OldAffectedCount < 0
            || preview.NewAffectedCount < 0
            || afterDto.OldAffectedCount < 0
            || afterDto.NewAffectedCount < 0
            || afterDto.AffectedUnionCount < 0
            || beforePeriod.PeriodId != entry.EntityId
            || afterOriginalPeriod.PeriodId != entry.EntityId
            || relatedDto.OriginalPeriodId != entry.EntityId
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
            || afterDto.AffectedUnionCount != affectedMembershipIds.Length
            || recalculation.RequestedCount != afterDto.AffectedUnionCount
            || recalculation.SucceededCount != afterDto.AffectedUnionCount
            || !affectedMembershipIds.ToHashSet().SetEquals(recalculation.MembershipIds)
            || !relatedOldMembershipIds.SequenceEqual(oldMembershipIds)
            || !relatedNewMembershipIds.SequenceEqual(newMembershipIds)
            || !relatedAffectedMembershipIds.SequenceEqual(affectedMembershipIds)
            || !relatedAffectedClientIds.SequenceEqual(affectedClientIds)
            || string.IsNullOrWhiteSpace(confirmationFingerprint)
            || previewIssuedAt > entry.RecordedAt
            || previewExpiresAt < entry.RecordedAt
            || previewExpiresAt <= previewIssuedAt)
        {
            throw new JsonException("Non-working day affected scope is inconsistent.");
        }

        if (expectedOriginalStatus == "corrected"
            ? relatedDto.ReplacementPeriodId != replacementPeriod?.PeriodId
                || relatedDto.CancellationId is not null
            : relatedDto.ReplacementPeriodId is not null
                || relatedDto.CancellationId != cancellation?.CancellationId)
        {
            throw new JsonException("Non-working day correction references are inconsistent.");
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

    private static IReadOnlyList<Guid> ReadIdList(
        IReadOnlyList<Guid>? ids,
        string propertyName,
        bool requireDistinct)
    {
        ids = ids
            ?? throw new JsonException(
                $"Audit summary property '{propertyName}' is required.");
        if (ids.Any(id => id == Guid.Empty)
            || (requireDistinct && ids.Distinct().Count() != ids.Count))
        {
            throw new JsonException(
                $"Audit summary property '{propertyName}' is inconsistent.");
        }

        return ids;
    }

    private IReadOnlyList<AuditEntryExplanationFactViewModel> PeriodFacts(
        SourcePeriodSnapshot period,
        int affectedCount,
        string statusLabel)
    {
        return
        [
            Fact("Period", DateRangeLabel(period)),
            Fact("Inclusive days", presentation.Days(period.InclusiveDays)),
            Fact("Reason code", period.ReasonCode),
            Fact("Reason comment", period.ReasonComment ?? presentation.Value("None")),
            Fact("Affected memberships", presentation.Number(affectedCount)),
            Fact("Status", presentation.Status(statusLabel)),
        ];
    }

    private string ApplicationDetails(IReadOnlyList<ApplicationScopeSnapshot> applications)
    {
        return applications.Count == 0
            ? presentation.Value("None")
            : string.Join(
                "; ",
                applications.Select(application =>
                    presentation.Text(
                        "Template.NonWorkingApplicationDetail",
                        presentation.ShortId(application.MembershipId),
                        presentation.ShortId(application.ClientId),
                        presentation.Date(application.StartDate),
                        presentation.Date(application.EndDate))));
    }

    private static Guid[] OrderedDistinctUnion(
        IEnumerable<Guid> first,
        IEnumerable<Guid> second)
    {
        return first.Concat(second).Distinct().Order().ToArray();
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

    private string DateRangeLabel(SourcePeriodSnapshot period) =>
        presentation.Text(
            "Template.DateRange",
            presentation.Date(period.StartDate),
            presentation.Date(period.EndDate));

    private string RecalculationLabel(RecalculationSnapshot recalculation) =>
        presentation.Text(
            "Template.RecalculationCount",
            presentation.Number(recalculation.SucceededCount),
            presentation.Number(recalculation.RequestedCount));

    private string CorrectionModeLabel(string mode) => mode switch
    {
        "replace_range" => presentation.Text("CorrectionMode.ReplaceRange"),
        "replace_reason" => presentation.Text("CorrectionMode.ReplaceReason"),
        _ => throw new JsonException("Correction mode is not supported."),
    };

    private string JoinChanged(params string[] keys) =>
        string.Join(", ", keys.Select(presentation.Changed));

    private AuditEntryExplanationFactViewModel Fact(string label, string value)
    {
        var semanticKey = label switch
        {
            "Preview scope" => "PreviewScope",
            "Preview issued" => "PreviewIssued",
            "Preview expires" => "PreviewExpires",
            "Affected memberships" => "AffectedMemberships",
            "Non-working period" => "NonWorkingPeriod",
            "Application details" => "ApplicationDetails",
            "Recalculated memberships" => "RecalculatedMemberships",
            "Recorded" => "Recorded",
            "Period" => "Period",
            "Inclusive days" => "InclusiveDays",
            "Reason code" => "ReasonCode",
            "Reason comment" => "ReasonComment",
            "Status" => "Status",
            "Correction type" => "CorrectionType",
            "Original status" => "OriginalStatus",
            "Original fact" => "OriginalFact",
            "Active applications" => "ActiveApplications",
            "Canceled" => "Canceled",
            _ => throw new InvalidOperationException($"Unsupported non-working-day audit fact label '{label}'."),
        };
        return new AuditEntryExplanationFactViewModel(presentation.Fact(semanticKey), value);
    }

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

    private sealed class AdditionRelatedDto
    {
        public required List<Guid>? AffectedMembershipIds { get; init; }

        public required List<Guid>? AffectedClientIds { get; init; }
    }

    private sealed class AdditionBeforeSummaryDto
    {
        public required AdditionPreviewDto? Preview { get; init; }
    }

    private sealed class AdditionAfterSummaryDto
    {
        public required ReplacementPeriodDto? Period { get; init; }

        public required int AffectedMembershipCount { get; init; }

        public required List<ReplacementApplicationDto?>? Applications { get; init; }

        public required RecalculationDto? Recalculation { get; init; }
    }

    private sealed class AdditionPreviewDto
    {
        public required string? ScopeFingerprint { get; init; }

        public required DateTimeOffset? IssuedAt { get; init; }

        public required DateTimeOffset? ExpiresAt { get; init; }

        public required int AffectedCount { get; init; }
    }

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
        public required string? ConfirmationFingerprint { get; init; }

        public required DateTimeOffset? IssuedAt { get; init; }

        public required DateTimeOffset? ExpiresAt { get; init; }

        public required int OldAffectedCount { get; init; }

        public required int NewAffectedCount { get; init; }
    }

    private sealed class CorrectionRelatedDto
    {
        public required Guid OriginalPeriodId { get; init; }

        public required Guid? ReplacementPeriodId { get; init; }

        public required Guid? CancellationId { get; init; }

        public required List<Guid>? OldMembershipIds { get; init; }

        public required List<Guid>? NewMembershipIds { get; init; }

        public required List<Guid>? AffectedMembershipIds { get; init; }

        public required List<Guid>? AffectedClientIds { get; init; }
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
