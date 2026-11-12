using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Maple2.Database.Context;

// A metadata database written by an older Maple2.File.Ingest can lack a column this code reads (a new column is
// added by a new ingest only). The server would start and then fail each query of that table, for the trophy table
// at every login. The check reads no rows: one statement per table that names every column the model maps.
public static class MetadataSchemaCheck {
    // The tables of the model that the database cannot answer for: a table or one of its columns is missing.
    public static List<string> StaleTables(DbContext context) {
        var stale = new List<string>();
        foreach ((string table, string sql) in Probes(context.Model)) {
            try {
                context.Database.ExecuteSqlRaw(sql);
            } catch (System.Exception ex) when (ex is not System.OperationCanceledException) {
                stale.Add(table);
            }
        }
        return stale;
    }

    public static IEnumerable<(string Table, string Sql)> Probes(IModel model) {
        foreach (IEntityType entityType in model.GetEntityTypes().OrderBy(type => type.Name)) {
            string? table = entityType.GetTableName();
            if (table == null) {
                continue;
            }
            string[] columns = entityType.GetProperties()
                .Select(property => property.GetColumnName())
                .Where(column => !string.IsNullOrEmpty(column))
                .OrderBy(column => column, System.StringComparer.Ordinal)
                .Select(column => $"`{column}`")
                .ToArray();
            if (columns.Length == 0) {
                continue;
            }
            yield return (table, $"SELECT {string.Join(", ", columns)} FROM `{table}` LIMIT 0");
        }
    }
}
