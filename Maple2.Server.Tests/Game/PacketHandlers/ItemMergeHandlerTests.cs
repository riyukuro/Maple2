using System;
using System.Collections.Generic;
using System.Linq;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.PacketHandlers;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The item merge (empower) dialog, in the order KMS2 sends: the smith speaks after the staged item's
// material list and after each result, with no request from the client.
public class ItemMergeHandlerTests {
    private const int SmithState = 10;
    private static readonly ScriptContent Staged = new("staged", "", "");
    private static readonly ScriptContent Done = new("done", "", "");

    private TestGameSession test = null!;
    private readonly ItemMergeHandler handler = new();
    private Item gear = null!;
    private Item crystal = null!;

    private static ScriptState State() => new(SmithState, ScriptStateType.Script, false, null, [
        new CinematicContent("", NpcTalkButton.None, 0, [], [new CinematicEventScript(8001, [Staged]), new CinematicEventScript(8002, [Done])]),
    ]);

    private GameSession Login(bool script = true) {
        test = new TestGameSession();
        ItemMetadata gearMetadata = TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1);
        gearMetadata = gearMetadata with { Limit = gearMetadata.Limit with { Level = 50 } };
        ItemMetadata crystalMetadata = TestGameSession.Metadata(37000002, type: 19);
        crystalMetadata = crystalMetadata with { Limit = crystalMetadata.Limit with { Level = 60 } };
        gear = test.Store(gearMetadata);
        crystal = test.Store(crystalMetadata, amount: 2);
        var option = new ItemMergeTable.Option([new ItemMergeTable.Range<int>(5, 5)], [new ItemMergeTable.Range<int>(0, 0)], [1]);
        test.SetServerTable(new ItemMergeTable(new Dictionary<int, Dictionary<int, ItemMergeTable.Entry>> {
            [37000002] = new() {
                [0] = new ItemMergeTable.Entry(0, 100, [], new Dictionary<BasicAttribute, ItemMergeTable.Option> { [BasicAttribute.Strength] = option },
                    new Dictionary<SpecialAttribute, ItemMergeTable.Option>()),
            },
        }));
        test.SetServerTable(test.ServerTableMetadata.ConstantsTable with { MergeSmithScriptID = SmithState });
        if (script) {
            test.Register(new ScriptMetadata(Constant.EmpowermentNpc, ScriptType.Npc, new Dictionary<int, ScriptState> { [SmithState] = State() }));
        }
        GameSession session = test.Start();
        session.Currency.Meso = 1000;
        test.TakeSent();
        return session;
    }

    private void Send(GameSession session, byte command, params long[] uids) {
        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(command);
            foreach (long uid in uids) {
                writer.WriteLong(uid);
            }
        }));
    }

    private static string Hex(Maple2.PacketLib.Tools.ByteWriter packet) => Convert.ToHexString(packet.ToArray());

    [Test]
    public void Stage_SendsTheMaterialsThenTheSmithsFirstLine() {
        GameSession session = Login();

        Send(session, 1, gear.Uid);

        Assert.That(test.TakeSent().Select(Convert.ToHexString), Is.EqualTo(new[] {
            Hex(ItemMergePacket.Stage([crystal.Uid])),
            Hex(NpcTalkPacket.Update(Staged)),
        }));
    }

    [Test]
    public void Stage_WithoutTheScriptSendsTheMaterialsOnly() {
        GameSession session = Login(script: false);

        Send(session, 1, gear.Uid);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.ItemMerge }));
    }

    // The crystal's preview is not followed by a line.
    [Test]
    public void SelectCrystal_SendsThePreviewOnly() {
        GameSession session = Login();
        Send(session, 1, gear.Uid);
        test.TakeSent();

        Send(session, 2, gear.Uid, crystal.Uid);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.ItemMerge }));
    }

    // The crystal, the meso, the result, then the smith's second line; again on the next merge of another item.
    [Test]
    public void Empower_SendsTheResultThenTheSmithsSecondLine() {
        GameSession session = Login();
        Send(session, 1, gear.Uid);
        Send(session, 2, gear.Uid, crystal.Uid);
        test.TakeSent();

        Send(session, 3, gear.Uid, crystal.Uid);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.ItemInventory, SendOp.Meso, SendOp.ItemMerge, SendOp.NpcTalk }));
        Assert.That(sent[2][2], Is.EqualTo(5));
        Assert.That(Convert.ToHexString(sent[3]), Is.EqualTo(Hex(NpcTalkPacket.Update(Done))));
        Assert.That(session.Currency.Meso, Is.EqualTo(900));
        Assert.That(session.Item.Inventory.Get(gear.Uid)!.Stats!.HasMergeOption, Is.True);
        Assert.That(test.Errors, Is.Empty);
    }

    // An item that already has an option: the choice (mode 4) has no line; the pick's result (mode 5) has.
    [Test]
    public void Choice_TheLineFollowsTheResult() {
        GameSession session = Login();
        Send(session, 1, gear.Uid);
        Send(session, 2, gear.Uid, crystal.Uid);
        Send(session, 3, gear.Uid, crystal.Uid);
        Send(session, 1, gear.Uid);
        Send(session, 2, gear.Uid, crystal.Uid);
        test.TakeSent();

        Send(session, 3, gear.Uid, crystal.Uid);
        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.ItemInventory, SendOp.Meso, SendOp.ItemMerge }));
        Assert.That(sent[2][2], Is.EqualTo(4));

        handler.Handle(session, TestGameSession.Packet(writer => {
            writer.WriteByte(4);
            writer.WriteBool(true);
        }));
        sent = test.TakeSent();
        Assert.That(sent.Select(TestGameSession.Op), Is.EqualTo(new[] { SendOp.ItemMerge, SendOp.NpcTalk }));
        Assert.That(Convert.ToHexString(sent[1]), Is.EqualTo(Hex(NpcTalkPacket.Update(Done))));
    }

    [Test]
    public void SmithLine() {
        ScriptState state = State();
        var random = new Random(1);
        Assert.That(ItemMergeManager.SmithLine(state, ItemMergeManager.StagedTalk, random), Is.EqualTo(Staged));
        Assert.That(ItemMergeManager.SmithLine(state, ItemMergeManager.ResultTalk, random), Is.EqualTo(Done));
        Assert.That(ItemMergeManager.SmithLine(state, 2, random), Is.Null);
        Assert.That(ItemMergeManager.SmithLine(state with { Contents = [] }, 0, random), Is.Null);
    }
}
