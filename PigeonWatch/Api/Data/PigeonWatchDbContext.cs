using Microsoft.EntityFrameworkCore;
using PigeonWatch.Data.Conventions;
using PigeonWatch.Data.Entities;

namespace PigeonWatch.Data;

public class PigeonWatchDbContext(DbContextOptions<PigeonWatchDbContext> options) : DbContext(options)
{
    internal DbSet<UserAccountEntity> UserAccounts => Set<UserAccountEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new UpperSnakeCaseNamingConvention());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccountEntity>(entity =>
        {
            entity.ToTable("UserAccount");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
            entity.HasIndex(e => e.NormalizedEmail).IsUnique();
            entity.HasIndex(e => e.NormalizedUserName).IsUnique();
            entity.HasIndex(e => e.NormalizedDisplayName).IsUnique();
        });
    }
}
