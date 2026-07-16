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
    private Process? _process;
    private PostgreSqlSmokeDatabase? _database;

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
        await SeedMarkVisitFixturesAsync(
            database,
            ownerResult.AccountId.Value,
            activeMembershipTypeId);
        await SeedPaymentHistoryFixtureAsync(
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
