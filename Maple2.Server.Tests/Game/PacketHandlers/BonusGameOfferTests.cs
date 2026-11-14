using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
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

    // Each spin takes one ticket and stores the take before its item is given (InventoryManager.ConsumeNow); a spin
    // count of 2 takes two of three tickets. Before, the tickets were taken in memory only, to the next session save.
    [Test]
    public void Spin_StoresEachTicketTakeBeforeItsItem() {
        const int npcId = 11000001;
        const int stateId = 1;
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var test = new TestGameSession();
        test.SetServerTable(Table());
        test.Register(TestGameSession.Metadata(20000001));
        var condition = (ScriptConditionMetadata) RuntimeHelpers.GetUninitializedObject(typeof(ScriptConditionMetadata));
        typeof(ScriptConditionMetadata).GetField("<Items>k__BackingField", any)!.SetValue(condition,
            new List<KeyValuePair<ItemComponent, bool>> { new(new ItemComponent(30000536, 1, 2, ItemTag.None), true) });
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>> {
            [npcId] = new() { [stateId] = condition },
        }));
        Item ticket = test.Store(TestGameSession.Metadata(30000536), 3);
        GameSession session = test.Start();
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Id>k__BackingField", any)!.SetValue(metadata, npcId);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));
        var state = (ScriptState) RuntimeHelpers.GetUninitializedObject(typeof(ScriptState));
        typeof(ScriptState).GetField("<Id>k__BackingField", any)!.SetValue(state, stateId);
        session.NpcScript = new NpcScriptManager(session, npc, null, state, NpcTalkType.Talk);
        session.BonusGameId = 6;
        test.TakeSent();

        new BonusGameHandler { TableMetadata = null!, ServerTableMetadata = test.ServerTableMetadata }.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(2);
        }));

        Assert.Multiple(() => {
            Assert.That(session.Item.Inventory.Get(ticket.Uid)?.Amount, Is.EqualTo(1));
            Assert.That(test.Row(ticket.Uid)?.Amount, Is.EqualTo(1), "the tickets are stored taken at the spin");
            Assert.That(session.Item.Inventory.Find(20000001).Sum(item => item.Amount), Is.EqualTo(2));
            Assert.That(test.Errors, Is.Empty);
        });
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
