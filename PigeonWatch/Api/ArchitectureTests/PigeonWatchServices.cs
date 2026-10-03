using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PigeonWatch.DependencyInjection;

namespace PigeonWatch.ArchitectureTests;

public class PigeonWatchServices
{
    public const string ConnectionString =
        "Server=.;Database=PigeonWatchArchitectureTests;Trusted_Connection=True";

    public static ServiceCollection Create()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString
            })
            .Build();

        ServiceCollection services = new();
        services.AddPigeonWatch(configuration);

        return services;
    }
}
