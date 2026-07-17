using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BodyLife.Crm.Infrastructure.Tests.Security;

public sealed class NonWorkingDayCorrectionTokenServiceTests
{
    private static readonly DateTimeOffset TestNow = new(
        2026,
        7,
        17,
        12,
        0,
        0,
        TimeSpan.Zero);
    private static readonly DateRange OriginalPeriod = new(
        new DateOnly(2026, 7, 20),
        new DateOnly(2026, 7, 22));
    private static readonly DateRange ReplacementPeriod = new(
        new DateOnly(2026, 7, 24),
        new DateOnly(2026, 7, 25));

    [Fact]
    public void IssueIsDeterministicForCanonicalMaterialAndSeparatesEveryMode()
    {
        var clock = new MutableTimeProvider(TestNow);
        var correctionService = CreateCorrectionService(clock);
        var previewService = CreatePreviewService(clock);
        var source = CreateSource();
        var canonicalInput = new NonWorkingDayPreviewInput(
            ReplacementPeriod,
            "caf\u00E9",
            "Temporary closure");
        var equivalentInput = new NonWorkingDayPreviewInput(
            ReplacementPeriod,
            "  cafe\u0301  ",
            "  Temporary closure  ");
        var firstMaterial = CreateRangeMaterial(source, canonicalInput);
        var equivalentMaterial = CreateRangeMaterial(source, equivalentInput);
        var reasonMaterial = CreateReasonMaterial(source);
        var cancelMaterial =
            NonWorkingDayCorrectionConfirmationMaterial.ForCancel(source);

        var first = correctionService.Issue(firstMaterial);
        var equivalent = correctionService.Issue(equivalentMaterial);
        var reason = correctionService.Issue(reasonMaterial);
        var cancel = correctionService.Issue(cancelMaterial);

        Assert.Equal(first.ConfirmationToken, equivalent.ConfirmationToken);
        Assert.Equal(
            first.ConfirmationFingerprint,
            equivalent.ConfirmationFingerprint);
        Assert.StartsWith(
            "bodylife-nwd-correction-v1.",
            first.ConfirmationToken,
            StringComparison.Ordinal);
        Assert.Matches("^[0-9A-F]{64}$", first.ConfirmationFingerprint);
        Assert.Equal(TestNow, first.IssuedAt);
        Assert.Equal(TestNow.AddMinutes(5), first.ExpiresAt);
        Assert.Equal(
            3,
            new[]
            {
                first.ConfirmationFingerprint,
                reason.ConfirmationFingerprint,
                cancel.ConfirmationFingerprint,
            }.Distinct(StringComparer.Ordinal).Count());

        AssertValid(correctionService.Validate(first.ConfirmationToken, firstMaterial));
        AssertValid(correctionService.Validate(reason.ConfirmationToken, reasonMaterial));
        AssertValid(correctionService.Validate(cancel.ConfirmationToken, cancelMaterial));

        var addToken = previewService.Issue(
            canonicalInput,
            firstMaterial.ReplacementScope!);
        AssertInvalid(correctionService.Validate(
            addToken.ConfirmationToken,
            firstMaterial));
        Assert.Equal(
            NonWorkingDayPreviewTokenValidationStatus.InvalidToken,
            previewService.Validate(
                first.ConfirmationToken,
                canonicalInput,
                firstMaterial.ReplacementScope!).Status);
    }

    [Fact]
    public void ValidateRejectsTamperingMalformedTokensAndWrongSigningKey()
    {
        var clock = new MutableTimeProvider(TestNow);
        var service = CreateCorrectionService(clock);
        var material = CreateRangeMaterial();
        var token = service.Issue(material).ConfirmationToken;
        var segments = token.Split('.');
        var tamperedPayload =
            $"{segments[0]}.{FlipFirstCharacter(segments[1])}.{segments[2]}";
        var tamperedSignature =
            $"{segments[0]}.{segments[1]}.{FlipFirstCharacter(segments[2])}";

        AssertInvalid(service.Validate(tamperedPayload, material));
        AssertInvalid(service.Validate(tamperedSignature, material));
        AssertInvalid(CreateCorrectionService(clock, keySeed: 31).Validate(
            token,
            material));

        foreach (var malformed in new string?[]
                 {
                     null,
                     string.Empty,
                     " ",
                     token + " ",
                     "wrong-prefix.payload.signature",
                     new('A', NonWorkingDayCorrectionConfirmation.MaxTokenLength + 1),
                 })
        {
            AssertInvalid(service.Validate(malformed, material));
        }
    }

