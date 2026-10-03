using Microsoft.EntityFrameworkCore;
using PigeonWatch.Data.Conventions;
using PigeonWatch.Data.Entities;

namespace PigeonWatch.Data;

public class PigeonWatchDbContext(DbContextOptions<PigeonWatchDbContext> options) : DbContext(options)
{
    internal DbSet<SmokeCheckEntity> SmokeChecks => Set<SmokeCheckEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Conventions.Add(_ => new UpperSnakeCaseNamingConvention());
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SmokeCheckEntity>(entity =>
        {
            entity.ToTable("SmokeChecks");
            entity.HasKey(e => e.Id);
        });
    }
}
