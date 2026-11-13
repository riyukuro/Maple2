using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// BonusGameDrop rows with notice="1": a won item of such a row is announced on the channel with ItemDropNotice
// (the winner's name and the item); a row without it sends nothing.
public class BonusGameNoticeTests {
    private static BonusGameTable.Drop.Item Row(int itemId, bool notice) => new(new ItemComponent(itemId, 1, 1, ItemTag.None), 100, notice);

    [Test]
    public void OnlyNoticeRowsAreAnnounced() {
        var test = new TestGameSession();
        GameSession session = test.Start();
        // The channel's sessions: only this player.
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object server = typeof(GameSession).GetField("server", any)!.GetValue(session)!;
        var sessions = new ConcurrentDictionary<long, GameSession> { [session.CharacterId] = session };
        typeof(Maple2.Server.Game.GameServer).GetField("sessions", any)!.SetValue(server, sessions);
        test.TakeSent();

        var plain = new Item(TestGameSession.Metadata(20000001), 1, 1) { Uid = 7 };
        var rare = new Item(TestGameSession.Metadata(20000002), 4, 1) { Uid = 8 };
        BonusGameHandler.SendDropNotices(session, [(Row(20000001, false), plain), (Row(20000002, true), rare)]);

        List<byte[]> notices = test.TakeSent().Where(packet => TestGameSession.Op(packet) == SendOp.ItemDropNotice).ToList();
        string name = session.Player.Value.Character.Name;
        Assert.That(notices, Has.Count.EqualTo(1));
        Assert.That(notices[0], Is.EqualTo(ItemDropNoticePacket.Notice(name, rare).ToArray()));
    }
}