    [Fact]
    public void ValidateUsesExactExpiryBoundaryBeforeCurrentMaterialComparison()
    {
        var clock = new MutableTimeProvider(TestNow);
        var service = CreateCorrectionService(clock);
        var material = CreateRangeMaterial();
        var confirmation = service.Issue(material);

        clock.UtcNow = confirmation.IssuedAt.AddMilliseconds(-1);
        AssertInvalid(service.Validate(confirmation.ConfirmationToken, material));

        clock.UtcNow = confirmation.ExpiresAt.AddMilliseconds(-1);
        AssertValid(service.Validate(confirmation.ConfirmationToken, material));

        clock.UtcNow = confirmation.ExpiresAt;
        var expired = service.Validate(
            confirmation.ConfirmationToken,
            CreateRangeMaterial(
                replacementInput: new NonWorkingDayPreviewInput(
                    ReplacementPeriod,
                    "maintenance",
                    "Changed after preview")));

        Assert.Equal(
            NonWorkingDayCorrectionTokenValidationStatus.Expired,
            expired.Status);
        Assert.False(expired.IsValid);
        Assert.Equal(
            confirmation.ConfirmationFingerprint,
            expired.ConfirmationFingerprint);
        Assert.Equal(confirmation.ExpiresAt, expired.ExpiresAt);
    }

    [Fact]
    public void ValidateDetectsEveryBoundSourceInputScopeAndModeChange()
    {
        var clock = new MutableTimeProvider(TestNow);
        var service = CreateCorrectionService(clock);
        var material = CreateRangeMaterial();
        var token = service.Issue(material).ConfirmationToken;
        var changedPeriod = new DateRange(
            ReplacementPeriod.StartDate,
            ReplacementPeriod.EndDate.AddDays(1));
        var changedMaterials = new[]
        {
            CreateRangeMaterial(CreateSource(periodId: Guid.NewGuid())),
            CreateRangeMaterial(CreateSource(reasonComment: "Changed source reason")),
            CreateRangeMaterial(CreateSource(applicationId: Guid.NewGuid())),
            CreateRangeMaterial(CreateSource(membershipId: Guid.NewGuid())),
            CreateRangeMaterial(CreateSource(clientId: Guid.NewGuid())),
            CreateRangeMaterial(CreateSource(confirmedAt: TestNow.AddMinutes(1))),
            CreateRangeMaterial(
                replacementInput: new NonWorkingDayPreviewInput(
                    ReplacementPeriod,
                    "maintenance",
                    "Changed replacement reason")),
            CreateRangeMaterial(
                replacementInput: new NonWorkingDayPreviewInput(
                    changedPeriod,
                    "maintenance")),
            CreateRangeMaterial(replacementMembershipId: Guid.NewGuid()),
            CreateReasonMaterial(),
            NonWorkingDayCorrectionConfirmationMaterial.ForCancel(CreateSource()),
        };

        Assert.All(
            changedMaterials,
            changedMaterial =>
            {
                var validation = service.Validate(token, changedMaterial);
                Assert.Equal(
                    NonWorkingDayCorrectionTokenValidationStatus
                        .ConfirmationMaterialMismatch,
                    validation.Status);
                Assert.False(validation.IsValid);
                Assert.NotNull(validation.ConfirmationFingerprint);
            });
    }

