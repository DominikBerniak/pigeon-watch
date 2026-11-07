using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using PigeonWatch.Data;
using PigeonWatch.Data.Storage;
using PigeonWatch.WebApi.Host;

namespace PigeonWatch.IntegrationTests;

public sealed class PigeonWatchApiFactory(TimeProvider? timeProvider = null, CultureInfo? requestCulture = null) : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = OpenConnection();
    private readonly FakeLogCollector logs = new();
    private readonly InMemorySightingPhotoStore photoStore = new();
    private int nextClientNumber;

    public FakeLogCollector Logs
    {
        get
        {
            return logs;
        }
    }

    public InMemorySightingPhotoStore PhotoStore
    {
        get
        {
            return photoStore;
        }
    }

    public HttpClient CreateApiClient(string? clientAddress = null)
    {
        HttpClient client = CreateClient();
        string address = clientAddress ?? NextClientAddress();
        client.DefaultRequestHeaders.Add(ClientAddressStartupFilter.HeaderName, address);

        return client;
    }

    public async Task<HttpResponseMessage> SendFromFreshAddressAsync(
        HttpRequestMessage request,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        using HttpClient client = CreateApiClient();

        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

        return response;
    }

    public async Task<TestUser> CreateUserAsync(string email, string password, string displayName, CancellationToken cancellationToken = default)
    {
        using HttpClient setupClient = CreateApiClient();
        AccountApiClient accounts = new(setupClient);

        HttpResponseMessage register = await accounts.RegisterAsync(email, password, displayName, cancellationToken);
        register.EnsureSuccessStatusCode();

        HttpResponseMessage login = await accounts.LoginAsync(email, password, cancellationToken);
        login.EnsureSuccessStatusCode();

        string loginBody = await login.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument tokens = JsonDocument.Parse(loginBody);
        string accessToken = tokens.RootElement.GetProperty("accessToken").GetString()!;
        string refreshToken = tokens.RootElement.GetProperty("refreshToken").GetString()!;

        using HttpRequestMessage generalRequest = new(HttpMethod.Get, "/configuration/general");
        HttpResponseMessage general = await SendFromFreshAddressAsync(generalRequest, accessToken, cancellationToken);
        general.EnsureSuccessStatusCode();

        string generalBody = await general.Content.ReadAsStringAsync(cancellationToken);
        using JsonDocument currentUser = JsonDocument.Parse(generalBody);
        Guid id = currentUser.RootElement.GetProperty("currentUser").GetProperty("id").GetGuid();

        HttpClient client = CreateApiClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        return new TestUser(email, password, displayName, id, accessToken, refreshToken, client);
    }

    public int CountUserAccounts()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM USER_ACCOUNT";
        int count = Convert.ToInt32(command.ExecuteScalar());

        return count;
    }

    public int CountSightings()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SIGHTING";
        int count = Convert.ToInt32(command.ExecuteScalar());

        return count;
    }

    public string? ReadPhotoBlobName(Guid sightingId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT PHOTO_BLOB_NAME FROM SIGHTING WHERE ID = $id";
        command.Parameters.AddWithValue("$id", sightingId);
        object? value = command.ExecuteScalar();

        return value is null or DBNull ? null : (string)value;
    }

    public int DeleteUserAccount(Guid id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM USER_ACCOUNT WHERE ID = $id";
        command.Parameters.AddWithValue("$id", id);
        int affected = command.ExecuteNonQuery();

        return affected;
    }

    public int ExpireLockout(string email)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE USER_ACCOUNT SET LOCKOUT_END = '2000-01-01 00:00:00+00:00' WHERE NORMALIZED_EMAIL = $email";
        command.Parameters.AddWithValue("$email", email.ToUpperInvariant());
        int affected = command.ExecuteNonQuery();

        return affected;
    }

    public void ClearLogs()
    {
        logs.Clear();
    }

    public void CloseDatabase()
    {
        connection.Close();
    }

    public IReadOnlyList<AuditUsers> ReadUserAccountAuditUsers()
    {
        return ReadAuditUsers("USER_ACCOUNT");
    }

    public IReadOnlyList<AuditUsers> ReadSightingAuditUsers()
    {
        return ReadAuditUsers("SIGHTING");
    }

    public IReadOnlyList<AuditUsers> ReadSightingThreadAuditUsers()
    {
        return ReadAuditUsers("SIGHTING_THREAD");
    }

    public IReadOnlyList<AuditUsers> ReadSightingMessageAuditUsers()
    {
        return ReadAuditUsers("SIGHTING_MESSAGE");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.AddProvider(new FakeLoggerProvider(logs)));

        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BlobStorage:ServiceUri"] = "https://pigeonwatchtests.blob.core.windows.net",
            ["BlobStorage:ContainerName"] = "sighting-photos",
            ["BlobStorage:LocalDirectory"] = null,
        }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<PigeonWatchDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PigeonWatchDbContext>>();

            services.AddDbContext<PigeonWatchDbContext>((serviceProvider, options) =>
                options
                    .AddInterceptors(serviceProvider.GetServices<ISaveChangesInterceptor>())
                    .UseSqlite(connection));

            services.AddTransient<IStartupFilter, ClientAddressStartupFilter>();
            services.AddTransient<IStartupFilter, RequestBodySizeStartupFilter>();

            if (requestCulture is not null)
                services.AddTransient<IStartupFilter>(_ => new RequestCultureStartupFilter(requestCulture));

            services.RemoveAll<ISightingPhotoStore>();
            services.AddSingleton<ISightingPhotoStore>(photoStore);

            if (timeProvider is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(timeProvider);
            }
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        IHost host = base.CreateHost(builder);

        using IServiceScope scope = host.Services.CreateScope();
        PigeonWatchDbContext db = scope.ServiceProvider.GetRequiredService<PigeonWatchDbContext>();
        db.Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            connection.Dispose();
    }

    private static SqliteConnection OpenConnection()
    {
        SqliteConnection openedConnection = new("DataSource=:memory:");
        openedConnection.Open();

        return openedConnection;
    }

    private string NextClientAddress()
    {
        int number = Interlocked.Increment(ref nextClientNumber);

        return $"10.{(number >> 16) & 255}.{(number >> 8) & 255}.{number & 255}";
    }

    private List<AuditUsers> ReadAuditUsers(string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT CREATE_USER, UPDATE_USER FROM {tableName}";
        using SqliteDataReader reader = command.ExecuteReader();
        List<AuditUsers> rows = [];

        while (reader.Read())
            rows.Add(new AuditUsers(reader.GetString(0), reader.GetString(1)));

        return rows;
    }
}
