using System.Text.RegularExpressions;
using Humanizer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using PigeonWatch.Data;

namespace PigeonWatch.ArchitectureTests;

public partial class DatabaseModelTests
{
    private static readonly string[] auditColumns = ["CREATE_USER", "CREATE_DATE", "UPDATE_USER", "UPDATE_DATE"];

    [Fact]
    public void Model_identifiers_are_upper_snake_case()
    {
        IModel model = BuildDesignTimeModel();
        IRelationalModel relationalModel = model.GetRelationalModel();

        List<(string Kind, string Name)> identifiers = [];
        foreach (ITable table in relationalModel.Tables)
        {
            identifiers.Add(("table", table.Name));
            identifiers.AddRange(table.Columns.Select(column => ($"column of {table.Name}", column.Name)));

            if (table.PrimaryKey is not null)
                identifiers.Add(("primary key", table.PrimaryKey.Name));

            identifiers.AddRange(table.UniqueConstraints.Select(key => ("key", key.Name)));
            identifiers.AddRange(table.ForeignKeyConstraints.Select(foreignKey => ("foreign key", foreignKey.Name)));
            identifiers.AddRange(table.Indexes.Select(index => ("index", index.Name)));
        }

        foreach (IView view in relationalModel.Views)
        {
            identifiers.Add(("view", view.Name));
            identifiers.AddRange(view.Columns.Select(column => ($"column of {view.Name}", column.Name)));
        }

        Assert.NotEmpty(relationalModel.Tables);
        AssertUpperSnakeCase(identifiers);
    }

    [Fact]
    public void Migrations_history_table_identifiers_are_upper_snake_case()
    {
        string createScript;
        using (ServiceProvider provider = PigeonWatchServices.Create().BuildServiceProvider())
        using (IServiceScope scope = provider.CreateScope())
        {
            PigeonWatchDbContext context = scope.ServiceProvider.GetRequiredService<PigeonWatchDbContext>();
            createScript = context.GetService<IHistoryRepository>().GetCreateScript();
        }

        List<(string Kind, string Name)> identifiers = BracketedIdentifier().Matches(createScript)
            .Select(match => ("migrations history identifier", match.Groups[1].Value))
            .ToList();
        List<string> names = identifiers.Select(identifier => identifier.Name).ToList();

        Assert.Contains("EF_MIGRATIONS_HISTORY", names);
        Assert.Contains("MIGRATION_ID", names);
        Assert.Contains("PRODUCT_VERSION", names);
        AssertUpperSnakeCase(identifiers);
    }

