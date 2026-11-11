using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Database.Storage;
using Maple2.Model.Game;
using Maple2.Model.Metadata;

namespace Maple2.Server.Tests.Json;

[TestFixture]
public class SpecialOptionConverterTests {
    private static readonly JsonSerializerOptions Options = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new SpecialOptionConverter() },
    };

    [Test]
    public void RoundTrip() {
        var options = new Dictionary<SpecialAttribute, SpecialOption> {
            [SpecialAttribute.IceDamage] = new SpecialOption(58),
            [SpecialAttribute.FireDamage] = new SpecialOption(0),
        };
        string json = JsonSerializer.Serialize(options, Options);
        Dictionary<SpecialAttribute, SpecialOption> read = JsonSerializer.Deserialize<Dictionary<SpecialAttribute, SpecialOption>>(json, Options)!;
        Assert.That(read[SpecialAttribute.IceDamage].Value, Is.EqualTo(58));
        Assert.That(read[SpecialAttribute.FireDamage].Value, Is.EqualTo(0));
    }

    [Test]
    public void ReadsOldRate() {
        // 58 thousandths, as the old float form stored it.
        SpecialOption rate = JsonSerializer.Deserialize<SpecialOption>("{\"Rate\":0.058}", Options);
        Assert.That(rate.Value, Is.EqualTo(58));
        SpecialOption value = JsonSerializer.Deserialize<SpecialOption>("{\"Rate\":12}", Options);
        Assert.That(value.Value, Is.EqualTo(12));
        SpecialOption empty = JsonSerializer.Deserialize<SpecialOption>("{}", Options);
        Assert.That(empty.Value, Is.EqualTo(0));
    }

    [Test]
    public void OldRowRoundTrips() {
        // A row as stored before the option became an int: a rate, a flat value and a negative rate.
        const string old = "{\"IceDamage\":{\"Rate\":0.058},\"FireDamage\":{\"Rate\":5},\"DarkDamage\":{\"Rate\":-0.02}}";
        Dictionary<SpecialAttribute, SpecialOption> read = JsonSerializer.Deserialize<Dictionary<SpecialAttribute, SpecialOption>>(old, Options)!;
        Assert.That(read[SpecialAttribute.IceDamage].Value, Is.EqualTo(58));
        Assert.That(read[SpecialAttribute.FireDamage].Value, Is.EqualTo(5));
        Assert.That(read[SpecialAttribute.DarkDamage].Value, Is.EqualTo(-20));

        string json = JsonSerializer.Serialize(read, Options);
        Assert.That(json, Is.EqualTo("{\"IceDamage\":{\"Value\":58},\"FireDamage\":{\"Value\":5},\"DarkDamage\":{\"Value\":-20}}"));
        Dictionary<SpecialAttribute, SpecialOption> again = JsonSerializer.Deserialize<Dictionary<SpecialAttribute, SpecialOption>>(json, Options)!;
        Assert.That(again[SpecialAttribute.IceDamage].Value, Is.EqualTo(58));
    }

    [Test]
    public void EnchantStoredAsZeroIsRebuilt() {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(15000001, "15000001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        var item = new Item(metadata, 4) {
            Enchant = new ItemEnchant(enchants: 2, basicOptions: new Dictionary<BasicAttribute, BasicOption> {
                [BasicAttribute.MinWeaponAtk] = new BasicOption(0),
            }),
        };
        var table = new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata> {
            [1] = new(1, item.Type.Type, 1, 4, 0.02f, 0, 99, [BasicAttribute.MinWeaponAtk, BasicAttribute.MaxWeaponAtk]),
            [2] = new(2, item.Type.Type, 2, 4, 0.03f, 0, 99, [BasicAttribute.MinWeaponAtk, BasicAttribute.MaxWeaponAtk]),
            [3] = new(3, item.Type.Type, 3, 4, 0.04f, 0, 99, [BasicAttribute.MinWeaponAtk, BasicAttribute.MaxWeaponAtk]),
        });

        GameStorage.RepairEnchant(table, item);
        Assert.That(item.Enchant.BasicOptions[BasicAttribute.MinWeaponAtk].Value, Is.EqualTo(50));
        Assert.That(item.Enchant.BasicOptions[BasicAttribute.MaxWeaponAtk].Value, Is.EqualTo(50));

        // An item that already holds values is left as it is.
        item.Enchant.BasicOptions[BasicAttribute.MinWeaponAtk] = new BasicOption(7);
        GameStorage.RepairEnchant(table, item);
        Assert.That(item.Enchant.BasicOptions[BasicAttribute.MinWeaponAtk].Value, Is.EqualTo(7));
    }

    [Test]
    public void ReadsFractionUnderValue() {
        Assert.That(JsonSerializer.Deserialize<SpecialOption>("{\"Value\":57.6}", Options).Value, Is.EqualTo(58));
        Assert.That(JsonSerializer.Deserialize<SpecialOption>("{\"Value\":58.0}", Options).Value, Is.EqualTo(58));
        Assert.That(JsonSerializer.Deserialize<SpecialOption>("{\"Value\":-3.4}", Options).Value, Is.EqualTo(-3));
        Assert.That(JsonSerializer.Deserialize<SpecialOption>("{\"Value\":58}", Options).Value, Is.EqualTo(58));
    }

    [Test]
    public void LimitBreakRateStoredAsZeroIsRebuilt() {
        var property = new ItemMetadataProperty(false, 0, 100, 18, 0, string.Empty, string.Empty, ItemTag.None, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], false, 0, false, [], [], [], 0, 0);
        var limit = new ItemMetadataLimit(Gender.All, 0, 0, 4, true, true, true, true, true, false, false, 0, [], []);
        var metadata = new ItemMetadata(15000001, "15000001", [], "", [], new ItemMetadataLife(0, 0), property, new ItemMetadataCustomize(0, 0), limit, null, null, [], null, null, null, null);
        var item = new Item(metadata, 4) {
            LimitBreak = new ItemLimitBreak(2, new Dictionary<BasicAttribute, BasicOption> {
                [BasicAttribute.Health] = new BasicOption(195),
                [BasicAttribute.MinWeaponAtk] = new BasicOption(0),
            }, new Dictionary<SpecialAttribute, SpecialOption>()),
        };
        UnlimitedEnchantOptionTable.Option Row(int health, float rate) => new(
            new Dictionary<BasicAttribute, int> { [BasicAttribute.Health] = health },
            new Dictionary<BasicAttribute, float> { [BasicAttribute.MinWeaponAtk] = rate, [BasicAttribute.MaxWeaponAtk] = rate },
            new Dictionary<SpecialAttribute, int>(), new Dictionary<SpecialAttribute, float>());
        var table = new UnlimitedEnchantOptionTable(new Dictionary<int, Dictionary<int, UnlimitedEnchantOptionTable.Option>> {
            [item.Type.Type] = new() { [1] = Row(190, 0.03f), [2] = Row(5, 0.02f), [3] = Row(5, 0.02f) },
        });

        GameStorage.RepairLimitBreak(table, item);
        Assert.That(item.LimitBreak.BasicOptions[BasicAttribute.MinWeaponAtk].Value, Is.EqualTo(50));
        Assert.That(item.LimitBreak.BasicOptions[BasicAttribute.MaxWeaponAtk].Value, Is.EqualTo(50));
        Assert.That(item.LimitBreak.BasicOptions[BasicAttribute.Health].Value, Is.EqualTo(195));

        // An option that already holds a value is left as it is.
        item.LimitBreak.BasicOptions[BasicAttribute.MinWeaponAtk] = new BasicOption(7);
        GameStorage.RepairLimitBreak(table, item);
        Assert.That(item.LimitBreak.BasicOptions[BasicAttribute.MinWeaponAtk].Value, Is.EqualTo(7));
    }

    [Test]
    public void BasicRateIsThousandths() {
        Assert.That(new BasicOption(0.25f).Value, Is.EqualTo(250));
        // In float these products fall just below the whole number.
        Assert.That(new BasicOption(0.251f).Value, Is.EqualTo(251));
        Assert.That(new BasicOption(0.253f).Value, Is.EqualTo(253));
        Assert.That(new BasicOption(0.502f).Value, Is.EqualTo(502));
        Assert.That(new SpecialOption(0.251f).Value, Is.EqualTo(251));
        Assert.That(new SpecialOption(0.253f).Value, Is.EqualTo(253));
        Assert.That(new SpecialOption(0.502f).Value, Is.EqualTo(502));
        Assert.That(new SpecialOption(-0.02f).Value, Is.EqualTo(-20));
        Assert.That(BasicOption.IsRatio(BasicAttribute.Defense), Is.True);
        Assert.That(BasicOption.IsRatio(BasicAttribute.Health), Is.False);
    }
}
