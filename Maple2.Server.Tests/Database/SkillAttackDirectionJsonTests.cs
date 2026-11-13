using System.Reflection;
using System.Text.Json;
using Maple2.Database.Storage;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Database;

// The skill metadata JSON leaves out values equal to their type's default, so an attack's direction 0 (shuffle) must
// still read back as 0, and a direction never given as the client's 3.
public class SkillAttackDirectionJsonTests {
    private static readonly JsonSerializerOptions Options = (JsonSerializerOptions) typeof(GameStorage).Assembly
        .GetType("Maple2.Database.Extensions.PropertyBuilderExtensions", true)!
        .GetField("Options", BindingFlags.NonPublic | BindingFlags.Static)!
        .GetValue(null)!;

    private static SkillMetadataAttack RoundTrip(string json) {
        SkillMetadataAttack attack = JsonSerializer.Deserialize<SkillMetadataAttack>(json, Options)!;
        return JsonSerializer.Deserialize<SkillMetadataAttack>(JsonSerializer.Serialize(attack, Options), Options)!;
    }

    [Test]
    public void ShuffleSurvives() {
        Assert.Multiple(() => {
            Assert.That(RoundTrip("{\"Direction\":0}").Direction, Is.EqualTo(0));
            Assert.That(RoundTrip("{\"Direction\":4}").Direction, Is.EqualTo(4));
            Assert.That(RoundTrip("{}").Direction, Is.Null);
        });
    }
}
