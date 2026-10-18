using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PigeonWatch.Data;
using PigeonWatch.WebApi.Host;

namespace PigeonWatch.IntegrationTests;

public sealed class PigeonWatchApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private int nextClientNumber;

    public PigeonWatchApiFactory()
    {
        connection.Open();
    }

    public HttpClient CreateApiClient(string? clientAddress = null)
    {
        HttpClient client = CreateClient();
        string address = clientAddress ?? $"10.0.0.{Interlocked.Increment(ref nextClientNumber)}";
        client.DefaultRequestHeaders.Add(ClientAddressStartupFilter.HeaderName, address);

        return client;
    }

    public int CountUserAccounts()
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM USER_ACCOUNT";
        int count = Convert.ToInt32(command.ExecuteScalar());

        return count;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<PigeonWatchDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PigeonWatchDbContext>>();

            services.AddDbContext<PigeonWatchDbContext>((serviceProvider, options) =>
                options
                    .AddInterceptors(serviceProvider.GetServices<ISaveChangesInterceptor>())
                    .UseSqlite(connection));

            services.AddTransient<IStartupFilter, ClientAddressStartupFilter>();
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
}
