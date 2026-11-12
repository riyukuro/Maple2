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
}
