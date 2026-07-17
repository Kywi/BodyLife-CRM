using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Infrastructure.Persistence;
using BodyLife.Crm.Infrastructure.Persistence.NonWorkingDays;
using BodyLife.Crm.Modules.Memberships;
using BodyLife.Crm.Modules.NonWorkingDays;
using BodyLife.Crm.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BodyLife.Crm.Infrastructure.Tests.Security;

public sealed class NonWorkingDayPreviewTokenServiceTests
{
    private static readonly DateTimeOffset TestNow =
        new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateRange TestPeriod =
        new(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 22));

    [Fact]
    public void IssueIsDeterministicAndAcceptsNormalizedEquivalentInput()
    {
        var clock = new MutableTimeProvider(TestNow);
        var service = CreateService(clock);
        var scope = CreateScope(TestPeriod);
        var canonicalInput = new NonWorkingDayPreviewInput(
            TestPeriod,
            "caf\u00E9",
            "Temporary closure");
        var equivalentInput = new NonWorkingDayPreviewInput(
            TestPeriod,
            "  cafe\u0301  ",
            "  Temporary closure  ");

        var first = service.Issue(canonicalInput, scope);
        var second = service.Issue(equivalentInput, scope);
        var validation = service.Validate(
            first.ConfirmationToken,
            equivalentInput,
            scope);

        Assert.Equal("caf\u00E9", equivalentInput.ReasonCode);
        Assert.Equal("Temporary closure", equivalentInput.ReasonComment);
        Assert.Equal(first.ConfirmationToken, second.ConfirmationToken);
        Assert.Equal(first.ScopeFingerprint, second.ScopeFingerprint);
        Assert.Matches("^[0-9A-F]{64}$", first.ScopeFingerprint);
        Assert.Equal(TestNow, first.IssuedAt);
        Assert.Equal(TestNow.AddMinutes(5), first.ExpiresAt);
        Assert.Equal(NonWorkingDayPreviewTokenValidationStatus.Valid, validation.Status);
        Assert.True(validation.IsValid);
        Assert.Equal(first.ScopeFingerprint, validation.ScopeFingerprint);
        Assert.Equal(first.IssuedAt, validation.IssuedAt);
        Assert.Equal(first.ExpiresAt, validation.ExpiresAt);
    }

    [Fact]
    public void ValidateExpiresAtExactBoundaryBeforeComparingCurrentScope()
    {
        var clock = new MutableTimeProvider(TestNow);
        var service = CreateService(clock);
        var input = CreateInput(TestPeriod);
        var scope = CreateScope(TestPeriod);
        var confirmation = service.Issue(input, scope);

        clock.UtcNow = confirmation.IssuedAt.AddMilliseconds(-1);
        AssertInvalid(service.Validate(confirmation.ConfirmationToken, input, scope));

        clock.UtcNow = confirmation.ExpiresAt.AddMilliseconds(-1);
        Assert.Equal(
            NonWorkingDayPreviewTokenValidationStatus.Valid,
            service.Validate(confirmation.ConfirmationToken, input, scope).Status);

        clock.UtcNow = confirmation.ExpiresAt;
        var expired = service.Validate(
            confirmation.ConfirmationToken,
            input,
            CreateScope(TestPeriod, changedSecondClient: true));

        Assert.Equal(NonWorkingDayPreviewTokenValidationStatus.Expired, expired.Status);
        Assert.False(expired.IsValid);
        Assert.Equal(confirmation.ScopeFingerprint, expired.ScopeFingerprint);
        Assert.Equal(confirmation.ExpiresAt, expired.ExpiresAt);
    }

    [Fact]
    public void ValidateRejectsTamperingMalformedTokensAndWrongSigningKey()
    {
        var clock = new MutableTimeProvider(TestNow);
        var service = CreateService(clock);
        var input = CreateInput(TestPeriod);
        var scope = CreateScope(TestPeriod);
        var token = service.Issue(input, scope).ConfirmationToken;
        var segments = token.Split('.');
        var tamperedPayload = $"{segments[0]}.{FlipFirstCharacter(segments[1])}.{segments[2]}";
        var tamperedSignature = $"{segments[0]}.{segments[1]}.{FlipFirstCharacter(segments[2])}";

        AssertInvalid(service.Validate(tamperedPayload, input, scope));
        AssertInvalid(service.Validate(tamperedSignature, input, scope));
        AssertInvalid(CreateService(clock, keySeed: 31).Validate(token, input, scope));

        foreach (var malformed in new string?[]
                 {
                     null,
                     string.Empty,
                     " ",
                     token + " ",
                     "wrong-prefix.payload.signature",
                     new('A', NonWorkingDayPreviewConfirmation.MaxTokenLength + 1),
                 })
        {
            AssertInvalid(service.Validate(malformed, input, scope));
        }
    }

    [Fact]
    public void ValidateDetectsEveryBoundInputAndScopeChange()
    {
        var clock = new MutableTimeProvider(TestNow);
        var service = CreateService(clock);
        var input = CreateInput(TestPeriod);
        var scope = CreateScope(TestPeriod);
        var token = service.Issue(input, scope).ConfirmationToken;
        var changedPeriod = new DateRange(
            TestPeriod.StartDate,
            TestPeriod.EndDate.AddDays(1));
        var validations = new[]
        {
            service.Validate(
                token,
                new NonWorkingDayPreviewInput(TestPeriod, "maintenance", input.ReasonComment),
                scope),
            service.Validate(
                token,
                new NonWorkingDayPreviewInput(TestPeriod, input.ReasonCode, "Changed comment"),
                scope),
            service.Validate(
                token,
                input,
                CreateScope(TestPeriod, changedSecondClient: true)),
            service.Validate(
                token,
                input,
                CreateScope(TestPeriod, changedSecondMembership: true)),
            service.Validate(
                token,
                CreateInput(changedPeriod),
                CreateScope(changedPeriod)),
        };

        Assert.All(
            validations,
            validation =>
            {
                Assert.Equal(
                    NonWorkingDayPreviewTokenValidationStatus.InputOrScopeMismatch,
                    validation.Status);
                Assert.False(validation.IsValid);
                Assert.NotNull(validation.ScopeFingerprint);
            });
    }

    [Fact]
    public void PreviewInputRequiresCanonicalBoundedReasonValues()
    {
        Assert.Throws<ArgumentException>(() =>
            new NonWorkingDayPreviewInput(new DateRange(default, default), "weather"));
        Assert.Throws<ArgumentNullException>(() =>
            new NonWorkingDayPreviewInput(TestPeriod, null!));
        Assert.Throws<ArgumentException>(() =>
            new NonWorkingDayPreviewInput(TestPeriod, "  "));
        Assert.Throws<ArgumentException>(() =>
            new NonWorkingDayPreviewInput(
                TestPeriod,
                new string('r', NonWorkingDayPreviewInput.ReasonCodeMaxLength + 1)));
        Assert.Throws<ArgumentException>(() =>
            new NonWorkingDayPreviewInput(
                TestPeriod,
                "weather",
                new string('c', NonWorkingDayPreviewInput.ReasonCommentMaxLength + 1)));

        var input = new NonWorkingDayPreviewInput(TestPeriod, " weather ", "   ");

        Assert.Equal("weather", input.ReasonCode);
        Assert.Null(input.ReasonComment);
    }

    [Fact]
    public void OptionsRequireCanonicalStrongKeyAndBoundedWholeSecondLifetime()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new NonWorkingDayPreviewTokenOptions(null!, TimeSpan.FromMinutes(5)));
        Assert.Throws<ArgumentException>(() =>
            new NonWorkingDayPreviewTokenOptions("not-base64", TimeSpan.FromMinutes(5)));
        Assert.Throws<ArgumentException>(() =>
            new NonWorkingDayPreviewTokenOptions(" " + SigningKey(), TimeSpan.FromMinutes(5)));
        Assert.Throws<ArgumentException>(() =>
            new NonWorkingDayPreviewTokenOptions(SigningKey(byteCount: 31), TimeSpan.FromMinutes(5)));
        Assert.Throws<ArgumentException>(() =>
            new NonWorkingDayPreviewTokenOptions(SigningKey(byteCount: 65), TimeSpan.FromMinutes(5)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NonWorkingDayPreviewTokenOptions(SigningKey(), TimeSpan.FromSeconds(59)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NonWorkingDayPreviewTokenOptions(SigningKey(), TimeSpan.FromMinutes(31)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NonWorkingDayPreviewTokenOptions(SigningKey(), TimeSpan.FromMilliseconds(90_500)));

        var options = new NonWorkingDayPreviewTokenOptions(
            SigningKey(),
            TimeSpan.FromMinutes(7));

        Assert.Equal(TimeSpan.FromMinutes(7), options.Lifetime);
    }

    [Fact]
    public void ConfigurationUsesDefaultLifetimeAndRejectsMissingOrInvalidValues()
    {
        var valid = Configuration(
            (SigningKeyPath, SigningKey()));
        var missingKey = Configuration();
        var invalidKey = Configuration(
            (SigningKeyPath, "weak"));
        var invalidLifetime = Configuration(
            (SigningKeyPath, SigningKey()),
            (LifetimePath, "not-a-duration"));

        Assert.Equal(
            NonWorkingDayPreviewTokenOptions.DefaultLifetime,
            NonWorkingDayPreviewTokenOptions.FromConfiguration(valid).Lifetime);
        Assert.Contains(
            SigningKeyPath,
            Assert.Throws<InvalidOperationException>(() =>
                NonWorkingDayPreviewTokenOptions.FromConfiguration(missingKey)).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            NonWorkingDayPreviewTokenOptions.SectionName,
            Assert.Throws<InvalidOperationException>(() =>
                NonWorkingDayPreviewTokenOptions.FromConfiguration(invalidKey)).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            LifetimePath,
            Assert.Throws<InvalidOperationException>(() =>
                NonWorkingDayPreviewTokenOptions.FromConfiguration(invalidLifetime)).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PersistenceRegistrationResolvesConfiguredSingletonAndKeepsSecretLazy()
    {
        var clock = new MutableTimeProvider(TestNow);
        var configuration = PersistenceConfiguration(includeSigningKey: true);
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddBodyLifePersistence(configuration);

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<INonWorkingDayPreviewTokenService>();
        var second = provider.GetRequiredService<INonWorkingDayPreviewTokenService>();

        Assert.IsType<HmacNonWorkingDayPreviewTokenService>(first);
        Assert.Same(first, second);

        var missingSecretServices = new ServiceCollection();
        var exception = Record.Exception(() =>
            missingSecretServices.AddBodyLifePersistence(
                PersistenceConfiguration(includeSigningKey: false)));

        Assert.Null(exception);

        using var missingSecretProvider = missingSecretServices.BuildServiceProvider();
        Assert.Contains(
            SigningKeyPath,
            Assert.Throws<InvalidOperationException>(() =>
                missingSecretProvider.GetRequiredService<INonWorkingDayPreviewTokenService>()).Message,
            StringComparison.Ordinal);
    }

    private static string SigningKeyPath =>
        $"{NonWorkingDayPreviewTokenOptions.SectionName}:{NonWorkingDayPreviewTokenOptions.SigningKeyName}";

    private static string LifetimePath =>
        $"{NonWorkingDayPreviewTokenOptions.SectionName}:{NonWorkingDayPreviewTokenOptions.LifetimeName}";

    private static HmacNonWorkingDayPreviewTokenService CreateService(
        TimeProvider timeProvider,
        int keySeed = 1)
    {
        return new HmacNonWorkingDayPreviewTokenService(
            new NonWorkingDayPreviewTokenOptions(
                SigningKey(keySeed),
                TimeSpan.FromMinutes(5)),
            timeProvider);
    }

    private static NonWorkingDayPreviewInput CreateInput(DateRange period)
    {
        return new NonWorkingDayPreviewInput(
            period,
            "weather_closure",
            "Severe weather");
    }

    private static MembershipNonWorkingDayAffectedScope CreateScope(
        DateRange period,
        bool changedSecondClient = false,
        bool changedSecondMembership = false)
    {
        return new MembershipNonWorkingDayAffectedScope(
            period,
            [
                new MembershipNonWorkingDayAffectedScopeItem(
                    Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    Guid.Parse("10000000-0000-0000-0000-000000000001"),
                    period),
                new MembershipNonWorkingDayAffectedScopeItem(
                    changedSecondMembership
                        ? Guid.Parse("00000000-0000-0000-0000-000000000099")
                        : Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    changedSecondClient
                        ? Guid.Parse("10000000-0000-0000-0000-000000000099")
                        : Guid.Parse("10000000-0000-0000-0000-000000000002"),
                    period),
            ]);
    }

    private static IConfiguration Configuration(
        params (string Key, string? Value)[] values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => pair.Value))
            .Build();
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

        return Configuration([.. values]);
    }

    private static string SigningKey(int seed = 1, int byteCount = 32)
    {
        return Convert.ToBase64String(
            Enumerable.Range(seed, byteCount)
                .Select(value => (byte)value)
                .ToArray());
    }

    private static string FlipFirstCharacter(string value)
    {
        var replacement = value[0] == 'A' ? 'B' : 'A';
        return replacement + value[1..];
    }

    private static void AssertInvalid(NonWorkingDayPreviewTokenValidation validation)
    {
        Assert.Equal(NonWorkingDayPreviewTokenValidationStatus.InvalidToken, validation.Status);
        Assert.False(validation.IsValid);
        Assert.Null(validation.ScopeFingerprint);
        Assert.Null(validation.IssuedAt);
        Assert.Null(validation.ExpiresAt);
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
