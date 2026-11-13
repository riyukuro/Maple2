using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model;
using Maple2.Tools.Collision;
using NUnit.Framework;

namespace Maple2.Server.Tests.Game;

// holdtime counts each second in state Hold on the player's map. The one row with a code names the map as the
// code (21300021: code 65000003), so the raise passes the map as the code as well as the target.
public class HoldTimeTrophyTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const int Trophy = 90000001;
    private const int MapId = 65000003;
    private TestGameSession test = null!;

    private void HoldOneSecond(int mapId, params AchievementMetadata[] trophies) {
        test = new TestGameSession();
        test.AddAchievement(trophies);
        test.Start();
        test.EnterMap(TestGameSession.Map(mapId));
        test.TakeSent();
        FieldPlayer player = test.Session.Player;
        player.Value.Character.MapId = mapId;
        typeof(Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, test.Session.Field);
        // The map's bounds hold the player, so the sync does not move them.
        var entities = (MapEntityMetadata) RuntimeHelpers.GetUninitializedObject(typeof(MapEntityMetadata));
        typeof(MapEntityMetadata).GetField("<BoundingBox>k__BackingField", Any)!.SetValue(entities,
            new Prism(new Circle(Vector2.Zero, 100000), -100000, 200000));
        typeof(FieldManager).GetField("<Entities>k__BackingField", Any)!.SetValue(test.Session.Field, entities);
        typeof(FieldPlayer).GetField("state", Any)!.SetValue(player, ActorState.Hold);
        typeof(FieldPlayer).GetProperty("StateSyncTimeTracking", Any)!.SetValue(player, 1000L);
        player.OnStateSync(new StateSync { State = ActorState.Hold, Position = player.Position });
    }

    [TestCase(MapId, 1L)]
    [TestCase(MapId + 1, null)]
    public void HoldTime_CountsOnTheCodedMap(int mapId, long? expected) {
        HoldOneSecond(mapId, TestGameSession.Trophy(Trophy, ConditionType.holdtime, [60], codes: [MapId]));
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(expected));
    }

    // A row without a code (23100010) counts the hold on any map.
    [Test]
    public void HoldTime_WithoutCodeCountsAnywhere() {
        HoldOneSecond(MapId + 1, TestGameSession.Trophy(Trophy, ConditionType.holdtime, [60]));
        Assert.That(test.TrophyCounter(Trophy), Is.EqualTo(1L));
    }
}
