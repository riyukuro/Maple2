using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// BonusGame 0 (load): the client sends it only when an npc talk opens RouletteDialog, with the dialog's argument as the
// game id (uitalkcinematic.cpp:1133-1140). The server loaded any game of the table, which the spin then played with the
// talk's spin count. The games are made up.
public class BonusGameOfferTests {
    private static BonusGameTable Table() {
        var games = new Dictionary<int, BonusGameTable.Game>();
        var drops = new Dictionary<int, BonusGameTable.Drop>();
        foreach (int id in new[] { 6, 7 }) {
            games[id] = new BonusGameTable.Game(id, 0, new ItemComponent(30000536, 1, 1, ItemTag.None), []);
            drops[id] = new BonusGameTable.Drop(id, [new BonusGameTable.Drop.Item(new ItemComponent(20000001, 1, 1, ItemTag.None), 100, false)]);
        }
        return new BonusGameTable(games, drops);
    }

    // A load of gameId after the talk offered `offered`; the game loaded and the packets sent.
    private static (int Loaded, int Sent) Load(int offered, int gameId, bool laterTalk = false) {
        var test = new TestGameSession();
        test.SetServerTable(Table());
        GameSession session = test.Start();
        session.BonusGameOffered = offered;
        if (laterTalk) {
            session.NpcScript = new NpcScriptManager(session, []);
        }
        test.TakeSent();
        new BonusGameHandler { TableMetadata = null!, ServerTableMetadata = test.ServerTableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(0);
            writer.WriteInt(gameId);
        }));
        return (session.BonusGameId, test.TakeSent().Count);
    }

    [Test]
    public void TheOfferedGame_IsLoaded() {
        Assert.That(Load(6, 6), Is.EqualTo((6, 1)));
    }

    [Test]
    public void AnotherGame_IsNotLoaded() {
        Assert.Multiple(() => {
            Assert.That(Load(6, 7), Is.EqualTo((0, 0)));
            Assert.That(Load(0, 7), Is.EqualTo((0, 0)));
        });
    }

    // A roulette an earlier talk opened is not taken in a later talk that opened none.
    [Test]
    public void AnEarlierTalksGame_IsNotLoaded() {
        Assert.That(Load(6, 6, laterTalk: true), Is.EqualTo((0, 0)));
    }

    [Test]
    public void OfferedGame_IsTheRouletteDialogsArgument() {
        Assert.Multiple(() => {
            Assert.That(BonusGameHandler.OfferedGame("RouletteDialog", "6"), Is.EqualTo(6));
            Assert.That(BonusGameHandler.OfferedGame("roulettedialog", " 12x"), Is.EqualTo(12));
            Assert.That(BonusGameHandler.OfferedGame("RouletteDialog", ""), Is.EqualTo(0));
            Assert.That(BonusGameHandler.OfferedGame("JobChangeDialog", "6"), Is.Null);
        });
    }
}
