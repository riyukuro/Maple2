using System.Collections.Generic;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// Staging an item for enchanting, in KMS2's order: the staged item, then the smith's line.
public class ItemEnchantStageTests {
    [Test]
    public void TheStagedItemComesBeforeTheSmithsLine() {
        var test = new TestGameSession();
        Item gear = test.Store(TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1));
        test.SetServerTable(new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata>()));
        test.SetServerTable(new ScriptEventConditionTable(new Dictionary<ScriptEventType, Dictionary<int, ScriptEventConditionMetadata>> {
            [ScriptEventType.EnchantSelect] = new() {
                [1] = new ScriptEventConditionMetadata(1, ScriptEventType.EnchantSelect, ItemEnchantError.s_itemenchant_unknown_err, 0, [0], 0,
                    EnchantDamageType.None, EnchantResult.None),
            },
        }));
        GameSession session = test.Start();
        session.NpcScript = new NpcScriptManager(session, [new CinematicEventScript(1, [new ScriptContent("select", "", "")])]);
        test.TakeSent();

        Assert.That(session.ItemEnchant.StageItem(EnchantType.Ophelia, gear.Uid), Is.True);

        Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.ItemEnchant, SendOp.NpcTalk }));
    }

    // The dialog's retry button sends only a talk event after the result's own event: the same item is staged
    // again, which is what makes the client drop the cost and the copies of the enchant before.
    [Test]
    public void Retry_StagesTheSameItemAgain() {
        var test = new TestGameSession();
        Item gear = test.Store(TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1));
        test.SetServerTable(new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata>()));
        test.SetServerTable(new ScriptEventConditionTable(new Dictionary<ScriptEventType, Dictionary<int, ScriptEventConditionMetadata>> {
            [ScriptEventType.EnchantSelect] = new() {
                [1] = new ScriptEventConditionMetadata(1, ScriptEventType.EnchantSelect, ItemEnchantError.s_itemenchant_unknown_err, 0, [0], 0,
                    EnchantDamageType.None, EnchantResult.None),
            },
        }));
        GameSession session = test.Start();
        session.NpcScript = new NpcScriptManager(session, [new CinematicEventScript(1, [new ScriptContent("select", "", "")])]);

        Assert.That(session.ItemEnchant.Retry(), Is.False);
        session.ItemEnchant.StageItem(EnchantType.Ophelia, gear.Uid);
        session.ItemEnchant.NpcTalkEvent(ScriptEventType.EnchantComplete);
        test.TakeSent();

        Assert.Multiple(() => {
            Assert.That(session.ItemEnchant.Retry(), Is.True);
            Assert.That(test.TakeSentOps(), Is.EqualTo(new[] { SendOp.ItemEnchant, SendOp.NpcTalk }));
            Assert.That(session.ItemEnchant.Type, Is.EqualTo(EnchantType.Ophelia));
        });
    }

    // Enchant pressed without the copies the grade asks for: error 3 (lack of ingredient), which the client shows
    // as a notice, then the smith's line; the item keeps its grade.
    [Test]
    public void EnchantWithoutTheCopies_AnswersError3() {
        var test = new TestGameSession();
        Item gear = test.Store(TestGameSession.Metadata(11400001, EquipSlot.CL, type: 1));
        test.SetServerTable(new EnchantOptionTable(new Dictionary<int, EnchantOptionMetadata>()));
        test.SetServerTable(new ScriptEventConditionTable(new Dictionary<ScriptEventType, Dictionary<int, ScriptEventConditionMetadata>> {
            [ScriptEventType.EnchantSelect] = new() {
                [1] = new ScriptEventConditionMetadata(1, ScriptEventType.EnchantSelect, ItemEnchantError.s_itemenchant_unknown_err, 0, [0], 0,
                    EnchantDamageType.None, EnchantResult.None),
            },
        }));
        GameSession session = test.Start();
        session.NpcScript = new NpcScriptManager(session, [new CinematicEventScript(1, [new ScriptContent("select", "", "")])]);
        Item staged = session.Item.GetGear(gear.Uid)!;
        staged.Enchant = new ItemEnchant(enchants: 10);
        session.ItemEnchant.StageItem(EnchantType.Ophelia, gear.Uid);
        test.TakeSent();

        Assert.That(session.ItemEnchant.Enchant(gear.Uid), Is.False);

        List<byte[]> sent = test.TakeSent();
        Assert.Multiple(() => {
            Assert.That(System.Convert.ToHexString(sent[0]),
                Is.EqualTo(System.Convert.ToHexString(Maple2.Server.Game.Packets.ItemEnchantPacket.Error(ItemEnchantError.s_itemenchant_lack_ingredient).ToArray())));
            Assert.That(staged.Enchant!.Enchants, Is.EqualTo(10));
        });
    }
}
