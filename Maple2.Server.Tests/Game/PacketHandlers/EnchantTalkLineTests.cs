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
        var line = new ScriptContent("done", "voice", "face");
        (GameSession session, TestGameSession test) = Start([new CinematicEventScript(7, [line])], new() {
            [7] = Row(7, [0], EnchantResult.None),
        });

        session.NpcScript!.EnchantResultScript(ScriptEventType.EnchantComplete);

        Assert.That(Sent(test), Is.EqualTo(new[] { Update(line) }));
    }

    // The KR rows of scriptEventCondition for enchant_complete: a row without enchantLevel (2100, 2203) is its result's
    // default line, so a success to +1 gets 2100. A row that names the level wins over the default (2300 before 2203).
    [TestCase(EnchantResult.Success, 1, 2100)]
    [TestCase(EnchantResult.Success, 3, 2000)]
    [TestCase(EnchantResult.Fail, 2, 2300)]
    [TestCase(EnchantResult.Fail, 4, 2203)]
    public void TheResultLine_FallsBackToTheRowWithoutALevel(EnchantResult result, int level, int expected) {
        int[] ids = [2000, 2100, 2203, 2300];
        CinematicEventScript[] events = ids.Select(id => new CinematicEventScript(id, [new ScriptContent($"l{id}", "", "")])).ToArray();
        (GameSession session, TestGameSession test) = Start(events, new() {
            [2000] = Row(2000, [2, 3, 4, 5], EnchantResult.Success),
            [2100] = Row(2100, [], EnchantResult.Success),
            [2203] = Row(2203, [], EnchantResult.Fail),
            [2300] = Row(2300, [1, 2], EnchantResult.Fail),
        });

        session.NpcScript!.EnchantResultScript(ScriptEventType.EnchantComplete, result, enchantLevel: level, rarity: 1);

        Assert.That(Sent(test), Is.EqualTo(new[] { Update(new ScriptContent($"l{expected}", "", "")) }));
    }

    private static ScriptEventConditionMetadata Row(int id, int[] levels, EnchantResult result) =>
        new(id, ScriptEventType.EnchantComplete, ItemEnchantError.s_itemenchant_unknown_err, 0, levels, 0, EnchantDamageType.None, result);

    private static string Update(ScriptContent line) => System.Convert.ToHexString(NpcTalkPacket.Update(line).ToArray());

    private static IEnumerable<string> Sent(TestGameSession test) => test.TakeSent().Select(System.Convert.ToHexString);

    private static (GameSession, TestGameSession) Start(CinematicEventScript[] events, Dictionary<int, ScriptEventConditionMetadata> rows) {
        var test = new TestGameSession();
        test.Register(new ScriptMetadata(Npc, ScriptType.Npc, new Dictionary<int, ScriptState> {
            [Master] = new(Master, ScriptStateType.Script, false, null,
                [new CinematicContent("", NpcTalkButton.None, 0, [], events)]),
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
            [ScriptEventType.EnchantComplete] = rows,
        }));
        GameSession session = test.Start();
        test.TakeSent();

        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Id>k__BackingField", any)!.SetValue(metadata, Npc);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", any)!.SetValue(npc, new Npc(metadata, null));
        session.NpcScript = new NpcScriptManager(session, npc, null, null, NpcTalkType.Talk);
        return (session, test);
    }
}
