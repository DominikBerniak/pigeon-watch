using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PigeonWatch.Data.Identity;

public class AdminRoleBootstrapWorker(
    AdminRoleBootstrapper bootstrapper,
    ILogger<AdminRoleBootstrapWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken = default)
    {
        try
        {
            await bootstrapper.RunAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Admin role bootstrap failed, so the configured admin roles were not granted at this start");
        }
    }
}
