using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Model.Widget;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game;

// The storm of a Mushking Royale game (map 82000000, its SurvivalContents widget): each StormTickMs a living
// participant outside the step's safe circle loses StormDamageRate of its total health, and gets the "outside the safe
// zone" line (Survival 0x1d) once each StormNoticeMs; one inside loses nothing.
public class SurvivalStormTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void Update_HurtsOnlyOutsideTheSafeCircle() {
        MapMetadata map = TestGameSession.Map(82000000);
        var a = new TestGameSession();
        a.AddMap(map);
        a.Start();
        a.EnterMap(map);
        var b = new TestGameSession(a.Options);
        b.Start();
        FieldManager field = a.Session.Field!;
        typeof(Maple2.Server.Game.Model.Actor<Player>).GetField("<ObjectId>k__BackingField", Any)!.SetValue(b.Session.Player, 2);
        field.Players[2] = b.Session.Player;
        b.Session.Field = field;
        typeof(Maple2.Server.Game.Model.Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(b.Session.Player, field);
        typeof(FieldManager).GetField(nameof(FieldManager.Widgets))!.SetValue(field, new Dictionary<string, Widget>());
        var widget = new SurvivalContentsWidget(field);
        field.Widgets["SurvivalContents"] = widget;
        // No StormData: the fallback storm of 3000 to 300, step 1's safe circle 2662.5 around the center.
        widget.Action("EnterStep", 0, "1");
        foreach (TestGameSession session in new[] { a, b }) {
            Maple2.Server.Game.Model.FieldPlayer player = session.Session.Player;
            typeof(Maple2.Server.Game.Model.Actor<Player>).GetField("<Field>k__BackingField", Any)!.SetValue(player, field);
            typeof(Maple2.Server.Game.Model.FieldPlayer).GetField("healthSync", Any)!.SetValue(player, new Maple2.Server.Game.Util.HealthSyncClock());
            typeof(Maple2.Server.Game.Model.FieldPlayer).GetField("regenClock", Any)!.SetValue(player, new Maple2.Server.Game.Util.RegenClock());
            var health = player.Stats.Values[BasicAttribute.Health];
            health.Total = 1000;
            health.Current = 1000;
        }
        a.Session.Player.Position = new Vector3(100, 0, 0);
        b.Session.Player.Position = new Vector3(2700, 0, 0);
        var game = new SurvivalGameField(field, [a.Session.Player, b.Session.Player]);
        a.TakeSent();
        b.TakeSent();
        string inStorm = System.Convert.ToHexString(MapleSurvivalPacket.InStorm().ToArray());

        long tick = field.FieldTick;
        game.Update(tick);
        Assert.That(b.Session.Player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(950));
        Assert.That(a.Session.Player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(1000));
        Assert.That(b.TakeSent().Select(System.Convert.ToHexString), Has.Some.EqualTo(inStorm));
        Assert.That(a.TakeSent().Select(System.Convert.ToHexString), Has.None.EqualTo(inStorm));

        // Within the tick nothing more; the next tick hurts again, without the line until StormNoticeMs passed.
        game.Update(tick + SurvivalGameField.StormTickMs - 1);
        Assert.That(b.Session.Player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(950));
        game.Update(tick + SurvivalGameField.StormTickMs);
        Assert.That(b.Session.Player.Stats.Values[BasicAttribute.Health].Current, Is.EqualTo(900));
        Assert.That(b.TakeSent().Select(System.Convert.ToHexString), Has.None.EqualTo(inStorm));
        game.Update(tick + SurvivalGameField.StormNoticeMs);
        Assert.That(b.TakeSent().Select(System.Convert.ToHexString), Has.Some.EqualTo(inStorm));
    }
}
