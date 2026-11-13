using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using Maple2.Database.Context;
using Maple2.Database.Storage;
using Maple2.File.Ingest.Mapper;
using Maple2.Model.Common;
using Maple2.Model.Enum;
using Maple2.Model.Metadata;
using Microsoft.EntityFrameworkCore;

namespace Maple2.Server.Tests.Ingest;

// The client's inventory tab names from stringcommon.xml (GetInventoryTabName, stringtable.cpp:931) and the table's
// fallback for a metadata db ingested before it. The values are made up.
public class InventoryTabNameTests {
    private const string Gear = "G\u00e9ar", Pieces = "\u30d4\u30fc\u30b9";

    private static XmlDocument Strings(params (string Id, string Value)[] keys) {
        var document = new XmlDocument();
        document.LoadXml("<ms2>" + string.Concat(keys.Select(key => $"<key id=\"{key.Id}\" value=\"{key.Value}\" feature=\"Any01\" />")) + "</ms2>");
        return document;
    }

    [Test]
    public void StringKey_NamesEachClientTabOnce() {
        string?[] keys = Enumerable.Range(0, InventoryTab.Count).Select(InventoryTab.StringKey).ToArray();
        Assert.That(keys, Has.None.Null);
        Assert.That(keys.Distinct().Count(), Is.EqualTo(InventoryTab.Count));
        Assert.That(InventoryTab.StringKey(0), Is.EqualTo("s_inventory_tab_equip"));
        Assert.That(InventoryTab.StringKey(4), Is.EqualTo("s_inventory_tab_life"));
        Assert.That(InventoryTab.StringKey(1), Is.EqualTo("s_inventory_tab_skin"));
        Assert.That(InventoryTab.StringKey(13), Is.EqualTo("s_inventory_tab_lapenshard"));
        Assert.That(InventoryTab.StringKey(14), Is.EqualTo("s_inventory_tab_piece"));
        Assert.That(InventoryTab.StringKey(15), Is.Null);
        Assert.That(InventoryTab.StringKey(-1), Is.Null);
    }

    [Test]
    public void Parse_TakesTheTabKeys_ByClientTab() {
        InventoryTabNameTable table = TableMapper.InventoryTabNames(Strings(
            ("s_inventory_tab_equip", Gear), ("s_inventory_tab_piece", Pieces), ("s_inventory_tab_survival", "x"),
            ("s_inventory_tab_coin", ""), ("s_other", "y")));

        Assert.That(table.Names, Is.EqualTo(new Dictionary<int, string> { [0] = Gear, [14] = Pieces }));
        Assert.That(table.Name(InventoryType.Gear), Is.EqualTo(Gear));
        Assert.That(table.Name(InventoryType.Fragment), Is.EqualTo(Pieces));
    }

    [Test]
    public void ATabWithoutAName_GetsTheTypeName() {
        Assert.That(InventoryTabNameTable.Empty.Name(InventoryType.Currency), Is.EqualTo("Currency"));
        var table = new InventoryTabNameTable(new Dictionary<int, string> { [11] = "" });
        Assert.That(table.Name(InventoryType.Currency), Is.EqualTo("Currency"));
    }

    // An old metadata db has no stringcommon row: the storage gives the empty table instead of throwing.
    [Test]
    public void AMissingRow_GivesTheFallback() {
        using var context = new MetadataContext(new DbContextOptionsBuilder().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        MethodInfo retrieve = typeof(TableMetadataStorage).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name == "Retrieve" && method.GetParameters().Length == 3)
            .MakeGenericMethod(typeof(InventoryTabNameTable));
        var missing = (Lazy<InventoryTabNameTable>) retrieve.Invoke(null, [context, TableNames.STRING_COMMON, InventoryTabNameTable.Empty])!;
        Assert.That(missing.Value, Is.SameAs(InventoryTabNameTable.Empty));

        var stored = new InventoryTabNameTable(new Dictionary<int, string> { [0] = Gear });
        context.TableMetadata.Add(new TableMetadata { Name = TableNames.STRING_COMMON, Table = stored });
        context.SaveChanges();
        var present = (Lazy<InventoryTabNameTable>) retrieve.Invoke(null, [context, TableNames.STRING_COMMON, InventoryTabNameTable.Empty])!;
        Assert.That(present.Value.Name(InventoryType.Gear), Is.EqualTo(Gear));
    }
}
