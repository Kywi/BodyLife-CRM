using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;

namespace BodyLife.Crm.Tests.Modules.NonWorkingDays;

public sealed class CorrectNonWorkingDaySourceContractsTests
{
    private static readonly DateRange Period = new(
        new DateOnly(2026, 1, 30),
        new DateOnly(2026, 2, 2));
    private static readonly DateTimeOffset PreviewedAt = new(
        2026,
        1,
        20,
        10,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateTimeOffset ConfirmedAt = PreviewedAt.AddMinutes(5);

    [Fact]
    public void CorrectionModesStatusesAndScopeBehaviorsHaveStableValues()
    {
        Assert.Equal(1, (int)NonWorkingDayCorrectionMode.ReplaceRange);
        Assert.Equal(2, (int)NonWorkingDayCorrectionMode.ReplaceReason);
        Assert.Equal(3, (int)NonWorkingDayCorrectionMode.Cancel);
        Assert.Equal(1, (int)NonWorkingDayCorrectionSourceStatus.Active);
        Assert.Equal(2, (int)NonWorkingDayCorrectionSourceStatus.Canceled);
        Assert.Equal(3, (int)NonWorkingDayCorrectionSourceStatus.Corrected);
        Assert.Equal(1, (int)NonWorkingDayCorrectionScopeBehavior.RecomputeReplacement);
        Assert.Equal(
            2,
            (int)NonWorkingDayCorrectionScopeBehavior.PreserveConfirmedApplications);
        Assert.Equal(3, (int)NonWorkingDayCorrectionScopeBehavior.NoReplacement);
    }

    [Fact]
    public void CorrectionPolicyMapsEveryModeToItsAcceptedScopeSemantics()
    {
        Assert.Equal(
            NonWorkingDayCorrectionScopeBehavior.RecomputeReplacement,
            NonWorkingDayCorrectionPolicy.GetScopeBehavior(
                NonWorkingDayCorrectionMode.ReplaceRange));
        Assert.Equal(
            NonWorkingDayCorrectionScopeBehavior.PreserveConfirmedApplications,
            NonWorkingDayCorrectionPolicy.GetScopeBehavior(
                NonWorkingDayCorrectionMode.ReplaceReason));
        Assert.Equal(
            NonWorkingDayCorrectionScopeBehavior.NoReplacement,
            NonWorkingDayCorrectionPolicy.GetScopeBehavior(
                NonWorkingDayCorrectionMode.Cancel));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NonWorkingDayCorrectionPolicy.GetScopeBehavior(
                (NonWorkingDayCorrectionMode)99));
    }

    [Fact]
    public void SourcePreservesAnImmutableDeterministicallyOrderedSnapshot()
    {
        var first = CreateApplication(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"));
        var second = CreateApplication(
            Guid.Parse("10000000-0000-0000-0000-000000000002"),
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            Guid.Parse("30000000-0000-0000-0000-000000000002"));
        var applications = new[] { first, second };

        var source = CreateSource(applications);
        applications[0] = second;

        Assert.Equal([first.ApplicationId, second.ApplicationId],
            source.Applications.Select(application => application.ApplicationId));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<NonWorkingDayCorrectionApplicationSource>)source.Applications)
                .Add(first));
    }

    [Fact]
    public void SourceRejectsAChangedOrInconsistentConfirmedSnapshot()
    {
        var first = CreateApplication(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("30000000-0000-0000-0000-000000000001"));
        var second = CreateApplication(
            Guid.Parse("10000000-0000-0000-0000-000000000002"),
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            Guid.Parse("30000000-0000-0000-0000-000000000002"));

        Assert.Throws<ArgumentException>(() => CreateSource([second, first]));
        Assert.Throws<ArgumentException>(() => CreateSource([
            first,
            CreateApplication(
                second.ApplicationId,
                first.MembershipId,
                second.ClientId),
        ]));
        Assert.Throws<ArgumentException>(() => CreateSource([
            CreateApplication(
                first.ApplicationId,
                first.MembershipId,
                first.ClientId,
                new DateRange(Period.StartDate, Period.EndDate.AddDays(1))),
        ]));
        Assert.Throws<ArgumentException>(() => CreateSource([
            CreateApplication(
                first.ApplicationId,
                first.MembershipId,
                first.ClientId,
                status: NonWorkingDayCorrectionSourceStatus.Corrected),
        ]));
        Assert.Throws<ArgumentException>(() => CreateSource(
            [first],
            NonWorkingDayCorrectionSourceStatus.Canceled,
            existingCancellationId: null));
        Assert.Throws<ArgumentException>(() => CreateSource(
            [CreateApplication(
                first.ApplicationId,
                first.MembershipId,
                first.ClientId,
                status: NonWorkingDayCorrectionSourceStatus.Canceled)],
            NonWorkingDayCorrectionSourceStatus.Canceled,
            Guid.Empty));
    }

    [Fact]
    public void ApplicationRejectsConfirmationBeforePreview()
    {
        Assert.Throws<ArgumentException>(() =>
            new NonWorkingDayCorrectionApplicationSource(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Period,
                ConfirmedAt,
                PreviewedAt,
                NonWorkingDayCorrectionSourceStatus.Active));
    }

    private static NonWorkingDayCorrectionSource CreateSource(
        IEnumerable<NonWorkingDayCorrectionApplicationSource> applications,
        NonWorkingDayCorrectionSourceStatus status =
            NonWorkingDayCorrectionSourceStatus.Active,
        Guid? existingCancellationId = null)
    {
        return new NonWorkingDayCorrectionSource(
            Guid.NewGuid(),
            Period,
            "weather_closure",
            "Severe weather",
            ConfirmedAt,
            Guid.NewGuid(),
            Guid.NewGuid(),
            status,
            applications,
            existingCancellationId);
    }

    private static NonWorkingDayCorrectionApplicationSource CreateApplication(
        Guid applicationId,
        Guid membershipId,
        Guid clientId,
        DateRange? range = null,
        NonWorkingDayCorrectionSourceStatus status =
            NonWorkingDayCorrectionSourceStatus.Active)
    {
        return new NonWorkingDayCorrectionApplicationSource(
            applicationId,
            membershipId,
            clientId,
            range ?? Period,
            PreviewedAt,
            ConfirmedAt,
            status);
    }
}
