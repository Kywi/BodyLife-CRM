using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.Infrastructure.Persistence.Audit;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class ReceptionAppFixture : IAsyncLifetime
{
    public const string SmokeLoginName = "owner";
    public const string SmokePassword = "correct horse battery";
    public const string SmokeAdminLoginName = "named.admin";
    public const string SmokeAdminPassword = "smoke admin password";

    private readonly ConcurrentQueue<string> _output = new();
    private readonly object _endingSoonReportSeedLock = new();
    private readonly object _lowRemainingReportSeedLock = new();
    private readonly object _negativeClientsReportSeedLock = new();
    private readonly object _inactiveClientsReportSeedLock = new();
    private readonly object _auditTimelineSeedLock = new();
    private readonly object _clientHistorySeedLock = new();
    private Task<EndingSoonReportSmokeScenario>? _endingSoonReportSeedTask;
    private Task<LowRemainingReportSmokeScenario>? _lowRemainingReportSeedTask;
    private Task<NegativeClientsReportSmokeScenario>? _negativeClientsReportSeedTask;
    private Task<InactiveClientsReportSmokeScenario>? _inactiveClientsReportSeedTask;
    private Task<AuditTimelineSmokeScenario>? _auditTimelineSeedTask;
    private Task<ClientHistorySmokeScenario>? _clientHistorySeedTask;
    private Process? _process;
    private PostgreSqlSmokeDatabase? _database;
    private Guid _ownerAccountId;
    private Guid _sharedAdminAccountId;
    private Guid _activeMembershipTypeId;

    public Uri BaseAddress { get; private set; } = null!;

    public string LoginName => SmokeLoginName;

    public string Password => SmokePassword;

    public string AdminLoginName => SmokeAdminLoginName;

    public string AdminPassword => SmokeAdminPassword;

    public Guid TabletEditableClientId { get; private set; }

    public Guid PhoneEditableClientId { get; private set; }

    public Guid StaleEditableClientId { get; private set; }

    public Guid CardChangeClientId { get; private set; }

    public Guid CardAssignClientId { get; private set; }

    public Guid CardStaleClientId { get; private set; }

    public Guid VisitTabletClientId { get; private set; }

    public Guid VisitTabletMembershipId { get; private set; }

    public Guid VisitPhoneClientId { get; private set; }

    public Guid VisitPhoneMembershipId { get; private set; }

    public Guid VisitZeroClientId { get; private set; }

    public Guid VisitZeroMembershipId { get; private set; }

    public Guid VisitNoMembershipClientId { get; private set; }

    public Guid VisitStaleClientId { get; private set; }

    public Guid VisitStaleMembershipId { get; private set; }

    public Guid VisitFreezeClientId { get; private set; }

    public Guid VisitFreezeMembershipId { get; private set; }

    public Guid VisitAdminClientId { get; private set; }

    public Guid VisitAdminMembershipId { get; private set; }

    public Guid PaymentHistoryClientId { get; private set; }

    public Guid PaymentHistoryMembershipId { get; private set; }

    public DateOnly DailyReportBusinessDate { get; private set; }

    public Guid DailyReportClientId { get; private set; }

    public Guid PaymentTabletClientId { get; private set; }

    public Guid PaymentTabletMembershipId { get; private set; }

    public Guid PaymentPhoneClientId { get; private set; }

    public Guid FreezeTabletClientId { get; private set; }

    public Guid FreezeTabletMembershipId { get; private set; }

    public Guid FreezePhoneClientId { get; private set; }

    public Guid FreezePhoneMembershipId { get; private set; }

    public Guid CancelFreezeTabletClientId { get; private set; }

    public Guid CancelFreezeTabletMembershipId { get; private set; }

    public Guid CancelFreezeTabletFreezeId { get; private set; }

    public Guid CancelFreezePhoneClientId { get; private set; }

    public Guid CancelFreezePhoneMembershipId { get; private set; }

    public Guid CancelFreezePhoneFreezeId { get; private set; }

    public DateOnly NonWorkingDayPreviewStartDate { get; private set; }

    public DateOnly NonWorkingDayPreviewEndDate { get; private set; }

    public Guid NonWorkingDayEndBoundaryClientId { get; private set; }

    public Guid NonWorkingDayEndBoundaryMembershipId { get; private set; }

    public Guid NonWorkingDayStartBoundaryClientId { get; private set; }

    public Guid NonWorkingDayStartBoundaryMembershipId { get; private set; }

    public Guid NonWorkingDayNoOverlapMembershipId { get; private set; }

    public NonWorkingDayAddSmokeScenario NonWorkingDayTabletAddScenario
    {
        get;
        private set;
    } = null!;

    public NonWorkingDayAddSmokeScenario NonWorkingDayPhoneAddScenario
    {
        get;
        private set;
    } = null!;

    public NonWorkingDayCorrectionSmokeScenario NonWorkingDayCorrectionScenario
    {
        get;
        private set;
    } = null!;

    public NonWorkingDayCorrectionMutationSmokeScenario
        NonWorkingDayTabletCorrectionMutationScenario
    {
        get;
        private set;
    } = null!;

    public NonWorkingDayCorrectionMutationSmokeScenario
        NonWorkingDayPhoneCorrectionMutationScenario
    {
        get;
        private set;
    } = null!;

    public NonWorkingDayCorrectionMutationSmokeScenario
        NonWorkingDayReasonCorrectionMutationScenario
    {
        get;
        private set;
    } = null!;

    public NonWorkingDayCorrectionMutationSmokeScenario
        NonWorkingDayStaleCorrectionMutationScenario
    {
        get;
        private set;
    } = null!;

    public Guid IssueMembershipTypeId { get; private set; }

    public Guid IssueTabletClientId { get; private set; }

    public Guid IssuePhoneClientId { get; private set; }

    public Guid IssuePhoneExistingMembershipId { get; private set; }

    public async Task ExpireSessionAsync(string deviceLabel)
    {
        var database = _database
            ?? throw new InvalidOperationException("The UI smoke database is not initialized.");
        var updatedRows = await database.ExpireSessionAsync(deviceLabel);
        Assert.Equal(1, updatedRows);
    }

    public async Task<bool> IsSessionEndedAsync(string deviceLabel)
    {
        var database = _database
            ?? throw new InvalidOperationException("The UI smoke database is not initialized.");
        return await database.IsSessionEndedAsync(deviceLabel);
    }

    public Task AdvanceClientUpdatedAtAsync(Guid clientId)
    {
        return RequireDatabase().AdvanceClientUpdatedAtAsync(clientId);
    }

    public Task<long> CountClientUpdateAuditEntriesAsync(Guid clientId)
    {
        return RequireDatabase().CountClientUpdateAuditEntriesAsync(clientId);
    }

    public Task<long> CountUpdateClientIdempotencyKeysAsync(Guid clientId)
    {
        return RequireDatabase().CountUpdateClientIdempotencyKeysAsync(clientId);
    }

    public Task<long> CountDuplicateAcknowledgementsAsync(Guid clientId)
    {
        return RequireDatabase().CountDuplicateAcknowledgementsAsync(clientId);
    }

    public Task<long> CountClientsAsync()
    {
        return RequireDatabase().CountClientsAsync();
    }

    public Task<Guid?> FindClientIdByCurrentCardAsync(string cardNumber)
    {
        return RequireDatabase().FindClientIdByCurrentCardAsync(cardNumber);
    }

    public Task<Guid?> FindClientIdByPhoneAsync(string phone)
    {
        return RequireDatabase().FindClientIdByPhoneAsync(phone);
    }

    public Task<long> CountClientCreateAuditEntriesAsync(Guid clientId)
    {
        return RequireDatabase().CountClientCreateAuditEntriesAsync(clientId);
    }

    public Task<long> CountCreateClientIdempotencyKeysAsync(Guid clientId)
    {
        return RequireDatabase().CountCreateClientIdempotencyKeysAsync(clientId);
    }

    public Task ReplaceCurrentCardForStaleTestAsync(Guid clientId, string newCardNumber)
    {
        return RequireDatabase().ReplaceCurrentCardForStaleTestAsync(clientId, newCardNumber);
    }

    public Task<long> CountCardAuditEntriesAsync(Guid clientId, string actionType)
    {
        return RequireDatabase().CountCardAuditEntriesAsync(clientId, actionType);
    }

    public Task<long> CountCardCommandIdempotencyKeysAsync(Guid clientId)
    {
        return RequireDatabase().CountCardCommandIdempotencyKeysAsync(clientId);
    }

    public Task<long> CountCardAssignmentsAsync(Guid clientId)
    {
        return RequireDatabase().CountCardAssignmentsAsync(clientId);
    }

    public Task<string?> ReadCurrentCardNumberAsync(Guid clientId)
    {
        return RequireDatabase().ReadCurrentCardNumberAsync(clientId);
    }

    public Task<long> CountMembershipTypesByNameAsync(string name)
    {
        return RequireDatabase().CountMembershipTypesByNameAsync(name);
    }

    public Task<Guid?> FindMembershipTypeIdByNameAsync(string name)
    {
        return RequireDatabase().FindMembershipTypeIdByNameAsync(name);
    }

    public Task<long> CountMembershipTypeCreateAuditEntriesAsync()
    {
        return RequireDatabase().CountMembershipTypeCreateAuditEntriesAsync();
    }

    public Task<long> CountCreateMembershipTypeIdempotencyKeysAsync()
    {
        return RequireDatabase().CountCreateMembershipTypeIdempotencyKeysAsync();
    }

    public Task<MembershipTypeSmokeSnapshot> ReadMembershipTypeAsync(Guid membershipTypeId)
    {
        return RequireDatabase().ReadMembershipTypeAsync(membershipTypeId);
    }

    public Task AdvanceMembershipTypeForStaleTestAsync(
        Guid membershipTypeId,
        string canonicalName)
    {
        return RequireDatabase().AdvanceMembershipTypeForStaleTestAsync(
            membershipTypeId,
            canonicalName);
    }

    public Task<long> CountMembershipTypeEditAuditEntriesAsync(Guid membershipTypeId)
    {
        return RequireDatabase().CountMembershipTypeEditAuditEntriesAsync(membershipTypeId);
    }

    public Task<long> CountEditMembershipTypeIdempotencyKeysAsync(Guid membershipTypeId)
    {
        return RequireDatabase().CountEditMembershipTypeIdempotencyKeysAsync(membershipTypeId);
    }

    public Task<string?> ReadLatestMembershipTypeEditReasonAsync(Guid membershipTypeId)
    {
        return RequireDatabase().ReadLatestMembershipTypeEditReasonAsync(membershipTypeId);
    }

    public Task<Guid> SeedActiveMembershipTypeForDeactivationAsync(string name)
    {
        return RequireDatabase().SeedMembershipTypeAsync(
            name,
            durationDays: 21,
            visitsLimit: 6,
            priceAmount: 725.00m,
            isActive: true,
            comment: "Lifecycle smoke target.",
            createdAt: new DateTimeOffset(2026, 7, 6, 9, 0, 0, TimeSpan.Zero),
            updatedAt: new DateTimeOffset(2026, 7, 7, 10, 0, 0, TimeSpan.Zero),
            deactivatedAt: null);
    }

    public Task<DateTime> DeactivateMembershipTypeForAlreadyInactiveTestAsync(
        Guid membershipTypeId)
    {
        return RequireDatabase().DeactivateMembershipTypeForAlreadyInactiveTestAsync(
            membershipTypeId);
    }

    public Task<long> CountMembershipTypeDeactivateAuditEntriesAsync(Guid membershipTypeId)
    {
        return RequireDatabase().CountMembershipTypeDeactivateAuditEntriesAsync(membershipTypeId);
    }

    public Task<long> CountDeactivateMembershipTypeIdempotencyKeysAsync(Guid membershipTypeId)
    {
        return RequireDatabase().CountDeactivateMembershipTypeIdempotencyKeysAsync(
            membershipTypeId);
    }

    public Task<string?> ReadLatestMembershipTypeDeactivateReasonAsync(Guid membershipTypeId)
    {
        return RequireDatabase().ReadLatestMembershipTypeDeactivateReasonAsync(membershipTypeId);
    }

    public Task<Guid> InsertExternalCountedVisitAsync(Guid clientId, Guid membershipId)
    {
        return RequireDatabase().InsertExternalCountedVisitAsync(clientId, membershipId);
    }

    public Task<Guid> InsertActiveFreezeForTodayAsync(Guid clientId, Guid membershipId)
    {
        return RequireDatabase().InsertActiveFreezeForTodayAsync(clientId, membershipId);
    }

    public Task<long> CountActiveVisitsAsync(Guid clientId, string? visitKind = null)
    {
        return RequireDatabase().CountActiveVisitsAsync(clientId, visitKind);
    }

    public Task<long> CountActiveVisitConsumptionsAsync(Guid clientId)
    {
        return RequireDatabase().CountActiveVisitConsumptionsAsync(clientId);
    }

    public Task<long> CountMarkVisitAuditEntriesAsync(Guid clientId)
    {
        return RequireDatabase().CountMarkVisitAuditEntriesAsync(clientId);
    }

    public Task<long> CountMarkVisitIdempotencyKeysAsync(Guid clientId)
    {
        return RequireDatabase().CountMarkVisitIdempotencyKeysAsync(clientId);
    }

    public Task<long> CountCancelVisitAuditEntriesAsync(Guid clientId)
    {
        return RequireDatabase().CountCancelVisitAuditEntriesAsync(clientId);
    }

    public Task<long> CountCancelVisitIdempotencyKeysAsync(Guid clientId)
    {
        return RequireDatabase().CountCancelVisitIdempotencyKeysAsync(clientId);
    }

    public Task<MembershipStateSmokeSnapshot> ReadMembershipStateAsync(Guid membershipId)
    {
        return RequireDatabase().ReadMembershipStateAsync(membershipId);
    }

    public Task<long> CountIssuedMembershipsAsync(Guid clientId)
    {
        return RequireDatabase().CountIssuedMembershipsAsync(clientId);
    }

    public Task<long> CountIssueMembershipAuditEntriesAsync(Guid clientId)
    {
        return RequireDatabase().CountIssueMembershipAuditEntriesAsync(clientId);
    }

    public Task<long> CountIssueMembershipIdempotencyKeysAsync(Guid clientId)
    {
        return RequireDatabase().CountIssueMembershipIdempotencyKeysAsync(clientId);
    }

    public Task<IssuedMembershipSmokeSnapshot> ReadLatestIssuedMembershipAsync(Guid clientId)
    {
        return RequireDatabase().ReadLatestIssuedMembershipAsync(clientId);
    }

    public Task<long> CountActivePaymentsAsync(Guid clientId)
    {
        return RequireDatabase().CountActivePaymentsAsync(clientId);
    }

    public Task<long> CountCreatePaymentAuditEntriesAsync(Guid clientId)
    {
        return RequireDatabase().CountCreatePaymentAuditEntriesAsync(clientId);
    }

    public Task<long> CountCreatePaymentIdempotencyKeysAsync(Guid clientId)
    {
        return RequireDatabase().CountCreatePaymentIdempotencyKeysAsync(clientId);
    }

    public Task<PaymentSmokeSnapshot> ReadLatestActivePaymentAsync(Guid clientId)
    {
        return RequireDatabase().ReadLatestActivePaymentAsync(clientId);
    }

    public Task<long> CountActiveFreezesAsync(Guid clientId)
    {
        return RequireDatabase().CountActiveFreezesAsync(clientId);
    }

    public Task<long> CountAddFreezeAuditEntriesAsync(Guid clientId)
    {
        return RequireDatabase().CountAddFreezeAuditEntriesAsync(clientId);
    }

    public Task<long> CountAddFreezeIdempotencyKeysAsync(Guid clientId)
    {
        return RequireDatabase().CountAddFreezeIdempotencyKeysAsync(clientId);
    }

    public Task<FreezeSmokeSnapshot> ReadLatestActiveFreezeAsync(Guid clientId)
    {
        return RequireDatabase().ReadLatestActiveFreezeAsync(clientId);
    }

    public Task<long> CountFreezeCancellationsAsync(Guid freezeId)
    {
        return RequireDatabase().CountFreezeCancellationsAsync(freezeId);
    }

    public Task<long> CountCancelFreezeAuditEntriesAsync(Guid freezeId)
    {
        return RequireDatabase().CountCancelFreezeAuditEntriesAsync(freezeId);
    }

    public Task<long> CountCancelFreezeIdempotencyKeysAsync(Guid clientId)
    {
        return RequireDatabase().CountCancelFreezeIdempotencyKeysAsync(clientId);
    }

    public Task<string> ReadFreezeStatusAsync(Guid freezeId)
    {
        return RequireDatabase().ReadFreezeStatusAsync(freezeId);
    }

    public Task<string> ReadFreezeCancellationReasonAsync(Guid freezeId)
    {
        return RequireDatabase().ReadFreezeCancellationReasonAsync(freezeId);
    }

    public Task<FreezeCancellationAuditSmokeSnapshot> ReadCancelFreezeAuditAsync(
        Guid freezeId)
    {
        return RequireDatabase().ReadCancelFreezeAuditAsync(freezeId);
    }

    public Task<NonWorkingDayMutationCountSmokeSnapshot>
        ReadNonWorkingDayMutationCountsAsync()
    {
        return RequireDatabase().ReadNonWorkingDayMutationCountsAsync();
    }

    public NonWorkingDayAddSmokeScenario GetNonWorkingDayAddScenario(
        string viewportName)
    {
        return viewportName switch
        {
            "tablet" => NonWorkingDayTabletAddScenario,
            "phone" => NonWorkingDayPhoneAddScenario,
            _ => throw new ArgumentOutOfRangeException(
                nameof(viewportName),
                viewportName,
                "NonWorkingDay add smoke viewport is not supported."),
        };
    }

    public Task MoveNonWorkingDayScenarioMembershipIntoScopeAsync(
        NonWorkingDayAddSmokeScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return RequireDatabase().MoveIssuedMembershipStartDateAsync(
            scenario.ScopeEntrantMembershipId,
            scenario.Period.EndDate);
    }

    public NonWorkingDayCorrectionMutationSmokeScenario
        GetNonWorkingDayCorrectionMutationScenario(string viewportName)
    {
        return viewportName switch
        {
            "tablet" => NonWorkingDayTabletCorrectionMutationScenario,
            "phone" => NonWorkingDayPhoneCorrectionMutationScenario,
            "reason" => NonWorkingDayReasonCorrectionMutationScenario,
            _ => throw new ArgumentOutOfRangeException(
                nameof(viewportName),
                viewportName,
                "NonWorkingDay correction smoke viewport is not supported."),
        };
    }

    public Task MoveNonWorkingDayCorrectionMembershipIntoScopeAsync(
        NonWorkingDayCorrectionMutationSmokeScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return RequireDatabase().MoveIssuedMembershipStartDateAsync(
            scenario.ScopeEntrantMembershipId,
            scenario.ReplacementPeriod.EndDate);
    }

    public Task<NonWorkingDayCorrectionMutationSmokeSnapshot>
        ReadNonWorkingDayCorrectionMutationAsync(Guid originalPeriodId)
    {
        return RequireDatabase()
            .ReadNonWorkingDayCorrectionMutationAsync(originalPeriodId);
    }

    public Task<long> CountPaymentCorrectionsAsync(Guid originalPaymentId)
    {
        return RequireDatabase().CountPaymentCorrectionsAsync(originalPaymentId);
    }

    public Task<long> CountPaymentCancellationsAsync(Guid paymentId)
    {
        return RequireDatabase().CountPaymentCancellationsAsync(paymentId);
    }

    public Task<long> CountCorrectPaymentAuditEntriesAsync(Guid clientId)
    {
        return RequireDatabase().CountCorrectPaymentAuditEntriesAsync(clientId);
    }

    public Task<long> CountCorrectPaymentIdempotencyKeysAsync(Guid clientId)
    {
        return RequireDatabase().CountCorrectPaymentIdempotencyKeysAsync(clientId);
    }

    public Task<EndingSoonReportSmokeScenario> EnsureEndingSoonReportScenarioAsync()
    {
        lock (_endingSoonReportSeedLock)
        {
            return _endingSoonReportSeedTask ??= SeedEndingSoonReportScenarioAsync();
        }
    }

    public Task<LowRemainingReportSmokeScenario> EnsureLowRemainingReportScenarioAsync()
    {
        lock (_lowRemainingReportSeedLock)
        {
            return _lowRemainingReportSeedTask ??= SeedLowRemainingReportScenarioAsync();
        }
    }

    public Task<NegativeClientsReportSmokeScenario>
        EnsureNegativeClientsReportScenarioAsync()
    {
        lock (_negativeClientsReportSeedLock)
        {
            return _negativeClientsReportSeedTask ??=
                SeedNegativeClientsReportScenarioAsync();
        }
    }

    public Task<InactiveClientsReportSmokeScenario>
        EnsureInactiveClientsReportScenarioAsync()
    {
        lock (_inactiveClientsReportSeedLock)
        {
            return _inactiveClientsReportSeedTask ??=
                SeedInactiveClientsReportScenarioAsync();
        }
    }

    public Task<AuditTimelineSmokeScenario> EnsureAuditTimelineScenarioAsync()
    {
        lock (_auditTimelineSeedLock)
        {
            return _auditTimelineSeedTask ??=
                RequireDatabase().SeedAuditTimelineAsync(
                    _ownerAccountId,
                    _sharedAdminAccountId);
        }
    }

    public Task<ClientHistorySmokeScenario> EnsureClientHistoryScenarioAsync()
    {
        lock (_clientHistorySeedLock)
        {
            return _clientHistorySeedTask ??=
                RequireDatabase().SeedClientHistoryAsync(
                    _ownerAccountId,
                    _sharedAdminAccountId,
                    _activeMembershipTypeId);
        }
    }

    public async Task InitializeAsync()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{FindAvailablePort()}");
        _database = await PostgreSqlSmokeDatabase.CreateAsync();
        await SeedAccountsAsync(_database);

        var repositoryRoot = FindRepositoryRoot();
        var webProjectPath = Path.Combine(repositoryRoot, "src", "BodyLife.Crm.Web", "BodyLife.Crm.Web.csproj");

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_BIN") ?? "dotnet",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = repositoryRoot,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add(Environment.GetEnvironmentVariable("CONFIGURATION") ?? "Release");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(webProjectPath);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["ASPNETCORE_URLS"] = BaseAddress.ToString().TrimEnd('/');
        startInfo.Environment["ConnectionStrings__BodyLife"] = _database.ConnectionString;
        startInfo.Environment["BodyLife__NonWorkingDayPreviewToken__SigningKey"] =
            Convert.ToBase64String(
                Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start BodyLife.Crm.Web.");
        _process.OutputDataReceived += (_, args) => EnqueueOutput(args.Data);
        _process.ErrorDataReceived += (_, args) => EnqueueOutput(args.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitForAppAsync();
    }

    public async Task DisposeAsync()
    {
        if (_process is null)
        {
            if (_database is not null)
            {
                await _database.DisposeAsync();
            }

            return;
        }

        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    private async Task WaitForAppAsync()
    {
        using var client = new HttpClient
        {
            BaseAddress = BaseAddress,
            Timeout = TimeSpan.FromSeconds(2),
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));

        while (!timeout.IsCancellationRequested)
        {
            if (_process is { HasExited: true })
            {
                throw new InvalidOperationException($"BodyLife.Crm.Web exited before the UI smoke test could connect.{Environment.NewLine}{CapturedOutput()}");
            }

            try
            {
                using var response = await client.GetAsync("/health/live", timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), timeout.Token);
        }

        throw new TimeoutException($"Timed out waiting for BodyLife.Crm.Web at {BaseAddress}.{Environment.NewLine}{CapturedOutput()}");
    }

    private async Task SeedAccountsAsync(PostgreSqlSmokeDatabase database)
    {
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var ownerBootstrapper = new OwnerBootstrapper(dbContext, TimeProvider.System);
        var ownerResult = await ownerBootstrapper.BootstrapOwnerAsync("BodyLife Owner");

        Assert.True(
            ownerResult.Status is OwnerBootstrapStatus.Created or OwnerBootstrapStatus.AlreadyExists,
            $"Owner bootstrap returned {ownerResult.Status}.");
        _ownerAccountId = ownerResult.AccountId!.Value;

        var passwordHashingService = new PasswordHashingService();
        var credentialsBootstrapper = new OwnerCredentialsBootstrapper(
            dbContext,
            passwordHashingService,
            TimeProvider.System);

        var credentialsResult = await credentialsBootstrapper.SetOwnerCredentialsAsync(
            SmokeLoginName,
            SmokePassword);

        Assert.Equal(OwnerCredentialsBootstrapStatus.Updated, credentialsResult.Status);

        await SeedReceptionClientsAsync(database, ownerResult.AccountId!.Value);
        var activeMembershipTypeId = await SeedMembershipTypesAsync(database);
        _activeMembershipTypeId = activeMembershipTypeId;
        await SeedMarkVisitFixturesAsync(
            database,
            ownerResult.AccountId.Value,
            activeMembershipTypeId);
        await SeedPaymentHistoryFixtureAsync(
            database,
            ownerResult.AccountId.Value,
            activeMembershipTypeId);
        await SeedDailyReportFixtureAsync(
            database,
            ownerResult.AccountId.Value);
        await SeedMembershipExtensionHistoryFixtureAsync(
            database,
            ownerResult.AccountId.Value,
            activeMembershipTypeId);
        await SeedAddFreezeFixturesAsync(
            database,
            ownerResult.AccountId.Value,
            activeMembershipTypeId);
        await SeedCancelFreezeFixturesAsync(
            database,
            ownerResult.AccountId.Value,
            activeMembershipTypeId);
        await SeedNonWorkingDayPreviewFixturesAsync(
            database,
            ownerResult.AccountId.Value,
            activeMembershipTypeId);
        await SeedAddNonWorkingDayFixturesAsync(
            database,
            ownerResult.AccountId.Value,
            activeMembershipTypeId);
        await SeedCorrectNonWorkingDayFixtureAsync(
            database,
            ownerResult.AccountId.Value,
            activeMembershipTypeId);
        await SeedCorrectNonWorkingDayMutationFixturesAsync(
            database,
            ownerResult.AccountId.Value,
            activeMembershipTypeId);
        await SeedAddPaymentFixturesAsync(
            database,
            ownerResult.AccountId.Value,
            activeMembershipTypeId);
        await SeedIssueMembershipFixturesAsync(
            database,
            ownerResult.AccountId.Value,
            activeMembershipTypeId);

        var ownerEnvelope = new CommandEnvelope(
            new ActorContext(
                new AccountId(ownerResult.AccountId!.Value),
                ActorRole.Owner,
                AccountKind.Owner,
                SessionId.New(),
                "UI smoke seed"),
            new RequestCorrelationId("ui-smoke-seed"),
            EntryOrigin.Normal,
            OccurredAt: null,
            IdempotencyKey: null,
            Reason: null,
            Comment: null);
        var auditAppender = new BusinessAuditAppender(dbContext);
        var lifecycleService = new StaffAccountLifecycleService(
            dbContext,
            auditAppender,
            TimeProvider.System);
        var adminResult = await lifecycleService.CreateStaffAccountAsync(
            ownerEnvelope,
            AccountKind.NamedAdmin,
            "Smoke Named Admin");
        Assert.Equal(StaffAccountLifecycleStatus.Created, adminResult.Status);
        var staffCredentialsService = new StaffCredentialsService(
            dbContext,
            passwordHashingService,
            auditAppender,
            TimeProvider.System);
        var staffCredentialsResult = await staffCredentialsService.SetStaffCredentialsAsync(
            ownerEnvelope,
            adminResult.AccountId!.Value,
            SmokeAdminLoginName,
            SmokeAdminPassword);
        Assert.Equal(StaffCredentialsStatus.Configured, staffCredentialsResult.Status);

        var sharedAdminResult = await lifecycleService.CreateStaffAccountAsync(
            ownerEnvelope,
            AccountKind.SharedReceptionAdmin,
            "Smoke Shared Reception");
        Assert.Equal(StaffAccountLifecycleStatus.Created, sharedAdminResult.Status);
        _sharedAdminAccountId = sharedAdminResult.AccountId!.Value;
    }

    private static async Task<Guid> SeedMembershipTypesAsync(PostgreSqlSmokeDatabase database)
    {
        var activeMembershipTypeId = await database.SeedMembershipTypeAsync(
            "Eight visits / 30 days",
            durationDays: 30,
            visitsLimit: 8,
            priceAmount: 950.00m,
            isActive: true,
            comment: "Standard reception offer.",
            createdAt: new DateTimeOffset(2026, 7, 1, 9, 0, 0, TimeSpan.Zero),
            updatedAt: new DateTimeOffset(2026, 7, 2, 10, 0, 0, TimeSpan.Zero),
            deactivatedAt: null);
        await database.SeedMembershipTypeAsync(
            "Legacy 12 visits / 45 days",
            durationDays: 45,
            visitsLimit: 12,
            priceAmount: 1200.00m,
            isActive: false,
            comment: "Retained for catalog history.",
            createdAt: new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero),
            updatedAt: new DateTimeOffset(2026, 7, 5, 11, 0, 0, TimeSpan.Zero),
            deactivatedAt: new DateTimeOffset(2026, 7, 5, 11, 0, 0, TimeSpan.Zero));

        return activeMembershipTypeId;
    }

    private async Task SeedMarkVisitFixturesAsync(
        PostgreSqlSmokeDatabase database,
        Guid ownerAccountId,
        Guid membershipTypeId)
    {
        VisitTabletClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Visit",
            "Tablet",
            "+380 67 600 01 01",
            "BL-VISIT-TABLET");
        VisitTabletMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            VisitTabletClientId,
            membershipTypeId,
            "Tablet four-visit snapshot",
            visitsLimitSnapshot: 4);

        VisitPhoneClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Visit",
            "Phone",
            "+380 67 600 02 02",
            "BL-VISIT-PHONE");
        VisitPhoneMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            VisitPhoneClientId,
            membershipTypeId,
            "Phone three-visit snapshot",
            visitsLimitSnapshot: 3);

        VisitZeroClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Visit",
            "Zero",
            "+380 67 600 03 03",
            "BL-VISIT-ZERO");
        VisitZeroMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            VisitZeroClientId,
            membershipTypeId,
            "Zero-remaining snapshot",
            visitsLimitSnapshot: 0);

        VisitNoMembershipClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Visit",
            "NoMembership",
            "+380 67 600 04 04",
            "BL-VISIT-NONE");

        VisitStaleClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Visit",
            "Stale",
            "+380 67 600 05 05",
            "BL-VISIT-STALE");
        VisitStaleMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            VisitStaleClientId,
            membershipTypeId,
            "Single-visit stale snapshot",
            visitsLimitSnapshot: 1);

        VisitFreezeClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Visit",
            "Freeze",
            "+380 67 600 06 06",
            "BL-VISIT-FREEZE");
        VisitFreezeMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            VisitFreezeClientId,
            membershipTypeId,
            "Freeze-block snapshot",
            visitsLimitSnapshot: 2);

        VisitAdminClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Visit",
            "Admin",
            "+380 67 600 07 07",
            "BL-VISIT-ADMIN");
        VisitAdminMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            VisitAdminClientId,
            membershipTypeId,
            "Admin cancel snapshot",
            visitsLimitSnapshot: 2);
    }

    private async Task SeedPaymentHistoryFixtureAsync(
        PostgreSqlSmokeDatabase database,
        Guid ownerAccountId,
        Guid membershipTypeId)
    {
        PaymentHistoryClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Payment",
            "History",
            "+380 67 700 01 01",
            "BL-PAYMENT-HISTORY");
        PaymentHistoryMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            PaymentHistoryClientId,
            membershipTypeId,
            "Payment history snapshot",
            visitsLimitSnapshot: 8);
        await database.SeedPaymentHistoryAsync(
            ownerAccountId,
            PaymentHistoryClientId,
            PaymentHistoryMembershipId);
    }

    private async Task SeedDailyReportFixtureAsync(
        PostgreSqlSmokeDatabase database,
        Guid ownerAccountId)
    {
        DailyReportBusinessDate = new DateOnly(2026, 6, 15);
        DailyReportClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Report",
            "Daily",
            "+380 67 700 01 03",
            "BL-DAILY-REPORT");
        await database.SeedDailyReportAsync(
            ownerAccountId,
            DailyReportClientId,
            DailyReportBusinessDate);
    }

    private async Task<EndingSoonReportSmokeScenario>
        SeedEndingSoonReportScenarioAsync()
    {
        if (_ownerAccountId == Guid.Empty || _activeMembershipTypeId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The ending-soon report fixture dependencies are not initialized.");
        }

        const int pageSize = 10;
        const int totalMemberships = 11;
        const int durationDays = 30;
        var asOfDate = new DateOnly(2050, 3, 10);
        var database = RequireDatabase();
        Guid zeroRemainingClientId = Guid.Empty;
        Guid extensionClientId = Guid.Empty;

        for (var index = 1; index <= totalMemberships; index++)
        {
            var clientId = await database.SeedClientAsync(
                _ownerAccountId,
                "Ending",
                $"Client {index:00}",
                $"+380 67 820 00 {index:00}",
                $"BL-ENDING-{index:00}");
            var daysLeft = (index - 1) % 8;
            var hasExtension = index == 5;
            var baseDaysLeft = hasExtension ? daysLeft - 2 : daysLeft;
            var membershipId = await database.SeedIssuedMembershipAsync(
                _ownerAccountId,
                clientId,
                _activeMembershipTypeId,
                $"Ending plan {index:00}",
                visitsLimitSnapshot: index == 1 ? 0 : 8,
                startDate: asOfDate
                    .AddDays(baseDaysLeft)
                    .AddDays(-(durationDays - 1)),
                durationDays: durationDays);

            if (index == 1)
            {
                zeroRemainingClientId = clientId;
            }

            if (hasExtension)
            {
                extensionClientId = clientId;
                await database.SeedEndingSoonFreezeAsync(
                    _ownerAccountId,
                    clientId,
                    membershipId,
                    asOfDate.AddDays(-5),
                    asOfDate.AddDays(-4));
            }
        }

        return new EndingSoonReportSmokeScenario(
            asOfDate,
            pageSize,
            totalMemberships,
            "Ending Client 01",
            zeroRemainingClientId,
            "Ending Client 05",
            extensionClientId,
            ExtensionEffectiveEndDate: new DateOnly(2050, 3, 14));
    }

    private async Task<LowRemainingReportSmokeScenario>
        SeedLowRemainingReportScenarioAsync()
    {
        if (_ownerAccountId == Guid.Empty || _activeMembershipTypeId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The low-remaining report fixture dependencies are not initialized.");
        }

        const int pageSize = 10;
        const int scenarioMemberships = 11;
        var asOfDate = new DateOnly(2051, 4, 15);
        var firstVisitAt = new DateTimeOffset(
            2051,
            4,
            8,
            9,
            0,
            0,
            TimeSpan.Zero);
        var database = RequireDatabase();
        Guid negativeClientId = Guid.Empty;
        Guid zeroClientId = Guid.Empty;
        Guid oneRemainingClientId = Guid.Empty;

        for (var index = 1; index <= scenarioMemberships; index++)
        {
            var clientId = await database.SeedClientAsync(
                _ownerAccountId,
                "Low",
                $"Client {index:00}",
                $"+380 67 830 00 {index:00}",
                $"BL-LOW-{index:00}");
            var visitsLimit = index switch
            {
                1 => 3,
                2 => 2,
                3 => 3,
                _ => 2,
            };
            var countedVisits = index switch
            {
                1 => 5,
                2 or 3 => 2,
                _ => 0,
            };
            var membershipId = await database.SeedIssuedMembershipAsync(
                _ownerAccountId,
                clientId,
                _activeMembershipTypeId,
                $"Low visits plan {index:00}",
                visitsLimitSnapshot: visitsLimit,
                startDate: asOfDate.AddDays(-10),
                durationDays: 60);

            if (countedVisits > 0)
            {
                await database.SeedCountedMembershipVisitsAsync(
                    _ownerAccountId,
                    clientId,
                    membershipId,
                    firstVisitAt.AddDays(index - 1),
                    countedVisits);
            }

            switch (index)
            {
                case 1:
                    negativeClientId = clientId;
                    break;
                case 2:
                    zeroClientId = clientId;
                    break;
                case 3:
                    oneRemainingClientId = clientId;
                    break;
            }
        }

        return new LowRemainingReportSmokeScenario(
            asOfDate,
            pageSize,
            await database.CountLowRemainingMembershipsAsync(threshold: 2),
            await database.CountLowRemainingMembershipsAsync(threshold: 1),
            "Low Client 01",
            negativeClientId,
            "Low Client 02",
            zeroClientId,
            "Low Client 03",
            oneRemainingClientId,
            OneRemainingLastVisitAt: firstVisitAt.AddDays(2).AddHours(1));
    }

    private async Task<NegativeClientsReportSmokeScenario>
        SeedNegativeClientsReportScenarioAsync()
    {
        if (_ownerAccountId == Guid.Empty || _activeMembershipTypeId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The negative-clients report fixture dependencies are not initialized.");
        }

        const int pageSize = 10;
        const int visitDerivedMemberships = 11;
        var asOfDate = new DateOnly(2052, 5, 20);
        var firstVisitAt = new DateTimeOffset(
            2052,
            5,
            5,
            9,
            0,
            0,
            TimeSpan.Zero);
        var database = RequireDatabase();
        Guid featuredClientId = Guid.Empty;
        Guid featuredFirstNegativeVisitId = Guid.Empty;

        for (var index = 1; index <= visitDerivedMemberships; index++)
        {
            var clientId = await database.SeedClientAsync(
                _ownerAccountId,
                "Negative",
                $"Client {index:00}",
                $"+380 67 840 00 {index:00}",
                $"BL-NEGATIVE-{index:00}");
            var membershipId = await database.SeedIssuedMembershipAsync(
                _ownerAccountId,
                clientId,
                _activeMembershipTypeId,
                $"Negative plan {index:00}",
                visitsLimitSnapshot: 2,
                startDate: asOfDate.AddDays(-30),
                durationDays: 90);
            var countedVisits = index == 1 ? 5 : 4;
            var visitIds = await database.SeedCountedMembershipVisitsAsync(
                _ownerAccountId,
                clientId,
                membershipId,
                firstVisitAt.AddDays(index - 1),
                countedVisits);

            if (index == 1)
            {
                featuredClientId = clientId;
                featuredFirstNegativeVisitId = visitIds[2];
            }
        }

        var openingClientId = await database.SeedClientAsync(
            _ownerAccountId,
            "Negative",
            "Opening",
            "+380 67 840 00 12",
            "BL-NEGATIVE-OPENING");
        var openingMembershipId = await database.SeedIssuedMembershipAsync(
            _ownerAccountId,
            openingClientId,
            _activeMembershipTypeId,
            "Negative opening plan",
            visitsLimitSnapshot: 8,
            startDate: asOfDate.AddDays(-30),
            durationDays: 90);
        await database.SeedNegativeMembershipOpeningStateAsync(
            _ownerAccountId,
            openingMembershipId,
            asOfDate.AddDays(-1),
            declaredRemainingVisits: -1,
            declaredNegativeBalance: 1);

        return new NegativeClientsReportSmokeScenario(
            asOfDate,
            pageSize,
            await database.CountNegativeMembershipsAsync(),
            "Negative Client 01",
            featuredClientId,
            featuredFirstNegativeVisitId,
            FeaturedFirstNegativeVisitDate: new DateOnly(2052, 5, 5),
            FeaturedLastCountedVisitAt: firstVisitAt.AddHours(4),
            FeaturedEffectiveEndDate: new DateOnly(2052, 7, 18),
            OpeningClientDisplayName: "Negative Opening",
            OpeningClientId: openingClientId);
    }

    private async Task<InactiveClientsReportSmokeScenario>
        SeedInactiveClientsReportScenarioAsync()
    {
        if (_ownerAccountId == Guid.Empty || _activeMembershipTypeId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The inactive-clients report fixture dependencies are not initialized.");
        }

        const int pageSize = 10;
        int[] daysInactive = [60, 45, 30, 29, 25, 21, 20, 18, 17, 15, 14];
        var asOfDate = new DateOnly(2025, 5, 20);
        var database = RequireDatabase();
        Guid featuredClientId = Guid.Empty;
        Guid featuredLastVisitId = Guid.Empty;
        Guid featuredMembershipId = Guid.Empty;
        Guid lastMembershipClientId = Guid.Empty;
        Guid lastMembershipId = Guid.Empty;
        Guid boundaryClientId = Guid.Empty;

        for (var index = 1; index <= daysInactive.Length; index++)
        {
            var clientId = await database.SeedClientAsync(
                _ownerAccountId,
                "Inactive",
                $"Client {index:00}",
                $"+380 67 850 00 {index:00}",
                $"BL-INACTIVE-{index:00}",
                operationalStatus: index == 1 ? "inactive" : "active");
            var occurredAt = new DateTimeOffset(
                asOfDate.AddDays(-daysInactive[index - 1])
                    .ToDateTime(new TimeOnly(9, index), DateTimeKind.Utc));
            var visitId = await database.SeedClientActivityVisitAsync(
                _ownerAccountId,
                clientId,
                occurredAt,
                visitKind: index == 1 ? "trial" : "one_off");

            if (index == 1)
            {
                featuredClientId = clientId;
                featuredLastVisitId = visitId;
                featuredMembershipId = await database.SeedIssuedMembershipAsync(
                    _ownerAccountId,
                    clientId,
                    _activeMembershipTypeId,
                    "Inactive current plan",
                    visitsLimitSnapshot: 8,
                    startDate: asOfDate.AddDays(-10),
                    durationDays: 90,
                    issuedAt: new DateTimeOffset(
                        asOfDate.AddDays(-11)
                            .ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc)));
                await database.SeedClientActivityVisitAsync(
                    _ownerAccountId,
                    clientId,
                    new DateTimeOffset(
                        asOfDate.AddDays(-1)
                            .ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc)),
                    visitKind: "one_off",
                    status: "canceled");
            }
            else if (index == 2)
            {
                lastMembershipClientId = clientId;
                lastMembershipId = await database.SeedIssuedMembershipAsync(
                    _ownerAccountId,
                    clientId,
                    _activeMembershipTypeId,
                    "Inactive last plan",
                    visitsLimitSnapshot: 6,
                    startDate: asOfDate.AddDays(-90),
                    durationDays: 30,
                    issuedAt: new DateTimeOffset(
                        asOfDate.AddDays(-91)
                            .ToDateTime(new TimeOnly(10, 0), DateTimeKind.Utc)));
            }

            if (index == daysInactive.Length)
            {
                boundaryClientId = clientId;
            }
        }

        var neverVisitedClientId = await database.SeedClientAsync(
            _ownerAccountId,
            "Absent",
            "Never",
            "+380 67 850 00 12",
            "BL-INACTIVE-NEVER");

        return new InactiveClientsReportSmokeScenario(
            asOfDate,
            pageSize,
            KnownInactiveClientCount: daysInactive.Length,
            ThresholdThirtyClientCount: 3,
            ThresholdSixtyClientCount: 1,
            FeaturedClientDisplayName: "Inactive Client 01",
            FeaturedClientId: featuredClientId,
            FeaturedLastVisitId: featuredLastVisitId,
            FeaturedLastVisitAt: new DateTimeOffset(
                asOfDate.AddDays(-60)
                    .ToDateTime(new TimeOnly(9, 1), DateTimeKind.Utc)),
            FeaturedMembershipId: featuredMembershipId,
            FeaturedEffectiveEndDate: asOfDate.AddDays(79),
            LastMembershipClientDisplayName: "Inactive Client 02",
            LastMembershipClientId: lastMembershipClientId,
            LastMembershipId: lastMembershipId,
            BoundaryClientDisplayName: "Inactive Client 11",
            BoundaryClientId: boundaryClientId,
            NeverVisitedClientDisplayName: "Absent Never",
            NeverVisitedClientId: neverVisitedClientId);
    }

    private static async Task SeedMembershipExtensionHistoryFixtureAsync(
        PostgreSqlSmokeDatabase database,
        Guid ownerAccountId,
        Guid membershipTypeId)
    {
        var clientId = await database.SeedClientAsync(
            ownerAccountId,
            "Extension",
            "History",
            "+380 67 700 01 02",
            "BL-EXTENSION-HISTORY");
        var membershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            clientId,
            membershipTypeId,
            "Extended membership snapshot",
            visitsLimitSnapshot: 8);
        await database.SeedMembershipExtensionHistoryAsync(
            ownerAccountId,
            clientId,
            membershipId);
    }

    private async Task SeedAddPaymentFixturesAsync(
        PostgreSqlSmokeDatabase database,
        Guid ownerAccountId,
        Guid membershipTypeId)
    {
        PaymentTabletClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Payment",
            "Tablet",
            "+380 67 700 02 01",
            "BL-PAYMENT-TABLET");
        PaymentTabletMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            PaymentTabletClientId,
            membershipTypeId,
            "Payment tablet snapshot",
            visitsLimitSnapshot: 6);

        PaymentPhoneClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Payment",
            "Phone",
            "+380 67 700 02 02",
            "BL-PAYMENT-PHONE");
    }

    private async Task SeedAddFreezeFixturesAsync(
        PostgreSqlSmokeDatabase database,
        Guid ownerAccountId,
        Guid membershipTypeId)
    {
        FreezeTabletClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Freeze",
            "Tablet",
            "+380 67 700 03 01",
            "BL-FREEZE-TABLET");
        FreezeTabletMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            FreezeTabletClientId,
            membershipTypeId,
            "Freeze tablet snapshot",
            visitsLimitSnapshot: 8);

        FreezePhoneClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Freeze",
            "Phone",
            "+380 67 700 03 02",
            "BL-FREEZE-PHONE");
        FreezePhoneMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            FreezePhoneClientId,
            membershipTypeId,
            "Freeze phone snapshot",
            visitsLimitSnapshot: 8);
    }

    private async Task SeedCancelFreezeFixturesAsync(
        PostgreSqlSmokeDatabase database,
        Guid ownerAccountId,
        Guid membershipTypeId)
    {
        CancelFreezeTabletClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Cancel",
            "Freeze Tablet",
            "+380 67 700 04 01",
            "BL-CANCEL-FREEZE-TABLET");
        CancelFreezeTabletMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            CancelFreezeTabletClientId,
            membershipTypeId,
            "Cancelable tablet snapshot",
            visitsLimitSnapshot: 8);
        CancelFreezeTabletFreezeId = await database.SeedCancelableFreezeAsync(
            ownerAccountId,
            CancelFreezeTabletClientId,
            CancelFreezeTabletMembershipId,
            "Tablet schedule changed");

        CancelFreezePhoneClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Cancel",
            "Freeze Phone",
            "+380 67 700 04 02",
            "BL-CANCEL-FREEZE-PHONE");
        CancelFreezePhoneMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            CancelFreezePhoneClientId,
            membershipTypeId,
            "Cancelable phone snapshot",
            visitsLimitSnapshot: 8);
        CancelFreezePhoneFreezeId = await database.SeedCancelableFreezeAsync(
            ownerAccountId,
            CancelFreezePhoneClientId,
            CancelFreezePhoneMembershipId,
            "Phone schedule changed");
    }

    private async Task SeedIssueMembershipFixturesAsync(
        PostgreSqlSmokeDatabase database,
        Guid ownerAccountId,
        Guid membershipTypeId)
    {
        IssueMembershipTypeId = membershipTypeId;
        IssueTabletClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Issue",
            "Tablet",
            "+380 67 800 01 01",
            "BL-ISSUE-TABLET");

        IssuePhoneClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Issue",
            "Phone",
            "+380 67 800 01 02",
            "BL-ISSUE-PHONE");
        IssuePhoneExistingMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            IssuePhoneClientId,
            membershipTypeId,
            "Existing negative snapshot",
            visitsLimitSnapshot: 0);
        await database.InsertExternalCountedVisitAsync(
            IssuePhoneClientId,
            IssuePhoneExistingMembershipId);
    }

    private async Task SeedNonWorkingDayPreviewFixturesAsync(
        PostgreSqlSmokeDatabase database,
        Guid ownerAccountId,
        Guid membershipTypeId)
    {
        NonWorkingDayPreviewStartDate = new DateOnly(2040, 2, 1);
        NonWorkingDayPreviewEndDate = new DateOnly(2040, 2, 3);

        NonWorkingDayEndBoundaryClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Closure",
            "End Boundary",
            "+380 67 900 01 01",
            "BL-CLOSURE-END");
        NonWorkingDayEndBoundaryMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            NonWorkingDayEndBoundaryClientId,
            membershipTypeId,
            "Closure end-boundary snapshot",
            visitsLimitSnapshot: 8,
            startDate: new DateOnly(2040, 1, 23),
            durationDays: 10);
        await database.SeedCancelableFreezeAsync(
            ownerAccountId,
            NonWorkingDayEndBoundaryClientId,
            NonWorkingDayEndBoundaryMembershipId,
            "Scheduled equipment pause",
            startDate: new DateOnly(2040, 2, 2),
            endDate: new DateOnly(2040, 2, 2));

        NonWorkingDayStartBoundaryClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Closure",
            "Start Boundary",
            "+380 67 900 01 02",
            "BL-CLOSURE-START");
        NonWorkingDayStartBoundaryMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            NonWorkingDayStartBoundaryClientId,
            membershipTypeId,
            "Closure start-boundary snapshot",
            visitsLimitSnapshot: 8,
            startDate: new DateOnly(2040, 2, 3),
            durationDays: 10);

        var noOverlapClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Closure",
            "No Overlap",
            "+380 67 900 01 03",
            "BL-CLOSURE-NONE");
        NonWorkingDayNoOverlapMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            noOverlapClientId,
            membershipTypeId,
            "Closure no-overlap snapshot",
            visitsLimitSnapshot: 8,
            startDate: new DateOnly(2040, 2, 4),
            durationDays: 10);
    }

    private async Task SeedAddNonWorkingDayFixturesAsync(
        PostgreSqlSmokeDatabase database,
        Guid ownerAccountId,
        Guid membershipTypeId)
    {
        NonWorkingDayTabletAddScenario = await SeedAddNonWorkingDayScenarioAsync(
            database,
            ownerAccountId,
            membershipTypeId,
            "Tablet",
            new DateRange(
                new DateOnly(2041, 4, 10),
                new DateOnly(2041, 4, 12)),
            "+380 67 910 01");
        NonWorkingDayPhoneAddScenario = await SeedAddNonWorkingDayScenarioAsync(
            database,
            ownerAccountId,
            membershipTypeId,
            "Phone",
            new DateRange(
                new DateOnly(2042, 5, 20),
                new DateOnly(2042, 5, 22)),
            "+380 67 920 01");
    }

    private static async Task<NonWorkingDayAddSmokeScenario>
        SeedAddNonWorkingDayScenarioAsync(
            PostgreSqlSmokeDatabase database,
            Guid ownerAccountId,
            Guid membershipTypeId,
            string viewportLabel,
            DateRange period,
            string phonePrefix)
    {
        var slug = viewportLabel.ToLowerInvariant();
        var endBoundaryClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Confirm",
            $"{viewportLabel} End",
            $"{phonePrefix} 01",
            $"BL-NWD-{slug}-END");
        var endBoundaryMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            endBoundaryClientId,
            membershipTypeId,
            $"NonWorkingDay {slug} end-boundary snapshot",
            visitsLimitSnapshot: 8,
            startDate: period.StartDate.AddDays(-9),
            durationDays: 10);

        var startBoundaryClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Confirm",
            $"{viewportLabel} Start",
            $"{phonePrefix} 02",
            $"BL-NWD-{slug}-START");
        var startBoundaryMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            startBoundaryClientId,
            membershipTypeId,
            $"NonWorkingDay {slug} start-boundary snapshot",
            visitsLimitSnapshot: 8,
            startDate: period.EndDate,
            durationDays: 10);

        var scopeEntrantClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Confirm",
            $"{viewportLabel} Entrant",
            $"{phonePrefix} 03",
            $"BL-NWD-{slug}-ENTRANT");
        var scopeEntrantMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            scopeEntrantClientId,
            membershipTypeId,
            $"NonWorkingDay {slug} entrant snapshot",
            visitsLimitSnapshot: 8,
            startDate: period.EndDate.AddDays(1),
            durationDays: 10);

        return new NonWorkingDayAddSmokeScenario(
            viewportLabel,
            period,
            ReasonCode: $"planned_closure_{slug}",
            ReasonComment: $"{viewportLabel} confirmed closure",
            endBoundaryClientId,
            endBoundaryMembershipId,
            startBoundaryClientId,
            startBoundaryMembershipId,
            scopeEntrantClientId,
            scopeEntrantMembershipId);
    }

    private async Task SeedCorrectNonWorkingDayFixtureAsync(
        PostgreSqlSmokeDatabase database,
        Guid ownerAccountId,
        Guid membershipTypeId)
    {
        var originalPeriod = new DateRange(
            new DateOnly(2043, 3, 10),
            new DateOnly(2043, 3, 12));
        var replacementPeriod = new DateRange(
            new DateOnly(2043, 3, 20),
            new DateOnly(2043, 3, 22));

        var sharedClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Correction",
            "Shared Scope",
            "+380 67 930 01 01",
            "BL-NWD-CORRECT-SHARED");
        var sharedMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            sharedClientId,
            membershipTypeId,
            "NonWorkingDay correction shared snapshot",
            visitsLimitSnapshot: 8,
            startDate: new DateOnly(2043, 3, 1),
            durationDays: 40);

        var originalOnlyClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Correction",
            "Original Only",
            "+380 67 930 01 02",
            "BL-NWD-CORRECT-OLD");
        var originalOnlyMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            originalOnlyClientId,
            membershipTypeId,
            "NonWorkingDay correction original-only snapshot",
            visitsLimitSnapshot: 8,
            startDate: new DateOnly(2043, 2, 20),
            durationDays: 22);

        var replacementOnlyClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Correction",
            "Replacement Only",
            "+380 67 930 01 03",
            "BL-NWD-CORRECT-NEW");
        var replacementOnlyMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            replacementOnlyClientId,
            membershipTypeId,
            "NonWorkingDay correction replacement-only snapshot",
            visitsLimitSnapshot: 8,
            startDate: replacementPeriod.StartDate,
            durationDays: 30);

        var periodId = await database.SeedNonWorkingDayCorrectionPeriodAsync(
            ownerAccountId,
            originalPeriod,
            "planned_maintenance",
            "Original ventilation closure",
            [
                new NonWorkingDayApplicationSmokeSeed(
                    sharedClientId,
                    sharedMembershipId),
                new NonWorkingDayApplicationSmokeSeed(
                    originalOnlyClientId,
                    originalOnlyMembershipId),
            ]);

        NonWorkingDayCorrectionScenario = new NonWorkingDayCorrectionSmokeScenario(
            periodId,
            originalPeriod,
            replacementPeriod,
            OriginalReasonCode: "planned_maintenance",
            OriginalReasonComment: "Original ventilation closure",
            ReplacementReasonCode: "rescheduled_maintenance",
            ReplacementReasonComment: "Rescheduled ventilation closure",
            CorrectionReason: "Owner corrected the closure plan",
            CorrectionComment: "Reviewed exact old and new scope",
            sharedClientId,
            sharedMembershipId,
            originalOnlyClientId,
            originalOnlyMembershipId,
            replacementOnlyClientId,
            replacementOnlyMembershipId);
    }

    private async Task SeedCorrectNonWorkingDayMutationFixturesAsync(
        PostgreSqlSmokeDatabase database,
        Guid ownerAccountId,
        Guid membershipTypeId)
    {
        NonWorkingDayTabletCorrectionMutationScenario =
            await SeedCorrectNonWorkingDayMutationScenarioAsync(
                database,
                ownerAccountId,
                membershipTypeId,
                "Tablet",
                "TABLET",
                2044,
                phoneSuffix: 40);
        NonWorkingDayPhoneCorrectionMutationScenario =
            await SeedCorrectNonWorkingDayMutationScenarioAsync(
                database,
                ownerAccountId,
                membershipTypeId,
                "Phone",
                "PHONE",
                2045,
                phoneSuffix: 50);
        NonWorkingDayReasonCorrectionMutationScenario =
            await SeedCorrectNonWorkingDayMutationScenarioAsync(
                database,
                ownerAccountId,
                membershipTypeId,
                "Reason",
                "REASON",
                2047,
                phoneSuffix: 70);
        NonWorkingDayStaleCorrectionMutationScenario =
            await SeedCorrectNonWorkingDayMutationScenarioAsync(
                database,
                ownerAccountId,
                membershipTypeId,
                "Stale",
                "STALE",
                2046,
                phoneSuffix: 60);
    }

    private static async Task<NonWorkingDayCorrectionMutationSmokeScenario>
        SeedCorrectNonWorkingDayMutationScenarioAsync(
            PostgreSqlSmokeDatabase database,
            Guid ownerAccountId,
            Guid membershipTypeId,
            string label,
            string slug,
            int year,
            int phoneSuffix)
    {
        var originalPeriod = new DateRange(
            new DateOnly(year, 3, 10),
            new DateOnly(year, 3, 12));
        var replacementPeriod = new DateRange(
            new DateOnly(year, 3, 20),
            new DateOnly(year, 3, 22));

        var sharedClientId = await database.SeedClientAsync(
            ownerAccountId,
            $"Correction {label}",
            "Shared",
            $"+380 67 940 {phoneSuffix:00} 01",
            $"NWC-{slug}-S");
        var sharedMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            sharedClientId,
            membershipTypeId,
            $"NonWorkingDay {label} shared snapshot",
            visitsLimitSnapshot: 8,
            startDate: new DateOnly(year, 3, 1),
            durationDays: 40);

        var originalOnlyClientId = await database.SeedClientAsync(
            ownerAccountId,
            $"Correction {label}",
            "Original",
            $"+380 67 940 {phoneSuffix:00} 02",
            $"NWC-{slug}-O");
        var originalOnlyMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            originalOnlyClientId,
            membershipTypeId,
            $"NonWorkingDay {label} original-only snapshot",
            visitsLimitSnapshot: 8,
            startDate: new DateOnly(year, 2, 20),
            durationDays: 22);

        var replacementOnlyClientId = await database.SeedClientAsync(
            ownerAccountId,
            $"Correction {label}",
            "Replacement",
            $"+380 67 940 {phoneSuffix:00} 03",
            $"NWC-{slug}-N");
        var replacementOnlyMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            replacementOnlyClientId,
            membershipTypeId,
            $"NonWorkingDay {label} replacement-only snapshot",
            visitsLimitSnapshot: 8,
            startDate: replacementPeriod.StartDate,
            durationDays: 30);

        var scopeEntrantClientId = await database.SeedClientAsync(
            ownerAccountId,
            $"Correction {label}",
            "Entrant",
            $"+380 67 940 {phoneSuffix:00} 04",
            $"NWC-{slug}-E");
        var scopeEntrantMembershipId = await database.SeedIssuedMembershipAsync(
            ownerAccountId,
            scopeEntrantClientId,
            membershipTypeId,
            $"NonWorkingDay {label} entrant snapshot",
            visitsLimitSnapshot: 8,
            startDate: replacementPeriod.EndDate.AddDays(1),
            durationDays: 20);

        var originalReasonCode = $"planned_{slug.ToLowerInvariant()}_closure";
        var originalReasonComment = $"Original {label} closure";
        var periodId = await database.SeedNonWorkingDayCorrectionPeriodAsync(
            ownerAccountId,
            originalPeriod,
            originalReasonCode,
            originalReasonComment,
            [
                new NonWorkingDayApplicationSmokeSeed(
                    sharedClientId,
                    sharedMembershipId),
                new NonWorkingDayApplicationSmokeSeed(
                    originalOnlyClientId,
                    originalOnlyMembershipId),
            ]);

        return new NonWorkingDayCorrectionMutationSmokeScenario(
            label,
            periodId,
            originalPeriod,
            replacementPeriod,
            originalReasonCode,
            originalReasonComment,
            ReplacementReasonCode: $"corrected_{slug.ToLowerInvariant()}_closure",
            ReplacementReasonComment: $"Corrected {label} closure",
            CorrectionReason: $"Owner corrected the {label} closure",
            CorrectionComment: $"Confirmed {label} old and new scope",
            sharedClientId,
            sharedMembershipId,
            originalOnlyClientId,
            originalOnlyMembershipId,
            replacementOnlyClientId,
            replacementOnlyMembershipId,
            scopeEntrantClientId,
            scopeEntrantMembershipId);
    }

    private async Task SeedReceptionClientsAsync(
        PostgreSqlSmokeDatabase database,
        Guid ownerAccountId)
    {
        await database.SeedClientAsync(
            ownerAccountId,
            "Kovalenko",
            "Olena",
            "+380 67 111 22 33",
            "BL-1001",
            "Prefers morning visits.");
        await database.SeedClientAsync(
            ownerAccountId,
            "Kovalenko",
            "Marta",
            "+380 67 222 44 55",
            "BL-2001");
        await database.SeedClientAsync(
            ownerAccountId,
            "Kovalenko",
            "Taras",
            "+380 67 333 66 77",
            cardNumber: null);
        await database.SeedClientAsync(
            ownerAccountId,
            "Dormant",
            "Client",
            "+380 67 444 88 99",
            cardNumber: null,
            operationalStatus: "inactive");
        TabletEditableClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Edit",
            "Tablet",
            "+380 67 555 01 01",
            "BL-EDIT-TABLET",
            "Tablet source.");
        PhoneEditableClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Edit",
            "Phone",
            "+380 67 555 02 02",
            "BL-EDIT-PHONE",
            "Phone source.");
        StaleEditableClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Stale",
            "Target",
            "+380 67 555 03 03",
            "BL-EDIT-STALE",
            "Stale source.");
        await database.SeedClientAsync(
            ownerAccountId,
            "Duplicate",
            "TabletMatch",
            "+380 67 777 88 91",
            "BL-DUP-TABLET");
        await database.SeedClientAsync(
            ownerAccountId,
            "Duplicate",
            "PhoneMatch",
            "+380 67 777 88 92",
            "BL-DUP-PHONE");
        await database.SeedClientAsync(
            ownerAccountId,
            "CreateDuplicate",
            "Tablet",
            "+380 67 777 88 93",
            "BL-CREATE-DUP");
        await database.SeedClientAsync(
            ownerAccountId,
            "Create",
            "Occupied",
            "+380 67 777 88 94",
            "BL-CREATE-TAKEN");
        CardChangeClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Card",
            "Change",
            "+380 67 555 04 04",
            "BL-CARD-OLD");
        CardAssignClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Cardless",
            "Phone",
            "+380 67 555 05 05",
            cardNumber: null);
        CardStaleClientId = await database.SeedClientAsync(
            ownerAccountId,
            "Card",
            "Stale",
            "+380 67 555 06 06",
            "BL-CARD-STALE");
        await database.SeedClientAsync(
            ownerAccountId,
            "Card",
            "Occupied",
            "+380 67 555 07 07",
            "BL-CARD-TAKEN");
    }

    private PostgreSqlSmokeDatabase RequireDatabase()
    {
        return _database
            ?? throw new InvalidOperationException("The UI smoke database is not initialized.");
    }

    private static int FindAvailablePort()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 0);
        using var listener = new TcpListener(endpoint);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BodyLife.Crm.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing BodyLife.Crm.sln.");
    }

    private void EnqueueOutput(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            _output.Enqueue(line);
        }
    }

    private string CapturedOutput()
    {
        return string.Join(Environment.NewLine, _output.TakeLast(80));
    }
}

