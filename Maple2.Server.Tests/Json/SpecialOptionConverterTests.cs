using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maple2.Database.Extensions;
using Maple2.Model.Enum;
using Maple2.Model.Game;

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
}
