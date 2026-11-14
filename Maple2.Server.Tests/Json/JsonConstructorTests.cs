using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Maple2.Model.Enum;
using Maple2.Model.Game.Shop;
using Maple2.Database.Extensions;
using Maple2.Model.Metadata;

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

    // A json column leaves out a member at its type's default (WhenWritingDefault), and reading fills a missing
    // constructor parameter with the parameter's own default: a parameter whose default is not the type's (the quest
    // rank, 5) read a stored 0 back as that default. KR 91000080 (rank S, 0) came back as rank 5 (none), so its
    // useMainFamePoint gave no fame.
    [Test]
    public void QuestRankZeroRoundTrip() {
        JsonSerializerOptions options = PropertyBuilderExtensions.Options;
        var basic = new QuestMetadataBasic(0, QuestType.AllianceQuest, 0, 0, false, string.Empty, false, false, false, 0, 0, null, null,
            Alliance: AllianceType.KritiasMapleAlliance, Rank: 0);
        QuestMetadataBasic? read = JsonSerializer.Deserialize<QuestMetadataBasic>(JsonSerializer.Serialize(basic, options), options);
        Assert.That(read!.Rank, Is.EqualTo(0));
    }

    // Every constructor parameter of Maple2.Model whose default is not its type's default: the database's json
    // options write its member at the type's default too, so it reads back as written.
    [Test]
    public void NonZeroParameterDefaultsAreWritten() {
        JsonSerializerOptions options = PropertyBuilderExtensions.Options;
        Assembly model = typeof(RestrictedBuyData).Assembly;
        var faults = (from type in model.GetTypes()
                      where (type.IsPublic || type.IsNestedPublic) && !type.IsAbstract && !type.ContainsGenericParameters
                      from ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                      from parameter in ctor.GetParameters()
                      where parameter.HasDefaultValue && parameter.DefaultValue != null && parameter.ParameterType.IsValueType
                            && !parameter.DefaultValue.Equals(Activator.CreateInstance(Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType))
                      let info = options.GetTypeInfo(type)
                      where info.Kind == JsonTypeInfoKind.Object
                      let property = info.Properties.FirstOrDefault(p => string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                      where property != null && (property.ShouldSerialize == null || !property.ShouldSerialize(null!, Activator.CreateInstance(parameter.ParameterType)))
                      select $"{type.FullName}({parameter.Name} = {parameter.DefaultValue})").Distinct().ToList();
        Assert.That(faults, Is.Empty, string.Join(";", faults));
    }
}
