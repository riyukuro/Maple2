using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maple2.Model.Enum;
using Maple2.Model.Game.Shop;

namespace Maple2.Server.Tests.Json;

[TestFixture]
public class JsonConstructorTests {
    // The options the database's json columns use (PropertyBuilderExtensions).
    private static readonly JsonSerializerOptions Options = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    [Test]
    public void BuyTimeOfDayRoundTrip() {
        var time = new BuyTimeOfDay(1200, 10600);
        BuyTimeOfDay read = JsonSerializer.Deserialize<BuyTimeOfDay>(JsonSerializer.Serialize(time, Options), Options);
        Assert.That(read.StartTimeOfDay, Is.EqualTo(1200));
        Assert.That(read.EndTimeOfDay, Is.EqualTo(10600));
    }

    [Test]
    public void RestrictedBuyDataRoundTrip() {
        var data = new RestrictedBuyData {
            StartTime = 1700000000,
            EndTime = 1800000000,
            TimeRanges = [new BuyTimeOfDay(0, 3600), new BuyTimeOfDay(7200, 86400)],
            Days = [ShopBuyDay.Monday, ShopBuyDay.Friday],
        };
        RestrictedBuyData? read = JsonSerializer.Deserialize<RestrictedBuyData>(JsonSerializer.Serialize(data, Options), Options);
        Assert.That(read, Is.Not.Null);
        Assert.That(read!.StartTime, Is.EqualTo(data.StartTime));
        Assert.That(read.EndTime, Is.EqualTo(data.EndTime));
        Assert.That(read.TimeRanges.Select(t => (t.StartTimeOfDay, t.EndTimeOfDay)),
            Is.EqualTo(new[] { (0, 3600), (7200, 86400) }));
        Assert.That(read.Days, Is.EqualTo(data.Days));
    }

    // Every [JsonConstructor] in Maple2.Model: each parameter must name a public property or field
    // (case-insensitive, as System.Text.Json binds them), or deserializing the type throws.
    [Test]
    public void JsonConstructorParametersBindToMembers() {
        Assembly model = typeof(RestrictedBuyData).Assembly;
        var faults = (from type in model.GetTypes()
                      from ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                      where ctor.GetCustomAttribute<JsonConstructorAttribute>() != null
                      from parameter in ctor.GetParameters()
                      where !type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                 .Any(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                            && !type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                                 .Any(f => string.Equals(f.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                      select $"{type.FullName}({parameter.Name})").ToList();
        Assert.That(faults, Is.Empty);
    }
}
