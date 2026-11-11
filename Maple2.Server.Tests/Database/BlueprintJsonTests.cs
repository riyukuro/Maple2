using System;
using System.Reflection;
using System.Text.Json;
using Maple2.Database.Storage;
using Maple2.Model.Enum;
using Maple2.Model.Game;

namespace Maple2.Server.Tests.Database;

// A blueprint is stored in two JSON columns: ugc-market-item.Blueprint and, inside the item's sub type,
// item.SubType. The game class ItemBlueprint keeps its data in fields, which System.Text.Json skips, but the
// columns hold the database record of the same name, which has properties. These tests go through the
// database's own serializer options and its own conversions, reached by reflection since both are internal.
public class BlueprintJsonTests {
    private static readonly Assembly Database = typeof(GameStorage).Assembly;
    private static readonly JsonSerializerOptions Options = (JsonSerializerOptions) Database
        .GetType("Maple2.Database.Extensions.PropertyBuilderExtensions", true)!
        .GetField("Options", BindingFlags.NonPublic | BindingFlags.Static)!
        .GetValue(null)!;
    private static readonly Type Record = Database.GetType("Maple2.Database.Model.ItemBlueprint", true)!;

    private static ItemBlueprint Sample() => new() {
        BlueprintUid = 7234567890123,
        Length = 5,
        Width = 6,
        Height = 7,
        CreationTime = DateTimeOffset.FromUnixTimeSeconds(1700000000),
        Type = BlueprintType.Copy,
        AccountId = 11,
        CharacterId = 22,
        CharacterName = "name",
    };

    private static MethodInfo Conversion(Type from, Type to) {
        foreach (MethodInfo method in Record.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
            if (method.Name == "op_Implicit" && method.ReturnType == to && method.GetParameters()[0].ParameterType == from) {
                return method;
            }
        }
        throw new InvalidOperationException("no conversion");
    }

    [Test]
    public void TheGameClassAloneWouldBeStoredEmpty() {
        // Why the column must not hold the game class.
        Assert.That(JsonSerializer.Serialize(Sample(), Options), Is.EqualTo("{}"));
    }

    [Test]
    public void TheMarketColumnKeepsEveryField() {
        object record = Conversion(typeof(ItemBlueprint), Record).Invoke(null, [Sample()])!;
        string json = JsonSerializer.Serialize(record, Record, Options);

        using JsonDocument document = JsonDocument.Parse(json);
        // The preview check reads this key with JSON_EXTRACT(Blueprint, '$.BlueprintUid').
        Assert.That(document.RootElement.GetProperty("BlueprintUid").GetInt64(), Is.EqualTo(7234567890123));

        object loaded = JsonSerializer.Deserialize(json, Record, Options)!;
        var back = (ItemBlueprint) Conversion(Record, typeof(ItemBlueprint)).Invoke(null, [loaded])!;
        ItemBlueprint want = Sample();
        Assert.Multiple(() => {
            Assert.That(back.BlueprintUid, Is.EqualTo(want.BlueprintUid));
            Assert.That(back.Length, Is.EqualTo(want.Length));
            Assert.That(back.Width, Is.EqualTo(want.Width));
            Assert.That(back.Height, Is.EqualTo(want.Height));
            Assert.That(back.CreationTime, Is.EqualTo(want.CreationTime));
            Assert.That(back.Type, Is.EqualTo(want.Type));
            Assert.That(back.AccountId, Is.EqualTo(want.AccountId));
            Assert.That(back.CharacterId, Is.EqualTo(want.CharacterId));
            Assert.That(back.CharacterName, Is.EqualTo(want.CharacterName));
        });
    }

    [Test]
    public void TheItemSubTypeColumnKeepsTheBlueprint() {
        Type subType = Database.GetType("Maple2.Database.Model.ItemSubType", true)!;
        Type ugc = Database.GetType("Maple2.Database.Model.ItemUgc", true)!;
        Type look = Database.GetType("Maple2.Database.Model.UgcItemLook", true)!;
        object lookRecord = look.GetMethod("op_Implicit", [typeof(UgcItemLook)])!.Invoke(null, [new UgcItemLook()])!;
        object record = Conversion(typeof(ItemBlueprint), Record).Invoke(null, [Sample()])!;
        object item = Activator.CreateInstance(ugc, lookRecord, record)!;

        string json = JsonSerializer.Serialize(item, subType, Options);
        object loaded = JsonSerializer.Deserialize(json, subType, Options)!;

        Assert.That(loaded, Is.InstanceOf(ugc));
        object blueprint = ugc.GetProperty("Blueprint")!.GetValue(loaded)!;
        Assert.That(Record.GetProperty("BlueprintUid")!.GetValue(blueprint), Is.EqualTo(7234567890123));
        Assert.That(Record.GetProperty("CharacterName")!.GetValue(blueprint), Is.EqualTo("name"));
    }

    [Test]
    public void TheMarketColumnIsTheRecordNotTheGameClass() {
        Type row = Database.GetType("Maple2.Database.Model.UgcMarketItem", true)!;
        Assert.That(row.GetProperty("Blueprint")!.PropertyType, Is.EqualTo(Record));
    }
}
