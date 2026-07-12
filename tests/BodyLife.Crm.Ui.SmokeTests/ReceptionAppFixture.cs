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
