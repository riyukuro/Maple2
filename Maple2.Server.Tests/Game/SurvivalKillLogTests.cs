using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager.Field;
using Maple2.Server.Game.Packets;

namespace Maple2.Server.Tests.Game;

// The kill log's status line: setStatus (survivalkilllogdialog.cpp:227) shows Survival 0x1b's first int as the
// survivors and 0x1a's int as the player's own kills (uisurvivalkilllogdialog.gfx). Before, the server sent the
// survivors in 0x1a, so the own-kills field showed the survivors and the survivor field stayed empty.
public class SurvivalKillLogTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Test]
    public void StartAndKill_SendSurvivorsAndOwnKills() {
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
        typeof(FieldManager).GetField(nameof(FieldManager.Widgets))!.SetValue(field, new Dictionary<string, Maple2.Server.Game.Model.Widget.Widget>());
        a.TakeSent();
        b.TakeSent();

        var game = new SurvivalGameField(field, [a.Session.Player, b.Session.Player]);

        byte[] survivors2 = MapleSurvivalPacket.Survivors(2, 2).ToArray();
        byte[] noKills = MapleSurvivalPacket.OwnKills(0).ToArray();
        List<byte[]> sentA = a.TakeSent();
        Assert.Multiple(() => {
            Assert.That(sentA, Has.Some.EqualTo(survivors2));
            Assert.That(sentA, Has.Some.EqualTo(noKills));
            Assert.That(b.TakeSent(), Has.Some.EqualTo(survivors2));
        });

        game.Damage(a.Session.Player, b.Session.Player, 0);
        game.Death(b.Session.Player);

        byte[] survivors1 = MapleSurvivalPacket.Survivors(1, 2).ToArray();
        sentA = a.TakeSent();
        Assert.Multiple(() => {
            Assert.That(sentA, Has.Some.EqualTo(survivors1));
            Assert.That(sentA, Has.Some.EqualTo(MapleSurvivalPacket.OwnKills(1).ToArray()));
            Assert.That(b.TakeSent(), Has.Some.EqualTo(survivors1));
        });
    }
}