public sealed record NonWorkingDayAddSmokeScenario(
    string ViewportLabel,
    DateRange Period,
    string ReasonCode,
    string ReasonComment,
    Guid EndBoundaryClientId,
    Guid EndBoundaryMembershipId,
    Guid StartBoundaryClientId,
    Guid StartBoundaryMembershipId,
    Guid ScopeEntrantClientId,
    Guid ScopeEntrantMembershipId)
{
    public string EndBoundaryClientDisplayName => $"Confirm {ViewportLabel} End";

    public string StartBoundaryClientDisplayName => $"Confirm {ViewportLabel} Start";

    public string ScopeEntrantClientDisplayName => $"Confirm {ViewportLabel} Entrant";
}

public sealed record EndingSoonReportSmokeScenario(
    DateOnly AsOfDate,
    int PageSize,
    int TotalMemberships,
    string ZeroRemainingClientDisplayName,
    Guid ZeroRemainingClientId,
    string ExtensionClientDisplayName,
    Guid ExtensionClientId,
    DateOnly ExtensionEffectiveEndDate);

public sealed record LowRemainingReportSmokeScenario(
    DateOnly AsOfDate,
    int PageSize,
    int TotalMemberships,
    int ThresholdOneMemberships,
    string NegativeClientDisplayName,
    Guid NegativeClientId,
    string ZeroClientDisplayName,
    Guid ZeroClientId,
    string OneRemainingClientDisplayName,
    Guid OneRemainingClientId,
    DateTimeOffset OneRemainingLastVisitAt);

