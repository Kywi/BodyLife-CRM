using BodyLife.Crm.Infrastructure.Persistence.Memberships;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Infrastructure.Tests.Security;

public sealed class MembershipIssuePreviewTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ValidatesExactMaterialAndRejectsTampering()
    {
        var clock = new MutableTimeProvider(Now);
        var service = CreateService(clock);
        var material = CreateMaterial();
        var token = service.Issue(material).Value;

        Assert.Equal(MembershipIssuePreviewTokenValidationStatus.Valid, service.Validate(token, material).Status);
        var pieces = token.Split('.');
        var tampered = $"{pieces[0]}.{Flip(pieces[1])}.{pieces[2]}";
        Assert.Equal(MembershipIssuePreviewTokenValidationStatus.InvalidToken, service.Validate(tampered, material).Status);
    }

    [Fact]
    public void RejectsExpiredTokenBeforeAcceptingSameMaterial()
    {
        var clock = new MutableTimeProvider(Now);
        var service = CreateService(clock);
        var material = CreateMaterial();
        var issued = service.Issue(material);
        clock.UtcNow = issued.ExpiresAt;

        Assert.Equal(MembershipIssuePreviewTokenValidationStatus.Expired, service.Validate(issued.Value, material).Status);
    }

    [Fact]
    public void RejectsCompleteCandidateSetAndMetadataChanges()
    {
        var clock = new MutableTimeProvider(Now);
        var service = CreateService(clock);
        var material = CreateMaterial();
        var token = service.Issue(material).Value;
        var sameOldestButChangedSecond = material with
        {
            CandidateVisits = [material.CandidateVisits[0], material.CandidateVisits[1] with { OldConsumptionId = Guid.NewGuid() }],
        };
        var changedMetadata = material with { MembershipTypeUpdatedAt = material.MembershipTypeUpdatedAt.AddTicks(1) };

        Assert.Equal(MembershipIssuePreviewTokenValidationStatus.PreviewMismatch, service.Validate(token, sameOldestButChangedSecond).Status);
        Assert.Equal(MembershipIssuePreviewTokenValidationStatus.PreviewMismatch, service.Validate(token, changedMetadata).Status);
    }

    private static HmacMembershipIssuePreviewTokenService CreateService(TimeProvider clock) => new(
        new NonWorkingDayPreviewTokenOptions(Convert.ToBase64String(Enumerable.Repeat((byte)41, 32).ToArray()), TimeSpan.FromMinutes(1)),
        clock);

    private static MembershipIssuePreviewTokenMaterial CreateMaterial()
    {
        var first = Candidate("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", new DateOnly(2026, 8, 1));
        var second = Candidate("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", new DateOnly(2026, 8, 2));
        return new MembershipIssuePreviewTokenMaterial(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Now, new DateOnly(2026, 8, 17), 2, 0, [first, second], 2);
    }

    private static MembershipNegativeVisitCoverageCandidate Candidate(string id, DateOnly date) => new(
        Guid.Parse(id), Guid.NewGuid(), Guid.NewGuid(), date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), date);

    private static string Flip(string value) => (value[0] == 'A' ? "B" : "A") + value[1..];

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
