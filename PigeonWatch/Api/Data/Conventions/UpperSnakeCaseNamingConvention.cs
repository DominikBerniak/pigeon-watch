using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace PigeonWatch.Data.Conventions;

internal class UpperSnakeCaseNamingConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            string? tableName = entityType.GetTableName();
            if (tableName is not null)
            {
                entityType.SetTableName(ToUpperSnakeCase(tableName));
            }

            foreach (IConventionProperty property in entityType.GetProperties())
            {
                StoreObjectIdentifier? table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);
                string? columnName = table is null ? property.GetColumnName() : property.GetColumnName(table.Value);
                if (columnName is not null)
                {
                    property.SetColumnName(ToUpperSnakeCase(columnName));
                }
            }
        }

        foreach (IConventionEntityType entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            foreach (IConventionKey key in entityType.GetKeys())
            {
                string? keyName = key.GetName();
                if (keyName is not null)
                {
                    key.SetName(ToUpperSnakeCase(keyName));
                }
            }

            foreach (IConventionForeignKey foreignKey in entityType.GetForeignKeys())
            {
                string? constraintName = foreignKey.GetConstraintName();
                if (constraintName is not null)
                {
                    foreignKey.SetConstraintName(ToUpperSnakeCase(constraintName));
                }
            }

            foreach (IConventionIndex index in entityType.GetIndexes())
            {
                string? indexName = index.GetDatabaseName();
                if (indexName is not null)
                {
                    index.SetDatabaseName(ToUpperSnakeCase(indexName));
                }
            }
        }
    }

    private string ToUpperSnakeCase(string name)
    {
        StringBuilder builder = new(name.Length + 8);

        for (int i = 0; i < name.Length; i++)
        {
            char current = name[i];

            if (!char.IsLetterOrDigit(current))
            {
                if (builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                continue;
            }

            if (char.IsUpper(current) && builder.Length > 0 && builder[^1] != '_')
            {
                char previous = name[i - 1];
                bool nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);

                if (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && nextIsLower))
                {
                    builder.Append('_');
                }
            }

            builder.Append(char.ToUpperInvariant(current));
        }

        return builder.ToString().TrimEnd('_');
    }
}
