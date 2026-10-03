using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PigeonWatch.BusinessObjects;
using PigeonWatch.Data.Entities;
using PigeonWatch.Data.Mappers;

namespace PigeonWatch.Data.Repositories;

public class SmokeCheckRepository(PigeonWatchDbContext db, ISmokeCheckMapper smokeCheckMapper) : ISmokeCheckRepository
{
    public async Task<SmokeCheckResult> InsertReadBackAndRollBackAsync(CancellationToken cancellationToken = default)
    {
        IExecutionStrategy strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            db.ChangeTracker.Clear();
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(ct);

            SmokeCheckEntity entity = new();
            db.SmokeChecks.Add(entity);
            await db.SaveChangesAsync(ct);

            SmokeCheckEntity readBack = await db.SmokeChecks
                .AsNoTracking()
                .SingleAsync(s => s.Id == entity.Id, ct);

            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();

            return smokeCheckMapper.ToResult(readBack);
        }, cancellationToken);
    }
}
