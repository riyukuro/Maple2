using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Maple2.Model.Enum;
using Maple2.Model.Game;
using Maple2.Model.Metadata;
using Maple2.Server.Game.Model;
using Maple2.Server.Game.Util;

namespace Maple2.Server.Tests.Game;

// A target condition's NpcRanks (BeginConditionTarget.NpcClasses): it holds only for an npc whose class is in the
// list. KR: 7 on the proc of 90000499.
public class NpcRanksConditionTests {
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldNpc Npc(int @class) {
        var basic = (NpcMetadataBasic) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadataBasic));
        typeof(NpcMetadataBasic).GetField("<Class>k__BackingField", Any)!.SetValue(basic, @class);
        var metadata = (NpcMetadata) RuntimeHelpers.GetUninitializedObject(typeof(NpcMetadata));
        typeof(NpcMetadata).GetField("<Basic>k__BackingField", Any)!.SetValue(metadata, basic);
        var npc = (FieldNpc) RuntimeHelpers.GetUninitializedObject(typeof(FieldNpc));
        typeof(Actor<Npc>).GetField("<Value>k__BackingField", Any)!.SetValue(npc, new Npc(metadata, null));
        return npc;
    }

    private static BeginCondition Condition(int[]? classes) {
        var target = new BeginConditionTarget([], new BeginConditionTarget.EventCondition(EventConditionType.OnOwnerAttackHit, false, [], []), [], [], [],
            new Dictionary<MasteryType, int>(), [], [], NpcClasses: classes);
        return new BeginCondition(0, 0, Gender.All, [], 1, 0, 0, 0, false, false, false, true, false, true, [],
            new Dictionary<BasicAttribute, long>(), [], [], [], [], null, target, null, null);
    }

    [Test]
    public void HoldsOnlyForAListedClass() {
        var test = new TestGameSession();
        test.Start();
        FieldPlayer player = test.Session.Player;
        Assert.Multiple(() => {
            Assert.That(Condition([7]).Check(player, player, Npc(7), EventConditionType.OnOwnerAttackHit), Is.True);
            Assert.That(Condition([7]).Check(player, player, Npc(3), EventConditionType.OnOwnerAttackHit), Is.False);
            Assert.That(Condition([7]).Check(player, player, player, EventConditionType.OnOwnerAttackHit), Is.False, "not an npc");
            Assert.That(Condition(null).Check(player, player, Npc(3), EventConditionType.OnOwnerAttackHit), Is.True, "no list");
        });
    }
}