    [Fact]
    public void PersistenceRegistrationResolvesLazyConfiguredSingleton()
    {
        var clock = new MutableTimeProvider(TestNow);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddBodyLifePersistence(
            PersistenceConfiguration(includeSigningKey: true));

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<INonWorkingDayCorrectionTokenService>();
        var second = provider.GetRequiredService<INonWorkingDayCorrectionTokenService>();

        Assert.IsType<HmacNonWorkingDayCorrectionTokenService>(first);
        Assert.Same(first, second);
        Assert.NotSame(
            first,
            provider.GetRequiredService<INonWorkingDayPreviewTokenService>());

        var missingSecretServices = new ServiceCollection();
        var registrationException = Record.Exception(() =>
            missingSecretServices.AddBodyLifePersistence(
                PersistenceConfiguration(includeSigningKey: false)));

        Assert.Null(registrationException);
        using var missingSecretProvider = missingSecretServices.BuildServiceProvider();
        Assert.Contains(
            SigningKeyPath,
            Assert.Throws<InvalidOperationException>(() =>
                missingSecretProvider
                    .GetRequiredService<INonWorkingDayCorrectionTokenService>()).Message,
            StringComparison.Ordinal);
    }

    private static string SigningKeyPath =>
        $"{NonWorkingDayPreviewTokenOptions.SectionName}:"
        + NonWorkingDayPreviewTokenOptions.SigningKeyName;

    private static HmacNonWorkingDayCorrectionTokenService CreateCorrectionService(
        TimeProvider timeProvider,
        int keySeed = 1)
    {
        return new HmacNonWorkingDayCorrectionTokenService(
            new NonWorkingDayPreviewTokenOptions(
                SigningKey(keySeed),
                TimeSpan.FromMinutes(5)),
            timeProvider);
    }

    private static HmacNonWorkingDayPreviewTokenService CreatePreviewService(
        TimeProvider timeProvider)
    {
        return new HmacNonWorkingDayPreviewTokenService(
            new NonWorkingDayPreviewTokenOptions(
                SigningKey(),
                TimeSpan.FromMinutes(5)),
            timeProvider);
    }

    private static NonWorkingDayCorrectionConfirmationMaterial CreateRangeMaterial(
        NonWorkingDayCorrectionSource? source = null,
        NonWorkingDayPreviewInput? replacementInput = null,
        Guid? replacementMembershipId = null)
    {
        source ??= CreateSource();
        replacementInput ??= new NonWorkingDayPreviewInput(
            ReplacementPeriod,
            "maintenance",
            "Boiler replacement");
        var replacementImpact = CreateReplacementImpact(
            source,
            replacementInput.Period,
            replacementMembershipId);
        return NonWorkingDayCorrectionConfirmationMaterial.ForReplaceRange(
            source,
            replacementInput,
            replacementImpact);
    }

    private static NonWorkingDayCorrectionConfirmationMaterial CreateReasonMaterial(
        NonWorkingDayCorrectionSource? source = null)
    {
        source ??= CreateSource();
        return NonWorkingDayCorrectionConfirmationMaterial.ForReplaceReason(
            source,
            new NonWorkingDayPreviewInput(
                OriginalPeriod,
                "weather_closure",
                "Corrected explanation"));
    }

    private static NonWorkingDayCorrectionSource CreateSource(
        Guid? periodId = null,
        string reasonComment = "Severe weather",
        Guid? applicationId = null,
        Guid? membershipId = null,
        Guid? clientId = null,
        DateTimeOffset? confirmedAt = null)
    {
        var confirmationTime = confirmedAt ?? TestNow;
        var application = new NonWorkingDayCorrectionApplicationSource(
            applicationId
                ?? Guid.Parse("10000000-0000-0000-0000-000000000001"),
            membershipId
                ?? Guid.Parse("20000000-0000-0000-0000-000000000001"),
            clientId
                ?? Guid.Parse("30000000-0000-0000-0000-000000000001"),
            OriginalPeriod,
            confirmationTime.AddMinutes(-5),
            confirmationTime,
            NonWorkingDayCorrectionSourceStatus.Active);

        return new NonWorkingDayCorrectionSource(
            periodId ?? Guid.Parse("40000000-0000-0000-0000-000000000001"),
            OriginalPeriod,
            "weather_closure",
            reasonComment,
            TestNow,
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            Guid.Parse("60000000-0000-0000-0000-000000000001"),
            NonWorkingDayCorrectionSourceStatus.Active,
            [application],
            existingCancellationId: null);
    }

