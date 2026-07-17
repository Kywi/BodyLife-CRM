using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.NonWorkingDays;

public sealed class NonWorkingDayCorrectionConfirmationMaterialTests
{
    private static readonly DateRange OriginalPeriod = new(
        new DateOnly(2026, 1, 30),
        new DateOnly(2026, 2, 2));
    private static readonly DateRange ReplacementPeriod = new(
        new DateOnly(2026, 2, 3),
        new DateOnly(2026, 2, 4));
    private static readonly DateTimeOffset CreatedAt = new(
        2026,
        1,
        20,
        9,
        0,
        0,
        TimeSpan.Zero);
    private static readonly Guid PeriodId = Guid.Parse(
        "01000000-0000-0000-0000-000000000001");
    private static readonly Guid FirstApplicationId = Guid.Parse(
        "11000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondApplicationId = Guid.Parse(
        "11000000-0000-0000-0000-000000000002");

    [Fact]
    public void FactoriesPreserveTheAcceptedModeSpecificScopeShapes()
    {
        var source = CreateSource();
        var rangeInput = new NonWorkingDayPreviewInput(
            ReplacementPeriod,
            "maintenance",
            "Boiler replacement");
        var replacementImpact = CreateReplacementImpact(source);
        var range = NonWorkingDayCorrectionConfirmationMaterial.ForReplaceRange(
            source,
            rangeInput,
            replacementImpact);
        var reasonInput = new NonWorkingDayPreviewInput(
            OriginalPeriod,
            "weather_closure",
            "Corrected explanation");
        var reason = NonWorkingDayCorrectionConfirmationMaterial.ForReplaceReason(
            source,
            reasonInput);
        var cancel = NonWorkingDayCorrectionConfirmationMaterial.ForCancel(source);

        Assert.Equal(PeriodId, range.PeriodId);
        Assert.Equal(NonWorkingDayCorrectionMode.ReplaceRange, range.Mode);
        Assert.Equal(
            NonWorkingDayCorrectionScopeBehavior.RecomputeReplacement,
            range.ScopeBehavior);
        Assert.Same(source, range.OriginalSource);
        Assert.Equal(
            [FirstApplicationId, SecondApplicationId],
            range.OriginalApplicationIds);
        Assert.Same(rangeInput, range.ReplacementInput);
        Assert.Same(replacementImpact.AffectedScope, range.ConfirmedScope);
        Assert.Same(replacementImpact.AffectedScope, range.ReplacementScope);
        Assert.Null(range.PreservedScope);

        Assert.Equal(NonWorkingDayCorrectionMode.ReplaceReason, reason.Mode);
        Assert.Equal(
            NonWorkingDayCorrectionScopeBehavior.PreserveConfirmedApplications,
            reason.ScopeBehavior);
        Assert.Same(reasonInput, reason.ReplacementInput);
        Assert.Null(reason.ReplacementScope);
        Assert.Same(reason.ConfirmedScope, reason.PreservedScope);
        Assert.Equal(OriginalPeriod, reason.PreservedScope!.Period);
        Assert.Equal(
            source.Applications.Select(application => application.MembershipId),
            reason.PreservedScope.AffectedMemberships
                .Select(item => item.MembershipId));
        Assert.Equal(
            source.Applications.Select(application => application.ClientId),
            reason.PreservedScope.AffectedMemberships.Select(item => item.ClientId));
        Assert.All(
            reason.PreservedScope.AffectedMemberships,
            item => Assert.Equal(OriginalPeriod, item.AppliedRange));

        Assert.Equal(NonWorkingDayCorrectionMode.Cancel, cancel.Mode);
        Assert.Equal(
            NonWorkingDayCorrectionScopeBehavior.NoReplacement,
            cancel.ScopeBehavior);
        Assert.Null(cancel.ReplacementInput);
        Assert.Null(cancel.ConfirmedScope);
        Assert.Null(cancel.ReplacementScope);
        Assert.Null(cancel.PreservedScope);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<Guid>)range.OriginalApplicationIds).Add(Guid.NewGuid()));
    }

    [Fact]
    public void RangeReplacementRequiresExactOldSourceExclusionAndNewPeriod()
    {
        var source = CreateSource();
        var input = new NonWorkingDayPreviewInput(
            ReplacementPeriod,
            "maintenance");

        Assert.Throws<ArgumentException>(() =>
            NonWorkingDayCorrectionConfirmationMaterial.ForReplaceRange(
                source,
                input,
                CreateReplacementImpact(
                    source,
                    replacedPeriodId: Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() =>
            NonWorkingDayCorrectionConfirmationMaterial.ForReplaceRange(
                source,
                input,
                CreateReplacementImpact(
                    source,
                    excludedApplicationIds: [FirstApplicationId])));
        Assert.Throws<ArgumentException>(() =>
            NonWorkingDayCorrectionConfirmationMaterial.ForReplaceRange(
                source,
                new NonWorkingDayPreviewInput(
                    new DateRange(
                        ReplacementPeriod.StartDate,
                        ReplacementPeriod.EndDate.AddDays(1)),
                    "maintenance"),
                CreateReplacementImpact(source)));
    }

    [Fact]
    public void ReasonReplacementPreservesPeriodAndEveryModeRequiresActiveSource()
    {
        var activeSource = CreateSource();
        var canceledSource = CreateSource(NonWorkingDayCorrectionSourceStatus.Canceled);

        Assert.Throws<ArgumentException>(() =>
            NonWorkingDayCorrectionConfirmationMaterial.ForReplaceReason(
                activeSource,
                new NonWorkingDayPreviewInput(
                    ReplacementPeriod,
                    "weather_closure")));
        Assert.Throws<ArgumentException>(() =>
            NonWorkingDayCorrectionConfirmationMaterial.ForReplaceRange(
                canceledSource,
                new NonWorkingDayPreviewInput(ReplacementPeriod, "maintenance"),
                CreateReplacementImpact(canceledSource)));
        Assert.Throws<ArgumentException>(() =>
            NonWorkingDayCorrectionConfirmationMaterial.ForReplaceReason(
                canceledSource,
                new NonWorkingDayPreviewInput(
                    OriginalPeriod,
                    "weather_closure")));
        Assert.Throws<ArgumentException>(() =>
            NonWorkingDayCorrectionConfirmationMaterial.ForCancel(canceledSource));
    }

    [Fact]
    public void ConfirmationAndValidationRequireCanonicalAuthenticatedMetadata()
    {
        var issuedAt = CreatedAt;
        var expiresAt = issuedAt.AddMinutes(5);
        var fingerprint = new string('A',
            NonWorkingDayCorrectionConfirmation.FingerprintLength);
        var confirmation = new NonWorkingDayCorrectionConfirmation(
            "bodylife-nwd-correction-v1.payload.signature",
            fingerprint,
            issuedAt,
            expiresAt);
        var validation = NonWorkingDayCorrectionTokenValidation.FromAuthenticatedToken(
            NonWorkingDayCorrectionTokenValidationStatus.Valid,
            fingerprint,
            issuedAt,
            expiresAt);

        Assert.Equal(fingerprint, confirmation.ConfirmationFingerprint);
        Assert.True(validation.IsValid);
        Assert.Equal(fingerprint, validation.ConfirmationFingerprint);
        Assert.Throws<ArgumentException>(() =>
            new NonWorkingDayCorrectionConfirmation(
                "token",
                fingerprint.ToLowerInvariant(),
                issuedAt,
                expiresAt));
        Assert.Throws<ArgumentException>(() =>
            new NonWorkingDayCorrectionConfirmation(
                "token",
                fingerprint,
                issuedAt.ToOffset(TimeSpan.FromHours(2)),
                expiresAt));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NonWorkingDayCorrectionTokenValidation.FromAuthenticatedToken(
                NonWorkingDayCorrectionTokenValidationStatus.InvalidToken,
                fingerprint,
                issuedAt,
                expiresAt));
    }

    private static NonWorkingDayCorrectionSource CreateSource(
        NonWorkingDayCorrectionSourceStatus status =
            NonWorkingDayCorrectionSourceStatus.Active)
    {
        var previewedAt = CreatedAt.AddMinutes(-5);
        var applications = new[]
        {
            new NonWorkingDayCorrectionApplicationSource(
                SecondApplicationId,
                Guid.Parse("21000000-0000-0000-0000-000000000001"),
                Guid.Parse("31000000-0000-0000-0000-000000000001"),
                OriginalPeriod,
                previewedAt,
                CreatedAt,
                status),
            new NonWorkingDayCorrectionApplicationSource(
                FirstApplicationId,
                Guid.Parse("21000000-0000-0000-0000-000000000002"),
                Guid.Parse("31000000-0000-0000-0000-000000000002"),
                OriginalPeriod,
                previewedAt,
                CreatedAt,
                status),
        };

        return new NonWorkingDayCorrectionSource(
            PeriodId,
            OriginalPeriod,
            "weather_closure",
            "Severe weather",
            CreatedAt,
            Guid.Parse("41000000-0000-0000-0000-000000000001"),
            Guid.Parse("51000000-0000-0000-0000-000000000001"),
            status,
            applications,
            status == NonWorkingDayCorrectionSourceStatus.Canceled
                ? Guid.Parse("61000000-0000-0000-0000-000000000001")
                : null);
    }

    private static MembershipNonWorkingDayReplacementImpactPreparation
        CreateReplacementImpact(
            NonWorkingDayCorrectionSource source,
            Guid? replacedPeriodId = null,
            IEnumerable<Guid>? excludedApplicationIds = null)
    {
        var membershipId = Guid.Parse(
            "71000000-0000-0000-0000-000000000001");
        var clientId = Guid.Parse(
            "81000000-0000-0000-0000-000000000001");
        var scope = new MembershipNonWorkingDayAffectedScope(
            ReplacementPeriod,
            [
                new MembershipNonWorkingDayAffectedScopeItem(
                    membershipId,
                    clientId,
                    ReplacementPeriod),
            ]);
        var terms = MembershipIssueTerms.FromIssuedSnapshot(
            Guid.Parse("91000000-0000-0000-0000-000000000001"),
            new IssuedMembershipSnapshot(
                "Replacement fixture",
                durationDays: 30,
                visitsLimit: 8,
                new Money(1000m, "UAH")),
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 3, 2));
        var currentState = MembershipCalculatedState.FromStoredCache(
            terms,
            countedVisits: 0,
            remainingVisits: 8,
            negativeBalance: 0,
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            extensionDays: 0,
            effectiveEndDate: terms.BaseEndDate,
            lastCountedVisitAt: null);
        var estimate = MembershipNonWorkingDayImpactEstimator.Estimate(
            currentState,
            currentDateRangeExtensions: null,
            ReplacementPeriod);
        var impact = new MembershipNonWorkingDayImpactPreparation(
            scope,
            [
                new MembershipNonWorkingDayImpactItem(
                    membershipId,
                    clientId,
                    ReplacementPeriod,
                    estimate),
            ]);

        return new MembershipNonWorkingDayReplacementImpactPreparation(
            replacedPeriodId ?? source.PeriodId,
            excludedApplicationIds
                ?? source.Applications
                    .Select(application => application.ApplicationId)
                    .Order(),
            impact);
    }
}