public sealed record NegativeClientsReportSmokeScenario(
    DateOnly AsOfDate,
    int PageSize,
    int TotalMemberships,
    string FeaturedClientDisplayName,
    Guid FeaturedClientId,
    Guid FeaturedFirstNegativeVisitId,
    DateOnly FeaturedFirstNegativeVisitDate,
    DateTimeOffset FeaturedLastCountedVisitAt,
    DateOnly FeaturedEffectiveEndDate,
    string OpeningClientDisplayName,
    Guid OpeningClientId);

public sealed record InactiveClientsReportSmokeScenario(
    DateOnly AsOfDate,
    int PageSize,
    int KnownInactiveClientCount,
    int ThresholdThirtyClientCount,
    int ThresholdSixtyClientCount,
    string FeaturedClientDisplayName,
    Guid FeaturedClientId,
    Guid FeaturedLastVisitId,
    DateTimeOffset FeaturedLastVisitAt,
    Guid FeaturedMembershipId,
    DateOnly FeaturedEffectiveEndDate,
    string LastMembershipClientDisplayName,
    Guid LastMembershipClientId,
    Guid LastMembershipId,
    string BoundaryClientDisplayName,
    Guid BoundaryClientId,
    string NeverVisitedClientDisplayName,
    Guid NeverVisitedClientId);

