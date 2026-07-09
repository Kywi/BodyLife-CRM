using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using BodyLife.Crm.Infrastructure.Persistence.UsersRoles;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Ui.SmokeTests;

public sealed class ReceptionAppFixture : IAsyncLifetime
{
    public const string SmokeLoginName = "owner";
    public const string SmokePassword = "correct horse battery";

    private readonly ConcurrentQueue<string> _output = new();
    private Process? _process;
    private PostgreSqlSmokeDatabase? _database;

    public Uri BaseAddress { get; private set; } = null!;

    public string LoginName => SmokeLoginName;

    public string Password => SmokePassword;

    public async Task InitializeAsync()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{FindAvailablePort()}");
        _database = await PostgreSqlSmokeDatabase.CreateAsync();
        await SeedAuthenticatedOwnerAsync(_database);

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

    private static async Task SeedAuthenticatedOwnerAsync(PostgreSqlSmokeDatabase database)
    {
        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync();

        var ownerBootstrapper = new OwnerBootstrapper(dbContext, TimeProvider.System);
        var ownerResult = await ownerBootstrapper.BootstrapOwnerAsync("BodyLife Owner");

        Assert.True(
            ownerResult.Status is OwnerBootstrapStatus.Created or OwnerBootstrapStatus.AlreadyExists,
            $"Owner bootstrap returned {ownerResult.Status}.");

        var credentialsBootstrapper = new OwnerCredentialsBootstrapper(
            dbContext,
            new PasswordHashingService(),
            TimeProvider.System);

        var credentialsResult = await credentialsBootstrapper.SetOwnerCredentialsAsync(
            SmokeLoginName,
            SmokePassword);

        Assert.Equal(OwnerCredentialsBootstrapStatus.Updated, credentialsResult.Status);
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
