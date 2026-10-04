using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PigeonWatch.Data.Entities;

namespace PigeonWatch.Data.Auditing;

public class AuditSaveChangesInterceptor(ICurrentUserProvider currentUserProvider, TimeProvider timeProvider) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyAuditValues(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditValues(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ApplyAuditValues(DbContext? context)
    {
        if (context is null)
            return;

        string userName = currentUserProvider.GetCurrentUserName();
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (EntityEntry<AuditableEntity> entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreateUser = userName;
                entry.Entity.CreateDate = now;
                entry.Entity.UpdateUser = userName;
                entry.Entity.UpdateDate = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(e => e.CreateUser).IsModified = false;
                entry.Property(e => e.CreateDate).IsModified = false;
                entry.Entity.UpdateUser = userName;
                entry.Entity.UpdateDate = now;
            }
        }
    }
}
