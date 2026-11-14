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

    // The item rows that carry the played client's rank (ItemMetadataProperty.ClientRank). The ingest sets it only
    // when MS2_PLAYED_CLIENT_XML names the played client's item data; without it every copy keeps the server pack's
    // rank, and the client's lists that match a copy's rarity (the enchant fodder) stay empty.
    public static (int Items, int Ranked) ItemClientRanks(DbContext context) {
        int items = context.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS `Value` FROM `item`").AsEnumerable().Single();
        int ranked = context.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS `Value` FROM `item` WHERE JSON_EXTRACT(`Property`, '$.ClientRank') IS NOT NULL").AsEnumerable().Single();
        return (items, ranked);
    }

    // The start-up warning for an item table without one client rank; null when the table has some (or no items).
    public static string? ClientRankWarning(int items, int ranked) =>
        items > 0 && ranked == 0
            ? $"Metadata item table has no client rank in its {items} items: it was written by an ingest without MS2_PLAYED_CLIENT_XML (the played client's Xml.m2d), so items keep the server pack's rank. Run Maple2.File.Ingest again with it."
            : null;

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