    [Fact]
    public void Table_and_view_names_are_singular()
    {
        IRelationalModel relationalModel = BuildDesignTimeModel().GetRelationalModel();

        List<(string Kind, string Name)> names = [];
        names.AddRange(relationalModel.Tables.Select(table => ("table", table.Name)));
        names.AddRange(relationalModel.Views.Select(view => ("view", view.Name)));
        names.Add(("table", MigrationsHistoryTableName()));

        List<string> offenders = names
            .Where(name => IsPlural(name.Name))
            .Select(name => $"{name.Kind} {name.Name}")
            .ToList();

        Assert.NotEmpty(relationalModel.Tables);
        Assert.True(
            offenders.Count == 0,
            $"Every table and view name must be singular (SMOKE_CHECK, not SMOKE_CHECKS). Offenders:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    [Theory]
    [InlineData("SMOKE_CHECKS", true)]
    [InlineData("USERS", true)]
    [InlineData("PIGEON_SIGHTINGS", true)]
    [InlineData("CATEGORIES", true)]
    [InlineData("SMOKE_CHECK", false)]
    [InlineData("USER", false)]
    [InlineData("PIGEON_SIGHTING", false)]
    [InlineData("ADDRESS", false)]
    [InlineData("STATUS", false)]
    [InlineData("EF_MIGRATIONS_HISTORY", false)]
    public void Plural_detection_recognises_plural_last_words(string name, bool expected)
    {
        Assert.Equal(expected, IsPlural(name));
    }

    [Fact]
    public void Entities_have_a_single_uniqueidentifier_ID_primary_key()
    {
        List<string> offenders = [];
        foreach (IEntityType entityType in MappedEntityTypes())
        {
            StoreObjectIdentifier table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)!.Value;
            IKey? primaryKey = entityType.FindPrimaryKey();

            if (primaryKey is null || primaryKey.Properties.Count != 1)
            {
                offenders.Add($"{entityType.DisplayName()} does not have a single-column primary key");
                continue;
            }

            IProperty keyProperty = primaryKey.Properties[0];
            string? columnName = keyProperty.GetColumnName(table);
            string? columnType = keyProperty.GetColumnType(table);

            if (columnName != "ID" || !string.Equals(columnType, "uniqueidentifier", StringComparison.OrdinalIgnoreCase))
                offenders.Add($"{entityType.DisplayName()} primary key is {columnName} {columnType}, expected ID uniqueidentifier");
        }

        Assert.True(
            offenders.Count == 0,
            $"Every entity must have a single ID uniqueidentifier primary key. Offenders:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    [Fact]
    public void Entities_derive_from_AuditableEntity_and_map_required_audit_columns()
    {
        Type auditableEntity = PigeonWatchAssemblies.Data.GetType("PigeonWatch.Data.Entities.AuditableEntity", throwOnError: true)!;

        List<string> offenders = [];
        foreach (IEntityType entityType in MappedEntityTypes())
        {
            if (!auditableEntity.IsAssignableFrom(entityType.ClrType))
                offenders.Add($"{entityType.DisplayName()} does not derive from {auditableEntity.Name}");

            StoreObjectIdentifier table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)!.Value;
            foreach (string auditColumn in auditColumns)
            {
                IProperty? property = entityType.GetProperties()
                    .SingleOrDefault(candidate => candidate.GetColumnName(table) == auditColumn);

                if (property is null)
                    offenders.Add($"{entityType.DisplayName()} does not map {auditColumn}");
                else if (property.IsColumnNullable(table))
                    offenders.Add($"{entityType.DisplayName()} maps {auditColumn} as nullable");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"Every entity must derive from {auditableEntity.Name} and map {string.Join(", ", auditColumns)} as NOT NULL. Offenders:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    private static List<IEntityType> MappedEntityTypes()
    {
        List<IEntityType> entityTypes = BuildDesignTimeModel()
            .GetEntityTypes()
            .Where(entityType => !entityType.IsOwned() && entityType.GetTableName() is not null)
            .ToList();

        Assert.NotEmpty(entityTypes);

        return entityTypes;
    }

    private static IModel BuildDesignTimeModel()
    {
        using ServiceProvider provider = PigeonWatchServices.Create().BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        PigeonWatchDbContext context = scope.ServiceProvider.GetRequiredService<PigeonWatchDbContext>();

        return context.GetService<IDesignTimeModel>().Model;
    }

    private static bool IsPlural(string name)
    {
        string lastWord = name.Split('_')[^1].ToLowerInvariant();

        return lastWord.Singularize(inputIsKnownToBePlural: false) != lastWord;
    }

    private static string MigrationsHistoryTableName()
    {
        using ServiceProvider provider = PigeonWatchServices.Create().BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        PigeonWatchDbContext context = scope.ServiceProvider.GetRequiredService<PigeonWatchDbContext>();
        string createScript = context.GetService<IHistoryRepository>().GetCreateScript();

        return BracketedIdentifier().Match(createScript).Groups[1].Value;
    }

    private static void AssertUpperSnakeCase(IEnumerable<(string Kind, string Name)> identifiers)
    {
        List<string> offenders = identifiers
            .Where(identifier => !UpperSnakeCase().IsMatch(identifier.Name))
            .Select(identifier => $"{identifier.Kind} {identifier.Name}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Every SQL identifier must match {UpperSnakeCase()}. Offenders:{Environment.NewLine}"
            + PigeonWatchAssemblies.Describe(offenders));
    }

    [GeneratedRegex("^[A-Z][A-Z0-9]*(_[A-Z0-9]+)*$")]
    private static partial Regex UpperSnakeCase();

    [GeneratedRegex(@"\[([^\]]+)\]")]
    private static partial Regex BracketedIdentifier();
}