public sealed record AuditTimelineSmokeScenario(
    Guid ClientId,
    DateOnly RecordedDate,
    int PageSize,
    int TotalEntries,
    Guid FeaturedAuditEntryId,
    Guid FeaturedEntityId,
    Guid SharedAccountId,
    Guid SharedSessionId,
    string SharedDeviceLabel,
    DateTimeOffset FeaturedOccurredAt,
    DateTimeOffset FeaturedRecordedAt,
    string FeaturedCorrelationId,
    AuditExplanationSmokeScenario Explanations);

public sealed record AuditExplanationSmokeScenario(
    Guid VisitCancellationAuditEntryId,
    Guid PaymentCorrectionAuditEntryId,
    Guid PaymentCancellationAuditEntryId,
    Guid MembershipTypeEditAuditEntryId,
    Guid MembershipTypeDeactivationAuditEntryId,
    string OriginalMembershipTypeName,
    string UpdatedMembershipTypeName,
    decimal OriginalMembershipTypePrice,
    decimal UpdatedMembershipTypePrice,
    decimal OriginalPaymentAmount,
    decimal ReplacementPaymentAmount,
    decimal CanceledPaymentAmount,
    int BeforeVisitRemaining,
    int AfterVisitRemaining,
    NonWorkingDayAuditExplanationSmokeScenario NonWorkingDays,
    FreezeCancellationAuditExplanationSmokeScenario FreezeCancellation,
    ClientCardAuditExplanationSmokeScenario ClientAndCards);

