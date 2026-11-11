using System.Linq;
using Maple2.Server.Game.Trigger;

namespace Maple2.Server.Tests.Game.Util;

// add_balloon_talk / remove_balloon_talk on a spawn point: its npcs and its monsters. Made-up ids.
public class TriggerBalloonTalkTests {
    private record Talker(string Name, int SpawnPointId);

    private static string[] Find(Talker[] npcs, Talker[] mobs, int spawnId) =>
        TriggerContext.SpawnPointNpcs(npcs, mobs, talker => talker.SpawnPointId, spawnId).Select(talker => talker.Name).ToArray();

    [Test]
    public void MonsterOfTheSpawnPoint_IsFound() {
        Assert.That(Find([new Talker("guide", 901)], [new Talker("boss", 902)], 902), Is.EqualTo(new[] { "boss" }));
    }

    [Test]
    public void NpcComesBeforeAMonster() {
        Assert.That(Find([new Talker("guide", 903)], [new Talker("boss", 903)], 903), Is.EqualTo(new[] { "guide", "boss" }));
    }

    [Test]
    public void NoTalkerOfTheSpawnPoint_IsEmpty() {
        Assert.That(Find([new Talker("guide", 901)], [new Talker("boss", 902)], 904), Is.Empty);
    }
}
