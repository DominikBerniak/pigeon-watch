#pragma warning disable EF1001
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.SqlServer.Migrations.Internal;

namespace PigeonWatch.Data.Conventions;

public class UpperSnakeCaseMigrationsHistory(HistoryRepositoryDependencies dependencies) : SqlServerHistoryRepository(dependencies)
{
    public const string HistoryTableName = "EF_MIGRATIONS_HISTORY";

    protected override void ConfigureTable(EntityTypeBuilder<HistoryRow> history)
    {
        base.ConfigureTable(history);
        history.Property(h => h.MigrationId).HasColumnName("MIGRATION_ID");
        history.Property(h => h.ProductVersion).HasColumnName("PRODUCT_VERSION");
    }
}