public sealed record NonWorkingDayAuditExplanationSmokeScenario(
    Guid CorrectedAuditEntryId,
    Guid CorrectedOriginalPeriodId,
    DateRange CorrectedOriginalPeriod,
    DateRange CorrectedReplacementPeriod,
    int CorrectedOldAffectedCount,
    int CorrectedNewAffectedCount,
    int CorrectedAffectedUnionCount,
    Guid CanceledAuditEntryId,
    Guid CanceledOriginalPeriodId,
    DateRange CanceledPeriod,
    int CanceledAffectedCount);

public sealed record FreezeCancellationAuditExplanationSmokeScenario(
    Guid AuditEntryId,
    Guid FreezeId,
    DateRange Range,
    string Reason,
    int BeforeExtensionDays,
    int AfterExtensionDays,
    DateOnly BeforeEffectiveEndDate,
    DateOnly AfterEffectiveEndDate);

public sealed record ClientCardAuditExplanationSmokeScenario(
    Guid ClientId,
    Guid ClientUpdateAuditEntryId,
    string OriginalDisplayName,
    string UpdatedDisplayName,
    string OriginalPhone,
    string UpdatedPhone,
    Guid MatchedClientId,
    Guid CardAssignedAuditEntryId,
    Guid CardChangedAuditEntryId,
    Guid CardClearedAuditEntryId,
    string AssignedCardNumber,
    string ReplacementCardNumber);

