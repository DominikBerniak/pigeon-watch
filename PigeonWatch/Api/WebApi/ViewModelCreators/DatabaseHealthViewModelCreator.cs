using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Models;

namespace PigeonWatch.WebApi.ViewModelCreators;

public class DatabaseHealthViewModelCreator : IDatabaseHealthViewModelCreator
{
    public DatabaseHealthModel Create(DatabaseHealthResult result) =>
        result.IsHealthy
            ? new DatabaseHealthModel
            {
                Status = "ok",
                Id = result.SmokeCheckId,
                CreatedAtUtc = result.CreatedAtUtc
            }
            : new DatabaseHealthModel { Status = "unavailable" };
}
