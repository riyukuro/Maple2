using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Error;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Core.Constants;
using Maple2.Server.Game.Manager;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Packets;
using Maple2.Server.Game.Session;

namespace Maple2.Server.Tests.Game.PacketHandlers;

// The enchant dialog opened from a talk with a field npc (Ophelia 11000508, NpcTalk 4): after the result the client
// sends event 3 (EnchantComplete) by itself, and the npc answers with a line of her script's enchant master state
// (ConstantsTable.EnchantMasterScriptID), sent as NpcTalk Update.
public class EnchantTalkLineTests {
    private const int Npc = 11000508;
    private const int Master = 31;

    [Test]
    public void AFieldNpcTalk_AnswersTheResultWithHerLine() {
        var test = new TestGameSession();
        var line = new ScriptContent("done", "voice", "face");
        test.Register(new ScriptMetadata(Npc, ScriptType.Npc, new Dictionary<int, ScriptState> {
            [Master] = new(Master, ScriptStateType.Script, false, null,
                [new CinematicContent("", NpcTalkButton.None, 0, [], [new CinematicEventScript(7, [line])])]),
        }));
        test.SetServerTable(new ConstantsTable(
            bagSlotTabGameCount: [48, 48], bagSlotTabSkinCount: [48, 48], bagSlotTabSummonCount: [48, 48],
            bagSlotTabMaterialCount: [48, 48], bagSlotTabMasteryCount: [48, 48], bagSlotTabLifeCount: [48, 48],
            bagSlotTabQuestCount: [48, 48], bagSlotTabGemCount: [48, 48], bagSlotTabPetCount: [48, 48],
            bagSlotTabActiveSkillCount: [48, 48], bagSlotTabCoinCount: [48, 48], bagSlotTabBadgeCount: [48, 48],
            bagSlotTabMiscCount: [48, 48], bagSlotTabLapenShardCount: [48, 48], bagSlotTabPieceCount: [48, 48],
            EnchantMasterScriptID: Master));
        test.SetServerTable(new ScriptConditionTable(new Dictionary<int, Dictionary<int, ScriptConditionMetadata>>()));
        test.SetServerTable(new ScriptEventConditionTable(new Dictionary<ScriptEventType, Dictionary<int, ScriptEventConditionMetadata>> {
            [ScriptEventType.EnchantComplete] = new() {
                [7] = new ScriptEventConditionMetadata(7, ScriptEventType.EnchantComplete, ItemEnchantError.s_itemenchant_unknown_err, 0, [0], 0,
                    EnchantDamageType.None, EnchantResult.None),
            },
        }));
        GameSession session = test.Start();
        test.TakeSent();

        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Id>k__BackingField", any)!.SetValue(metadata, Npc);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));
        session.NpcScript = new NpcScriptManager(session, npc, null, null, NpcTalkType.Talk);

        session.NpcScript.EnchantResultScript(ScriptEventType.EnchantComplete);

        List<byte[]> sent = test.TakeSent();
        Assert.That(sent.Select(System.Convert.ToHexString),
            Is.EqualTo(new[] { System.Convert.ToHexString(NpcTalkPacket.Update(line).ToArray()) }));
    }
}