public sealed record ClientHistorySmokeScenario(
    Guid ClientId,
    string ClientDisplayName,
    string CardNumber,
    DateOnly OccurredDate,
    int PageSize,
    int TotalEntries,
    Guid FeaturedAuditEntryId,
    Guid OriginalPaymentAuditEntryId,
    Guid SharedSessionId,
    string SharedDeviceLabel,
    DateTimeOffset FeaturedOccurredAt,
    DateTimeOffset FeaturedRecordedAt,
    decimal OriginalPaymentAmount,
    decimal ReplacementPaymentAmount);

public sealed record NonWorkingDayCorrectionSmokeScenario(
    Guid PeriodId,
    DateRange OriginalPeriod,
    DateRange ReplacementPeriod,
    string OriginalReasonCode,
    string OriginalReasonComment,
    string ReplacementReasonCode,
    string ReplacementReasonComment,
    string CorrectionReason,
    string CorrectionComment,
    Guid SharedClientId,
    Guid SharedMembershipId,
    Guid OriginalOnlyClientId,
    Guid OriginalOnlyMembershipId,
    Guid ReplacementOnlyClientId,
    Guid ReplacementOnlyMembershipId)
{
    public string SharedClientDisplayName => "Correction Shared Scope";

    public string OriginalOnlyClientDisplayName => "Correction Original Only";

    public string ReplacementOnlyClientDisplayName =>
        "Correction Replacement Only";
}

public sealed record NonWorkingDayCorrectionMutationSmokeScenario(
    string Label,
    Guid PeriodId,
    DateRange OriginalPeriod,
    DateRange ReplacementPeriod,
    string OriginalReasonCode,
    string OriginalReasonComment,
    string ReplacementReasonCode,
    string ReplacementReasonComment,
    string CorrectionReason,
    string CorrectionComment,
    Guid SharedClientId,
    Guid SharedMembershipId,
    Guid OriginalOnlyClientId,
    Guid OriginalOnlyMembershipId,
    Guid ReplacementOnlyClientId,
    Guid ReplacementOnlyMembershipId,
    Guid ScopeEntrantClientId,
    Guid ScopeEntrantMembershipId)
{
    public string SharedClientDisplayName => $"Correction {Label} Shared";

    public string OriginalOnlyClientDisplayName =>
        $"Correction {Label} Original";

    public string ReplacementOnlyClientDisplayName =>
        $"Correction {Label} Replacement";

    public string ScopeEntrantClientDisplayName =>
        $"Correction {Label} Entrant";
}