    private static MembershipNonWorkingDayReplacementImpactPreparation
        CreateReplacementImpact(
            NonWorkingDayCorrectionSource source,
            DateRange replacementPeriod,
            Guid? replacementMembershipId)
    {
        var membershipId = replacementMembershipId
            ?? Guid.Parse("70000000-0000-0000-0000-000000000001");
        var clientId = Guid.Parse("80000000-0000-0000-0000-000000000001");
        var scope = new MembershipNonWorkingDayAffectedScope(
            replacementPeriod,
            [
                new MembershipNonWorkingDayAffectedScopeItem(
                    membershipId,
                    clientId,
                    replacementPeriod),
            ]);
        var baseEndDate = new DateOnly(2026, 8, 18);
        var terms = MembershipIssueTerms.FromIssuedSnapshot(
            Guid.Parse("90000000-0000-0000-0000-000000000001"),
            new IssuedMembershipSnapshot(
                "Correction token fixture",
                durationDays: 30,
                visitsLimit: 8,
                new Money(1000m, "UAH")),
            new DateOnly(2026, 7, 20),
            baseEndDate);
        var state = MembershipCalculatedState.FromStoredCache(
            terms,
            countedVisits: 0,
            remainingVisits: 8,
            negativeBalance: 0,
            firstNegativeVisitId: null,
            firstNegativeVisitDate: null,
            extensionDays: 0,
            effectiveEndDate: baseEndDate,
            lastCountedVisitAt: null);
        var estimate = MembershipNonWorkingDayImpactEstimator.Estimate(
            state,
            currentDateRangeExtensions: null,
            replacementPeriod);
        var impact = new MembershipNonWorkingDayImpactPreparation(
            scope,
            [
                new MembershipNonWorkingDayImpactItem(
                    membershipId,
                    clientId,
                    replacementPeriod,
                    estimate),
            ]);

        return new MembershipNonWorkingDayReplacementImpactPreparation(
            source.PeriodId,
            source.Applications
                .Select(application => application.ApplicationId)
                .Order(),
            impact);
    }

    private static IConfiguration PersistenceConfiguration(bool includeSigningKey)
    {
        var values = new List<(string Key, string? Value)>
        {
            (
                $"ConnectionStrings:{BodyLifeDbContextOptions.ConnectionStringName}",
                BodyLifeDbContextOptions.LocalDevelopmentConnectionString),
        };
        if (includeSigningKey)
        {
            values.Add((SigningKeyPath, SigningKey()));
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(
                values.ToDictionary(pair => pair.Key, pair => pair.Value))
            .Build();
    }

    private static string SigningKey(int seed = 1)
    {
        return Convert.ToBase64String(
            Enumerable.Range(seed, 32)
                .Select(value => (byte)value)
                .ToArray());
    }

    private static string FlipFirstCharacter(string value)
    {
        var replacement = value[0] == 'A' ? 'B' : 'A';
        return replacement + value[1..];
    }

    private static void AssertValid(
        NonWorkingDayCorrectionTokenValidation validation)
    {
        Assert.Equal(
            NonWorkingDayCorrectionTokenValidationStatus.Valid,
            validation.Status);
        Assert.True(validation.IsValid);
        Assert.NotNull(validation.ConfirmationFingerprint);
        Assert.NotNull(validation.IssuedAt);
        Assert.NotNull(validation.ExpiresAt);
    }

    private static void AssertInvalid(
        NonWorkingDayCorrectionTokenValidation validation)
    {
        Assert.Equal(
            NonWorkingDayCorrectionTokenValidationStatus.InvalidToken,
            validation.Status);
        Assert.False(validation.IsValid);
        Assert.Null(validation.ConfirmationFingerprint);
        Assert.Null(validation.IssuedAt);
        Assert.Null(validation.ExpiresAt);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
